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

namespace OpenRA.Traits
{
	[TraitLocation(SystemActors.Player | SystemActors.EditorPlayer)]
	[Desc("Required for shroud and fog visibility checks. Add this to the player actor.")]
	public class ShroudInfo : TraitInfo, ILobbyOptions
	{
		[FluentReference]
		[Desc("Descriptive label for the fog checkbox in the lobby.")]
		public readonly string FogCheckboxLabel = "checkbox-fog-of-war.label";

		[FluentReference]
		[Desc("Tooltip description for the fog checkbox in the lobby.")]
		public readonly string FogCheckboxDescription = "checkbox-fog-of-war.description";

		[Desc("Default value of the fog checkbox in the lobby.")]
		public readonly bool FogCheckboxEnabled = true;

		[Desc("Prevent the fog enabled state from being changed in the lobby.")]
		public readonly bool FogCheckboxLocked = false;

		[Desc("Whether to display the fog checkbox in the lobby.")]
		public readonly bool FogCheckboxVisible = true;

		[Desc("Display order for the fog checkbox in the lobby.")]
		public readonly int FogCheckboxDisplayOrder = 0;

		[FluentReference]
		[Desc("Descriptive label for the explored map checkbox in the lobby.")]
		public readonly string ExploredMapCheckboxLabel = "checkbox-explored-map.label";

		[FluentReference]
		[Desc("Tooltip description for the explored map checkbox in the lobby.")]
		public readonly string ExploredMapCheckboxDescription = "checkbox-explored-map.description";

		[Desc("Default value of the explore map checkbox in the lobby.")]
		public readonly bool ExploredMapCheckboxEnabled = false;

		[Desc("Prevent the explore map enabled state from being changed in the lobby.")]
		public readonly bool ExploredMapCheckboxLocked = false;

		[Desc("Whether to display the explore map checkbox in the lobby.")]
		public readonly bool ExploredMapCheckboxVisible = true;

		[Desc("Display order for the explore map checkbox in the lobby.")]
		public readonly int ExploredMapCheckboxDisplayOrder = 0;

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			yield return new LobbyBooleanOption(map, "explored", ExploredMapCheckboxLabel, ExploredMapCheckboxDescription,
				ExploredMapCheckboxVisible, ExploredMapCheckboxDisplayOrder, ExploredMapCheckboxEnabled, ExploredMapCheckboxLocked);
			yield return new LobbyBooleanOption(map, "fog", FogCheckboxLabel, FogCheckboxDescription,
				FogCheckboxVisible, FogCheckboxDisplayOrder, FogCheckboxEnabled, FogCheckboxLocked);
		}

