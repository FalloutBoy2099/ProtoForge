namespace ProtoForge.Core.Formats.Pro
{
	using System;
	using System.Collections.Generic;
	using System.Linq;

	/// <summary>Well-known field keys shared by several prototype layouts.</summary>
	public static class FieldKeys
	{
		public const String ProtoId = "ProtoId";
		public const String MessageId = "MessageId";
		public const String FrmId = "FrmId";
		public const String LightRadius = "LightRadius";
		public const String LightIntensity = "LightIntensity";
		public const String Flags = "Flags";
		public const String FlagsExt = "FlagsExt";
		public const String ScriptId = "ScriptId";
		public const String Material = "Material";
		public const String SubType = "SubType";
		public const String InventoryFrmId = "InventoryFrmId";
		public const String SoundId = "SoundId";
		public const String Weight = "Weight";
		public const String Cost = "Cost";
		public const String Size = "Size";
		public const String HeadFrmId = "HeadFrmId";
		public const String AiPacket = "AiPacket";
		public const String Team = "Team";
		public const String CritterFlags = "CritterFlags";
		public const String Caliber = "Caliber";
		public const String AmmoPid = "AmmoPid";
		public const String ProjectilePid = "ProjectilePid";
		public const String PowerPid = "PowerPid";
		public const String MinDamage = "MinDamage";
		public const String MaxDamage = "MaxDamage";
		public const String DamageDivisor = "DamageDivisor";
		public const String DamageMultiplier = "DamageMultiplier";
		public const String MaleFrmId = "MaleFrmId";
		public const String FemaleFrmId = "FemaleFrmId";
	}

	/// <summary>The ordered field list of one prototype layout.</summary>
	public sealed class ProtoSchema
	{
		private readonly Dictionary<String, ProtoField> _byKey;

		public ProtoSchema(ProtoKind kind, IReadOnlyList<ProtoField> fields)
		{
			Kind = kind;
			Fields = fields;
			_byKey = fields.ToDictionary(f => f.Key, StringComparer.Ordinal);
			Groups = [.. fields.Select(f => f.Group).Distinct(StringComparer.Ordinal)];
		}

		public ProtoKind Kind { get; }

		public IReadOnlyList<ProtoField> Fields { get; }

		/// <summary>Group identifiers in display order.</summary>
		public IReadOnlyList<String> Groups { get; }

		public ProtoField? Find(String key)
		{
			return _byKey.TryGetValue(key, out ProtoField? field) ? field : null;
		}

		public ProtoField Require(String key)
		{
			return Find(key) ?? throw new KeyNotFoundException($"Field '{key}' does not exist in the {Kind} schema.");
		}

		public IEnumerable<ProtoField> GetGroup(String group)
		{
			return Fields.Where(f => String.Equals(f.Group, group, StringComparison.Ordinal));
		}

		/// <summary>Field covering the byte at <paramref name="offset"/> (ignoring bit sub-fields), if known.</summary>
		public ProtoField? FindAtOffset(Int32 offset)
		{
			return Fields.FirstOrDefault(f => !f.IsVirtual && offset >= f.Offset && offset < f.End);
		}
	}
}