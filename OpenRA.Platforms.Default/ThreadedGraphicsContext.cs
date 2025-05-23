#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using OpenRA.Primitives;

namespace OpenRA.Platforms.Default
{
	/// <summary>
	/// Creates a dedicated thread for the graphics device. An internal message queue is used to perform actions on the
	/// device. This allows calls to be enqueued to be processed asynchronously and thus free up the calling thread.
	/// </summary>
	sealed class ThreadedGraphicsContext : IGraphicsContext
	{
		// PERF: Maintain several object pools to reduce allocations.
		readonly Dictionary<Type, object> vertexBufferPools = [];
		readonly Stack<Message> messagePool = [];
		readonly Queue<Message> messages = [];

		public readonly int VertexBatchSize;
		public readonly int IndexBatchSize;
		readonly object syncObject = new();
		readonly Thread renderThread;
		volatile ExceptionDispatchInfo messageException;

		// Delegates that perform actions on the real device.
		Func<object> doClear;
		Action doClearDepthBuffer;
		Action doDisableDepthBuffer;
		Action doEnableDepthBuffer;
		Action doDisableScissor;
		Action doPresent;
		Func<string> getGLVersion;
		Func<ITexture> getCreateTexture;
		Func<object, IFrameBuffer> getCreateFrameBuffer;
		Func<object, IShader> getCreateShader;
		Func<object, object> getCreateEmptyVertexBuffer;
		Func<object, object> getCreateVertexBuffer;
		Func<object, IIndexBuffer> getCreateIndexBuffer;
		Action<object> doDrawPrimitives;
		Action<object> doDrawElements;
		Action<object> doEnableScissor;
		Action<object> doSetBlendMode;
		Action<object> doSetVSync;

		public ThreadedGraphicsContext(Sdl2GraphicsContext context, int vertexBatchSize, int indexBatchSize)
		{
			VertexBatchSize = vertexBatchSize;
			IndexBatchSize = indexBatchSize;
			renderThread = new Thread(RenderThread)
			{
				Name = "ThreadedGraphicsContext RenderThread",
				IsBackground = true
			};
			lock (syncObject)
			{
				// Start and wait for the rendering thread to have initialized before returning.
				// Otherwise, the delegates may not have been set yet.
				renderThread.Start(context);
				Monitor.Wait(syncObject);
			}
		}

