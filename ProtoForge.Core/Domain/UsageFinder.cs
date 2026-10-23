namespace ProtoForge.Core.Domain
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Formats.Ai;
	using Formats.Frm;
	using Formats.Map;
	using Formats.Pro;

	/// <summary>How two prototypes (or a prototype and an AI packet) are related.</summary>
	public enum UsageKind
	{
		/// <summary>Another prototype points at the target (ammunition of a weapon, projectile, key...).</summary>
		UsedBy,

		/// <summary>The target points at another prototype or at an AI packet.</summary>
		Uses,

		/// <summary>Another prototype shows the same image.</summary>
		SameImage,

		/// <summary>Another prototype runs the same script.</summary>
		SameScript,

		/// <summary>Another critter has the same AI packet.</summary>
		SameAiPacket,

		/// <summary>A drug that an AI packet lists as a preferred drug.</summary>
		AiPacketDrug,

		/// <summary>The prototype is placed on a map (or carried by an object placed on it).</summary>
		PlacedOnMap,

		/// <summary>A script instance of a map runs the script of the prototype.</summary>
		ScriptOnMap,

		/// <summary>A compiled script names the prototype (to create a creature, for instance).</summary>
		ReferencedByScript,
	}

	/// <summary>
	/// One relation: the field it goes through, the prototype at the other end (when there is one) and a text for the rest
	/// (the name of a map or of a script). For a map, <see cref="Count"/> is the number of objects placed on it and <see cref="SecondCount"/> the
	/// number carried in inventories; for a script on a map, <see cref="Count"/> is the number of script instances.
	/// </summary>
	public sealed record UsageRef(UsageKind Kind, String FieldKey, ProtoEntry? Entry, String Detail, Int32 Count = 0, Int32 SecondCount = 0);

	/// <summary>Finds where a prototype is used: by other prototypes, by AI packets, and which prototypes share its image, script or AI.</summary>
	public static class UsageFinder
	{
		/// <summary>The most references listed for one kind (a common image can be used by thousands of prototypes).</summary>
		public const Int32 MaxPerKind = 300;

		public static IReadOnlyList<UsageRef> Find(ProtoCatalog catalog, ProtoEntry target, AiPacketCatalog packets, ReferenceIndex? index = null, IReadOnlyList<String>? scriptNames = null)
		{
			if (target.File is null)
			{
				return [];
			}

			Int32 pid = target.Pid.Value;
			List<UsageRef> result = [];

			// What the target points at.
			foreach (ProtoField field in GetFields(target.File.Schema).Where(f => f.Editor == FieldEditor.Proto))
			{
				if (target.GetValue(field) is { } value && value != -1 && catalog.Find(value) is { } used)
				{
					result.Add(new UsageRef(UsageKind.Uses, field.Key, used, String.Empty));
				}
			}

			Int32 aiNumber = target.File.Schema.Find(FieldKeys.AiPacket) is { } aiField && target.GetValue(aiField) is { } number ? number : -1;
			if (aiNumber != -1 && packets.Find(aiNumber) is { } packet)
			{
				result.Add(new UsageRef(UsageKind.Uses, FieldKeys.AiPacket, null, packet.DisplayName));
			}

			HashSet<(ArtType Type, Int32 Index)> images = [];
			Int32 script = -1;
			foreach (ProtoField field in GetFields(target.File.Schema))
			{
				Int32? value = target.GetValue(field);
				if (value is null or -1)
				{
					continue;
				}

				if (field.Editor == FieldEditor.Art)
				{
					FrameId fid = new(value.Value);
					images.Add((fid.Type, fid.Index));
				}
				else if (field.Editor == FieldEditor.Script)
				{
					script = value.Value;
				}
			}

			Int32 scriptIndex = script == -1 ? -1 : script & 0xFFFFFF;
			if (scriptIndex >= 0 && scriptNames is not null && scriptIndex < scriptNames.Count)
			{
				result.Add(new UsageRef(UsageKind.Uses, FieldKeys.ScriptId, null, $"{scriptIndex}: {scriptNames[scriptIndex]}"));
			}

			Dictionary<UsageKind, Int32> counts = [];
			foreach (ProtoEntry other in catalog.All)
			{
				if (ReferenceEquals(other, target) || other.File is null)
				{
					continue;
				}

				foreach (ProtoField field in GetFields(other.File.Schema))
				{
					if (other.GetValue(field) is not { } value || value == -1)
					{
						continue;
					}

					UsageKind? kind = field.Editor switch
					{
						FieldEditor.Proto when value == pid => UsageKind.UsedBy,
						FieldEditor.Art when images.Contains((new FrameId(value).Type, new FrameId(value).Index)) => UsageKind.SameImage,
						FieldEditor.Script when script != -1 && value == script => UsageKind.SameScript,
						_ when aiNumber != -1 && field.Key == FieldKeys.AiPacket && value == aiNumber => UsageKind.SameAiPacket,
						_ => null,
					};
					if (kind is { } found && counts.GetValueOrDefault(found) < MaxPerKind)
					{
						counts[found] = counts.GetValueOrDefault(found) + 1;
						result.Add(new UsageRef(found, field.Key, other, String.Empty));
					}
				}
			}

			if (target.Kind == ProtoKind.Drug)
			{
				foreach (AiPacket listed in packets.Packets)
				{
					listed.Properties.TryGetValue(AiParameters.DrugsKey, out String? drugs);
					if (AiParameters.ParseDrugs(drugs).Contains(pid))
					{
						result.Add(new UsageRef(UsageKind.AiPacketDrug, AiParameters.DrugsKey, null, listed.DisplayName));
					}
				}
			}

			if (index is not null)
			{
				AddMapAndScriptUsage(result, index, pid, scriptIndex);
			}

			return result;
		}

		private static void AddMapAndScriptUsage(List<UsageRef> result, ReferenceIndex index, Int32 pid, Int32 scriptIndex)
		{
			Int32 placedMaps = 0;
			Int32 scriptMaps = 0;
			foreach (MapSummary map in index.Maps)
			{
				Int32 placed = map.Placed.GetValueOrDefault(pid);
				Int32 carried = map.Carried.GetValueOrDefault(pid);
				if (placed + carried > 0 && placedMaps++ < MaxPerKind)
				{
					result.Add(new UsageRef(UsageKind.PlacedOnMap, String.Empty, null, map.FileName, placed, carried));
				}

				Int32 instances = scriptIndex >= 0 ? map.Scripts.GetValueOrDefault(scriptIndex) + (map.MapScript == scriptIndex ? 1 : 0) : 0;
				if (instances > 0 && scriptMaps++ < MaxPerKind)
				{
					result.Add(new UsageRef(UsageKind.ScriptOnMap, FieldKeys.ScriptId, null, map.FileName, instances));
				}
			}

			if (index.ScriptsByPid.TryGetValue(pid, out List<String>? scripts))
			{
				foreach (String name in scripts.Take(MaxPerKind))
				{
					result.Add(new UsageRef(UsageKind.ReferencedByScript, String.Empty, null, name));
				}
			}
		}

		// The fields that can relate two prototypes, per schema (a critter has about a hundred fields, almost none of them relevant).
		private static readonly Dictionary<ProtoSchema, ProtoField[]> RelevantFields = [];

		private static ProtoField[] GetFields(ProtoSchema schema)
		{
			lock (RelevantFields)
			{
				if (!RelevantFields.TryGetValue(schema, out ProtoField[]? fields))
				{
					fields = [.. schema.Fields.Where(f => !f.IsVirtual && (f.Editor is FieldEditor.Proto or FieldEditor.Art or FieldEditor.Script || f.Key == FieldKeys.AiPacket))];
					RelevantFields[schema] = fields;
				}

				return fields;
			}
		}
	}
}