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
using System.IO;
using System.Linq;
using OpenRA.FileFormats;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.UtilityCommands
{
	sealed class ConvertSpriteToPngCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--png";

		bool IUtilityCommand.ValidateArguments(string[] args)
		{
			return args.Length >= 3;
		}

		[Desc("SPRITEFILE PALETTE [--noshadow] [--nopadding]",
			  "Convert a shp/tmp/R8 to a series of PNGs, optionally removing shadow")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			// HACK: The engine code assumes that Game.modData is set.
			var modData = Game.ModData = utility.ModData;

			var src = args[1];
			var shadowIndex = Array.Empty<int>();
			if (args.Contains("--noshadow"))
			{
				Array.Resize(ref shadowIndex, shadowIndex.Length + 3);
				shadowIndex[^1] = 1;
				shadowIndex[^2] = 3;
				shadowIndex[^3] = 4;
			}

			var palette = new ImmutablePalette(args[2], [0], shadowIndex);
			var palColors = new Color[Palette.Size];
			for (var i = 0; i < Palette.Size; i++)
				palColors[i] = palette.GetColor(i);

			var frames = FrameLoader.GetFrames(File.OpenRead(src), modData.SpriteLoaders, src, out _);

			var usePadding = !args.Contains("--nopadding");
			var count = 0;
			var prefix = Path.GetFileNameWithoutExtension(src);

			foreach (var frame in frames)
			{
				var frameSize = usePadding && !frame.DisableExportPadding ? frame.FrameSize : frame.Size;
				var offset = usePadding && !frame.DisableExportPadding ? (frame.Offset - 0.5f * new float2(frame.Size - frame.FrameSize)).ToInt2() : int2.Zero;

				// shp(ts) may define empty frames
				if (frameSize.Width == 0 && frameSize.Height == 0)
				{
					count++;
					continue;
				}

				// TODO: expand frame with zero padding
				var pngData = frame.Data;
				if (frameSize != frame.Size)
				{
					var width = Math.Min(frame.Size.Width, frameSize.Width - offset.X);
					var height = Math.Min(frame.Size.Height, frameSize.Height - offset.Y);
					pngData = new byte[frameSize.Width * frameSize.Height];
					for (var h = 0; h < height; h++)
						Array.Copy(
							frame.Data, h * frame.Size.Width,
							pngData, (h + offset.Y) * frameSize.Width + offset.X,
							width);
				}

				var png = new Png(pngData, SpriteFrameType.Indexed8, frameSize.Width, frameSize.Height, palColors);
				png.Save($"{prefix}-{count++:D4}.png");
			}

			Console.WriteLine("Saved {0}-[0..{1}].png", prefix, count - 1);
		}
	}
}
