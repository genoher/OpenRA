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
using System.Globalization;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public static class LobbyUtils
	{
		[FluentReference]
		const string Open = "options-lobby-slot.open";

		[FluentReference]
		const string Closed = "options-lobby-slot.closed";

		[FluentReference]
		const string Bots = "options-lobby-slot.bots";

		[FluentReference]
		const string BotPlayer = "label-bot-player";

		[FluentReference]
		const string BotsDisabled = "options-lobby-slot.bots-disabled";

		[FluentReference]
		const string Slot = "options-lobby-slot.slot";

		sealed record SlotDropDownOption(string Title, string Order, Func<bool> Selected);

		public static void ShowSlotDropDown(DropDownButtonWidget dropdown, Session.Slot slot,
			Session.Client client, OrderManager orderManager, MapPreview map, ModData modData)
		{
			var open = FluentProvider.GetMessage(Open);
			var closed = FluentProvider.GetMessage(Closed);
			var options = new Dictionary<string, IEnumerable<SlotDropDownOption>>
			{
				{
					FluentProvider.GetMessage(Slot), new List<SlotDropDownOption>
					{
						new(open, "slot_open " + slot.PlayerReference, () => !slot.Closed && client == null),
						new(closed, "slot_close " + slot.PlayerReference, () => slot.Closed)
					}
				}
			};

			var bots = new List<SlotDropDownOption>();
			if (slot.AllowBots)
			{
				foreach (var b in map.PlayerActorInfo.TraitInfos<IBotInfo>())
				{
					var botController = orderManager.LobbyInfo.Clients.FirstOrDefault(c => c.IsAdmin);
					bots.Add(new SlotDropDownOption(map.GetMessage(b.Name),
						$"slot_bot {slot.PlayerReference} {botController.Index} {b.Type}",
						() => client != null && client.Bot == b.Type));
				}
			}

			options.Add(bots.Count > 0 ? FluentProvider.GetMessage(Bots) : FluentProvider.GetMessage(BotsDisabled), bots);

			ScrollItemWidget SetupItem(SlotDropDownOption o, ScrollItemWidget itemTemplate)
			{
				var item = ScrollItemWidget.Setup(itemTemplate,
					o.Selected,
					() => orderManager.IssueOrder(Order.Command(o.Order)));
				item.Get<LabelWidget>("LABEL").GetText = () => o.Title;
				return item;
			}

			dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 180, options, SetupItem);
		}

		public static void ShowPlayerActionDropDown(DropDownButtonWidget dropdown,
			Session.Client c, OrderManager orderManager, Widget lobby, Action before, Action after)
		{
			Action<bool> okPressed = tempBan => { orderManager.IssueOrder(Order.Command($"kick {c.Index} {tempBan}")); after(); };
			var onClick = new Action(() =>
			{
				before();

				Game.LoadWidget(null, "KICK_CLIENT_DIALOG", lobby.Get("TOP_PANELS_ROOT"), new WidgetArgs
				{
					{ "clientName", c.Name },
					{ "okPressed", okPressed },
					{ "cancelPressed", after }
				});
			});

			var options = new List<DropDownOption>
			{
				new()
				{
					Title = "Kick",
					OnClick = onClick
				},
			};

			if (orderManager.LobbyInfo.GlobalSettings.Dedicated)
			{
				options.Add(new DropDownOption
				{
					Title = "Transfer Admin",
					OnClick = () => orderManager.IssueOrder(Order.Command($"make_admin {c.Index}"))
				});
			}

			if (!c.IsObserver && orderManager.LobbyInfo.GlobalSettings.AllowSpectators)
			{
				options.Add(new DropDownOption
				{
					Title = "Move to Spectator",
					OnClick = () => orderManager.IssueOrder(Order.Command($"make_spectator {c.Index}"))
				});
			}

			ScrollItemWidget SetupItem(DropDownOption o, ScrollItemWidget itemTemplate)
			{
				var item = ScrollItemWidget.Setup(itemTemplate, o.IsSelected, o.OnClick);
				var labelWidget = item.Get<LabelWidget>("LABEL");
				labelWidget.GetText = () => o.Title;
				return item;
			}

			dropdown.ShowDropDown("PLAYERACTION_DROPDOWN_TEMPLATE", 167, options, SetupItem);
		}

		public static void ShowTeamDropDown(DropDownButtonWidget dropdown, Session.Client client,
			OrderManager orderManager, int teamCount)
		{
			ScrollItemWidget SetupItem(int ii, ScrollItemWidget itemTemplate)
			{
				var item = ScrollItemWidget.Setup(itemTemplate,
					() => client.Team == ii,
					() => orderManager.IssueOrder(Order.Command($"team {client.Index} {ii}")));
				item.Get<LabelWidget>("LABEL").GetText = () => ii == 0 ? "-" : ii.ToString(NumberFormatInfo.CurrentInfo);
				return item;
			}

			var options = Enumerable.Range(0, teamCount + 1);
			dropdown.ShowDropDown("TEAM_DROPDOWN_TEMPLATE", 150, options, SetupItem);
		}

		public static void ShowHandicapDropDown(DropDownButtonWidget dropdown, Session.Client client,
			OrderManager orderManager)
		{
			ScrollItemWidget SetupItem(int ii, ScrollItemWidget itemTemplate)
			{
				var item = ScrollItemWidget.Setup(itemTemplate,
					() => client.Handicap == ii,
					() => orderManager.IssueOrder(Order.Command($"handicap {client.Index} {ii}")));

				var label = $"{ii}%";
				item.Get<LabelWidget>("LABEL").GetText = () => label;
				return item;
			}

			// Handicaps may be set between 0 - 95% in steps of 5%
			var options = Enumerable.Range(0, 20).Select(i => 5 * i);
			dropdown.ShowDropDown("TEAM_DROPDOWN_TEMPLATE", 150, options, SetupItem);
		}

		public static void ShowSpawnDropDown(DropDownButtonWidget dropdown, Session.Client client,
			OrderManager orderManager, IEnumerable<int> spawnPoints)
		{
			ScrollItemWidget SetupItem(int ii, ScrollItemWidget itemTemplate)
			{
				var item = ScrollItemWidget.Setup(itemTemplate,
					() => client.SpawnPoint == ii,
					() => SetSpawnPoint(orderManager, client, ii));
				item.Get<LabelWidget>("LABEL").GetText = () => ii == 0 ? "-" : Convert.ToChar('A' - 1 + ii).ToString();
				return item;
			}

			dropdown.ShowDropDown("SPAWN_DROPDOWN_TEMPLATE", 150, spawnPoints, SetupItem);
		}

		/// <summary>Splits a string into two parts on the first instance of a given token.</summary>
		public static (string First, string Second) SplitOnFirstToken(string input, string token = "\n")
		{
			if (string.IsNullOrEmpty(input))
				return (null, null);

			var split = input.IndexOf(token, StringComparison.Ordinal);
			var first = split > 0 ? input[..split] : input;
			var second = split > 0 ? input[(split + token.Length)..] : null;
			return (first, second);
		}

		public static void ShowFactionDropDown(DropDownButtonWidget dropdown, Session.Client client,
			OrderManager orderManager, Dictionary<string, LobbyFaction> factions)
		{
			ScrollItemWidget SetupItem(string factionId, ScrollItemWidget itemTemplate)
			{
				var item = ScrollItemWidget.Setup(itemTemplate,
					() => client.Faction == factionId,
					() => orderManager.IssueOrder(Order.Command($"faction {client.Index} {factionId}")));
				var faction = factions[factionId];

				var label = item.Get<LabelWidget>("LABEL");
				var labelText = WidgetUtils.TruncateText(FluentProvider.GetMessage(faction.Name), label.Bounds.Width, Game.Renderer.Fonts[label.Font]);
				label.GetText = () => labelText;

				var flag = item.Get<ImageWidget>("FLAG");
				flag.GetImageCollection = () => "flags";
				flag.GetImageName = () => factionId;

				var description = faction.Description != null ? FluentProvider.GetMessage(faction.Description) : null;
				var (text, desc) = SplitOnFirstToken(description);
				item.GetTooltipText = () => text;
				item.GetTooltipDesc = () => desc;

				return item;
			}

			var options = factions.Where(f => f.Value.Selectable).GroupBy(f => f.Value.Side)
				.ToDictionary(g => g.Key != null ? FluentProvider.GetMessage(g.Key) : "", g => g.Select(f => FluentProvider.GetMessage(f.Key)));

			dropdown.ShowDropDown("FACTION_DROPDOWN_TEMPLATE", 154, options, SetupItem);
		}

		public static void SelectSpawnPoint(OrderManager orderManager, MapPreviewWidget mapPreview, MapPreview preview, MouseInput mi)
		{
			if (orderManager.LocalClient.State == Session.ClientState.Ready)
				return;

			if (mi.Button == MouseButton.Left)
				SelectPlayerSpawnPoint(orderManager, mapPreview, preview, mi);

			if (mi.Button == MouseButton.Right)
				ClearPlayerSpawnPoint(orderManager, mapPreview, preview, mi);
		}

		static void SelectPlayerSpawnPoint(OrderManager orderManager, MapPreviewWidget mapPreview, MapPreview preview, MouseInput mi)
		{
			var selectedSpawn = DetermineSelectedSpawnPoint(mapPreview, preview, mi);

			var locals = orderManager.LobbyInfo.Clients.Where(c => c.Index == orderManager.LocalClient.Index || (Game.IsHost && c.Bot != null));
			var playerToMove = locals.FirstOrDefault(c => (selectedSpawn == 0) ^ (c.SpawnPoint == 0) && !c.IsObserver);
			SetSpawnPoint(orderManager, playerToMove, selectedSpawn);
		}

		static void ClearPlayerSpawnPoint(OrderManager orderManager, MapPreviewWidget mapPreview, MapPreview preview, MouseInput mi)
		{
			var selectedSpawn = DetermineSelectedSpawnPoint(mapPreview, preview, mi);
			if (Game.IsHost || orderManager.LobbyInfo.Clients.FirstOrDefault(cc => cc.SpawnPoint == selectedSpawn) == orderManager.LocalClient)
				orderManager.IssueOrder(Order.Command($"clear_spawn {selectedSpawn}"));
		}

		static int DetermineSelectedSpawnPoint(MapPreviewWidget mapPreview, MapPreview preview, MouseInput mi)
		{
			var spawnSize = ChromeProvider.GetImage("lobby-bits", "spawn-unclaimed").Size.XY;
			var selectedSpawn = preview.SpawnPoints
				.Select((sp, i) => (SpawnLocation: mapPreview.ConvertToPreview(sp, preview.GridType), Index: i))
				.Where(a => ((a.SpawnLocation - mi.Location).ToFloat2() / spawnSize * 2).LengthSquared <= 1)
				.Select(a => a.Index + 1)
				.FirstOrDefault();
			return selectedSpawn;
		}

		static void SetSpawnPoint(OrderManager orderManager, Session.Client playerToMove, int selectedSpawnPoint)
		{
			var owned =
				orderManager.LobbyInfo.Clients.Any(c => c.SpawnPoint == selectedSpawnPoint) ||
				orderManager.LobbyInfo.DisabledSpawnPoints.Contains(selectedSpawnPoint);
			if (selectedSpawnPoint == 0 || !owned)
				orderManager.IssueOrder(Order.Command($"spawn {(playerToMove ?? orderManager.LocalClient).Index} {selectedSpawnPoint}"));
		}

		public static List<int> AvailableSpawnPoints(int spawnPoints, Session lobbyInfo)
		{
			return Enumerable.Range(1, spawnPoints).Except(lobbyInfo.DisabledSpawnPoints).ToList();
		}

		public static bool InsufficientEnabledSpawnPoints(MapPreview map, Session lobbyInfo)
		{
			// If a map doesn't define spawn points we always have enough space
			var spawnPoints = map.SpawnPoints.Length;
			if (spawnPoints == 0)
				return false;

			return AvailableSpawnPoints(spawnPoints, lobbyInfo).Count < lobbyInfo.Clients.Count(c => !c.IsObserver);
		}

		public static Color LatencyColor(Session.Client client)
		{
			if (client == null)
				return Color.Gray;

			switch (client.ConnectionQuality)
			{
				case Session.ConnectionQuality.Good: return Color.LimeGreen;
				case Session.ConnectionQuality.Moderate: return Color.Orange;
				case Session.ConnectionQuality.Poor: return Color.Red;
				default: return Color.Gray;
			}
		}

		public static string LatencyDescription(Session.Client client)
		{
			if (client == null)
				return "Unknown";

			switch (client.ConnectionQuality)
			{
				case Session.ConnectionQuality.Good: return "Good";
				case Session.ConnectionQuality.Moderate: return "Moderate";
				case Session.ConnectionQuality.Poor: return "Poor";
				default: return "Unknown";
			}
		}

		public static void SetupLatencyWidget(Widget parent, Session.Client c, OrderManager orderManager)
		{
			var visible = c != null && c.Bot == null;
			var block = parent.GetOrNull("LATENCY");
			if (block != null)
			{
				block.IsVisible = () => visible;

				if (visible)
					block.Get<ColorBlockWidget>("LATENCY_COLOR").GetColor = () => LatencyColor(c);
			}

			var tooltip = parent.Get<ClientTooltipRegionWidget>("LATENCY_REGION");
			tooltip.IsVisible = () => visible;
			if (visible)
				tooltip.Bind(orderManager, null, c);
		}

		public static void SetupProfileWidget(Widget parent, Session.Client c, OrderManager orderManager, WorldRenderer worldRenderer)
		{
			var profile = parent.GetOrNull<ImageWidget>("PROFILE");
			if (profile != null)
			{
				var imageName = c.IsBot ? "bot" :
					c.IsAdmin ? "admin-" :
						"player-";

				if (!c.IsBot)
					imageName += c.Fingerprint != null ? "registered" : "anonymous";

				profile.GetImageName = () => imageName;
				profile.IsVisible = () => true;
			}

			var profileTooltip = parent.GetOrNull<ClientTooltipRegionWidget>("PROFILE_TOOLTIP");
			if (profileTooltip != null)
			{
				if (c.Fingerprint != null)
					profileTooltip.Template = "REGISTERED_PLAYER_TOOLTIP";

				if (c.IsBot)
					profileTooltip.Template = "BOT_TOOLTIP";

				profileTooltip.Bind(orderManager, worldRenderer, c);

				profileTooltip.IsVisible = () => true;
			}
		}

		public static void SetupEditableNameWidget(Widget parent, Session.Client c, OrderManager orderManager, WorldRenderer worldRenderer)
		{
			var name = parent.Get<TextFieldWidget>("NAME");
			name.IsVisible = () => true;
			name.IsDisabled = () => orderManager.LocalClient.IsReady;

			name.Text = c.Name;
			var escPressed = false;
			name.OnLoseFocus = () =>
			{
				if (escPressed)
				{
					escPressed = false;
					return;
				}

				name.Text = name.Text.Trim();
				if (name.Text.Length == 0)
					name.Text = c.Name;
				else if (name.Text != c.Name)
				{
					name.Text = Settings.SanitizedPlayerName(name.Text);
					orderManager.IssueOrder(Order.Command("name " + name.Text));
					Game.Settings.Player.Name = name.Text;
					Game.Settings.Save();
				}
			};

			name.OnEnterKey = _ => { name.YieldKeyboardFocus(); return true; };
			name.OnEscKey = _ =>
			{
				name.Text = c.Name;
				escPressed = true;
				name.YieldKeyboardFocus();
				return true;
			};

			SetupProfileWidget(name, c, orderManager, worldRenderer);

			HideChildWidget(parent, "SLOT_OPTIONS");
		}

		public static void SetupNameWidget(Widget parent, Session.Client c, OrderManager orderManager, WorldRenderer worldRenderer, MapPreview map)
		{
			var label = parent.Get<LabelWidget>("NAME");
			label.IsVisible = () => true;
			var font = Game.Renderer.Fonts[label.Font];

			var clientName = new CachedTransform<MapStatus, string>(s =>
			{
				var name = c.Name;
				if (c.IsBot && !map.TryGetMessage(c.Name, out name))
					name = FluentProvider.GetMessage(BotPlayer);

				return WidgetUtils.TruncateText(name, label.Bounds.Width, font);
			});

			label.GetText = () => clientName.Update(map.Status);

			SetupProfileWidget(parent, c, orderManager, worldRenderer);
		}

		public static void SetupEditableSlotWidget(Widget parent, Session.Slot s, Session.Client c,
			OrderManager orderManager, MapPreview map, ModData modData)
		{
			var slot = parent.Get<DropDownButtonWidget>("SLOT_OPTIONS");
			slot.IsVisible = () => true;
			slot.IsDisabled = () => orderManager.LocalClient.IsReady;

			var truncated = new CachedTransform<string, string>(name =>
				WidgetUtils.TruncateText(name, slot.Bounds.Width - slot.Bounds.Height - slot.LeftMargin - slot.RightMargin,
				Game.Renderer.Fonts[slot.Font]));

			var closed = FluentProvider.GetMessage(Closed);
			var open = FluentProvider.GetMessage(Open);

			var clientName = new CachedTransform<MapStatus, string>(s =>
			{
				if (c.IsBot)
				{
					if (map.TryGetMessage(c.Name, out var message))
						return message;
					else
						return FluentProvider.GetMessage(BotPlayer);
				}

				return c.Name;
			});

			slot.GetText = () => truncated.Update(c != null ?
				clientName.Update(map.Status)
				: s.Closed ? closed : open);

			slot.OnMouseDown = _ => ShowSlotDropDown(slot, s, c, orderManager, map, modData);

			// Ensure Name selector (if present) is hidden
			HideChildWidget(parent, "NAME");
		}

		public static void SetupSlotWidget(Widget parent, ModData modData, Session.Slot s, Session.Client c)
		{
			var name = parent.Get<LabelWidget>("NAME");
			name.IsVisible = () => true;
			name.GetText = () => c != null ? c.Name : s.Closed
				? FluentProvider.GetMessage(Closed)
				: FluentProvider.GetMessage(Open);

			// Ensure Slot selector (if present) is hidden
			HideChildWidget(parent, "SLOT_OPTIONS");
		}

		public static void SetupPlayerActionWidget(Widget parent, Session.Client c, OrderManager orderManager,
			WorldRenderer worldRenderer, Widget lobby, Action before, Action after)
		{
			var slot = parent.Get<DropDownButtonWidget>("PLAYER_ACTION");
			slot.IsVisible = () => Game.IsHost && c.Index != orderManager.LocalClient.Index;
			slot.IsDisabled = () => orderManager.LocalClient.IsReady;

			var truncated = new CachedTransform<string, string>(name =>
				WidgetUtils.TruncateText(name, slot.Bounds.Width - slot.Bounds.Height - slot.LeftMargin - slot.RightMargin,
				Game.Renderer.Fonts[slot.Font]));

			slot.GetText = () => truncated.Update(c != null ? c.Name : string.Empty);
			slot.OnMouseDown = _ => ShowPlayerActionDropDown(slot, c, orderManager, lobby, before, after);

			SetupProfileWidget(slot, c, orderManager, worldRenderer);

			// Ensure Name selector (if present) is hidden
			HideChildWidget(parent, "NAME");
		}

		public static void SetupKickSpectatorsWidget(Widget parent, OrderManager orderManager, Widget lobby, Action before, Action after, bool skirmishMode)
		{
			var checkBox = parent.Get<CheckboxWidget>("TOGGLE_SPECTATORS");
			checkBox.IsChecked = () => orderManager.LobbyInfo.GlobalSettings.AllowSpectators;
			checkBox.IsVisible = () => orderManager.LocalClient.IsAdmin && !skirmishMode;
			checkBox.IsDisabled = () => false;

			void OkPressed()
			{
				orderManager.IssueOrder(Order.Command($"allow_spectators {!orderManager.LobbyInfo.GlobalSettings.AllowSpectators}"));
				orderManager.IssueOrders(
					orderManager.LobbyInfo.Clients.Where(
						c => c.IsObserver && !c.IsAdmin).Select(
							client => Order.Command($"kick {client.Index} {client.Name}")).ToArray());

				after();
			}

			checkBox.OnClick = () =>
			{
				before();

				var spectatorCount = orderManager.LobbyInfo.Clients.Count(c => c.IsObserver);
				if (spectatorCount > 0)
				{
					Game.LoadWidget(null, "KICK_SPECTATORS_DIALOG", lobby.Get("TOP_PANELS_ROOT"), new WidgetArgs
					{
						{ "clientCount", spectatorCount },
						{ "okPressed", OkPressed },
						{ "cancelPressed", after }
					});
				}
				else
				{
					orderManager.IssueOrder(Order.Command($"allow_spectators {!orderManager.LobbyInfo.GlobalSettings.AllowSpectators}"));
					after();
				}
			};
		}

		public static void SetupEditableColorWidget(Widget parent, Session.Slot s, Session.Client c,
			OrderManager orderManager, WorldRenderer worldRenderer, IColorPickerManagerInfo colorManager)
		{
			var colorDropdown = parent.Get<DropDownButtonWidget>("COLOR");
			colorDropdown.IsDisabled = () => (s != null && s.LockColor) || orderManager.LocalClient.IsReady;
			colorDropdown.OnMouseDown = _ => colorManager.ShowColorDropDown(colorDropdown, c.Color, c.Faction, worldRenderer, color =>
			{
				if (c == orderManager.LocalClient)
				{
					Game.Settings.Player.Color = color;
					Game.Settings.Save();
				}

				orderManager.IssueOrder(Order.Command($"color {c.Index} {color}"));
			});

			SetupColorWidget(colorDropdown, c);
		}

		public static void SetupColorWidget(Widget parent, Session.Client c)
		{
			var color = parent.Get<ColorBlockWidget>("COLORBLOCK");
			color.GetColor = () => c.Color;
		}

		public static void SetupEditableFactionWidget(Widget parent, Session.Slot s, Session.Client c, OrderManager orderManager,
			Dictionary<string, LobbyFaction> factions)
		{
			var dropdown = parent.Get<DropDownButtonWidget>("FACTION");
			dropdown.IsDisabled = () => s.LockFaction || orderManager.LocalClient.IsReady;
			dropdown.OnMouseDown = _ => ShowFactionDropDown(dropdown, c, orderManager, factions);

			var description = factions[c.Faction].Description != null ? FluentProvider.GetMessage(factions[c.Faction].Description) : null;
			var (text, desc) = SplitOnFirstToken(description);
			dropdown.GetTooltipText = () => text;
			dropdown.GetTooltipDesc = () => desc;

			SetupFactionWidget(dropdown, c, factions);
		}

		public static void SetupFactionWidget(Widget parent, Session.Client c, Dictionary<string, LobbyFaction> factions)
		{
			var factionName = parent.Get<LabelWidget>("FACTIONNAME");
			var font = Game.Renderer.Fonts[factionName.Font];
			var truncated = new CachedTransform<string, string>(clientFaction =>
				WidgetUtils.TruncateText(FluentProvider.GetMessage(factions[clientFaction].Name), factionName.Bounds.Width, font));
			factionName.GetText = () => truncated.Update(c.Faction);

			var factionFlag = parent.Get<ImageWidget>("FACTIONFLAG");
			factionFlag.GetImageName = () => c.Faction;
			factionFlag.GetImageCollection = () => "flags";
		}

		public static void SetupEditableTeamWidget(Widget parent, Session.Slot s, Session.Client c, OrderManager orderManager, MapPreview map)
		{
			var dropdown = parent.Get<DropDownButtonWidget>("TEAM_DROPDOWN");
			dropdown.IsVisible = () => true;
			dropdown.IsDisabled = () => s.LockTeam || orderManager.LocalClient.IsReady;
			dropdown.OnMouseDown = _ => ShowTeamDropDown(dropdown, c, orderManager, map.PlayerCount);
			dropdown.GetText = () => (c.Team == 0) ? "-" : c.Team.ToString(NumberFormatInfo.CurrentInfo);

			HideChildWidget(parent, "TEAM");
		}

		public static void SetupTeamWidget(Widget parent, Session.Client c)
		{
			var team = parent.Get<LabelWidget>("TEAM");
			team.IsVisible = () => true;
			team.GetText = () => (c.Team == 0) ? "-" : c.Team.ToString(NumberFormatInfo.CurrentInfo);
			HideChildWidget(parent, "TEAM_DROPDOWN");
		}

		public static void SetupEditableHandicapWidget(Widget parent, Session.Slot s, Session.Client c, OrderManager orderManager)
		{
			var dropdown = parent.Get<DropDownButtonWidget>("HANDICAP_DROPDOWN");
			dropdown.IsVisible = () => true;
			dropdown.IsDisabled = () => s.LockHandicap || orderManager.LocalClient.IsReady;
			dropdown.OnMouseDown = _ => ShowHandicapDropDown(dropdown, c, orderManager);

			var handicapLabel = new CachedTransform<int, string>(h => $"{h}%");
			dropdown.GetText = () => handicapLabel.Update(c.Handicap);

			HideChildWidget(parent, "HANDICAP");
		}

		public static void SetupHandicapWidget(Widget parent, Session.Client c)
		{
			var team = parent.Get<LabelWidget>("HANDICAP");
			team.IsVisible = () => true;

			var handicapLabel = new CachedTransform<int, string>(h => $"{h}%");
			team.GetText = () => handicapLabel.Update(c.Handicap);
			HideChildWidget(parent, "HANDICAP_DROPDOWN");
		}

		public static void SetupEditableSpawnWidget(Widget parent, Session.Slot s, Session.Client c, OrderManager orderManager, MapPreview map)
		{
			var dropdown = parent.Get<DropDownButtonWidget>("SPAWN_DROPDOWN");
			dropdown.IsVisible = () => true;
			dropdown.IsDisabled = () => s.LockSpawn || orderManager.LocalClient.IsReady;
			dropdown.OnMouseDown = _ =>
			{
				var spawnPoints = Enumerable.Range(0, map.SpawnPoints.Length + 1).Except(
					orderManager.LobbyInfo.Clients.Where(
					client => client != c && client.SpawnPoint != 0).Select(client => client.SpawnPoint))
					.Except(orderManager.LobbyInfo.DisabledSpawnPoints);
				ShowSpawnDropDown(dropdown, c, orderManager, spawnPoints);
			};
			dropdown.GetText = () => (c.SpawnPoint == 0) ? "-" : Convert.ToChar('A' - 1 + c.SpawnPoint).ToString();

			HideChildWidget(parent, "SPAWN");
		}

		public static void SetupSpawnWidget(Widget parent, Session.Client c)
		{
			var spawn = parent.Get<LabelWidget>("SPAWN");
			spawn.IsVisible = () => true;
			spawn.GetText = () => (c.SpawnPoint == 0) ? "-" : Convert.ToChar('A' - 1 + c.SpawnPoint).ToString();
			HideChildWidget(parent, "SPAWN_DROPDOWN");
		}

		public static void SetupEditableReadyWidget(Widget parent, Session.Client c, OrderManager orderManager, MapPreview map, bool isEnabled)
		{
			var status = parent.Get<CheckboxWidget>("STATUS_CHECKBOX");
			status.IsVisible = () => true;
			status.IsDisabled = () => c.Bot != null || map.Status != MapStatus.Available || !isEnabled;
			if (c.Bot == null)
			{
				var isChecked = new PredictedCachedTransform<Session.Client, bool>(cc => cc.IsReady);
				status.IsChecked = () => isChecked.Update(c);
				status.OnClick = () =>
				{
					var state = isChecked.Update(c) ? Session.ClientState.NotReady : Session.ClientState.Ready;
					orderManager.IssueOrder(Order.Command($"state {state}"));
					isChecked.Predict(!c.IsReady);
				};
			}
			else
				status.IsChecked = () => true;
		}

		public static void SetupReadyWidget(Widget parent, Session.Client c)
		{
			parent.Get<ImageWidget>("STATUS_IMAGE").IsVisible = () => c.IsReady || c.Bot != null;
		}

		public static void HideReadyWidgets(Widget parent)
		{
			HideChildWidget(parent, "STATUS_CHECKBOX");
			HideChildWidget(parent, "STATUS_IMAGE");
		}

		static void HideChildWidget(Widget parent, string widgetId)
		{
			var widget = parent.GetOrNull(widgetId);
			if (widget != null)
				widget.IsVisible = () => false;
		}
	}
}
