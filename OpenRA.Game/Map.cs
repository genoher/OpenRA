#region Copyright & License Information
/*
 * Copyright 2007-2011 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenRA.FileFormats;
using OpenRA.Traits;

namespace OpenRA
{
	public class Map
	{
		[FieldLoader.Ignore] IFolder Container;
		public string Path { get; private set; }

		// Yaml map data
		public string Uid { get; private set; }
		public int MapFormat;
		public bool Selectable;
		public bool UseAsShellmap;
		public string RequiresMod;

		public string Title;
		public string Type = "Conquest";
		public string Description;
		public string Author;
		public string Tileset;

		[FieldLoader.Ignore] public Lazy<Dictionary<string, ActorReference>> Actors;

		public int PlayerCount { get { return Players.Count(p => p.Value.Playable); } }

		public int2[] GetSpawnPoints()
		{
			return Actors.Value.Values
				.Where(a => a.Type == "mpspawn")
				.Select(a => a.InitDict.Get<LocationInit>().value)
				.ToArray();
		}

		public Rectangle Bounds;

		// Yaml map data
		[FieldLoader.Ignore] public Dictionary<string, PlayerReference> Players = new Dictionary<string, PlayerReference>();
		[FieldLoader.Ignore] public Lazy<List<SmudgeReference>> Smudges;

		[FieldLoader.Ignore] public List<MiniYamlNode> Rules = new List<MiniYamlNode>();
		[FieldLoader.Ignore] public List<MiniYamlNode> Sequences = new List<MiniYamlNode>();
		[FieldLoader.Ignore] public List<MiniYamlNode> Weapons = new List<MiniYamlNode>();
		[FieldLoader.Ignore] public List<MiniYamlNode> Voices = new List<MiniYamlNode>();

		// Binary map data
		[FieldLoader.Ignore] public byte TileFormat = 1;
		public int2 MapSize;

		[FieldLoader.Ignore] public Lazy<TileReference<ushort, byte>[,]> MapTiles;
		[FieldLoader.Ignore] public Lazy<TileReference<byte, byte>[,]> MapResources;
		[FieldLoader.Ignore] public string [,] CustomTerrain;

		public Map() {}	/* doesn't really produce a valid map, but enough for loading a mod */

		public static Map FromTileset(string tileset)
		{
			var tile = OpenRA.Rules.TileSets[tileset].Templates.First();
			var tileRef = new TileReference<ushort,byte> { type = tile.Key, index = (byte)0 };

			Map map = new Map()
			{
				Title = "Name your map here",
				Description = "Describe your map here",
				Author = "Your name here",
				MapSize = new int2(1, 1),
				Tileset = tileset,
				MapResources = Lazy.New(() => new TileReference<byte, byte>[1, 1]),
				MapTiles = Lazy.New(() => new TileReference<ushort, byte>[1, 1] { { tileRef } }),
				Actors = Lazy.New(() => new Dictionary<string, ActorReference>()),
				Smudges = Lazy.New(() => new List<SmudgeReference>())
			};

			return map;
		}

		void AssertExists(string filename)
		{
			using(var s = Container.GetContent(filename))
				if (s == null)
					throw new InvalidOperationException("Required file {0} not present in this map".F(filename));
		}

		public Map(string path)
		{
			Path = path;
			Container = FileSystem.OpenPackage(path, int.MaxValue);

			AssertExists("map.yaml");
			AssertExists("map.bin");

			var yaml = new MiniYaml( null, MiniYaml.FromStream(Container.GetContent("map.yaml")) );
			FieldLoader.Load(this, yaml);
			Uid = ComputeHash();

			// Support for formats 1-3 dropped 2011-02-11.
			// Use release-20110207 to convert older maps to format 4
			// Use release-20110511 to convert older maps to format 5
			if (MapFormat < 5)
				throw new InvalidDataException("Map format {0} is not supported.\n File: {1}".F(MapFormat, path));

			// Load players
			foreach (var kv in yaml.NodesDict["Players"].NodesDict)
			{
				var player = new PlayerReference(kv.Value);
				Players.Add(player.Name, player);
			}

			Actors = Lazy.New(() =>
			{
				var ret =  new Dictionary<string, ActorReference>();
				foreach (var kv in yaml.NodesDict["Actors"].NodesDict)
					ret.Add(kv.Key, new ActorReference(kv.Value.Value, kv.Value.NodesDict));
				return ret;
			});

			// Smudges
			Smudges = Lazy.New(() =>
			{
				var ret = new List<SmudgeReference>();
				foreach (var kv in yaml.NodesDict["Smudges"].NodesDict)
				{
					var vals = kv.Key.Split(' ');
					var loc = vals[1].Split(',');
					ret.Add(new SmudgeReference(vals[0], new int2(int.Parse(loc[0]), int.Parse(loc[1])), int.Parse(vals[2])));
				}

				return ret;
			});

			Rules = NodesOrEmpty(yaml, "Rules");
			Sequences = NodesOrEmpty(yaml, "Sequences");
			Weapons = NodesOrEmpty(yaml, "Weapons");
			Voices = NodesOrEmpty(yaml, "Voices");

			CustomTerrain = new string[MapSize.X, MapSize.Y];

			MapTiles = Lazy.New(() => LoadMapTiles());
			MapResources = Lazy.New(() => LoadResourceTiles());
		}

		static List<MiniYamlNode> NodesOrEmpty(MiniYaml y, string s)
		{
			return y.NodesDict.ContainsKey(s) ? y.NodesDict[s].Nodes : new List<MiniYamlNode>();
		}

		public void Save(string toPath)
		{
			MapFormat = 5;

			var root = new List<MiniYamlNode>();
			var fields = new []
			{
				"Selectable",
				"MapFormat",
				"RequiresMod",
				"Title",
				"Description",
				"Author",
				"Tileset",
				"MapSize",
				"Bounds",
				"UseAsShellmap",
				"Type",
			};

			foreach (var field in fields)
			{
				var f = this.GetType().GetField(field);
				if (f.GetValue(this) == null) continue;
				root.Add( new MiniYamlNode( field, FieldSaver.FormatValue( this, f ) ) );
			}

			root.Add( new MiniYamlNode( "Players", null,
				Players.Select( p => new MiniYamlNode(
					"PlayerReference@{0}".F( p.Key ),
					FieldSaver.SaveDifferences( p.Value, new PlayerReference() ) ) ).ToList() ) );

			root.Add( new MiniYamlNode( "Actors", null,
				Actors.Value.Select( x => new MiniYamlNode(
					x.Key,
					x.Value.Save() ) ).ToList() ) );

			root.Add(new MiniYamlNode("Smudges", MiniYaml.FromList<SmudgeReference>( Smudges.Value )));
			root.Add(new MiniYamlNode("Rules", null, Rules));
			root.Add(new MiniYamlNode("Sequences", null, Sequences));
			root.Add(new MiniYamlNode("Weapons", null, Weapons));
			root.Add(new MiniYamlNode("Voices", null, Voices));

			var entries = new Dictionary<string, byte[]>();
			entries.Add("map.bin", SaveBinaryData());
			var s = root.WriteToString();
			entries.Add("map.yaml", Encoding.UTF8.GetBytes(s));

			// Saving the map to a new location
			if (toPath != Path)
			{
				Path = toPath;

				// Create a new map package
				// TODO: Add other files (custom assets) to the entries list
				Container = FileSystem.CreatePackage(Path, int.MaxValue, entries);
			}

			// Update existing package
			Container.Write(entries);
		}

		static byte ReadByte(Stream s)
		{
			int ret = s.ReadByte();
			if (ret == -1)
				throw new NotImplementedException();
			return (byte)ret;
		}

		static ushort ReadWord(Stream s)
		{
			ushort ret = ReadByte(s);
			ret |= (ushort)(ReadByte(s) << 8);

			return ret;
		}

		public TileReference<ushort, byte>[,] LoadMapTiles()
		{
			var tiles = new TileReference<ushort, byte>[MapSize.X, MapSize.Y];
			using (var dataStream = Container.GetContent("map.bin"))
			{
				if (ReadByte(dataStream) != 1)
					throw new InvalidDataException("Unknown binary map format");

				// Load header info
				var width = ReadWord(dataStream);
				var height = ReadWord(dataStream);

				if (width != MapSize.X || height != MapSize.Y)
					throw new InvalidDataException("Invalid tile data");


				// Load tile data
				for (int i = 0; i < MapSize.X; i++)
					for (int j = 0; j < MapSize.Y; j++)
					{
						ushort tile = ReadWord(dataStream);
						byte index = ReadByte(dataStream);
						if (index == byte.MaxValue)
							index = (byte)(i % 4 + (j % 4) * 4);

						tiles[i, j] = new TileReference<ushort, byte>(tile, index);
					}
			}
			return tiles;
		}

		public TileReference<byte, byte>[,] LoadResourceTiles()
		{
			var resources = new TileReference<byte, byte>[MapSize.X, MapSize.Y];

			using (var dataStream = Container.GetContent("map.bin"))
			{
				if (ReadByte(dataStream) != 1)
					throw new InvalidDataException("Unknown binary map format");

				// Load header info
				var width = ReadWord(dataStream);
				var height = ReadWord(dataStream);

				if (width != MapSize.X || height != MapSize.Y)
					throw new InvalidDataException("Invalid tile data");

				// Skip past tile data
				for (var i = 0; i < 3*MapSize.X*MapSize.Y; i++)
					ReadByte(dataStream);

				// Load resource data
				for (var i = 0; i < MapSize.X; i++)
					for (var j = 0; j < MapSize.Y; j++)
				{
					byte type = ReadByte(dataStream);
					byte index = ReadByte(dataStream);
					resources[i, j] = new TileReference<byte, byte>(type, index);
				}
			}
			return resources;
		}

		public byte[] SaveBinaryData()
		{
			var dataStream = new MemoryStream();
			using (var writer = new BinaryWriter(dataStream))
			{
				// File header consists of a version byte, followed by 2 ushorts for width and height
				writer.Write(TileFormat);
				writer.Write((ushort)MapSize.X);
				writer.Write((ushort)MapSize.Y);

				if (!OpenRA.Rules.TileSets.ContainsKey(Tileset))
					throw new InvalidOperationException(
						"Tileset used by the map ({0}) does not exist in this mod. Valid tilesets are: {1}"
						.F(Tileset, OpenRA.Rules.TileSets.Keys.JoinWith(",")));

				// Tile data
				for (var i = 0; i < MapSize.X; i++)
					for (var j = 0; j < MapSize.Y; j++)
					{
						writer.Write(MapTiles.Value[i, j].type);
						var PickAny = OpenRA.Rules.TileSets[Tileset].Templates[MapTiles.Value[i, j].type].PickAny;
						writer.Write(PickAny ? (byte)(i % 4 + (j % 4) * 4) : MapTiles.Value[i, j].index);
					}

				// Resource data
				for (var i = 0; i < MapSize.X; i++)
					for (var j = 0; j < MapSize.Y; j++)
					{
						writer.Write(MapResources.Value[i, j].type);
						writer.Write(MapResources.Value[i, j].index);
					}
			}
			return dataStream.ToArray();
		}

		public bool IsInMap(int2 xy) { return IsInMap(xy.X, xy.Y); }
		public bool IsInMap(int x, int y) { return Bounds.Contains(x,y); }

		static T[,] ResizeArray<T>(T[,] ts, T t, int width, int height)
		{
			var result = new T[width, height];
			for (var i = 0; i < width; i++)
				for (var j = 0; j < height; j++)
					result[i, j] = i <= ts.GetUpperBound(0) && j <= ts.GetUpperBound(1)
						? ts[i, j] : t;
			return result;
		}

		public void Resize(int width, int height)		// editor magic.
		{
			var oldMapTiles = MapTiles.Value;
			var oldMapResources = MapResources.Value;

			MapTiles = Lazy.New(() => ResizeArray(oldMapTiles, oldMapTiles[0, 0], width, height));
			MapResources = Lazy.New(() => ResizeArray(oldMapResources, oldMapResources[0, 0], width, height));
			MapSize = new int2(width, height);
		}

		public void ResizeCordon(int left, int top, int right, int bottom)
		{
			Bounds = Rectangle.FromLTRB(left, top, right, bottom);
		}

		string ComputeHash()
		{
			// UID is calculated by taking an SHA1 of the yaml and binary data
			// Read the relevant data into a buffer
			var data = Container.GetContent("map.yaml").ReadAllBytes()
				.Concat(Container.GetContent("map.bin").ReadAllBytes()).ToArray();

			// Take the SHA1
			using (var csp = SHA1.Create())
				return new string(csp.ComputeHash(data).SelectMany(a => a.ToString("x2")).ToArray());
		}

		public void MakeDefaultPlayers()
		{
			Players.Clear();

			var firstRace = OpenRA.Rules.Info["world"].Traits
				.WithInterface<CountryInfo>().First(c => c.Selectable).Race;

			Players.Add("Neutral", new PlayerReference
			{
				Name = "Neutral",
				Race = firstRace,
				OwnsWorld = true,
				NonCombatant = true
			});

			var numSpawns = GetSpawnPoints().Length;
			for (var index = 0; index < numSpawns; index++)
			{
				var p = new PlayerReference
				{
					Name = "Multi{0}".F(index),
					Race = "Random",
					Playable = true,
					DefaultStartingUnits = true,
					Enemies = new[] { "Creeps" }
				};
				Players.Add(p.Name, p);
			}

			Players.Add("Creeps", new PlayerReference
			{
				Name = "Creeps",
				Race = firstRace,
				NonCombatant = true,
				Enemies = Players.Where(p => p.Value.Playable).Select(p => p.Key).ToArray()
			});
		}
	}
}
