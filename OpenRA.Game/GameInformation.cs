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

namespace OpenRA
{
	public class GameInformation
	{
		[FluentReference("name", "number")]
		const string EnumeratedBotName = "enumerated-bot-name";

		public string Mod;
		public string Version;

		public string MapUid;
		public string MapTitle;
		public int FinalGameTick;

		/// <summary>Game start timestamp (when the recoding started).</summary>
		public DateTime StartTimeUtc;

		/// <summary>Game end timestamp (when the recoding stopped).</summary>
		public DateTime EndTimeUtc;

		/// <summary>Gets the game's duration, from the time the game started until the replay recording stopped.</summary>
		public TimeSpan Duration => EndTimeUtc > StartTimeUtc ? EndTimeUtc - StartTimeUtc : TimeSpan.Zero;

		public IList<Player> Players { get; }
		public HashSet<int> DisabledSpawnPoints = [];
		public MapPreview MapPreview => Game.ModData.MapCache[MapUid];
		public IEnumerable<Player> HumanPlayers { get { return Players.Where(p => p.IsHuman); } }
		public bool IsSinglePlayer => HumanPlayers.Count() == 1;

		readonly Dictionary<OpenRA.Player, Player> playersByRuntime;

		public GameInformation()
		{
			Players = [];
			playersByRuntime = [];
		}

		public static GameInformation Deserialize(string data, string path)
		{
			try
			{
				var info = new GameInformation();

				var nodes = MiniYaml.FromString(data, path);
				foreach (var node in nodes)
				{
					var keyParts = node.Key.Split('@');

					switch (keyParts[0])
					{
						case "Root":
							FieldLoader.Load(info, node.Value);
							break;

						case "Player":
							info.Players.Add(FieldLoader.Load<Player>(node.Value));
							break;
					}
				}

				return info;
			}
			catch (YamlException)
			{
				Log.Write("debug", $"GameInformation deserialized invalid MiniYaml:\n{data}");
				throw;
			}
		}

		public string Serialize()
		{
			var nodes = new List<MiniYamlNode>
			{
				new("Root", FieldSaver.Save(this))
			};

			for (var i = 0; i < Players.Count; i++)
				nodes.Add(new MiniYamlNode($"Player@{i}", FieldSaver.Save(Players[i])));

			return nodes.WriteToString();
		}

		/// <summary>Adds the player information at start-up.</summary>
		public void AddPlayer(OpenRA.Player runtimePlayer, Session lobbyInfo)
		{
			ArgumentNullException.ThrowIfNull(runtimePlayer);

			ArgumentNullException.ThrowIfNull(lobbyInfo);

			// We don't care about spectators and map players
			if (runtimePlayer.NonCombatant || !runtimePlayer.Playable)
				return;

			// Find the lobby client that created the runtime player
			var client = lobbyInfo.ClientWithIndex(runtimePlayer.ClientIndex);
			if (client == null)
				return;

			var player = new Player
			{
				ClientIndex = runtimePlayer.ClientIndex,
				Name = runtimePlayer.PlayerName,
				IsHuman = !runtimePlayer.IsBot,
				IsBot = runtimePlayer.IsBot,
				BotType = runtimePlayer.BotType,
				FactionName = runtimePlayer.Faction.Name,
				FactionId = runtimePlayer.Faction.InternalName,
				DisplayFactionName = runtimePlayer.DisplayFaction.Name,
				DisplayFactionId = runtimePlayer.DisplayFaction.InternalName,
				Color = OpenRA.Player.GetColor(runtimePlayer),
				Team = client.Team,
				Handicap = client.Handicap,
				SpawnPoint = runtimePlayer.SpawnPoint,
				IsRandomFaction = runtimePlayer.Faction.InternalName != client.Faction,
				IsRandomSpawnPoint = runtimePlayer.DisplaySpawnPoint == 0,
				Fingerprint = client.Fingerprint
			};

			playersByRuntime.Add(runtimePlayer, player);
			Players.Add(player);
		}

		/// <summary>Gets the player information for the specified runtime player instance.</summary>
		public Player GetPlayer(OpenRA.Player runtimePlayer)
		{
			playersByRuntime.TryGetValue(runtimePlayer, out var player);

			return player;
		}

		public string ResolvedPlayerName(Player player)
		{
			if (player.IsBot)
			{
				var number = Players.Where(p => p.BotType == player.BotType).ToList().IndexOf(player) + 1;
				return FluentProvider.GetMessage(EnumeratedBotName,
						"name", FluentProvider.GetMessage(player.Name),
						"number", number);
			}

			return player.Name;
		}

		public class Player
		{
			#region Start-up information

			public int ClientIndex;

			/// <summary>The player name, not guaranteed to be unique.</summary>
			public string Name;
			public bool IsHuman;
			public bool IsBot;
			public string BotType;

			/// <summary>The faction's display name.</summary>
			public string FactionName;

			/// <summary>The faction ID, a.k.a. the faction's internal name.</summary>
			public string FactionId;
			public Color Color;

			/// <summary>The faction (including Random, etc.) that was selected in the lobby.</summary>
			public string DisplayFactionName;
			public string DisplayFactionId;

			/// <summary>The team ID on start-up, or 0 if the player is not part of a team.</summary>
			public int Team;
			public int SpawnPoint;
			public int Handicap;

			/// <summary>True if the faction was chosen at random; otherwise, false.</summary>
			public bool IsRandomFaction;

			/// <summary>True if the spawn point was chosen at random; otherwise, false.</summary>
			public bool IsRandomSpawnPoint;

			/// <summary>Player authentication fingerprint for the OpenRA forum.</summary>
			public string Fingerprint;

			#endregion

			#region

			/// <summary>The game outcome for this player.</summary>
			public WinState Outcome;

			/// <summary>The time when this player won or lost the game.</summary>
			public DateTime OutcomeTimestampUtc;

			/// <summary>The frame at which this player disconnected.</summary>
			public int DisconnectFrame;

			#endregion
		}
	}
}
