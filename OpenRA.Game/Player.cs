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
using Eluant;
using Eluant.ObjectBinding;
using OpenRA.Graphics;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Scripting;
using OpenRA.Support;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA
{
	[Flags]
	public enum PowerState
	{
		Normal = 1,
		Low = 2,
		Critical = 4
	}

	public enum WinState { Undefined, Won, Lost }

	public class PlayerBitMask { }

	public class Player : IScriptBindable, IScriptNotifyBind, ILuaTableBinding, ILuaEqualityBinding, ILuaToStringBinding
	{
		[FluentReference("name", "number")]
		const string EnumeratedBotName = "enumerated-bot-name";

		public readonly Actor PlayerActor;
		public readonly string PlayerName;
		public readonly string InternalName;
		public readonly FactionInfo Faction;
		public readonly bool NonCombatant = false;
		public readonly bool Playable = true;
		public readonly int ClientIndex;
		public readonly CPos HomeLocation;
		public readonly int Handicap;
		public readonly PlayerReference PlayerReference;
		public readonly bool IsBot;
		public readonly string BotType;
		public readonly Shroud Shroud;
		public readonly FrozenActorLayer FrozenActorLayer;

		readonly Color color;

		/// <summary>Returns player color with relationship colors applied.</summary>
		public Color Color { get; private set; }

		/// <summary>The faction (including Random, etc.) that was selected in the lobby.</summary>
		public readonly FactionInfo DisplayFaction;

		/// <summary>The spawn point index that was assigned for client-based players.</summary>
		public readonly int SpawnPoint;

		/// <summary>The spawn point index (including 0 for Random) that was selected in the lobby for client-based players.</summary>
		public readonly int DisplaySpawnPoint;

		public WinState WinState = WinState.Undefined;
		public bool HasObjectives = false;

		// Players in mission maps must not leave the player view
		public bool Spectating => !inMissionMap && (spectating || WinState != WinState.Undefined);

		public World World { get; }

		readonly bool inMissionMap;
		readonly bool spectating;
		readonly IUnlocksRenderPlayer[] unlockRenderPlayer;
		readonly INotifyPlayerDisconnected[] notifyDisconnected;

		readonly IReadOnlyCollection<IBotInfo> botInfos;
		string resolvedPlayerName;

		// Each player is identified with a unique bit in the set
		// Cache masks for the player's index and ally/enemy player indices for performance.
		public LongBitSet<PlayerBitMask> PlayerMask;
		public LongBitSet<PlayerBitMask> AlliedPlayersMask = default;
		public LongBitSet<PlayerBitMask> EnemyPlayersMask = default;

		public bool UnlockedRenderPlayer
		{
			get
			{
				if (unlockRenderPlayer.Any(x => x.RenderPlayerUnlocked))
					return true;

				return WinState != WinState.Undefined && !inMissionMap;
			}
		}

		/// <summary>The chosen player name including localized and enumerated bot names.</summary>
		public string ResolvedPlayerName
		{
			get
			{
				resolvedPlayerName ??= ResolvePlayerName();
				return resolvedPlayerName;
			}
		}

		public static FactionInfo ResolveFaction(
			string factionName, IEnumerable<FactionInfo> factionInfos, MersenneTwister playerRandom, bool requireSelectable = true)
		{
			var selectableFactions = factionInfos
				.Where(f => !requireSelectable || f.Selectable)
				.ToList();

			var selected = selectableFactions.FirstOrDefault(f => f.InternalName == factionName)
				?? selectableFactions.Random(playerRandom);

			// Don't loop infinite
			for (var i = 0; i <= 10 && selected.RandomFactionMembers.Count > 0; i++)
			{
				var faction = selected.RandomFactionMembers.Random(playerRandom);
				selected = selectableFactions.FirstOrDefault(f => f.InternalName == faction);

				if (selected == null)
					throw new YamlException($"Unknown faction: {faction}");
			}

			return selected;
		}

		static FactionInfo ResolveFaction(World world, string factionName, MersenneTwister playerRandom, bool requireSelectable)
		{
			var factionInfos = world.Map.Rules.Actors[SystemActors.World].TraitInfos<FactionInfo>();
			return ResolveFaction(factionName, factionInfos, playerRandom, requireSelectable);
		}

		static FactionInfo ResolveDisplayFaction(World world, string factionName)
		{
			var factions = world.Map.Rules.Actors[SystemActors.World].TraitInfos<FactionInfo>();

			return factions.FirstOrDefault(f => f.InternalName == factionName) ?? factions.First();
		}

		public Player(World world, Session.Client client, PlayerReference pr, MersenneTwister playerRandom)
		{
			World = world;
			InternalName = pr.Name;
			PlayerReference = pr;

			inMissionMap = world.Map.Visibility.HasFlag(MapVisibility.MissionSelector);
			botInfos = World.Map.Rules.Actors[SystemActors.Player].TraitInfos<IBotInfo>();

			// Real player or host-created bot
			if (client != null)
			{
				ClientIndex = client.Index;
				color = client.Color;
				Color = color;
				PlayerName = client.Name;

				BotType = client.Bot;
				Faction = ResolveFaction(world, client.Faction, playerRandom, !pr.LockFaction);
				DisplayFaction = ResolveDisplayFaction(world, client.Faction);

				var assignSpawnPoints = world.WorldActor.TraitOrDefault<IAssignSpawnPoints>();
				HomeLocation = assignSpawnPoints?.AssignHomeLocation(world, client, playerRandom) ?? pr.HomeLocation;
				SpawnPoint = assignSpawnPoints?.SpawnPointForPlayer(this) ?? client.SpawnPoint;
				DisplaySpawnPoint = client.SpawnPoint;

				Handicap = client.Handicap;
			}
			else
			{
				// Map player
				ClientIndex = world.LobbyInfo.Clients.FirstOrDefault(c => c.IsAdmin)?.Index ?? 0; // Owned by the host (TODO: fix this)
				color = pr.Color;
				Color = pr.Color;
				PlayerName = pr.Name;
				NonCombatant = pr.NonCombatant;
				Playable = pr.Playable;
				spectating = pr.Spectating;
				BotType = pr.Bot;
				Faction = ResolveFaction(world, pr.Faction, playerRandom, false);
				DisplayFaction = ResolveDisplayFaction(world, pr.Faction);
				HomeLocation = pr.HomeLocation;
				SpawnPoint = DisplaySpawnPoint = 0;
				Handicap = pr.Handicap;
			}

			if (!spectating)
				PlayerMask = new LongBitSet<PlayerBitMask>(InternalName);

			// Set this property before running any Created callbacks on the player actor
			IsBot = BotType != null;

			// Special case handling is required for the Player actor:
			// Since Actor.Created would be called before PlayerActor is assigned here
			// querying player traits in INotifyCreated.Created would crash.
			// Therefore assign the uninitialized actor and run the Created callbacks
			// by calling Initialize ourselves.
			var playerActorType = world.Type == WorldType.Editor ? SystemActors.EditorPlayer : SystemActors.Player;
			PlayerActor = new Actor(world, playerActorType.ToString(), [new OwnerInit(this)]);
			PlayerActor.Initialize(true);

			Shroud = PlayerActor.Trait<Shroud>();
			FrozenActorLayer = PlayerActor.TraitOrDefault<FrozenActorLayer>();

			// Enable the bot logic on the host
			if (IsBot && Game.IsHost)
			{
				var logic = PlayerActor.TraitsImplementing<IBot>().FirstOrDefault(b => b.Info.Type == BotType);
				if (logic == null)
					Log.Write("debug", $"Invalid bot type: {BotType}");
				else
					logic.Activate(this);
			}

			unlockRenderPlayer = PlayerActor.TraitsImplementing<IUnlocksRenderPlayer>().ToArray();
			notifyDisconnected = PlayerActor.TraitsImplementing<INotifyPlayerDisconnected>().ToArray();
		}

		public override string ToString()
		{
			return $"{ResolvedPlayerName} ({ClientIndex})";
		}

		string ResolvePlayerName()
		{
			if (IsBot)
			{
				var botInfo = botInfos.First(b => b.Type == BotType);
				var botsOfSameType = World.Players.Where(c => c.BotType == BotType).ToArray();
				return FluentProvider.GetMessage(EnumeratedBotName,
					"name", FluentProvider.GetMessage(botInfo.Name),
					"number", botsOfSameType.IndexOf(this) + 1);
			}

			return PlayerName;
		}

		public PlayerRelationship RelationshipWith(Player other)
		{
			if (this == other)
				return PlayerRelationship.Ally;

			// Observers are considered allies to active combatants
			if (other == null || other.Spectating)
				return NonCombatant ? PlayerRelationship.Neutral : PlayerRelationship.Ally;

			if (AlliedPlayersMask.Overlaps(other.PlayerMask))
				return PlayerRelationship.Ally;

			if (EnemyPlayersMask.Overlaps(other.PlayerMask))
				return PlayerRelationship.Enemy;

			return PlayerRelationship.Neutral;
		}

		/// <summary>Returns true if player is null.</summary>
		public bool IsAlliedWith(Player p)
		{
			return RelationshipWith(p) == PlayerRelationship.Ally;
		}

		/// <summary>Returns <see cref="color"/>, ignoring player relationship colors.</summary>
		public static Color GetColor(Player p) => p.color;

		public static void SetupRelationshipColors(Player[] players, Player viewer, WorldRenderer worldRenderer, bool firstRun)
		{
			foreach (var p in players)
			{
				p.Color = PlayerRelationshipColor(p, viewer);
				worldRenderer.UpdatePalettesForPlayer(p.InternalName, p.Color, !firstRun);
			}
		}

		public static Color PlayerRelationshipColor(Player player, Player viewer)
		{
			if (!Game.Settings.Game.UsePlayerStanceColors || viewer == null || viewer.Spectating)
				return player.color;

			if (viewer == player)
				return ChromeMetrics.Get<Color>("PlayerStanceColorSelf");

			if (player.IsAlliedWith(viewer))
				return ChromeMetrics.Get<Color>("PlayerStanceColorAllies");

			if (player.NonCombatant)
				return ChromeMetrics.Get<Color>("PlayerStanceColorNeutrals");

			return ChromeMetrics.Get<Color>("PlayerStanceColorEnemies");
		}

		internal void PlayerDisconnected(Player p)
		{
			foreach (var np in notifyDisconnected)
				np.PlayerDisconnected(PlayerActor, p);
		}

		#region Scripting interface

		Lazy<ScriptPlayerInterface> luaInterface;
		public void OnScriptBind(ScriptContext context)
		{
			luaInterface ??= Exts.Lazy(() => new ScriptPlayerInterface(context, this));
		}

		public LuaValue this[LuaRuntime runtime, LuaValue keyValue]
		{
			get => luaInterface.Value[runtime, keyValue];
			set => luaInterface.Value[runtime, keyValue] = value;
		}

		public LuaValue Equals(LuaRuntime runtime, LuaValue left, LuaValue right)
		{
			if (!left.TryGetClrValue(out Player a) || !right.TryGetClrValue(out Player b))
				return false;

			return a == b;
		}

		public LuaValue ToString(LuaRuntime runtime)
		{
			return $"Player ({ResolvedPlayerName})";
		}

		#endregion
	}
}
