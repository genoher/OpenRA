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

using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Displays the player name above the unit")]
	public class WithNameTagDecorationInfo : WithDecorationBaseInfo
	{
		public readonly int MaxLength = 10;

		public readonly string Font = "TinyBold";

		[Desc("Display in this color when not using the player color.")]
		public readonly Color Color = Color.White;

		[Desc("Use the player color of the current owner.")]
		public readonly bool UsePlayerColor = false;

		public override object Create(ActorInitializer init) { return new WithNameTagDecoration(init.Self, this); }

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (!Game.ModData.Manifest.Get<Fonts>().FontList.ContainsKey(Font))
				throw new YamlException($"Font '{Font}' is not listed in the mod.yaml's Fonts section");

			base.RulesetLoaded(rules, ai);
		}
	}

	public class WithNameTagDecoration : WithDecorationBase<WithNameTagDecorationInfo>, INotifyOwnerChanged
	{
		readonly SpriteFont font;
		readonly WithNameTagDecorationInfo info;
		string name;

		public WithNameTagDecoration(Actor self, WithNameTagDecorationInfo info)
			: base(self, info)
		{
			font = Game.Renderer.Fonts[info.Font];
			this.info = info;

			name = self.Owner.ResolvedPlayerName;
			if (name.Length > info.MaxLength)
				name = name[..info.MaxLength];
		}

		protected override IEnumerable<IRenderable> RenderDecoration(Actor self, WorldRenderer wr, int2 screenPos)
		{
			if (IsTraitDisabled || self.IsDead || !self.IsInWorld || !ShouldRender(self))
				return [];

			var size = font.Measure(name);
			return
			[
				new UITextRenderable(font, self.CenterPosition, screenPos - size / 2, 0, info.UsePlayerColor ? self.OwnerColor() : info.Color, name)
			];
		}

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			name = self.Owner.ResolvedPlayerName;
			if (name.Length > Info.MaxLength)
				name = name[..Info.MaxLength];
		}
	}
}
