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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.EditorWorld)]
	[Desc("Required for the map editor to work. Attach this to the world actor.")]
	public class EditorResourceLayerInfo : TraitInfo, IResourceLayerInfo
	{
		[FieldLoader.LoadUsing(nameof(LoadResourceTypes))]
		public readonly Dictionary<string, ResourceLayerInfo.ResourceTypeInfo> ResourceTypes = null;

		// Copied from ResourceLayerInfo
		protected static object LoadResourceTypes(MiniYaml yaml)
		{
			var ret = new Dictionary<string, ResourceLayerInfo.ResourceTypeInfo>();
			var resources = yaml.NodeWithKeyOrDefault("ResourceTypes");
			if (resources != null)
				foreach (var r in resources.Value.Nodes)
					ret[r.Key] = new ResourceLayerInfo.ResourceTypeInfo(r.Value);

			return ret;
		}

		[Desc("Override the density saved in maps with values calculated based on the number of neighbouring resource cells.")]
		public readonly bool RecalculateResourceDensity = false;

		bool IResourceLayerInfo.TryGetTerrainType(string resourceType, out string terrainType)
		{
			if (resourceType == null || !ResourceTypes.TryGetValue(resourceType, out var resourceInfo))
			{
				terrainType = null;
				return false;
			}

			terrainType = resourceInfo.TerrainType;
			return true;
		}

		bool IResourceLayerInfo.TryGetResourceIndex(string resourceType, out byte index)
		{
			if (resourceType == null || !ResourceTypes.TryGetValue(resourceType, out var resourceInfo))
			{
				index = 0;
				return false;
			}

			index = resourceInfo.ResourceIndex;
			return true;
		}

		public override object Create(ActorInitializer init) { return new EditorResourceLayer(init.Self, this); }
	}

	public class EditorResourceLayer : IResourceLayer, IWorldLoaded, INotifyActorDisposing
	{
		readonly EditorResourceLayerInfo info;
		protected readonly Map Map;
		protected readonly Dictionary<byte, string> ResourceTypesByIndex;
		protected readonly CellLayer<ResourceLayerContents> Tiles;
		protected Dictionary<string, int> resourceValues;

		public int NetWorth { get; protected set; }

		bool disposed;

		public event Action<CPos, string> CellChanged;

		ResourceLayerContents IResourceLayer.GetResource(CPos cell) { return Tiles.Contains(cell) ? Tiles[cell] : default; }
		int IResourceLayer.GetMaxDensity(string resourceType)
		{
			return info.ResourceTypes.TryGetValue(resourceType, out var resourceInfo) ? resourceInfo.MaxDensity : 0;
		}

		bool IResourceLayer.CanAddResource(string resourceType, CPos cell, int amount) { return CanAddResource(resourceType, cell, amount); }
		int IResourceLayer.AddResource(string resourceType, CPos cell, int amount) { return AddResource(resourceType, cell, amount); }
		int IResourceLayer.RemoveResource(string resourceType, CPos cell, int amount) { return RemoveResource(resourceType, cell, amount); }
		void IResourceLayer.ClearResources(CPos cell) { ClearResources(cell); }
		bool IResourceLayer.IsVisible(CPos cell) { return Map.Contains(cell); }
		bool IResourceLayer.IsEmpty => false;
		IResourceLayerInfo IResourceLayer.Info => info;

		public EditorResourceLayer(Actor self, EditorResourceLayerInfo info)
		{
			if (self.World.Type != WorldType.Editor)
				return;

			this.info = info;
			Map = self.World.Map;
			Tiles = new CellLayer<ResourceLayerContents>(Map);
			ResourceTypesByIndex = info.ResourceTypes.ToDictionary(
				kv => kv.Value.ResourceIndex,
				kv => kv.Key);

			Map.Resources.CellEntryChanged += UpdateCell;
		}

		public void WorldLoaded(World w, WorldRenderer wr)
		{
			if (w.Type != WorldType.Editor)
				return;

			var playerResourcesInfo = w.Map.Rules.Actors[SystemActors.Player].TraitInfoOrDefault<PlayerResourcesInfo>();
			resourceValues = playerResourcesInfo?.ResourceValues ?? [];

			foreach (var cell in Map.AllCells)
				UpdateCell(cell);
		}

		public void UpdateCell(CPos cell)
		{
			var uv = cell.ToMPos(Map);
			if (!Map.Resources.Contains(uv))
				return;

			var tile = Map.Resources[uv];
			var t = Tiles[uv];

			var newTile = ResourceLayerContents.Empty;
			var newTerrain = byte.MaxValue;
			if (ResourceTypesByIndex.TryGetValue(tile.Type, out var resourceType) && info.ResourceTypes.TryGetValue(resourceType, out var resourceInfo))
			{
				newTile = new ResourceLayerContents(resourceType, CalculateCellDensity(new ResourceLayerContents(resourceType, tile.Index), cell));
				newTerrain = Map.Rules.TerrainInfo.GetTerrainIndex(resourceInfo.TerrainType);
			}

			// Nothing has changed
			if (newTile.Type == t.Type && newTile.Density == t.Density)
				return;

			UpdateNetWorth(t.Type, t.Density, newTile.Type, newTile.Density);
			Tiles[uv] = newTile;
			Map.CustomTerrain[uv] = newTerrain;
			CellChanged?.Invoke(cell, newTile.Type);

			if (!info.RecalculateResourceDensity)
				return;

			// Update neighbour density to account for this cell
			foreach (var d in CVec.Directions)
			{
				var neighbouringCell = cell + d;
				if (!Tiles.Contains(neighbouringCell))
					continue;

				var neighbouringTile = Tiles[neighbouringCell];
				if (neighbouringTile.Type == null)
					continue;

				var density = CalculateCellDensity(neighbouringTile, neighbouringCell);
				if (neighbouringTile.Density == density)
					continue;

				UpdateNetWorth(neighbouringTile.Type, neighbouringTile.Density, neighbouringTile.Type, density);
				Tiles[neighbouringCell] = new ResourceLayerContents(neighbouringTile.Type, density);

				CellChanged?.Invoke(neighbouringCell, neighbouringTile.Type);
			}
		}

		void UpdateNetWorth(string oldResourceType, int oldDensity, string newResourceType, int newDensity)
		{
			if (oldResourceType != null && oldDensity > 0 && resourceValues.TryGetValue(oldResourceType, out var oldResourceValue))
				NetWorth -= oldDensity * oldResourceValue;

			if (newResourceType != null && newDensity > 0 && resourceValues.TryGetValue(newResourceType, out var newResourceValue))
				NetWorth += newDensity * newResourceValue;
		}

		public int CalculateRegionValue(CellRegion sourceRegion)
		{
			var resourceValueInRegion = 0;
			foreach (var cell in sourceRegion.CellCoords)
			{
				var mcell = cell.ToMPos(Map);
				if (!Map.Resources.Contains(mcell))
					continue;

				var resource = Map.Resources[mcell].Type;
				if (resource != 0
					&& ResourceTypesByIndex.TryGetValue(resource, out var resourceType)
					&& resourceValues.TryGetValue(resourceType, out var resourceValuePerUnit))
					resourceValueInRegion += Tiles[mcell].Density * resourceValuePerUnit;
			}

			return resourceValueInRegion;
		}

		/// <summary>
		/// Matches the logic in <see cref="ResourceLayer"/> trait.
		/// </summary>
		protected virtual int CalculateCellDensity(ResourceLayerContents contents, CPos cell)
		{
			var resources = Map.Resources;
			if (contents.Type == null || !info.ResourceTypes.TryGetValue(contents.Type, out var resourceInfo) || resources[cell].Type != resourceInfo.ResourceIndex)
				return 0;

			if (!info.RecalculateResourceDensity)
				return contents.Density.Clamp(1, resourceInfo.MaxDensity);

			// Set density based on the number of neighboring resources.
			var adjacent = 0;
			var directions = CVec.Directions;
			for (var i = 0; i < directions.Length; i++)
			{
				var c = cell + directions[i];
				if (resources.Contains(c) && resources[c].Type == resourceInfo.ResourceIndex)
					++adjacent;
			}

			// We need to have at least one resource in the cell.
			// HACK: we should not be lerping to 9, as maximum adjacent resources is 8.
			// HACK: it's too disruptive to fix.
			return Math.Max(int2.Lerp(0, resourceInfo.MaxDensity, adjacent, 9), 1);
		}

		protected virtual bool AllowResourceAt(string resourceType, CPos cell)
		{
			if (!Map.Ramp.Contains(cell) || Map.Ramp[cell] != 0)
				return false;

			if (!info.ResourceTypes.TryGetValue(resourceType, out var resourceInfo))
				return false;

			// Ignore custom terrain types when spawning resources in the editor
			var terrainInfo = Map.Rules.TerrainInfo;
			var terrainType = terrainInfo.TerrainTypes[terrainInfo.GetTerrainInfo(Map.Tiles[cell]).TerrainType].Type;

			// TODO: Check against actors in the EditorActorLayer
			return resourceInfo.AllowedTerrainTypes.Contains(terrainType);
		}

		bool CanAddResource(string resourceType, CPos cell, int amount = 1)
		{
			var resources = Map.Resources;
			if (!resources.Contains(cell))
				return false;

			if (!info.ResourceTypes.TryGetValue(resourceType, out var resourceInfo))
				return false;

			// The editor allows the user to replace one resource type with another, so treat mismatching resource type as an empty cell
			var content = resources[cell];
			if (content.Type != resourceInfo.ResourceIndex)
				return amount <= resourceInfo.MaxDensity && AllowResourceAt(resourceType, cell);

			var oldDensity = content.Type == resourceInfo.ResourceIndex ? content.Index : 0;
			return oldDensity + amount <= resourceInfo.MaxDensity;
		}

		protected virtual int AddResource(string resourceType, CPos cell, int amount = 1)
		{
			var resources = Map.Resources;
			if (!resources.Contains(cell))
				return 0;

			if (!info.ResourceTypes.TryGetValue(resourceType, out var resourceInfo))
				return 0;

			// The editor allows the user to replace one resource type with another, so treat mismatching resource type as an empty cell
			var content = resources[cell];
			var oldDensity = content.Type == resourceInfo.ResourceIndex ? content.Index : 0;
			var density = (byte)Math.Min(resourceInfo.MaxDensity, oldDensity + amount);
			Map.Resources[cell] = new ResourceTile(resourceInfo.ResourceIndex, density);

			return density - oldDensity;
		}

		protected virtual int RemoveResource(string resourceType, CPos cell, int amount = 1)
		{
			var resources = Map.Resources;
			if (!resources.Contains(cell))
				return 0;

			if (!info.ResourceTypes.TryGetValue(resourceType, out var resourceInfo))
				return 0;

			var content = resources[cell];
			if (content.Type == 0 || content.Type != resourceInfo.ResourceIndex)
				return 0;

			var oldDensity = content.Index;
			var density = (byte)Math.Max(0, oldDensity - amount);
			resources[cell] = density > 0 ? new ResourceTile(resourceInfo.ResourceIndex, density) : default;

			return oldDensity - density;
		}

		protected virtual void ClearResources(CPos cell)
		{
			Map.Resources[cell] = default;
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			if (disposed)
				return;

			Map.Resources.CellEntryChanged -= UpdateCell;

			disposed = true;
		}
	}
}