		void RenderThread(object contextObject)
		{
			using (var context = (Sdl2GraphicsContext)contextObject)
			{
				// This lock allows the constructor to block until initialization completes.
				lock (syncObject)
				{
					context.InitializeOpenGL();

					doClear = () => { context.Clear(); return null; };
					doClearDepthBuffer = context.ClearDepthBuffer;
					doDisableDepthBuffer = context.DisableDepthBuffer;
					doEnableDepthBuffer = context.EnableDepthBuffer;
					doDisableScissor = context.DisableScissor;
					doPresent = context.Present;
					getGLVersion = () => context.GLVersion;
					getCreateTexture = () => new ThreadedTexture(this, (ITextureInternal)context.CreateTexture());
					getCreateFrameBuffer =
						tuple =>
						{
							var t = ((Size, Color))tuple;
							return new ThreadedFrameBuffer(this,
								context.CreateFrameBuffer(t.Item1, (ITextureInternal)CreateTexture(), t.Item2));
						};
					getCreateShader = bindings => new ThreadedShader(this, context.CreateShader((IShaderBindings)bindings));
					getCreateEmptyVertexBuffer =
						tuple =>
						{
							(object t, var type) = ((int, Type))tuple;
							var vertexBuffer = context.GetType()
								.GetMethod(nameof(CreateEmptyVertexBuffer))
								.MakeGenericMethod(type)
								.Invoke(context, [t]);

							return typeof(ThreadedVertexBuffer<>).MakeGenericType(type).GetConstructors()[0].Invoke([this, vertexBuffer]);
						};
					getCreateVertexBuffer =
						tuple =>
						{
							var (array, dynamic, type) = ((object, bool, Type))tuple;
							var vertexBuffer = context.GetType()
								.GetMethod(nameof(CreateVertexBuffer))
								.MakeGenericMethod(type)
								.Invoke(context, [array, dynamic]);
							return typeof(ThreadedVertexBuffer<>).MakeGenericType(type).GetConstructors()[0].Invoke([this, vertexBuffer]);
						};
					getCreateIndexBuffer = indices => new ThreadedIndexBuffer(this, context.CreateIndexBuffer((uint[])indices));
					doDrawPrimitives =
						tuple =>
						{
							var t = ((PrimitiveType, int, int))tuple;
							context.DrawPrimitives(t.Item1, t.Item2, t.Item3);
						};
					doDrawElements =
						tuple =>
						{
							var t = ((int, int))tuple;
							context.DrawElements(t.Item1, t.Item2);
						};
					doEnableScissor =
						tuple =>
						{
							var t = ((int, int, int, int))tuple;
							context.EnableScissor(t.Item1, t.Item2, t.Item3, t.Item4);
						};
					doSetBlendMode = mode => context.SetBlendMode((BlendMode)mode);
					doSetVSync = enabled => context.SetVSyncEnabled((bool)enabled);

					Monitor.Pulse(syncObject);
				}

				// Run a message loop.
				// Only this rendering thread can perform actions on the real device,
				// so other threads must send us a message which we process here.
				Message message;
				while (true)
				{
					lock (messages)
					{
						if (messages.Count == 0)
						{
							if (messageException != null)
								break;

							Monitor.Wait(messages);
						}

						message = messages.Dequeue();
					}

					if (message == null)
						break;

					message.Execute();
				}
			}
		}

		internal T[] GetVertices<T>(int size)
		{
			lock (vertexBufferPools)
			{
				Stack<T[]> pool;
				if (!vertexBufferPools.TryGetValue(typeof(T), out var poolObject))
				{
					pool = new Stack<T[]>();
					vertexBufferPools.Add(typeof(T), pool);
				}
				else
					pool = (Stack<T[]>)poolObject;

				if (size <= VertexBatchSize && pool.Count > 0)
					return pool.Pop();
			}

			return new T[size < VertexBatchSize ? VertexBatchSize : size];
		}

		internal void ReturnVertices<T>(T[] vertices)
		{
			if (vertices.Length == VertexBatchSize)
				lock (vertexBufferPools)
					((Stack<T[]>)vertexBufferPools[typeof(T)]).Push(vertices);
		}

		sealed class Message
		{
			public Message(ThreadedGraphicsContext device)
			{
				this.device = device;
			}

			readonly AutoResetEvent completed = new(false);
			readonly ThreadedGraphicsContext device;
			volatile Action action;
			volatile Action<object> actionWithParam;
			volatile Func<object> func;
			volatile Func<object, object> funcWithParam;
			volatile object param;
			volatile object result;
			volatile ExceptionDispatchInfo edi;

			public void SetAction(Action method)
			{
				action = method;
			}

			public void SetAction(Action<object> method, object state)
			{
				actionWithParam = method;
				param = state;
			}

			public void SetAction(Func<object> method)
			{
				func = method;
			}

			public void SetAction(Func<object, object> method, object state)
			{
				funcWithParam = method;
				param = state;
			}

			public void Execute()
			{
				var wasSend = action != null || actionWithParam != null;
				try
				{
					if (action != null)
					{
						action();
						result = null;
						action = null;
					}
					else if (actionWithParam != null)
					{
						actionWithParam(param);
						result = null;
						actionWithParam = null;
						param = null;
					}
					else if (func != null)
					{
						result = func();
						func = null;
					}
					else
					{
						result = funcWithParam(param);
						funcWithParam = null;
						param = null;
					}
				}
				catch (Exception ex)
				{
					edi = ExceptionDispatchInfo.Capture(ex);

					if (wasSend)
						device.messageException = edi;

					result = null;
					param = null;
					action = null;
					actionWithParam = null;
					func = null;
					funcWithParam = null;
				}

				if (wasSend)
				{
					lock (device.messagePool)
						device.messagePool.Push(this);
				}
				else
				{
					completed.Set();
				}
			}

