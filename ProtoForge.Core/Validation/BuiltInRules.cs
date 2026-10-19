namespace ProtoForge.Core.Validation
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Linq;
	using Domain;
	using Formats.Frm;
	using Formats.Lst;
	using Formats.Pro;
	using Reference;
	using Vfs;

	/// <summary>File presence, size, PID consistency and LST hygiene.</summary>
	public sealed class FileIntegrityRule : ValidationRuleBase
	{
		public override String Id
		{
			get { return "file.integrity"; }
		}

		public override void Validate(ValidationContext context, ProtoEntry entry, IssueSink sink)
		{
			if (entry.File is null)
			{
				if (String.IsNullOrWhiteSpace(entry.FileName))
				{
					sink.Report(Severity.Warning, "BlankLstLine", entry, null, entry.Index);
				}
				else if (entry.LoadError is null)
				{
					sink.Report(Severity.Error, "FileMissing", entry, null, entry.FileName);
				}
				else if (String.Equals(entry.LoadError, "missing", StringComparison.Ordinal))
				{
					sink.Report(Severity.Error, "FileMissing", entry, null, entry.FileName);
				}
				else
				{
					sink.Report(Severity.Error, "FileUnreadable", entry, null, entry.LoadError);
				}

				return;
			}

			ProtoFile file = entry.File;
			if (!file.HasExpectedLength)
			{
				sink.Report(file.Length < file.Kind.GetExpectedSize() ? Severity.Error : Severity.Warning, "BadSize", entry, null,
					file.Length, file.Kind, file.Kind.GetExpectedSize());
			}

			if (file.StoredProtoId != entry.Pid.Value)
			{
				sink.Report(Severity.Error, "PidMismatch", entry, FieldKeys.ProtoId, new ProtoId(file.StoredProtoId), entry.Pid);
			}
		}

		public override void ValidateAll(ValidationContext context, IssueSink sink)
		{
			foreach (ProtoObjectType type in ProtoObjectTypeExtensions.All)
			{
				IReadOnlyList<ProtoEntry> entries = context.Catalog.GetEntries(type);
				if (entries.Count == 0)
				{
					continue;
				}

				foreach (IGrouping<String, ProtoEntry> group in entries.Where(e => e.FileName.Length > 0).GroupBy(e => e.FileName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
				{
					sink.Report(Severity.Warning, "DuplicateFile", group.First(), null, group.Key);
				}

				HashSet<String> listed = new(entries.Select(e => e.FileName), StringComparer.OrdinalIgnoreCase);
				foreach (String path in context.Files.Enumerate(type.GetProtoDirectory()))
				{
					String name = GamePath.GetFileName(path);
					if (GamePath.GetExtension(path) == ".pro" && !listed.Contains(name))
					{
						sink.Report(Severity.Info, "OrphanFile", null, null, path);
					}
				}
			}
		}
	}

	/// <summary>Names and descriptions found in the MSG files.</summary>
	public sealed class MessageRule : ValidationRuleBase
	{
		public override String Id
		{
			get { return "text"; }
		}

		public override void Validate(ValidationContext context, ProtoEntry entry, IssueSink sink)
		{
			ProtoFile? file = entry.File;
			if (file is null || entry.Type == ProtoObjectType.Tile)
			{
				return;
			}

			Int32 id = file.MessageId;
			if (id <= 0)
			{
				sink.Report(Severity.Info, "NoMessageId", entry, FieldKeys.MessageId);
				return;
			}

			if (entry.Name.Length == 0)
			{
				sink.Report(Severity.Warning, "MissingName", entry, FieldKeys.MessageId, id);
			}
			else if (entry.Description.Length == 0 && entry.Type is ProtoObjectType.Item or ProtoObjectType.Critter)
			{
				sink.Report(Severity.Info, "MissingDescription", entry, FieldKeys.MessageId, id + 1);
			}

			Int32 conventional = entry.Index * 100;
			if (id != conventional)
			{
				sink.Report(Severity.Info, "MessageIdConvention", entry, FieldKeys.MessageId, id, conventional);
			}
		}

		public override void ValidateAll(ValidationContext context, IssueSink sink)
		{
			foreach (ProtoObjectType type in ProtoObjectTypeExtensions.All)
			{
				Dictionary<Int32, ProtoEntry> owners = [];
				foreach (ProtoEntry entry in context.Catalog.GetEntries(type))
				{
					Int32 id = entry.File?.MessageId ?? 0;
					if (id <= 0)
					{
						continue;
					}

					if (owners.TryGetValue(id, out ProtoEntry? other))
					{
						sink.Report(Severity.Warning, "DuplicateMessageId", entry, FieldKeys.MessageId, id, other.Pid);
					}
					else
					{
						owners[id] = entry;
					}
				}
			}
		}
	}

	/// <summary>Frame ids must point at an art list entry whose file exists.</summary>
	public sealed class ArtReferenceRule : ValidationRuleBase
	{
		public override String Id
		{
			get { return "art"; }
		}

		public override void Validate(ValidationContext context, ProtoEntry entry, IssueSink sink)
		{
			ProtoFile? file = entry.File;
			if (file is null)
			{
				return;
			}

			foreach (ProtoField field in file.Schema.Fields.Where(f => f.Editor == FieldEditor.Art))
			{
				Int32? value = file.TryGet(field);
				if (value is null || value.Value == -1)
				{
					continue;
				}

				FrameId fid = new(value.Value);
				if (field.ArtType is { } expected && fid.Type != expected)
				{
					sink.Report(Severity.Error, "ArtTypeMismatch", entry, field.Key, field.Key, fid.Type, expected);
					continue;
				}

				LstFile? list = context.Art.GetList(fid.Type);
				if (list is null)
				{
					continue;
				}

				if (fid.Index >= list.Count)
				{
					sink.Report(Severity.Error, "InvalidArtIndex", entry, field.Key, fid, fid.Index);
					continue;
				}

				String? path = context.Art.ResolvePath(fid);
				if (path is null || !context.Files.Exists(path))
				{
					sink.Report(Severity.Warning, "MissingArt", entry, field.Key, field.Key, fid, path ?? "?");
				}
			}
		}
	}

	/// <summary>Script ids must exist in scripts.lst (the low 24 bits are the line number, counted from 1).</summary>
	public sealed class ScriptRule : ValidationRuleBase
	{
		public override String Id
		{
			get { return "script"; }
		}

		public override void Validate(ValidationContext context, ProtoEntry entry, IssueSink sink)
		{
			Int32? script = entry.File?.TryGet(FieldKeys.ScriptId);
			if (script is null || script.Value == -1 || context.ScriptCount == 0)
			{
				return;
			}

			Int32 index = script.Value & 0xFFFFFF;
			if (index < 1 || index > context.ScriptCount)
			{
				sink.Report(Severity.Error, "ScriptOutOfRange", entry, FieldKeys.ScriptId, index, context.ScriptCount);
			}
			else if (index <= context.ScriptNames.Count && context.ScriptNames[index - 1] is { Length: > 0 } name && !context.Files.Exists("scripts/" + (name.Contains('.', StringComparison.Ordinal) ? name : name + ".int")))
			{
				sink.Report(Severity.Warning, "ScriptFileMissing", entry, FieldKeys.ScriptId, index, name);
			}
		}
	}

	/// <summary>Values that are impossible or suspicious for the engine.</summary>
	public sealed class ValueRule : ValidationRuleBase
	{
		private const UInt32 TransparencyMask = 0x00004000 | 0x00010000 | 0x00020000 | 0x00040000 | 0x00080000;
		private const UInt32 RuntimeMask = 0x01000000 | 0x02000000 | 0x04000000 | 0x40000000;

		private static readonly HashSet<String> SkippedChoiceSets =
		[
			ChoiceCatalog.AiPacket,
			ChoiceCatalog.Team,
			ChoiceCatalog.Caliber,
			ChoiceCatalog.Perk,
			ChoiceCatalog.ElevatorType,
			ChoiceCatalog.SoundId,
		];

		public override String Id
		{
			get { return "values"; }
		}

		public override void Validate(ValidationContext context, ProtoEntry entry, IssueSink sink)
		{
			ProtoFile? file = entry.File;
			if (file is null)
			{
				return;
			}

			CheckGenericFields(context, entry, file, sink);
			CheckFlags(entry, file, sink);
			CheckLight(entry, file, sink);

			switch (file.Kind)
			{
				case ProtoKind.Weapon:
					CheckWeapon(context, entry, file, sink);
					break;
				case ProtoKind.Ammo:
					CheckAmmo(entry, file, sink);
					break;
				case ProtoKind.Critter:
					CheckCritter(context, entry, file, sink);
					break;
				default:
					break;
			}
		}

		private static void CheckGenericFields(ValidationContext context, ProtoEntry entry, ProtoFile file, IssueSink sink)
		{
			foreach (ProtoField field in file.Schema.Fields.Where(f => !f.IsVirtual))
			{
				Int32? value = file.TryGet(field);
				if (value is null)
				{
					continue;
				}

				if (field.Size == 4 && value.Value == unchecked((Int32)0xCCCCCCCC) && field.Editor != FieldEditor.Flags)
				{
					sink.Report(Severity.Info, "UninitializedValue", entry, field.Key, field.Key);
					continue;
				}

				switch (field.Editor)
				{
					case FieldEditor.Number:
						if (!field.IsAdvanced && (value.Value < field.Min || value.Value > field.Max))
						{
							sink.Report(Severity.Warning, "ValueOutOfRange", entry, field.Key, field.Key, value.Value, field.Min, field.Max);
						}

						break;
					case FieldEditor.Choice:
						if (field.ChoiceSet is not null && !SkippedChoiceSets.Contains(field.ChoiceSet)
							&& !context.Choices.Contains(field.ChoiceSet, value.Value))
						{
							sink.Report(Severity.Warning, "InvalidChoice", entry, field.Key, field.Key, value.Value);
						}
						else if (field.ChoiceSet == ChoiceCatalog.Perk && !context.Choices.Contains(ChoiceCatalog.Perk, value.Value))
						{
							sink.Report(Severity.Warning, "PerkInvalid", entry, field.Key, field.Key, value.Value);
						}

						break;
					case FieldEditor.Flags:
						if (field.FlagSet is not null && field.FlagSet != FlagSets.ObjectFlags)
						{
							FlagSet set = FlagSets.Get(field.FlagSet);
							UInt32 unknown = unchecked((UInt32)value.Value) & ~set.KnownMask;
							if (unknown != 0)
							{
								sink.Report(Severity.Info, "UnknownFlagBits", entry, field.Key, field.Key, unknown.ToString("X8", CultureInfo.InvariantCulture));
							}
						}

						break;
					default:
						break;
				}
			}
		}

		private static void CheckFlags(ProtoEntry entry, ProtoFile file, IssueSink sink)
		{
			UInt32 flags = unchecked((UInt32)file.Flags);
			UInt32 transparency = flags & TransparencyMask;
			Boolean none = (flags & 0x00008000) != 0;
			if ((none && transparency != 0) || System.Numerics.BitOperations.PopCount(transparency) > 1)
			{
				sink.Report(Severity.Info, "TransparencyConflict", entry, FieldKeys.Flags);
			}

			if (entry.Type == ProtoObjectType.Item && (flags & RuntimeMask) != 0)
			{
				UInt32 runtime = flags & RuntimeMask;
				FlagBit? bit = FlagSets.Get(FlagSets.ObjectFlags).Bits.FirstOrDefault(b => (b.Mask & runtime) != 0);
				sink.Report(Severity.Info, "RuntimeFlag", entry, FieldKeys.Flags, bit?.Name ?? runtime.ToString("X8", CultureInfo.InvariantCulture));
			}
		}

		private static void CheckLight(ProtoEntry entry, ProtoFile file, IssueSink sink)
		{
			Int32? radius = file.TryGet(FieldKeys.LightRadius);
			Int32? intensity = file.TryGet(FieldKeys.LightIntensity);
			if (radius is null || intensity is null)
			{
				return;
			}

			if (radius.Value is < 0 or > 8 || intensity.Value is < 0 or > 65536)
			{
				sink.Report(Severity.Warning, "LightOutOfRange", entry, FieldKeys.LightRadius, radius.Value, intensity.Value);
			}
		}

		private static void CheckWeapon(ValidationContext context, ProtoEntry entry, ProtoFile file, IssueSink sink)
		{
			Int32 min = file.Get(FieldKeys.MinDamage);
			Int32 max = file.Get(FieldKeys.MaxDamage);
			if (min > max)
			{
				sink.Report(Severity.Error, "MinGreaterThanMax", entry, FieldKeys.MinDamage, min, max);
			}

			Int32 ammoPid = file.Get(FieldKeys.AmmoPid, -1);
			if (ammoPid != -1)
			{
				ProtoEntry? ammo = context.Catalog.Find(ammoPid);
				if (ammo?.Kind != ProtoKind.Ammo)
				{
					sink.Report(Severity.Error, "AmmoReferenceInvalid", entry, FieldKeys.AmmoPid, new ProtoId(ammoPid));
				}
				else
				{
					Int32 weaponCaliber = file.Get(FieldKeys.Caliber);
					Int32 ammoCaliber = ammo.File!.Get(FieldKeys.Caliber);
					if (weaponCaliber != ammoCaliber)
					{
						sink.Report(Severity.Warning, "CaliberMismatch", entry, FieldKeys.Caliber, weaponCaliber, ammoCaliber);
					}
				}
			}

			Int32 projectile = file.Get(FieldKeys.ProjectilePid, -1);
			if (projectile != -1 && context.Catalog.IsLoaded(ProtoObjectType.Misc)
				&& (new ProtoId(projectile).Type != ProtoObjectType.Misc || context.Catalog.Find(projectile) is null))
			{
				sink.Report(Severity.Warning, "ProjectileInvalid", entry, FieldKeys.ProjectilePid, new ProtoId(projectile));
			}
		}

		private static void CheckAmmo(ProtoEntry entry, ProtoFile file, IssueSink sink)
		{
			if (file.Get(FieldKeys.DamageDivisor, 1) == 0)
			{
				sink.Report(Severity.Error, "AmmoDivisorZero", entry, FieldKeys.DamageDivisor);
			}

			if (file.Get(FieldKeys.DamageMultiplier, 1) == 0)
			{
				sink.Report(Severity.Warning, "AmmoMultiplierZero", entry, FieldKeys.DamageMultiplier);
			}
		}

		private static void CheckCritter(ValidationContext context, ProtoEntry entry, ProtoFile file, IssueSink sink)
		{
			foreach (String name in ProtoSchemas.SpecialNames)
			{
				Int32 total = file.Get(name) + file.Get(ProtoSchemas.BonusPrefix + name);
				if (total is < 1 or > 10)
				{
					sink.Report(Severity.Warning, "ValueOutOfRange", entry, name, name + " (base + bonus)", total, 1, 10);
				}
			}

			Int32? packet = file.TryGet(FieldKeys.AiPacket);
			if (packet is not null && context.AiPackets.Packets.Count > 0 && context.AiPackets.Find(packet.Value) is null)
			{
				sink.Report(Severity.Warning, "AiPacketMissing", entry, FieldKeys.AiPacket, packet.Value);
			}
		}
	}
}