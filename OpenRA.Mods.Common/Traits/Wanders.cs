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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Wanders around aimlessly while idle.")]
	public class WandersInfo : ConditionalTraitInfo, Requires<IMoveInfo>
	{
		public readonly int WanderMoveRadius = 1;

		[Desc("Number of ticks to wait before decreasing the effective move radius.")]
		public readonly int ReduceMoveRadiusDelay = 5;

		[Desc("Minimum amount of ticks the actor will sit idly before starting to wander.")]
		public readonly int MinMoveDelay = 0;

		[Desc("Maximum amount of ticks the actor will sit idly before starting to wander.")]
		public readonly int MaxMoveDelay = 0;

		[Desc("The terrain types that this actor should avoid wandering on to.")]
		public readonly HashSet<string> AvoidTerrainTypes = [];

		public override object Create(ActorInitializer init) { return new Wanders(init.Self, this); }
	}

	public class Wanders : ConditionalTrait<WandersInfo>, INotifyIdle, INotifyBecomingIdle
	{
		readonly WandersInfo info;
		readonly IMoveInfo moveInfo;
		protected readonly IMove Move;

		int countdown;
		int ticksIdle;
		int effectiveMoveRadius;

		public Wanders(Actor self, WandersInfo info)
			: base(info)
		{
			this.info = info;
			effectiveMoveRadius = info.WanderMoveRadius;
			countdown = self.World.SharedRandom.Next(info.MinMoveDelay, info.MaxMoveDelay);
			Move = self.Trait<IMove>();
			moveInfo = self.Info.TraitInfo<IMoveInfo>();
		}

		protected virtual void OnBecomingIdle(Actor self)
		{
			countdown = self.World.SharedRandom.Next(info.MinMoveDelay, info.MaxMoveDelay);
		}

		void INotifyBecomingIdle.OnBecomingIdle(Actor self)
		{
			OnBecomingIdle(self);
		}

		protected virtual void TickIdle(Actor self)
		{
			if (IsTraitDisabled)
				return;

			if (--countdown > 0)
				return;

			var targetCell = PickTargetLocation(self);
			if (targetCell.HasValue)
				DoAction(self, targetCell.Value);
		}

		void INotifyIdle.TickIdle(Actor self)
		{
			TickIdle(self);
		}

		CPos? PickTargetLocation(Actor self)
		{
			var target = self.CenterPosition + new WVec(0, -1024 * effectiveMoveRadius, 0).Rotate(WRot.FromFacing(self.World.SharedRandom.Next(255)));
			var targetCell = self.World.Map.CellContaining(target);

			if (!self.World.Map.Contains(targetCell))
			{
				// If MoveRadius is too big there might not be a valid cell to order the attack to (if actor is on a small island and can't leave)
				if (++ticksIdle % info.ReduceMoveRadiusDelay == 0)
					effectiveMoveRadius--;

				// We'll be back the next tick; better to sit idle for a few seconds than prolong this tick indefinitely with a loop
				return null;
			}

			if (info.AvoidTerrainTypes.Count > 0)
			{
				var terrainType = self.World.Map.GetTerrainInfo(targetCell).Type;
				if (Info.AvoidTerrainTypes.Contains(terrainType))
					return null;
			}

			ticksIdle = 0;
			effectiveMoveRadius = info.WanderMoveRadius;

			return targetCell;
		}

		public virtual void DoAction(Actor self, CPos targetCell)
		{
			self.QueueActivity(Move.MoveTo(targetCell, targetLineColor: moveInfo.GetTargetLineColor()));
		}
	}
}