			public object Result()
			{
				completed.WaitOne();

				var localEdi = edi;
				edi = null;
				var localResult = result;
				result = null;

				localEdi?.Throw();
				return localResult;
			}
		}

		Message GetMessage()
		{
			lock (messagePool)
				if (messagePool.Count > 0)
					return messagePool.Pop();

			return new Message(this);
		}

		void QueueMessage(Message message)
		{
			var exception = messageException;
			exception?.Throw();

			lock (messages)
			{
				messages.Enqueue(message);
				if (messages.Count == 1)
					Monitor.Pulse(messages);
			}
		}

		object RunMessage(Message message)
		{
			QueueMessage(message);
			var result = message.Result();
			lock (messagePool)
				messagePool.Push(message);
			return result;
		}

		/// <summary>
		/// Sends a message to the rendering thread.
		/// This method blocks until the message is processed, and returns the result.
		/// </summary>
		public T Send<T>(Func<T> method) where T : class
		{
			if (renderThread == Thread.CurrentThread)
				return method();

			var message = GetMessage();
			message.SetAction(method);
			return (T)RunMessage(message);
		}

		/// <summary>
		/// Sends a message to the rendering thread.
		/// This method blocks until the message is processed, and returns the result.
		/// </summary>
		public T Send<T>(Func<object, T> method, object state) where T : class
		{
			if (renderThread == Thread.CurrentThread)
				return method(state);

			var message = GetMessage();
			message.SetAction(method, state);
			return (T)RunMessage(message);
		}

		/// <summary>
		/// Posts a message to the rendering thread.
		/// This method then returns immediately and does not wait for the message to be processed.
		/// </summary>
		public void Post(Action method)
		{
			if (renderThread == Thread.CurrentThread)
			{
				method();
				return;
			}

			var message = GetMessage();
			message.SetAction(method);
			QueueMessage(message);
		}

		/// <summary>
		/// Posts a message to the rendering thread.
		/// This method then returns immediately and does not wait for the message to be processed.
		/// </summary>
		public void Post(Action<object> method, object state)
		{
			if (renderThread == Thread.CurrentThread)
			{
				method(state);
				return;
			}

			var message = GetMessage();
			message.SetAction(method, state);
			QueueMessage(message);
		}

		public void Dispose()
		{
			// Use a null message to signal the rendering thread to clean up, then wait for it to complete.
			QueueMessage(null);
			renderThread.Join();
		}

		public string GLVersion => Send(getGLVersion);

		public void Clear()
		{
			// We send the clear even though we could just post it.
			// This ensures all previous messages have been processed before we return.
			// This prevents us from queuing up work faster than it can be processed if rendering is behind.
			Send(doClear);
		}

		public void ClearDepthBuffer()
		{
			Post(doClearDepthBuffer);
		}

		public IFrameBuffer CreateFrameBuffer(Size s)
		{
			return Send(getCreateFrameBuffer, (s, Color.FromArgb(0)));
		}

		public IFrameBuffer CreateFrameBuffer(Size s, Color clearColor)
		{
			return Send(getCreateFrameBuffer, (s, clearColor));
		}

		public IShader CreateShader(IShaderBindings bindings)
		{
			return Send(getCreateShader, bindings);
		}

		public ITexture CreateTexture()
		{
			return Send(getCreateTexture);
		}

