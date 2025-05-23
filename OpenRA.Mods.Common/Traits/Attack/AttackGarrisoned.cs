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
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class FirePort
	{
		public WVec Offset;
		public WAngle Yaw;
		public WAngle Cone;
	}

	[Desc("Cargo can fire their weapons out of fire ports.")]
	public class AttackGarrisonedInfo : AttackFollowInfo, IRulesetLoaded, Requires<CargoInfo>
	{
		[FieldLoader.Require]
		[Desc("Fire port offsets in local coordinates.")]
		public readonly WVec[] PortOffsets = null;

		[FieldLoader.Require]
		[Desc("Fire port yaw angles.")]
		public readonly WAngle[] PortYaws = null;

		[FieldLoader.Require]
		[Desc("Fire port yaw cone angle.")]
		public readonly WAngle[] PortCones = null;

		public FirePort[] Ports { get; private set; }

		[PaletteReference]
		public readonly string MuzzlePalette = "effect";

		public override object Create(ActorInitializer init) { return new AttackGarrisoned(init.Self, this); }
		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (PortOffsets.Length == 0)
				throw new YamlException("PortOffsets must have at least one entry.");

			if (PortYaws.Length != PortOffsets.Length)
				throw new YamlException("PortYaws must define an angle for each port.");

			if (PortCones.Length != PortOffsets.Length)
				throw new YamlException("PortCones must define an angle for each port.");

			Ports = new FirePort[PortOffsets.Length];

			for (var i = 0; i < PortOffsets.Length; i++)
			{
				Ports[i] = new FirePort
				{
					Offset = PortOffsets[i],
					Yaw = PortYaws[i],
					Cone = PortCones[i],
				};
			}

			base.RulesetLoaded(rules, ai);
		}
	}

	public class AttackGarrisoned : AttackFollow, INotifyPassengerEntered, INotifyPassengerExited, IRender
	{
		public new readonly AttackGarrisonedInfo Info;
		INotifyAttack[] notifyAttacks;
		readonly Lazy<BodyOrientation> coords;
		readonly List<Armament> armaments;
		readonly List<AnimationWithOffset> muzzles;
		readonly Dictionary<Actor, IFacing> paxFacing;
		readonly Dictionary<Actor, IPositionable> paxPos;
		readonly Dictionary<Actor, RenderSprites> paxRender;

		public AttackGarrisoned(Actor self, AttackGarrisonedInfo info)
			: base(self, info)
		{
			Info = info;
			coords = Exts.Lazy(self.Trait<BodyOrientation>);
			armaments = [];
			muzzles = [];
			paxFacing = [];
			paxPos = [];
			paxRender = [];
		}

		protected override void Created(Actor self)
		{
			notifyAttacks = self.TraitsImplementing<INotifyAttack>().ToArray();
			base.Created(self);
		}

		protected override Func<IEnumerable<Armament>> InitializeGetArmaments(Actor self)
		{
			return () => armaments;
		}

		void INotifyPassengerEntered.OnPassengerEntered(Actor self, Actor passenger)
		{
			paxFacing.Add(passenger, passenger.Trait<IFacing>());
			paxPos.Add(passenger, passenger.Trait<IPositionable>());
			paxRender.Add(passenger, passenger.Trait<RenderSprites>());

			foreach (var a in passenger.TraitsImplementing<Armament>())
			{
				if (Info.Armaments.Contains(a.Info.Name))
				{
					a.AddNotifyAttacks(self, notifyAttacks);
					armaments.Add(a);
				}
			}
		}

		void INotifyPassengerExited.OnPassengerExited(Actor self, Actor passenger)
		{
			paxFacing.Remove(passenger);
			paxPos.Remove(passenger);
			paxRender.Remove(passenger);

			foreach (var a in armaments.ToList())
			{
				if (a.Actor == passenger)
				{
					a.RemoveNotifyAttacks(notifyAttacks);
					armaments.Remove(a);
				}
			}
		}

		FirePort SelectFirePort(Actor self, WAngle targetYaw)
		{
			// Pick a random port that faces the target
			var bodyYaw = facing != null ? facing.Facing : WAngle.Zero;
			var indices = Enumerable.Range(0, Info.Ports.Length).Shuffle(self.World.SharedRandom);
			foreach (var i in indices)
			{
				var yaw = bodyYaw + Info.Ports[i].Yaw;
				var leftTurn = (yaw - targetYaw).Angle;
				var rightTurn = (targetYaw - yaw).Angle;
				if (Math.Min(leftTurn, rightTurn) <= Info.Ports[i].Cone.Angle)
					return Info.Ports[i];
			}

			return null;
		}

		WVec PortOffset(Actor self, FirePort p)
		{
			var bodyOrientation = coords.Value.QuantizeOrientation(self.Orientation);
			return coords.Value.LocalToWorld(p.Offset.Rotate(bodyOrientation));
		}

		public override void DoAttack(Actor self, in Target target)
		{
			if (!CanAttack(self, target))
				return;

			var pos = self.CenterPosition;
			var targetedPosition = GetTargetPosition(pos, target);
			var targetYaw = (targetedPosition - pos).Yaw;

			foreach (var a in Armaments)
			{
				if (a.IsTraitDisabled)
					continue;

				var port = SelectFirePort(self, targetYaw);
				if (port == null)
					return;

				paxFacing[a.Actor].Facing = targetYaw;
				paxPos[a.Actor].SetCenterPosition(a.Actor, pos + PortOffset(self, port));

				if (!a.CheckFire(a.Actor, facing, target))
					continue;

				if (a.Info.MuzzleSequence != null)
				{
					// Muzzle facing is fixed once the firing starts
					var muzzleAnim = new Animation(self.World, paxRender[a.Actor].GetImage(a.Actor), () => targetYaw);
					var sequence = a.Info.MuzzleSequence;
					var muzzleFlash = new AnimationWithOffset(muzzleAnim,
						() => PortOffset(self, port),
						() => false,
						p => RenderUtils.ZOffsetFromCenter(self, p, 1024));

					muzzles.Add(muzzleFlash);
					muzzleAnim.PlayThen(sequence, () => muzzles.Remove(muzzleFlash));
				}
			}
		}

		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			var pal = wr.Palette(Info.MuzzlePalette);

			// Display muzzle flashes
			foreach (var m in muzzles)
				foreach (var r in m.Render(self, pal))
					yield return r;
		}

		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			// Muzzle flashes don't contribute to actor bounds
			yield break;
		}

		protected override void Tick(Actor self)
		{
			base.Tick(self);

			// Take a copy so that Tick() can remove animations
			foreach (var m in muzzles.ToArray())
				m.Animation.Tick();
		}
	}
}