		public override object Create(ActorInitializer init) { return new Shroud(init.Self, this); }
	}

	public class Shroud : ISync, INotifyCreated, ITick
	{
		public enum SourceType : byte { PassiveVisibility, Shroud, Visibility }
		public event Action<PPos> OnShroudChanged;
		public int RevealedCells { get; private set; }

		enum ShroudCellType : byte { Shroud, Fog, Visible }
		readonly record struct ShroudSource(SourceType Type, PPos[] ProjectedCells);

		// Visible is not a super set of Explored. IsExplored may return false even if IsVisible returns true.
		[Flags]
		public enum CellVisibility : byte { Hidden = 0x0, Explored = 0x1, Visible = 0x2 }

		readonly ShroudInfo info;
		readonly Map map;

		// Individual shroud modifier sources (type and area)
		readonly Dictionary<object, ShroudSource> sources = [];

		// Per-cell count of each source type, used to resolve the final cell type
		readonly ProjectedCellLayer<short> passiveVisibleCount;
		readonly ProjectedCellLayer<short> visibleCount;
		readonly ProjectedCellLayer<short> generatedShroudCount;
		readonly ProjectedCellLayer<bool> explored;
		readonly ProjectedCellLayer<bool> touched;
		bool anyCellTouched;

		// Per-cell cache of the resolved cell type (shroud/fog/visible)
		readonly ProjectedCellLayer<ShroudCellType> resolvedType;

		bool disabledChanged;
		[Sync]
		bool disabled;
		public bool Disabled
		{
			get => disabled;

			set
			{
				if (disabled == value)
					return;

				disabled = value;
				disabledChanged = true;
			}
		}

		bool fogEnabled;
		public bool FogEnabled => !Disabled && fogEnabled;
		public bool ExploreMapEnabled { get; private set; }

		public int Hash { get; private set; }

		// Enabled at runtime on first use
		bool shroudGenerationEnabled;
		bool passiveVisibilityEnabled;

		public Shroud(Actor self, ShroudInfo info)
		{
			this.info = info;
			map = self.World.Map;

			passiveVisibleCount = new ProjectedCellLayer<short>(map);
			visibleCount = new ProjectedCellLayer<short>(map);
			generatedShroudCount = new ProjectedCellLayer<short>(map);
			explored = new ProjectedCellLayer<bool>(map);
			touched = new ProjectedCellLayer<bool>(map);
			anyCellTouched = true;

			// Defaults to 0 = Shroud
			resolvedType = new ProjectedCellLayer<ShroudCellType>(map);
		}

		void INotifyCreated.Created(Actor self)
		{
			var gs = self.World.LobbyInfo.GlobalSettings;
			fogEnabled = gs.OptionOrDefault("fog", info.FogCheckboxEnabled);

			ExploreMapEnabled = gs.OptionOrDefault("explored", info.ExploredMapCheckboxEnabled);
			if (ExploreMapEnabled)
				ExploreAll();

			if (!fogEnabled && ExploreMapEnabled)
				RevealedCells = map.ProjectedCells.Length;
		}

		void ITick.Tick(Actor self)
		{
			if (!anyCellTouched && !disabledChanged)
				return;

			anyCellTouched = false;

			if (OnShroudChanged == null)
			{
				disabledChanged = false;
				return;
			}

			// PERF: Parts of this loop are very hot.
			// We loop over the direct index that represents the PPos in
			// the ProjectedCellLayers, converting to a PPos only if
			// it is needed (which is the uncommon case.)
			if (disabledChanged)
			{
				touched.SetAll(false);
				var maxIndex = touched.MaxIndex;
				for (var index = 0; index < maxIndex; index++)
					UpdateCell(index, self);
			}
			else
			{
				// PERF: Most cells are unchanged, use IndexOf for fast vectorized search.
				var index = touched.IndexOf(true, 0);
				while (index != -1)
				{
					touched[index] = false;
					UpdateCell(index, self);
					index = touched.IndexOf(true, index + 1);
				}
			}

			Hash = Sync.HashPlayer(self.Owner) + self.World.WorldTick;
			disabledChanged = false;
		}

		void UpdateCell(int index, Actor self)
		{
			var type = ShroudCellType.Shroud;

			if (explored[index])
			{
				var count = visibleCount[index];
				if (!shroudGenerationEnabled || count > 0 || generatedShroudCount[index] == 0)
				{
					if (passiveVisibilityEnabled)
						count += passiveVisibleCount[index];

					type = count > 0 ? ShroudCellType.Visible : ShroudCellType.Fog;
				}
			}

			// PERF: Most cells are unchanged
			var oldResolvedType = resolvedType[index];
			if (type != oldResolvedType || disabledChanged)
			{
				resolvedType[index] = type;
				var puv = touched.PPosFromIndex(index);
				if (map.Contains(puv))
					OnShroudChanged(puv);

				if (!disabledChanged && (fogEnabled || !ExploreMapEnabled))
				{
					if (type == ShroudCellType.Visible)
						RevealedCells++;
					else if (fogEnabled && oldResolvedType == ShroudCellType.Visible)
						RevealedCells--;
				}

				if (self.Owner.WinState == WinState.Lost)
					RevealedCells = 0;
			}
		}

		public static IEnumerable<PPos> ProjectedCellsInRange(Map map, WPos pos, WDist minRange, WDist maxRange, int maxHeightDelta = -1)
		{
			// Account for potential extra half-cell from odd-height terrain
			var r = (maxRange.Length + 1023 + 512) / 1024;
			var minLimit = minRange.LengthSquared;
			var maxLimit = maxRange.LengthSquared;

			// Project actor position into the shroud plane
			var projectedPos = pos - new WVec(0, pos.Z, pos.Z);
			var projectedCell = map.CellContaining(projectedPos);
			var projectedHeight = pos.Z / 512;

			foreach (var c in map.FindTilesInAnnulus(projectedCell, minRange.Length / 1024, r, true))
			{
				var dist = (map.CenterOfCell(c) - projectedPos).HorizontalLengthSquared;
				if (dist <= maxLimit && (dist == 0 || dist > minLimit))
				{
					var puv = (PPos)c.ToMPos(map);
					if (maxHeightDelta < 0 || map.ProjectedHeight(puv) < projectedHeight + maxHeightDelta)
						yield return puv;
				}
			}
		}

		public static IEnumerable<PPos> ProjectedCellsInRange(Map map, CPos cell, WDist range, int maxHeightDelta = -1)
		{
			return ProjectedCellsInRange(map, map.CenterOfCell(cell), WDist.Zero, range, maxHeightDelta);
		}

		public void AddSource(object key, SourceType type, PPos[] projectedCells)
		{
			if (!sources.TryAdd(key, new ShroudSource(type, projectedCells)))
				throw new InvalidOperationException("Attempting to add duplicate shroud source");

			foreach (var puv in projectedCells)
			{
				// Force cells outside the visible bounds invisible
				if (!map.Contains(puv))
					continue;

				var index = touched.Index(puv);
				touched[index] = true;
				anyCellTouched = true;
				switch (type)
				{
					case SourceType.PassiveVisibility:
						passiveVisibilityEnabled = true;
						passiveVisibleCount[index]++;
						explored[index] = true;
						break;
					case SourceType.Visibility:
						visibleCount[index]++;
						explored[index] = true;
						break;
					case SourceType.Shroud:
						shroudGenerationEnabled = true;
						generatedShroudCount[index]++;
						break;
				}
			}
		}

		public void RemoveSource(object key)
		{
			if (!sources.Remove(key, out var state))
				return;

			foreach (var puv in state.ProjectedCells)
			{
				// Cells outside the visible bounds don't increment visibleCount
				if (map.Contains(puv))
				{
					var index = touched.Index(puv);
					touched[index] = true;
					anyCellTouched = true;
					switch (state.Type)
					{
						case SourceType.PassiveVisibility:
							passiveVisibleCount[index]--;
							break;
						case SourceType.Visibility:
							visibleCount[index]--;
							break;
						case SourceType.Shroud:
							generatedShroudCount[index]--;
							break;
					}
				}
			}
		}

		public void ExploreProjectedCells(IEnumerable<PPos> cells)
		{
			foreach (var puv in cells)
			{
				if (map.Contains(puv))
				{
					var index = touched.Index(puv);
					if (!explored[index])
					{
						touched[index] = true;
						anyCellTouched = true;
						explored[index] = true;
					}
				}
			}
		}

		public void Explore(Shroud s)
		{
			if (map.Bounds != s.map.Bounds)
				throw new ArgumentException("The map bounds of these shrouds do not match.", nameof(s));

			foreach (var puv in map.ProjectedCells)
			{
				var index = touched.Index(puv);
				if (!explored[index] && s.explored[index])
				{
					touched[index] = true;
					anyCellTouched = true;
					explored[index] = true;
				}
			}
		}

		public void ExploreAll()
		{
			foreach (var puv in map.ProjectedCells)
			{
				var index = touched.Index(puv);
				if (!explored[index])
				{
					touched[index] = true;
					anyCellTouched = true;
					explored[index] = true;
				}
			}
		}

		public void ResetExploration()
		{
			foreach (var puv in map.ProjectedCells)
			{
				var index = touched.Index(puv);
				touched[index] = true;
				explored[index] = visibleCount[index] + passiveVisibleCount[index] > 0;
			}

			anyCellTouched = true;
		}

		public bool IsExplored(WPos pos)
		{
			return IsExplored(map.ProjectedCellCovering(pos));
		}

		public bool IsExplored(CPos cell)
		{
			return IsExplored(cell.ToMPos(map));
		}

		public bool IsExplored(MPos uv)
		{
			if (!map.Contains(uv))
				return false;

			foreach (var puv in map.ProjectedCellsCovering(uv))
				if (IsExplored(puv))
					return true;

			return false;
		}

		public bool IsExplored(PPos puv)
		{
			if (Disabled)
				return map.Contains(puv);

			return resolvedType.Contains(puv) && resolvedType[puv] > ShroudCellType.Shroud;
		}

		public bool IsVisible(WPos pos)
		{
			return IsVisible(map.ProjectedCellCovering(pos));
		}

		public bool IsVisible(CPos cell)
		{
			return IsVisible(cell.ToMPos(map));
		}

		public bool IsVisible(MPos uv)
		{
			foreach (var puv in map.ProjectedCellsCovering(uv))
				if (IsVisible(puv))
					return true;

			return false;
		}

		// In internal shroud coords
		public bool IsVisible(PPos puv)
		{
			if (!FogEnabled)
				return map.Contains(puv);

			return resolvedType.Contains(puv) && resolvedType[puv] == ShroudCellType.Visible;
		}

		public bool Contains(PPos uv)
		{
			// Check that uv is inside the map area. There is nothing special
			// about explored here: any of the CellLayers would have been suitable.
			return explored.Contains(uv);
		}

		public CellVisibility GetVisibility(WPos pos)
		{
			return GetVisibility(map.ProjectedCellCovering(pos));
		}

		// PERF: Combine IsExplored and IsVisible.
		public CellVisibility GetVisibility(PPos puv)
		{
			var state = CellVisibility.Hidden;

			if (Disabled)
			{
				if (fogEnabled)
				{
					// Shroud disabled, Fog enabled
					if (resolvedType.Contains(puv))
					{
						state |= CellVisibility.Explored;

						if (resolvedType[puv] == ShroudCellType.Visible)
							state |= CellVisibility.Visible;
					}
				}
				else if (map.Contains(puv))
					state |= CellVisibility.Explored | CellVisibility.Visible;
			}
			else
			{
				if (fogEnabled)
				{
					// Shroud and Fog enabled
					if (resolvedType.Contains(puv))
					{
						var rt = resolvedType[puv];
						if (rt == ShroudCellType.Visible)
							state |= CellVisibility.Explored | CellVisibility.Visible;
						else if (rt > ShroudCellType.Shroud)
							state |= CellVisibility.Explored;
					}
				}
				else if (resolvedType.Contains(puv))
				{
					// We do not set Explored since IsExplored may return false.
					state |= CellVisibility.Visible;

					if (resolvedType[puv] > ShroudCellType.Shroud)
						state |= CellVisibility.Explored;
				}
			}

			return state;
		}
	}
}