		public IVertexBuffer<T> CreateEmptyVertexBuffer<T>(int size) where T : struct
		{
			return (IVertexBuffer<T>)Send(getCreateEmptyVertexBuffer, (size, typeof(T)));
		}

		public IVertexBuffer<T> CreateVertexBuffer<T>(T[] data, bool dynamic = true) where T : struct
		{
			return (IVertexBuffer<T>)Send(getCreateVertexBuffer, ((object)data, dynamic, typeof(T)));
		}

		public IIndexBuffer CreateIndexBuffer(uint[] indices)
		{
			return Send(getCreateIndexBuffer, indices);
		}

		public T[] CreateVertices<T>(int size) where T : struct
		{
			return GetVertices<T>(size);
		}

		public void DisableDepthBuffer()
		{
			Post(doDisableDepthBuffer);
		}

		public void DisableScissor()
		{
			Post(doDisableScissor);
		}

		public void DrawPrimitives(PrimitiveType type, int firstVertex, int numVertices)
		{
			Post(doDrawPrimitives, (type, firstVertex, numVertices));
		}

		public void DrawElements(int numIndices, int offset)
		{
			Post(doDrawElements, (numIndices, offset));
		}

		public void EnableDepthBuffer()
		{
			Post(doEnableDepthBuffer);
		}

		public void EnableScissor(int left, int top, int width, int height)
		{
			Post(doEnableScissor, (left, top, width, height));
		}

		public void Present()
		{
			Post(doPresent);
		}

		public void SetBlendMode(BlendMode mode)
		{
			Post(doSetBlendMode, mode);
		}

		public void SetVSyncEnabled(bool enabled)
		{
			Post(doSetVSync, enabled);
		}
	}

	sealed class ThreadedFrameBuffer : IFrameBuffer
	{
		readonly ThreadedGraphicsContext device;
		readonly Func<ITexture> getTexture;
		readonly Action bind;
		readonly Action unbind;
		readonly Action dispose;
		readonly Action<object> enableScissor;
		readonly Action disableScissor;

		public ThreadedFrameBuffer(ThreadedGraphicsContext device, IFrameBuffer frameBuffer)
		{
			this.device = device;
			getTexture = () => frameBuffer.Texture;
			bind = frameBuffer.Bind;
			unbind = frameBuffer.Unbind;
			dispose = frameBuffer.Dispose;

			enableScissor = rect => frameBuffer.EnableScissor((Rectangle)rect);
			disableScissor = frameBuffer.DisableScissor;
		}

		public ITexture Texture => device.Send(getTexture);

		public void Bind()
		{
			device.Post(bind);
		}

		public void Unbind()
		{
			device.Post(unbind);
		}

		public void EnableScissor(Rectangle rect)
		{
			device.Post(enableScissor, rect);
		}

		public void DisableScissor()
		{
			device.Post(disableScissor);
		}

		public void Dispose()
		{
			device.Post(dispose);
		}
	}

	sealed class ThreadedVertexBuffer<T> : IVertexBuffer<T> where T : struct
	{
		readonly ThreadedGraphicsContext device;
		readonly Action bind;
		readonly Action<object> setData1;
		readonly Action<object> setData2;
		readonly Func<object, object> setData3;
		readonly Action dispose;

		public ThreadedVertexBuffer(ThreadedGraphicsContext device, IVertexBuffer<T> vertexBuffer)
		{
			this.device = device;
			bind = vertexBuffer.Bind;
			setData1 = tuple =>
			{
				var t = ((T[], int))tuple;
				vertexBuffer.SetData(t.Item1, t.Item2);
				device.ReturnVertices(t.Item1);
			};

			setData2 = tuple =>
			{
				var t = ((T[], int, int, int))tuple;
				vertexBuffer.SetData(t.Item1, t.Item2, t.Item3, t.Item4);
				device.ReturnVertices(t.Item1);
			};

			setData3 = tuple => { setData2(tuple); return null; };
			dispose = vertexBuffer.Dispose;
		}

