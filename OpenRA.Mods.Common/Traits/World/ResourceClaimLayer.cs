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

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Allows harvesters to coordinate their operations. Attach this to the world actor.")]
	public sealed class ResourceClaimLayerInfo : TraitInfo<ResourceClaimLayer> { }

	public sealed class ResourceClaimLayer
	{
		readonly Dictionary<CPos, List<Actor>> claimByCell = new(32);
		readonly Dictionary<Actor, CPos> claimByActor = new(32);

		/// <summary>
		/// Attempt to reserve the resource in a cell for the given actor.
		/// </summary>
		public bool TryClaimCell(Actor claimer, CPos cell)
		{
			if (claimByCell.TryGetValue(cell, out var claimers))
			{
				// Clean up any stale claims
				claimers.RemoveAll(a => a.IsDead);

				// Prevent harvesters from the player or their allies fighting over the same cell
				if (claimers.Any(c => c != claimer && claimer.Owner.IsAlliedWith(c.Owner)))
					return false;
			}

			// Remove the actor's last claim, if it has one
			if (claimByActor.TryGetValue(claimer, out var lastClaim) &&
				claimByCell.TryGetValue(lastClaim, out var lastClaimers))
				lastClaimers.Remove(claimer);

			if (claimers == null)
				claimByCell.Add(cell, claimers = []);
			claimByActor[claimer] = cell;
			return true;
		}

		/// <summary>
		/// Returns false if the cell is already reserved by an allied actor.
		/// </summary>
		public bool CanClaimCell(Actor claimer, CPos cell)
		{
			return !claimByCell.TryGetValue(cell, out var claimers) ||
				!claimers.Any(c => c != claimer && !c.IsDead && claimer.Owner.IsAlliedWith(c.Owner));
		}

		/// <summary>
		/// Release the last resource claim made by this actor.
		/// </summary>
		public void RemoveClaim(Actor claimer)
		{
			if (claimByActor.TryGetValue(claimer, out var lastClaim) &&
				claimByCell.TryGetValue(lastClaim, out var lastClaimers))
				lastClaimers.Remove(claimer);

			claimByActor.Remove(claimer);
		}
	}
}
