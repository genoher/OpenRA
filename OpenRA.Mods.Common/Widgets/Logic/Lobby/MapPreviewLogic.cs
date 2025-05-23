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
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class MapPreviewLogic : ChromeLogic
	{
		[FluentReference]
		const string Connecting = "label-connecting";

		[FluentReference("size")]
		const string Downloading = "label-downloading-map";

		[FluentReference("size", "progress")]
		const string DownloadingPercentage = "label-downloading-map-progress";

		[FluentReference]
		const string RetryInstall = "button-retry-install";

		[FluentReference]
		const string RetrySearch = "button-retry-search";

		[FluentReference("author")]
		const string CreatedBy = "label-created-by";

		readonly int blinkTickLength = 10;
		readonly Dictionary<PreviewStatus, Widget[]> previewWidgets = [];
		readonly Func<(MapPreview Map, Session.MapStatus Status)> getMap;

		enum PreviewStatus
		{
			Unknown,
			Playable,
			Incompatible,
			Validating,
			DownloadAvailable,
			Searching,
			Downloading,
			DownloadError,
			Unavailable,
			UpdateAvailable,
			UpdateDownloadAvailable,
		}

		PreviewStatus currentStatus;
		bool blink;
		int blinkTick;
		readonly ButtonWidget retryButton;
		bool mapUpdateAvailable = false;

		[ObjectCreator.UseCtor]
		internal MapPreviewLogic(Widget widget, ModData modData, Func<(MapPreview Map, Session.MapStatus Status)> getMap,
			Action<MapPreviewWidget, MapPreview, MouseInput> onMouseDown, Func<Dictionary<int, SpawnOccupant>> getSpawnOccupants,
			bool mapUpdatesEnabled, Action<string> onMapUpdate, Func<HashSet<int>> getDisabledSpawnPoints, bool showUnoccupiedSpawnpoints)
		{
			this.getMap = getMap;

			Widget SetupMapPreview(Widget parent)
			{
				var preview = parent.Get<MapPreviewWidget>("MAP_PREVIEW");
				preview.Preview = () => getMap().Map;
				if (onMouseDown != null)
					preview.OnMouseDown = mi => onMouseDown(preview, getMap().Map, mi);

				if (getSpawnOccupants != null)
					preview.SpawnOccupants = getSpawnOccupants;

				if (getDisabledSpawnPoints != null)
					preview.DisabledSpawnPoints = getDisabledSpawnPoints;

				preview.ShowUnoccupiedSpawnpoints = showUnoccupiedSpawnpoints;

				var titleLabel = parent.Get<LabelWithTooltipWidget>("MAP_TITLE");
				titleLabel.IsVisible = () => getMap().Map != MapCache.UnknownMap;
				var font = Game.Renderer.Fonts[titleLabel.Font];
				var titleCache = new CachedTransform<string, string>(str =>
				{
					var truncatedText = WidgetUtils.TruncateText(str, titleLabel.Bounds.Width, font);

					if (str != truncatedText)
						titleLabel.GetTooltipText = () => str;
					else
						titleLabel.GetTooltipText = null;

					return truncatedText;
				});

				titleLabel.GetText = () => titleCache.Update(getMap().Map.Title);

				return parent;
			}

			var authorCache = new CachedTransform<string, string>(
				text => FluentProvider.GetMessage(CreatedBy, "author", text));

			Widget SetupAuthorAndMapType(Widget parent)
			{
				var typeLabel = parent.Get<LabelWidget>("MAP_TYPE");
				var typeCache = new CachedTransform<string[], string>(c => c.FirstOrDefault() ?? "");

				typeLabel.GetText = () => typeCache.Update(getMap().Map.Categories);

				var authorLabel = parent.Get<LabelWidget>("MAP_AUTHOR");
				var font = Game.Renderer.Fonts[authorLabel.Font];
				var truncateCache = new CachedTransform<string, string>(author =>
					WidgetUtils.TruncateText(authorCache.Update(author), authorLabel.Bounds.Width, font));

				authorLabel.GetText = () => truncateCache.Update(getMap().Map.Author);

				return parent;
			}

			var mapRepository = modData.Manifest.Get<WebServices>().MapRepository;

			Widget SetUpInstallButton(Widget parent)
			{
				var button = parent.Get<ButtonWidget>("MAP_INSTALL");
				button.IsHighlighted = () => blink;
				button.OnClick = () => getMap().Map.Install(mapRepository);
				return parent;
			}

			var updateButton = widget.Get<ButtonWidget>("MAP_UPDATE");
			updateButton.IsHighlighted = () => blink;
			updateButton.OnClick = () =>
			{
				var uid = getMap().Map.Uid;
				var newUid = modData.MapCache.GetUpdatedMap(uid);
				if (newUid != null && newUid != uid)
					onMapUpdate(newUid);
			};

			Widget SetUpDownloadProgress(Widget parent)
			{
				var progressbar = parent.Get<ProgressBarWidget>("MAP_PROGRESSBAR");
				progressbar.IsIndeterminate = () => getMap().Map.DownloadPercentage == 0;
				progressbar.GetPercentage = () => getMap().Map.DownloadPercentage;

				var downloadingLabel = parent.Get<LabelWidget>("MAP_STATUS_DOWNLOADING");
				downloadingLabel.GetText = () =>
				{
					var (map, _) = getMap();
					if (map.DownloadBytes == 0)
						return FluentProvider.GetMessage(Connecting);

					// Server does not provide the total file length.
					if (map.DownloadPercentage == 0)
						return FluentProvider.GetMessage(Downloading, "size", map.DownloadBytes / 1024);

					return FluentProvider.GetMessage(DownloadingPercentage, "size", map.DownloadBytes / 1024, "progress", map.DownloadPercentage);
				};

				return parent;
			}

			retryButton = widget.Get<ButtonWidget>("MAP_RETRY");
			retryButton.OnClick = () =>
			{
				retryTriggered = true;
				var (map, _) = getMap();

				var uid = modData.MapCache.GetUpdatedMap(map.Uid);
				mapUpdateAvailable = mapUpdatesEnabled && uid != null && map.Uid != uid;

				if (map.Status == MapStatus.DownloadError)
					map.Install(mapRepository);
				else if (map.Status == MapStatus.Unavailable)
					modData.MapCache.QueryRemoteMapDetails(mapRepository, [map.Uid]);
			};

			var retryInstall = FluentProvider.GetMessage(RetryInstall);
			var retrySearch = FluentProvider.GetMessage(RetrySearch);
			retryButton.GetText = () => getMap().Map.Status == MapStatus.DownloadError ? retryInstall : retrySearch;

			var previewLarge = SetupMapPreview(widget.Get("MAP_LARGE"));
			var previewSmall = SetupMapPreview(widget.Get("MAP_SMALL"));

			// Widgets to be made visible.
			previewWidgets[PreviewStatus.Unknown] =
				[previewLarge];
			previewWidgets[PreviewStatus.Playable] =
				[previewLarge, SetupAuthorAndMapType(widget.Get("MAP_AVAILABLE"))];
			previewWidgets[PreviewStatus.Incompatible] =
				[previewLarge, widget.Get("MAP_INCOMPATIBLE")];
			previewWidgets[PreviewStatus.Validating] =
				[previewSmall, widget.Get("MAP_VALIDATING")];
			previewWidgets[PreviewStatus.UpdateAvailable] =
				[previewSmall, widget.Get("MAP_UPDATE_AVAILABLE"), updateButton];
			previewWidgets[PreviewStatus.DownloadAvailable] =
				[previewSmall, SetUpInstallButton(SetupAuthorAndMapType(widget.Get("MAP_DOWNLOAD_AVAILABLE")))];
			previewWidgets[PreviewStatus.UpdateDownloadAvailable] =
				[previewSmall, SetUpInstallButton(widget.Get("MAP_UPDATE_DOWNLOAD_AVAILABLE")), updateButton];
			previewWidgets[PreviewStatus.Searching] =
				[previewSmall, widget.Get("MAP_SEARCHING")];
			previewWidgets[PreviewStatus.Downloading] =
				[previewSmall, SetUpDownloadProgress(widget.Get("MAP_DOWNLOADING"))];
			previewWidgets[PreviewStatus.Unavailable] =
				[previewSmall, widget.Get("MAP_UNAVAILABLE"), retryButton];
			previewWidgets[PreviewStatus.DownloadError] =
				[previewSmall, widget.Get("MAP_ERROR"), retryButton];

			// Hide all widgets.
			foreach (var preview in previewWidgets)
				foreach (var p in preview.Value)
					p.IsVisible = () => false;
		}

		Widget[] visibleWidgets = [];

		void UpdateVisibility()
		{
			foreach (var widget in visibleWidgets)
				widget.IsVisible = () => false;

			visibleWidgets = previewWidgets[currentStatus];
			foreach (var widget in visibleWidgets)
				widget.IsVisible = () => true;
		}

		bool retryTriggered = false;

		public override void Tick()
		{
			if (++blinkTick >= blinkTickLength)
			{
				blink ^= true;
				blinkTick = 0;
			}

			var (map, serverStatus) = getMap();

			// Combine Session.MapStatus and MapStatus into PreviewStatus.
			PreviewStatus? status = null;
			if (map == MapCache.UnknownMap)
				status = PreviewStatus.Unknown;
			else
			{
				switch (map.Status)
				{
					case MapStatus.Available:
						if (serverStatus.HasFlag(Session.MapStatus.Playable))
							status = PreviewStatus.Playable;
						else if (serverStatus.HasFlag(Session.MapStatus.Incompatible))
							status = PreviewStatus.Incompatible;
						else if (serverStatus.HasFlag(Session.MapStatus.Validating))
							status = PreviewStatus.Validating;
						else
							return;
						break;
					case MapStatus.DownloadAvailable:
						if (mapUpdateAvailable)
							status = PreviewStatus.UpdateDownloadAvailable;
						else
							status = PreviewStatus.DownloadAvailable;
						break;
					case MapStatus.Searching:
						status = PreviewStatus.Searching;
						break;
					case MapStatus.Unavailable:
						if (mapUpdateAvailable)
							status = PreviewStatus.UpdateAvailable;
						else
							status = PreviewStatus.Unavailable;
						break;
					case MapStatus.DownloadError:
						status = PreviewStatus.DownloadError;
						break;
					case MapStatus.Downloading:
						status = PreviewStatus.Downloading;
						break;
				}
			}

			// Trigger only on status change.
			if (status != null && status != currentStatus)
			{
				// When a map becomes invalid, make sure the `Retry` button is triggered.
				if (status == PreviewStatus.Unavailable || status == PreviewStatus.UpdateAvailable)
				{
					if (retryTriggered)
						retryTriggered = false;
					else
					{
						retryButton.OnClick();
						if (map.Status == MapStatus.Searching)
							status = PreviewStatus.Searching;
						else if (mapUpdateAvailable)
							status = PreviewStatus.UpdateAvailable;
						else
							status = PreviewStatus.Unavailable;
					}
				}
				else if (status != PreviewStatus.Searching)
					retryTriggered = false;

				currentStatus = status.Value;
				UpdateVisibility();
			}
		}
	}
}