		public void Bind()
		{
			device.Post(bind);
		}

		public void SetData(T[] vertices, int length)
		{
			var buffer = device.GetVertices<T>(length);
			Array.Copy(vertices, buffer, length);
			device.Post(setData1, (buffer, length));
		}

		/// <summary>
		/// PERF: The vertices array is passed without copying to the render thread. Upon return `vertices` may reference another
		/// array object of at least the same size - containing random values.
		/// </summary>
		public void SetData(ref T[] vertices, int length)
		{
			device.Post(setData1, (vertices, length));
			vertices = device.GetVertices<T>(vertices.Length);
		}

		public void SetData(T[] vertices, int offset, int start, int length)
		{
			if (length <= device.VertexBatchSize)
			{
				// If we are able to use a buffer without allocation, post a message to avoid blocking.
				var buffer = device.GetVertices<T>(length);
				Array.Copy(vertices, offset, buffer, 0, length);
				device.Post(setData2, (buffer, 0, start, length));
			}
			else
			{
				// If the length is too large for a buffer, send a message and block to avoid allocations.
				device.Send(setData3, (vertices, offset, start, length));
			}
		}

		public void Dispose()
		{
			device.Post(dispose);
		}
	}

	sealed class ThreadedIndexBuffer : IIndexBuffer
	{
		readonly ThreadedGraphicsContext device;
		readonly Action bind;
		readonly Action dispose;

		public ThreadedIndexBuffer(ThreadedGraphicsContext device, IIndexBuffer indexBuffer)
		{
			this.device = device;
			bind = indexBuffer.Bind;
			dispose = indexBuffer.Dispose;
		}

		public void Bind()
		{
			device.Post(bind);
		}

		public void Dispose()
		{
			device.Post(dispose);
		}
	}

	sealed class ThreadedTexture : ITextureInternal
	{
		readonly ThreadedGraphicsContext device;
		readonly Func<object> getScaleFilter;
		readonly Action<object> setScaleFilter;
		readonly Func<object> getSize;
		readonly Action<object> setEmpty;
		readonly Func<byte[]> getData;
		readonly Action<object> setData1;
		readonly Func<object, object> setData2;
		readonly Action<object> setData3;
		readonly Func<object, object> setData4;
		readonly Action<object> setData5;
		readonly Action dispose;

		public ThreadedTexture(ThreadedGraphicsContext device, ITextureInternal texture)
		{
			this.device = device;
			ID = texture.ID;
			getScaleFilter = () => texture.ScaleFilter;
			setScaleFilter = value => texture.ScaleFilter = (TextureScaleFilter)value;
			getSize = () => texture.Size;
			setEmpty = tuple => { var t = ((int, int))tuple; texture.SetEmpty(t.Item1, t.Item2); };
			getData = texture.GetData;
			setData1 = tuple => { var t = ((byte[], int, int))tuple; texture.SetData(t.Item1, t.Item2, t.Item3); };
			setData2 = tuple => { setData1(tuple); return null; };
			setData3 = tuple => { var t = ((float[], int, int))tuple; texture.SetFloatData(t.Item1, t.Item2, t.Item3); };
			setData4 = tuple => { setData3(tuple); return null; };
			setData5 = rect => texture.SetDataFromReadBuffer((Rectangle)rect);
			dispose = texture.Dispose;
		}

		public uint ID { get; }

		public TextureScaleFilter ScaleFilter
		{
			get => (TextureScaleFilter)device.Send(getScaleFilter);

			set => device.Post(setScaleFilter, value);
		}

		public Size Size => (Size)device.Send(getSize);

		public void SetEmpty(int width, int height)
		{
			device.Post(setEmpty, (width, height));
		}

		public byte[] GetData()
		{
			return device.Send(getData);
		}

