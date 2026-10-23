namespace ProtoForge.Core.Domain
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Formats.Ai;
	using Formats.Map;
	using Formats.Pro;

	/// <summary>
	/// The prototypes that nothing seems to use: not placed on a map, not carried by an object of a map, not named by a script,
	/// not pointed at by another prototype (the ammunition of a weapon, a projectile) and not a preferred drug of an AI packet.
	/// A candidate list for cleaning a mod, not a proof: the engine and the scripts can use an item by its number without any trace
	/// that can be read, and the prototypes that scripts create are only known for creatures, scenery, walls and misc objects.
	/// </summary>
	public static class UnusedPrototypes
	{
		public static IReadOnlyList<ProtoEntry> Find(ProtoCatalog catalog, AiPacketCatalog packets, ReferenceIndex index)
		{
			HashSet<Int32> used = [];
			foreach (MapSummary map in index.Maps)
			{
				used.UnionWith(map.Placed.Keys);
				used.UnionWith(map.Carried.Keys);
			}

			used.UnionWith(index.ScriptsByPid.Keys);

			Dictionary<ProtoSchema, ProtoField[]> pointers = [];
			foreach (ProtoEntry entry in catalog.All)
			{
				if (entry.File is not { } file)
				{
					continue;
				}

				if (!pointers.TryGetValue(file.Schema, out ProtoField[]? fields))
				{
					fields = [.. file.Schema.Fields.Where(f => !f.IsVirtual && f.Editor == FieldEditor.Proto)];
					pointers[file.Schema] = fields;
				}

				foreach (ProtoField field in fields)
				{
					if (file.TryGet(field) is { } value && value != -1)
					{
						used.Add(value);
					}
				}
			}

			foreach (AiPacket packet in packets.Packets)
			{
				packet.Properties.TryGetValue(AiParameters.DrugsKey, out String? drugs);
				used.UnionWith(AiParameters.ParseDrugs(drugs));
			}

			// Tiles are not objects of a map (a map stores the number of an image), so they cannot be judged here.
			return [.. catalog.All.Where(e => e.File is not null && e.Type != ProtoObjectType.Tile && !used.Contains(e.Pid.Value)).OrderBy(e => e.Pid.Value)];
		}
	}
}