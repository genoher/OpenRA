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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenRA.FileFormats;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	[IncludeStaticFluentReferences(typeof(ReplayUtils))]
	public class ReplayBrowserLogic : ChromeLogic
	{
		[FluentReference("time")]
		const string Duration = "label-duration";

		[FluentReference]
		const string Singleplayer = "options-replay-type.singleplayer";

		[FluentReference]
		const string Multiplayer = "options-replay-type.multiplayer";

		[FluentReference]
		const string Today = "options-replay-date.today";

		[FluentReference]
		const string LastWeek = "options-replay-date.last-week";

		[FluentReference]
		const string LastFortnight = "options-replay-date.last-fortnight";

		[FluentReference]
		const string LastMonth = "options-replay-date.last-month";

		[FluentReference]
		const string ReplayDurationVeryShort = "options-replay-duration.very-short";

		[FluentReference]
		const string ReplayDurationShort = "options-replay-duration.short";

		[FluentReference]
		const string ReplayDurationMedium = "options-replay-duration.medium";

		[FluentReference]
		const string ReplayDurationLong = "options-replay-duration.long";

		[FluentReference]
		const string RenameReplayTitle = "dialog-rename-replay.title";

		[FluentReference]
		const string RenameReplayPrompt = "dialog-rename-replay.prompt";

		[FluentReference]
		const string RenameReplayAccept = "dialog-rename-replay.confirm";

		[FluentReference]
		const string DeleteReplayTitle = "dialog-delete-replay.title";

		[FluentReference("replay")]
		const string DeleteReplayPrompt = "dialog-delete-replay.prompt";

		[FluentReference]
		const string DeleteReplayAccept = "dialog-delete-replay.confirm";

		[FluentReference]
		const string DeleteAllReplaysTitle = "dialog-delete-all-replays.title";

		[FluentReference("count")]
		const string DeleteAllReplaysPrompt = "dialog-delete-all-replays.prompt";

		[FluentReference]
		const string DeleteAllReplaysAccept = "dialog-delete-all-replays.confirm";

		[FluentReference("file")]
		const string ReplayDeletionFailed = "notification-replay-deletion-failed";

		[FluentReference]
		const string Players = "label-players";

		[FluentReference("team")]
		const string TeamNumber = "label-team-name";

		[FluentReference]
		const string NoTeam = "label-no-team";

		[FluentReference]
		const string Victory = "options-winstate.victory";

		[FluentReference]
		const string Defeat = "options-winstate.defeat";

		static Filter filter = new();

		readonly Widget panel;
		readonly ScrollPanelWidget replayList, playerList;
		readonly ScrollItemWidget playerTemplate, playerHeader;
		readonly List<ReplayMetadata> replays = [];
		readonly Dictionary<ReplayMetadata, ReplayState> replayState = [];
		readonly Action onStart;
		readonly ModData modData;
		readonly WebServices services;

		MapPreview map;
		ReplayMetadata selectedReplay;

		volatile bool cancelLoadingReplays;

		[ObjectCreator.UseCtor]
		public ReplayBrowserLogic(Widget widget, ModData modData, Action onExit, Action onStart)
		{
			map = MapCache.UnknownMap;
			panel = widget;

			services = modData.Manifest.Get<WebServices>();
			this.modData = modData;
			this.onStart = onStart;
			Game.BeforeGameStart += OnGameStart;

			playerList = panel.Get<ScrollPanelWidget>("PLAYER_LIST");
			playerHeader = playerList.Get<ScrollItemWidget>("HEADER");
			playerTemplate = playerList.Get<ScrollItemWidget>("TEMPLATE");
			playerList.RemoveChildren();

			panel.Get<ButtonWidget>("CANCEL_BUTTON").OnClick = () => { cancelLoadingReplays = true; Ui.CloseWindow(); onExit(); };

			replayList = panel.Get<ScrollPanelWidget>("REPLAY_LIST");
			var template = panel.Get<ScrollItemWidget>("REPLAY_TEMPLATE");

			var mod = modData.Manifest;
			var dir = Path.Combine(Platform.SupportDir, "Replays", mod.Id, mod.Metadata.Version);

			if (Directory.Exists(dir))
				ThreadPool.QueueUserWorkItem(_ => LoadReplays(dir, template));

			var watch = panel.Get<ButtonWidget>("WATCH_BUTTON");
			watch.IsDisabled = () => selectedReplay == null || map.Status != MapStatus.Available;
			watch.OnClick = WatchReplay;

			var mapPreviewRoot = panel.Get("MAP_PREVIEW_ROOT");
			mapPreviewRoot.IsVisible = () => selectedReplay != null;
			panel.Get("REPLAY_INFO").IsVisible = () => selectedReplay != null;

			var spawnOccupants = new CachedTransform<ReplayMetadata, Dictionary<int, SpawnOccupant>>(r =>
			{
				// Avoid using .ToDictionary to improve robustness against replays defining duplicate spawn assignments
				var occupants = new Dictionary<int, SpawnOccupant>();
				foreach (var p in r.GameInfo.Players)
					if (p.SpawnPoint != 0)
						occupants[p.SpawnPoint] = new SpawnOccupant(p);

				return occupants;
			});

			var noSpawns = new HashSet<int>();
			var disabledSpawnPoints = new CachedTransform<ReplayMetadata, HashSet<int>>(r => r.GameInfo.DisabledSpawnPoints ?? noSpawns);

			Ui.LoadWidget("MAP_PREVIEW", mapPreviewRoot, new WidgetArgs
			{
				{ "orderManager", null },
				{ "getMap", (Func<(MapPreview, Session.MapStatus)>)(() => (map, Session.MapStatus.Playable)) },
				{ "onMouseDown", null },
				{ "getSpawnOccupants", (Func<Dictionary<int, SpawnOccupant>>)(() => spawnOccupants.Update(selectedReplay)) },
				{ "getDisabledSpawnPoints", (Func<HashSet<int>>)(() => disabledSpawnPoints.Update(selectedReplay)) },
				{ "showUnoccupiedSpawnpoints", false },
				{ "mapUpdatesEnabled", false },
				{ "onMapUpdate", (Action<string>)(_ => { }) },
			});

			var replayDuration = new CachedTransform<ReplayMetadata, string>(r =>
				FluentProvider.GetMessage(Duration, "time", WidgetUtils.FormatTimeSeconds((int)selectedReplay.GameInfo.Duration.TotalSeconds)));
			panel.Get<LabelWidget>("DURATION").GetText = () => replayDuration.Update(selectedReplay);

			SetupFilters();
			SetupManagement();
		}

		void LoadReplays(string dir, ScrollItemWidget template)
		{
			using (new Support.PerfTimer("Load replays"))
			{
				var loadedReplays = new ConcurrentBag<ReplayMetadata>();
				Parallel.ForEach(Directory.GetFiles(dir, "*.orarep", SearchOption.AllDirectories), (fileName, pls) =>
				{
					if (cancelLoadingReplays)
					{
						pls.Stop();
						return;
					}

					var replay = ReplayMetadata.Read(fileName);
					if (replay != null)
						loadedReplays.Add(replay);
				});

				if (cancelLoadingReplays)
					return;

				var sortedReplays = loadedReplays.OrderByDescending(replay => replay.GameInfo.StartTimeUtc).ToList();
				Game.RunAfterTick(() =>
				{
					replayList.RemoveChildren();
					foreach (var replay in sortedReplays)
						AddReplay(replay, template);

					SetupReplayDependentFilters();
					ApplyFilter();
				});
			}
		}

		void SetupFilters()
		{
			// Game type
			{
				var ddb = panel.GetOrNull<DropDownButtonWidget>("FLT_GAMETYPE_DROPDOWNBUTTON");
				if (ddb != null)
				{
					// Using list to maintain the order
					var options = new List<(GameType GameType, string Text)>
					{
						(GameType.Any, ddb.GetText()),
						(GameType.Singleplayer, FluentProvider.GetMessage(Singleplayer)),
						(GameType.Multiplayer, FluentProvider.GetMessage(Multiplayer))
					};

					var lookup = options.ToDictionary(kvp => kvp.GameType, kvp => kvp.Text);

					ddb.GetText = () => lookup[filter.Type];
					ddb.OnMouseDown = _ =>
					{
						ScrollItemWidget SetupItem((GameType GameType, string Text) option, ScrollItemWidget tpl)
						{
							var item = ScrollItemWidget.Setup(
								tpl,
								() => filter.Type == option.GameType,
								() => { filter.Type = option.GameType; ApplyFilter(); });
							item.Get<LabelWidget>("LABEL").GetText = () => option.Text;
							return item;
						}

						ddb.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 330, options, SetupItem);
					};
				}
			}

			// Date type
			{
				var ddb = panel.GetOrNull<DropDownButtonWidget>("FLT_DATE_DROPDOWNBUTTON");
				if (ddb != null)
				{
					// Using list to maintain the order
					var options = new List<(DateType DateType, string Text)>
					{
						(DateType.Any, ddb.GetText()),
						(DateType.Today, FluentProvider.GetMessage(Today)),
						(DateType.LastWeek, FluentProvider.GetMessage(LastWeek)),
						(DateType.LastFortnight, FluentProvider.GetMessage(LastFortnight)),
						(DateType.LastMonth, FluentProvider.GetMessage(LastMonth))
					};

					var lookup = options.ToDictionary(kvp => kvp.DateType, kvp => kvp.Text);

					ddb.GetText = () => lookup[filter.Date];
					ddb.OnMouseDown = _ =>
					{
						ScrollItemWidget SetupItem((DateType DateType, string Text) option, ScrollItemWidget tpl)
						{
							var item = ScrollItemWidget.Setup(
								tpl,
								() => filter.Date == option.DateType,
								() => { filter.Date = option.DateType; ApplyFilter(); });

							item.Get<LabelWidget>("LABEL").GetText = () => option.Text;
							return item;
						}

						ddb.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 330, options, SetupItem);
					};
				}
			}

			// Duration
			{
				var ddb = panel.GetOrNull<DropDownButtonWidget>("FLT_DURATION_DROPDOWNBUTTON");
				if (ddb != null)
				{
					// Using list to maintain the order
					var options = new List<(DurationType DurationType, string Text)>
					{
						(DurationType.Any, ddb.GetText()),
						(DurationType.VeryShort, FluentProvider.GetMessage(ReplayDurationVeryShort)),
						(DurationType.Short, FluentProvider.GetMessage(ReplayDurationShort)),
						(DurationType.Medium, FluentProvider.GetMessage(ReplayDurationMedium)),
						(DurationType.Long, FluentProvider.GetMessage(ReplayDurationLong))
					};

					var lookup = options.ToDictionary(kvp => kvp.DurationType, kvp => kvp.Text);

					ddb.GetText = () => lookup[filter.Duration];
					ddb.OnMouseDown = _ =>
					{
						ScrollItemWidget SetupItem((DurationType DurationType, string Text) option, ScrollItemWidget tpl)
						{
							var item = ScrollItemWidget.Setup(
								tpl,
								() => filter.Duration == option.DurationType,
								() => { filter.Duration = option.DurationType; ApplyFilter(); });
							item.Get<LabelWidget>("LABEL").GetText = () => option.Text;
							return item;
						}

						ddb.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 330, options, SetupItem);
					};
				}
			}

			// Outcome (depends on Player)
			{
				var ddb = panel.GetOrNull<DropDownButtonWidget>("FLT_OUTCOME_DROPDOWNBUTTON");
				if (ddb != null)
				{
					ddb.IsDisabled = () => string.IsNullOrEmpty(filter.PlayerName);

					// Using list to maintain the order
					var options = new List<(WinState WinState, string Text)>
					{
						(WinState.Undefined, ddb.GetText()),
						(WinState.Lost, FluentProvider.GetMessage(Defeat)),
						(WinState.Won, FluentProvider.GetMessage(Victory))
					};

					var lookup = options.ToDictionary(kvp => kvp.WinState, kvp => kvp.Text);

					ddb.GetText = () => lookup[filter.Outcome];
					ddb.OnMouseDown = _ =>
					{
						ScrollItemWidget SetupItem((WinState WinState, string Text) option, ScrollItemWidget tpl)
						{
							var item = ScrollItemWidget.Setup(
								tpl,
								() => filter.Outcome == option.WinState,
								() => { filter.Outcome = option.WinState; ApplyFilter(); });
							item.Get<LabelWidget>("LABEL").GetText = () => option.Text;
							return item;
						}

						ddb.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 330, options, SetupItem);
					};
				}
			}

			// Reset button
			{
				var button = panel.Get<ButtonWidget>("FLT_RESET_BUTTON");
				button.IsDisabled = () => filter.IsEmpty;
				button.OnClick = () => { filter = new Filter(); ApplyFilter(); };
			}
		}

		void SetupReplayDependentFilters()
		{
			// Map
			{
				var ddb = panel.GetOrNull<DropDownButtonWidget>("FLT_MAPNAME_DROPDOWNBUTTON");
				if (ddb != null)
				{
					var options = replays.Select(r => r.GameInfo.MapTitle).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
					options.Sort(StringComparer.OrdinalIgnoreCase);
					options.Insert(0, null); // no filter

					var anyText = ddb.GetText();
					ddb.GetText = () => string.IsNullOrEmpty(filter.MapName) ? anyText : filter.MapName;
					ddb.OnMouseDown = _ =>
					{
						ScrollItemWidget SetupItem(string option, ScrollItemWidget tpl)
						{
							var item = ScrollItemWidget.Setup(
								tpl,
								() => string.Equals(filter.MapName, option, StringComparison.CurrentCultureIgnoreCase),
								() => { filter.MapName = option; ApplyFilter(); });
							item.Get<LabelWidget>("LABEL").GetText = () => option ?? anyText;
							return item;
						}

						ddb.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 330, options, SetupItem);
					};
				}
			}

			// Players
			{
				var ddb = panel.GetOrNull<DropDownButtonWidget>("FLT_PLAYER_DROPDOWNBUTTON");
				if (ddb != null)
				{
					var options = replays.SelectMany(r => r.GameInfo.Players.Select(p => r.GameInfo.ResolvedPlayerName(p)))
						.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

					options.Sort(StringComparer.OrdinalIgnoreCase);
					options.Insert(0, null); // no filter

					var anyText = ddb.GetText();
					ddb.GetText = () => string.IsNullOrEmpty(filter.PlayerName) ? anyText : filter.PlayerName;
					ddb.OnMouseDown = _ =>
					{
						ScrollItemWidget SetupItem(string option, ScrollItemWidget tpl)
						{
							var item = ScrollItemWidget.Setup(
								tpl,
								() => string.Equals(filter.PlayerName, option, StringComparison.CurrentCultureIgnoreCase),
								() => { filter.PlayerName = option; ApplyFilter(); });
							item.Get<LabelWidget>("LABEL").GetText = () => option ?? anyText;
							return item;
						}

						ddb.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 330, options, SetupItem);
					};
				}
			}

			// Faction (depends on Player)
			{
				var ddb = panel.GetOrNull<DropDownButtonWidget>("FLT_FACTION_DROPDOWNBUTTON");
				if (ddb != null)
				{
					ddb.IsDisabled = () => string.IsNullOrEmpty(filter.PlayerName);

					var options = replays
						.SelectMany(r => r.GameInfo.Players.Select(p => p.FactionName).Where(n => !string.IsNullOrEmpty(n)))
						.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
					options.Sort(StringComparer.OrdinalIgnoreCase);
					options.Insert(0, null); // no filter

					var anyText = ddb.GetText();
					ddb.GetText = () => string.IsNullOrEmpty(filter.Faction) ? anyText : FluentProvider.GetMessage(filter.Faction);
					ddb.OnMouseDown = _ =>
					{
						ScrollItemWidget SetupItem(string option, ScrollItemWidget tpl)
						{
							var item = ScrollItemWidget.Setup(
								tpl,
								() => string.Equals(filter.Faction, option, StringComparison.CurrentCultureIgnoreCase),
								() => { filter.Faction = option; ApplyFilter(); });
							item.Get<LabelWidget>("LABEL").GetText = () => option != null ? FluentProvider.GetMessage(option) : anyText;
							return item;
						}

						ddb.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 330, options, SetupItem);
					};
				}
			}
		}

		void SetupManagement()
		{
			var renameButton = panel.Get<ButtonWidget>("MNG_RENSEL_BUTTON");
			renameButton.IsDisabled = () => selectedReplay == null;
			renameButton.OnClick = () =>
			{
				var r = selectedReplay;
				var initialName = Path.GetFileNameWithoutExtension(r.FilePath);
				var directoryName = Path.GetDirectoryName(r.FilePath);
				var invalidChars = Path.GetInvalidFileNameChars();

				ConfirmationDialogs.TextInputPrompt(modData,
					RenameReplayTitle,
					RenameReplayPrompt,
					initialName,
					onAccept: newName => RenameReplay(r, newName),
					onCancel: null,
					acceptText: RenameReplayAccept,
					cancelText: null,
					inputValidator: newName =>
					{
						if (newName == initialName)
							return false;

						if (string.IsNullOrWhiteSpace(newName))
							return false;

						if (newName.IndexOfAny(invalidChars) >= 0)
							return false;

						if (File.Exists(Path.Combine(directoryName, newName)))
							return false;

						return true;
					});
			};

			void OnDeleteReplay(ReplayMetadata r, Action after)
			{
				ConfirmationDialogs.ButtonPrompt(modData,
					title: DeleteReplayTitle,
					text: DeleteReplayPrompt,
					textArguments: ["replay", Path.GetFileNameWithoutExtension(r.FilePath)],
					onConfirm: () =>
					{
						DeleteReplay(r);
						after?.Invoke();
					},
					confirmText: DeleteReplayAccept,
					onCancel: () => { });
			}

			var deleteButton = panel.Get<ButtonWidget>("MNG_DELSEL_BUTTON");
			deleteButton.IsDisabled = () => selectedReplay == null;
			deleteButton.OnClick = () =>
			{
				OnDeleteReplay(selectedReplay, () =>
				{
					if (selectedReplay == null)
						SelectFirstVisibleReplay();
				});
			};

			var deleteAllButton = panel.Get<ButtonWidget>("MNG_DELALL_BUTTON");
			deleteAllButton.IsDisabled = () => !replayState.Any(kvp => kvp.Value.Visible);
			deleteAllButton.OnClick = () =>
			{
				var list = replayState.Where(kvp => kvp.Value.Visible).Select(kvp => kvp.Key).ToList();
				if (list.Count == 0)
					return;

				if (list.Count == 1)
				{
					OnDeleteReplay(list[0], () => { if (selectedReplay == null) SelectFirstVisibleReplay(); });
					return;
				}

				ConfirmationDialogs.ButtonPrompt(modData,
					title: DeleteAllReplaysTitle,
					text: DeleteAllReplaysPrompt,
					textArguments: ["count", list.Count],
					onConfirm: () =>
					{
						foreach (var replayMetadata in list)
							DeleteReplay(replayMetadata);

						if (selectedReplay == null)
							SelectFirstVisibleReplay();
					},
					confirmText: DeleteAllReplaysAccept,
					onCancel: () => { });
			};
		}

		void RenameReplay(ReplayMetadata replay, string newFilenameWithoutExtension)
		{
			try
			{
				var item = replayState[replay].Item;
				replay.RenameFile(newFilenameWithoutExtension);
				item.GetText = () => newFilenameWithoutExtension;

				var label = item.Get<LabelWithTooltipWidget>("TITLE");
				WidgetUtils.TruncateLabelToTooltip(label, item.GetText());
			}
			catch (Exception ex)
			{
				Log.Write("debug", ex.ToString());
			}
		}

		void DeleteReplay(ReplayMetadata replay)
		{
			try
			{
				File.Delete(replay.FilePath);
			}
			catch (Exception ex)
			{
				TextNotificationsManager.Debug(FluentProvider.GetMessage(ReplayDeletionFailed, "file", replay.FilePath));
				Log.Write("debug", ex.ToString());
				return;
			}

			if (replay == selectedReplay)
				SelectReplay(null);

			replayList.RemoveChild(replayState[replay].Item);
			replays.Remove(replay);
			replayState.Remove(replay);
		}

		static bool EvaluateReplayVisibility(ReplayMetadata replay)
		{
			// Game type
			if ((filter.Type == GameType.Multiplayer && replay.GameInfo.IsSinglePlayer) || (filter.Type == GameType.Singleplayer && !replay.GameInfo.IsSinglePlayer))
				return false;

			// Date type
			if (filter.Date != DateType.Any)
			{
				TimeSpan t;
				switch (filter.Date)
				{
					case DateType.Today:
						t = TimeSpan.FromDays(1d);
						break;

					case DateType.LastWeek:
						t = TimeSpan.FromDays(7d);
						break;

					case DateType.LastFortnight:
						t = TimeSpan.FromDays(14d);
						break;

					case DateType.LastMonth:
					default:
						t = TimeSpan.FromDays(30d);
						break;
				}

				if (replay.GameInfo.StartTimeUtc < DateTime.UtcNow - t)
					return false;
			}

			// Duration
			if (filter.Duration != DurationType.Any)
			{
				var minutes = replay.GameInfo.Duration.TotalMinutes;
				switch (filter.Duration)
				{
					case DurationType.VeryShort:
						if (minutes >= 5)
							return false;
						break;

					case DurationType.Short:
						if (minutes < 5 || minutes >= 20)
							return false;
						break;

					case DurationType.Medium:
						if (minutes < 20 || minutes >= 60)
							return false;
						break;

					case DurationType.Long:
						if (minutes < 60)
							return false;
						break;
				}
			}

			// Map
			if (!string.IsNullOrEmpty(filter.MapName) &&
				!string.Equals(filter.MapName, replay.GameInfo.MapTitle, StringComparison.CurrentCultureIgnoreCase))
				return false;

			// Player
			if (!string.IsNullOrEmpty(filter.PlayerName))
			{
				var player = replay.GameInfo.Players.FirstOrDefault(
					p => string.Equals(filter.PlayerName, replay.GameInfo.ResolvedPlayerName(p), StringComparison.CurrentCultureIgnoreCase));
				if (player == null)
					return false;

				// Outcome
				if (filter.Outcome != WinState.Undefined && filter.Outcome != player.Outcome)
					return false;

				// Faction
				if (!string.IsNullOrEmpty(filter.Faction) &&
					!string.Equals(filter.Faction, player.FactionName, StringComparison.CurrentCultureIgnoreCase))
					return false;
			}

			return true;
		}

		void ApplyFilter()
		{
			foreach (var replay in replays)
				replayState[replay].Visible = EvaluateReplayVisibility(replay);

			if (selectedReplay == null || !replayState[selectedReplay].Visible)
				SelectFirstVisibleReplay();

			replayList.Layout.AdjustChildren();
			replayList.ScrollToSelectedItem();
		}

		void SelectFirstVisibleReplay()
		{
			SelectReplay(replays.FirstOrDefault(r => replayState[r].Visible));
		}

		void SelectReplay(ReplayMetadata replay)
		{
			selectedReplay = replay;
			map = selectedReplay != null ? selectedReplay.GameInfo.MapPreview : MapCache.UnknownMap;

			if (replay == null)
				return;

			try
			{
				if (map.Status == MapStatus.Unavailable && Game.Settings.Game.AllowDownloading)
					modData.MapCache.QueryRemoteMapDetails(services.MapRepository, [map.Uid]);

				var players = replay.GameInfo.Players
					.GroupBy(p => p.Team)
					.OrderBy(g => g.Key)
					.ToList();

				var teams = new Dictionary<string, IEnumerable<GameInformation.Player>>();
				var noTeams = players.Count == 1;
				foreach (var p in players)
				{
					var label = noTeams ? FluentProvider.GetMessage(Players) : p.Key > 0
						? FluentProvider.GetMessage(TeamNumber, "team", p.Key)
						: FluentProvider.GetMessage(NoTeam);

					teams.Add(label, p);
				}

				playerList.RemoveChildren();

				foreach (var kv in teams)
				{
					var group = kv.Key;
					if (group.Length > 0)
					{
						var header = ScrollItemWidget.Setup(playerHeader, () => false, () => { });
						header.Get<LabelWidget>("LABEL").GetText = () => group;
						playerList.AddChild(header);
					}

					foreach (var option in kv.Value)
					{
						var o = option;

						var color = o.Color;

						var item = ScrollItemWidget.Setup(playerTemplate, () => false, () => { });

						var label = item.Get<LabelWidget>("LABEL");
						var font = Game.Renderer.Fonts[label.Font];
						var name = WidgetUtils.TruncateText(replay.GameInfo.ResolvedPlayerName(o), label.Bounds.Width, font);
						label.GetText = () => name;
						label.GetColor = () => color;

						var flag = item.Get<ImageWidget>("FLAG");
						flag.GetImageCollection = () => "flags";
						var factionInfo = modData.DefaultRules.Actors[SystemActors.World].TraitInfos<FactionInfo>();
						flag.GetImageName = () => (factionInfo != null && factionInfo.Any(f => f.InternalName == o.FactionId)) ? o.FactionId : "Random";

						playerList.AddChild(item);
					}
				}
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Exception while parsing replay: {replay}");
				Log.Write("debug", e);
				SelectReplay(null);
			}
		}

		void WatchReplay()
		{
			if (selectedReplay != null && ReplayUtils.PromptConfirmReplayCompatibility(selectedReplay, modData))
			{
				cancelLoadingReplays = true;

				DiscordService.UpdateStatus(DiscordState.WatchingReplay);

				Game.JoinReplay(selectedReplay.FilePath);
			}
		}

		void AddReplay(ReplayMetadata replay, ScrollItemWidget template)
		{
			replays.Add(replay);

			var item = ScrollItemWidget.Setup(template,
				() => selectedReplay == replay,
				() => SelectReplay(replay),
				WatchReplay);

			replayState[replay] = new ReplayState
			{
				Item = item,
				Visible = true
			};

			var itemText = Path.GetFileNameWithoutExtension(replay.FilePath);
			item.GetText = () => itemText;
			var label = item.Get<LabelWithTooltipWidget>("TITLE");
			WidgetUtils.TruncateLabelToTooltip(label, itemText);

			item.IsVisible = () => replayState[replay].Visible;
			replayList.AddChild(item);
		}

		void OnGameStart()
		{
			Ui.CloseWindow();
			onStart();
		}

		bool disposed;
		protected override void Dispose(bool disposing)
		{
			if (disposing && !disposed)
			{
				disposed = true;
				Game.BeforeGameStart -= OnGameStart;
			}

			base.Dispose(disposing);
		}

		sealed class ReplayState
		{
			public bool Visible;
			public ScrollItemWidget Item;
		}

		sealed class Filter
		{
			public GameType Type;
			public DateType Date;
			public DurationType Duration;
			public WinState Outcome;
			public string PlayerName;
			public string MapName;
			public string Faction;

			public bool IsEmpty =>
				Type == default
				&& Date == default
				&& Duration == default
				&& Outcome == default
				&& string.IsNullOrEmpty(PlayerName)
				&& string.IsNullOrEmpty(MapName)
				&& string.IsNullOrEmpty(Faction);
		}

		enum GameType
		{
			Any,
			Singleplayer,
			Multiplayer
		}

		enum DateType
		{
			Any,
			Today,
			LastWeek,
			LastFortnight,
			LastMonth
		}

		enum DurationType
		{
			Any,
			VeryShort,
			Short,
			Medium,
			Long
		}
	}
}