		public void SetData(byte[] colors, int width, int height)
		{
			// Objects 85000 bytes or more will be directly allocated in the Large Object Heap (LOH).
			// https://docs.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap
			if (colors.Length < 85000)
			{
				// If we are able to create a small array the GC can collect easily, post a message to avoid blocking.
				var temp = new byte[colors.Length];
				Array.Copy(colors, temp, temp.Length);
				device.Post(setData1, (temp, width, height));
			}
			else
			{
				// If the length is large and would result in an array on the Large Object Heap (LOH),
				// send a message and block to avoid LOH allocation as this requires a Gen2 collection.
				device.Send(setData2, (colors, width, height));
			}
		}

		public void SetFloatData(float[] data, int width, int height)
		{
			// Objects 85000 bytes or more will be directly allocated in the Large Object Heap (LOH).
			// https://docs.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap
			if (data.Length < 21250)
			{
				// If we are able to create a small array the GC can collect easily, post a message to avoid blocking.
				var temp = new float[data.Length];
				Array.Copy(data, temp, temp.Length);
				device.Post(setData3, (temp, width, height));
			}
			else
			{
				// If the length is large and would result in an array on the Large Object Heap (LOH),
				// send a message and block to avoid LOH allocation as this requires a Gen2 collection.
				device.Send(setData4, (data, width, height));
			}
		}

		public void SetDataFromReadBuffer(Rectangle rect)
		{
			device.Post(setData5, rect);
		}

		public void Dispose()
		{
			device.Post(dispose);
		}
	}

	sealed class ThreadedShader : IShader
	{
		readonly ThreadedGraphicsContext device;
		readonly Action prepareRender;
		readonly Action<object> setBool;
		readonly Action<object> setMatrix;
		readonly Action<object> setTexture;
		readonly Action<object> setVec1;
		readonly Action<object> setVec2;
		readonly Action<object> setVec3;
		readonly Action<object> setVec4;
		readonly Action bind;

		public ThreadedShader(ThreadedGraphicsContext device, IShader shader)
		{
			this.device = device;
			bind = shader.Bind;
			prepareRender = shader.PrepareRender;
			setBool = tuple => { var t = ((string, bool))tuple; shader.SetBool(t.Item1, t.Item2); };
			setMatrix = tuple => { var t = ((string, float[]))tuple; shader.SetMatrix(t.Item1, t.Item2); };
			setTexture = tuple => { var t = ((string, ITexture))tuple; shader.SetTexture(t.Item1, t.Item2); };
			setVec1 = tuple => { var t = ((string, float))tuple; shader.SetVec(t.Item1, t.Item2); };
			setVec2 = tuple => { var t = ((string, float[], int))tuple; shader.SetVec(t.Item1, t.Item2, t.Item3); };
			setVec3 = tuple => { var t = ((string, float, float))tuple; shader.SetVec(t.Item1, t.Item2, t.Item3); };
			setVec4 = tuple => { var t = ((string, float, float, float))tuple; shader.SetVec(t.Item1, t.Item2, t.Item3, t.Item4); };
		}

		public void Bind()
		{
			device.Post(bind);
		}

		public void PrepareRender()
		{
			device.Post(prepareRender);
		}

		public void SetBool(string name, bool value)
		{
			device.Post(setBool, (name, value));
		}

		public void SetMatrix(string param, float[] mtx)
		{
			device.Post(setMatrix, (param, mtx));
		}

		public void SetTexture(string param, ITexture texture)
		{
			device.Post(setTexture, (param, texture));
		}

		public void SetVec(string name, float x)
		{
			device.Post(setVec1, (name, x));
		}

		public void SetVec(string name, float[] vec, int length)
		{
			device.Post(setVec2, (name, vec, length));
		}

		public void SetVec(string name, float x, float y)
		{
			device.Post(setVec3, (name, x, y));
		}

		public void SetVec(string name, float x, float y, float z)
		{
			device.Post(setVec4, (name, x, y, z));
		}
	}
}
