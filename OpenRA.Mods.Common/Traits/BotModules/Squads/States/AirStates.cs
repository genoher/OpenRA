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
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Squads
{
	abstract class AirStateBase : StateBase
	{
		protected const int MissileUnitMultiplier = 3;

		protected static int CountAntiAirUnits(Squad owner, IReadOnlyCollection<Actor> units)
		{
			if (units.Count == 0)
				return 0;

			var missileUnitsCount = 0;
			foreach (var unit in units)
			{
				if (unit == null || unit.Info.HasTraitInfo<AircraftInfo>())
					continue;

				foreach (var ab in unit.TraitsImplementing<AttackBase>())
				{
					if (ab.IsTraitDisabled || ab.IsTraitPaused)
						continue;

					foreach (var a in ab.Armaments)
					{
						if (a.Weapon.IsValidTarget(owner.SquadManager.Info.AircraftTargetType))
						{
							missileUnitsCount++;
							break;
						}
					}
				}
			}

			return missileUnitsCount;
		}

		protected static Actor FindDefenselessTarget(Squad owner)
		{
			FindSafePlace(owner, out var target, true);
			return target;
		}

		protected static CPos? FindSafePlace(Squad owner, out Actor detectedEnemyTarget, bool needTarget)
		{
			var map = owner.World.Map;
			var dangerRadius = owner.SquadManager.Info.DangerScanRadius;
			detectedEnemyTarget = null;

			var columnCount = (map.MapSize.Width + dangerRadius - 1) / dangerRadius;
			var rowCount = (map.MapSize.Height + dangerRadius - 1) / dangerRadius;

			var checkIndices = Exts.MakeArray(columnCount * rowCount, i => i).Shuffle(owner.World.LocalRandom);
			foreach (var i in checkIndices)
			{
				var pos = new MPos(i % columnCount * dangerRadius + dangerRadius / 2, i / columnCount * dangerRadius + dangerRadius / 2).ToCPos(map);

				if (NearToPosSafely(owner, map.CenterOfCell(pos), out detectedEnemyTarget))
				{
					if (needTarget && detectedEnemyTarget == null)
						continue;

					return pos;
				}
			}

			return null;
		}

		protected static bool NearToPosSafely(Squad owner, WPos loc)
		{
			return NearToPosSafely(owner, loc, out _);
		}

		protected static bool NearToPosSafely(Squad owner, WPos loc, out Actor detectedEnemyTarget)
		{
			detectedEnemyTarget = null;
			var dangerRadius = owner.SquadManager.Info.DangerScanRadius;
			var unitsAroundPos = owner.World.FindActorsInCircle(loc, WDist.FromCells(dangerRadius))
				.Where(owner.SquadManager.IsPreferredEnemyUnit).ToList();

			if (unitsAroundPos.Count == 0)
				return true;

			if (CountAntiAirUnits(owner, unitsAroundPos) * MissileUnitMultiplier < owner.Units.Count)
			{
				detectedEnemyTarget = unitsAroundPos.Random(owner.Random);
				return true;
			}

			return false;
		}

		// Checks the number of anti air enemies around units
		protected virtual bool ShouldFlee(Squad owner)
		{
			return ShouldFlee(owner, enemies => CountAntiAirUnits(owner, enemies) * MissileUnitMultiplier > owner.Units.Count);
		}
	}

	sealed class AirIdleState : AirStateBase, IState
	{
		public void Activate(Squad owner) { }

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			if (ShouldFlee(owner))
			{
				owner.FuzzyStateMachine.ChangeState(owner, new AirFleeState());
				return;
			}

			var e = FindDefenselessTarget(owner);
			if (e == null)
				return;

			owner.SetActorToTarget((e, WVec.Zero));
			owner.FuzzyStateMachine.ChangeState(owner, new AirAttackState());
		}

		public void Deactivate(Squad owner) { }
	}

	sealed class AirAttackState : AirStateBase, IState
	{
		public void Activate(Squad owner) { }

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			var leader = owner.CenterUnit();
			if (!owner.IsTargetValid(leader))
			{
				var closestEnemy = owner.SquadManager.FindClosestEnemy(leader);
				owner.SetActorToTarget(closestEnemy);
				if (closestEnemy.Actor == null)
				{
					owner.FuzzyStateMachine.ChangeState(owner, new AirFleeState());
					return;
				}
			}

			if (!NearToPosSafely(owner, owner.Target.CenterPosition))
			{
				owner.FuzzyStateMachine.ChangeState(owner, new AirFleeState());
				return;
			}

			foreach (var a in owner.Units)
			{
				if (BusyAttack(a))
					continue;

				var ammoPools = a.TraitsImplementing<AmmoPool>().ToArray();
				if (!ReloadsAutomatically(ammoPools, a.TraitOrDefault<Rearmable>()))
				{
					if (IsRearming(a))
						continue;

					if (!HasAmmo(ammoPools))
					{
						owner.Bot.QueueOrder(new Order("ReturnToBase", a, false));
						continue;
					}
				}

				if (CanAttackTarget(a, owner.TargetActor))
					owner.Bot.QueueOrder(new Order("Attack", a, owner.Target, false));
			}
		}

		public void Deactivate(Squad owner) { }
	}

	sealed class AirFleeState : AirStateBase, IState
	{
		public void Activate(Squad owner) { }

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			foreach (var a in owner.Units)
			{
				var ammoPools = a.TraitsImplementing<AmmoPool>().ToArray();
				if (!ReloadsAutomatically(ammoPools, a.TraitOrDefault<Rearmable>()) && !FullAmmo(ammoPools))
				{
					if (IsRearming(a))
						continue;

					owner.Bot.QueueOrder(new Order("ReturnToBase", a, false));
					continue;
				}

				owner.Bot.QueueOrder(new Order("Move", a, Target.FromCell(owner.World, RandomBuildingLocation(owner)), false));
			}

			owner.FuzzyStateMachine.ChangeState(owner, new AirIdleState());
		}

		public void Deactivate(Squad owner) { }
	}
}
