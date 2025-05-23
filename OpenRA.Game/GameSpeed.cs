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
using OpenRA.Traits;

namespace OpenRA
{
	public class GameSpeed
	{
		[FluentReference]
		[FieldLoader.Require]
		public readonly string Name;

		[FieldLoader.Require]
		public readonly int Timestep;

		[FieldLoader.Require]
		public readonly int OrderLatency;
	}

	public class GameSpeeds : IGlobalModData
	{
		[FieldLoader.Require]
		public readonly string DefaultSpeed;

		[IncludeFluentReferences(LintDictionaryReference.Values)]
		[FieldLoader.LoadUsing(nameof(LoadSpeeds))]
		public readonly Dictionary<string, GameSpeed> Speeds;

		static object LoadSpeeds(MiniYaml y)
		{
			var ret = new Dictionary<string, GameSpeed>();
			var speedsNode = y.NodeWithKeyOrDefault("Speeds");
			if (speedsNode == null)
				throw new YamlException("Error parsing GameSpeeds: Missing Speeds node!");

			foreach (var node in speedsNode.Value.Nodes)
			{
				try
				{
					ret.Add(node.Key, FieldLoader.Load<GameSpeed>(node.Value));
				}
				catch (FieldLoader.MissingFieldsException e)
				{
					var label = e.Missing.Length > 1 ? "Required properties missing" : "Required property missing";
					throw new YamlException($"Error parsing GameSpeed {node.Key}: {label}: {e.Missing.JoinWith(", ")}");
				}
			}

			return ret;
		}
	}
}
