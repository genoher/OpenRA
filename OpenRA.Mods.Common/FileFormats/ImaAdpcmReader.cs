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

namespace OpenRA.Mods.Common.FileFormats
{
	public static class ImaAdpcmReader
	{
		static readonly int[] IndexAdjust = [-1, -1, -1, -1, 2, 4, 6, 8];
		static readonly int[] StepTable =
		[
			7, 8, 9, 10, 11, 12, 13, 14, 16,
			17, 19, 21, 23, 25, 28, 31, 34, 37,
			41, 45, 50, 55, 60, 66, 73, 80, 88,
			97, 107, 118, 130, 143, 157, 173, 190, 209,
			230, 253, 279, 307, 337, 371, 408, 449, 494,
			544, 598, 658, 724, 796, 876, 963, 1060, 1166,
			1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749,
			3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484,
			7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289,
			16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
		];

		public static short DecodeImaAdpcmSample(byte b, ref int index, ref int current)
		{
			var sb = (b & 8) != 0;
			b &= 7;

			var delta = StepTable[index] * b / 4 + StepTable[index] / 8;
			if (sb)
				delta = -delta;

			current += delta;
			if (current > short.MaxValue)
				current = short.MaxValue;

			if (current < short.MinValue)
				current = short.MinValue;

			index += IndexAdjust[b];
			if (index < 0)
				index = 0;

			if (index > 88)
				index = 88;

			return (short)current;
		}

		public static byte[] LoadImaAdpcmSound(ReadOnlySpan<byte> raw, ref int index)
		{
			var currentSample = 0;
			return LoadImaAdpcmSound(raw, ref index, ref currentSample);
		}

		public static byte[] LoadImaAdpcmSound(ReadOnlySpan<byte> raw, ref int index, ref int currentSample)
		{
			var dataSize = raw.Length;
			var outputSize = raw.Length * 4;

			var output = new byte[outputSize];
			var offset = 0;

			while (dataSize-- > 0)
			{
				var b = raw[offset / 4];

				var t = DecodeImaAdpcmSample(b, ref index, ref currentSample);
				output[offset++] = (byte)t;
				output[offset++] = (byte)(t >> 8);

				t = DecodeImaAdpcmSample((byte)(b >> 4), ref index, ref currentSample);
				output[offset++] = (byte)t;
				output[offset++] = (byte)(t >> 8);
			}

			return output;
		}
	}
}
