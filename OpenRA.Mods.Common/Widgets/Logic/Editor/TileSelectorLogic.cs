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
using System.Globalization;
using System.IO;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	[IncludeStaticFluentReferences(
		typeof(PaintTileEditorAction),
		typeof(FloodFillEditorAction),
		typeof(CommonSelectorLogic))]
	public class TileSelectorLogic : CommonSelectorLogic
	{
		sealed class TileSelectorTemplate
		{
			public readonly TerrainTemplateInfo Template;
			public readonly string[] Categories;
			public readonly string[] SearchTerms;
			public readonly string Tooltip;

			public TileSelectorTemplate(TerrainTemplateInfo template)
			{
				Template = template;
				Categories = template.Categories;
				Tooltip = template.Id.ToString(NumberFormatInfo.CurrentInfo);
				SearchTerms = [Tooltip];
			}
		}

		readonly ITemplatedTerrainInfo terrainInfo;
		readonly TileSelectorTemplate[] allTemplates;

		[ObjectCreator.UseCtor]
		public TileSelectorLogic(Widget widget, ModData modData, World world, WorldRenderer worldRenderer)
			: base(widget, modData, world, worldRenderer, "TILETEMPLATE_LIST", "TILEPREVIEW_TEMPLATE")
		{
			terrainInfo = world.Map.Rules.TerrainInfo as ITemplatedTerrainInfo;
			if (terrainInfo == null)
				throw new InvalidDataException("TileSelectorLogic requires a template-based tileset.");

			allTemplates = terrainInfo.Templates.Values.Select(t => new TileSelectorTemplate(t)).ToArray();

			allCategories = allTemplates.SelectMany(t => t.Categories)
				.Distinct()
				.OrderBy(CategoryOrder)
				.ToArray();

			foreach (var c in allCategories)
			{
				SelectedCategories.Add(c);
				FilteredCategories.Add(c);
			}

			SearchTextField.OnTextEdited = () =>
			{
				searchFilter = SearchTextField.Text.Trim();
				FilteredCategories.Clear();

				if (!string.IsNullOrEmpty(searchFilter))
					FilteredCategories.AddRange(
						allTemplates.Where(t => t.SearchTerms.Any(
							s => s.Contains(searchFilter, StringComparison.CurrentCultureIgnoreCase)))
						.SelectMany(t => t.Categories)
						.Distinct()
						.OrderBy(CategoryOrder));
				else
					FilteredCategories.AddRange(allCategories);

				InitializePreviews();
			};

			InitializePreviews();
		}

		int CategoryOrder(string category)
		{
			var i = terrainInfo.EditorTemplateOrder.IndexOf(category);
			return i >= 0 ? i : int.MaxValue;
		}

		protected override void InitializePreviews()
		{
			Panel.RemoveChildren();
			if (SelectedCategories.Count == 0)
				return;

			foreach (var t in allTemplates)
			{
				if (!SelectedCategories.Overlaps(t.Categories))
					continue;

				if (!string.IsNullOrEmpty(searchFilter) &&
					!t.SearchTerms.Any(s => s.Contains(searchFilter, StringComparison.CurrentCultureIgnoreCase)))
					continue;

				var tileId = t.Template.Id;
				var item = ScrollItemWidget.Setup(ItemTemplate,
					() => Editor.CurrentBrush is EditorTileBrush editorCursor && editorCursor.TerrainTemplate.Id == tileId,
					() => Editor.SetBrush(new EditorTileBrush(Editor, tileId, WorldRenderer)));

				var preview = item.Get<TerrainTemplatePreviewWidget>("TILE_PREVIEW");
				preview.SetTemplate(terrainInfo.Templates[tileId]);

				// Scale templates to fit within the panel
				var scale = 1f;
				if (scale * preview.IdealPreviewSize.X > ItemTemplate.Bounds.Width)
					scale = (ItemTemplate.Bounds.Width - Panel.ItemSpacing) / (float)preview.IdealPreviewSize.X;

				preview.GetScale = () => scale;
				preview.Bounds.Width = (int)(scale * preview.IdealPreviewSize.X);
				preview.Bounds.Height = (int)(scale * preview.IdealPreviewSize.Y);

				item.Bounds.Width = preview.Bounds.Width + 2 * preview.Bounds.X;
				item.Bounds.Height = preview.Bounds.Height + 2 * preview.Bounds.Y;
				item.IsVisible = () => true;
				item.GetTooltipText = () => t.Tooltip;

				Panel.AddChild(item);
			}
		}
	}
}
