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

using OpenRA.Graphics;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
	[Desc("Fades the world from/to black at the start/end of the game, and can (optionally) desaturate the world")]
	public class MenuPostProcessEffectInfo : TraitInfo
	{
		public readonly int FadeInLength = 10;

		[Desc("Time (in ticks) to fade between states")]
		public readonly int FadeLength = 10;

		[Desc("Effect style to fade to during gameplay. Accepts values of None or Desaturated.")]
		public readonly MenuPostProcessEffect.EffectType Effect = MenuPostProcessEffect.EffectType.None;

		[Desc("Effect style to fade from when starting the game. Accepts values of None, Black or Desaturated.")]
		public readonly MenuPostProcessEffect.EffectType GameStartEffect = MenuPostProcessEffect.EffectType.Black;

		[Desc("Effect style to fade to when exiting the game. Accepts values of None, Black or Desaturated.")]
		public readonly MenuPostProcessEffect.EffectType GameExitEffect = MenuPostProcessEffect.EffectType.Black;

		[Desc("Effect style to fade to when opening the in-game menu. Accepts values of None, Black or Desaturated.")]
		public readonly MenuPostProcessEffect.EffectType MenuEffect = MenuPostProcessEffect.EffectType.None;

		public override object Create(ActorInitializer init) { return new MenuPostProcessEffect(this); }
	}

	public class MenuPostProcessEffect : RenderPostProcessPassBase, INotifyGameLoaded, IPostWorldLoaded
	{
		public enum EffectType { None, Black, Desaturated }
		public readonly MenuPostProcessEffectInfo Info;

		EffectType from;
		EffectType to;

		long startTime;
		long endTime;

		public MenuPostProcessEffect(MenuPostProcessEffectInfo info)
			: base("menufade", PostProcessPassType.AfterShroud)
		{
			Info = info;
			to = info.GameStartEffect;
		}

		public void Fade(EffectType type, int length = -1)
		{
			if (length < 0)
				length = Info.FadeLength;

			startTime = Game.RunTime;
			endTime = startTime + Ui.Timestep * length;

			from = to;
			to = type;
		}

		protected override bool Enabled => to != EffectType.None || (from != EffectType.None && Game.RunTime < endTime);

		protected override void PrepareRender(WorldRenderer wr, IShader shader)
		{
			var blend = (endTime - Game.RunTime) * 1f / (endTime - startTime);
			if (blend < 0)
				blend = startTime = endTime = 0;

			shader.SetVec("From", (int)from);
			shader.SetVec("To", (int)to);
			shader.SetVec("Blend", blend);
		}

		void IPostWorldLoaded.PostWorldLoaded(World w, WorldRenderer wr)
		{
			// HACK: Defer fade-in until the GameLoaded notification for game saves
			if (!w.IsLoadingGameSave)
				Fade(Info.Effect, Info.FadeInLength);
		}

		void INotifyGameLoaded.GameLoaded(World world)
		{
			// HACK: Let the menu opening trigger the fade for game saves
			// to avoid glitches resulting from trying to trigger both
			// the standard and menu fades at the same time
			if (world.IsReplay)
				Fade(Info.Effect, Info.FadeInLength);
		}
	}
}
