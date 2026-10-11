namespace ProtoForge.Core.Templates
{
	using System;
	using Formats.Pro;

	/// <summary>Builds well-formed prototype files with the engine defaults for each kind.</summary>
	public static class ProtoFactory
	{
		private const Int32 DefaultItemFlags = unchecked((Int32)0xA0000008);

		public static ProtoFile CreateDefault(ProtoKind kind, ProtoId pid)
		{
			ProtoFile file = new(kind, new Byte[kind.GetExpectedSize()]);
			foreach (ProtoField field in file.Schema.Fields)
			{
				if (!field.IsVirtual)
				{
					file.TrySet(field, field.DefaultValue);
				}
			}

			file.StoredProtoId = pid.Value;
			file.TrySet(FieldKeys.MessageId, pid.Index * 100);

			Int32? subType = kind.GetSubTypeValue();
			if (subType is not null)
			{
				file.TrySet(FieldKeys.SubType, subType.Value);
			}

			switch (kind)
			{
				case ProtoKind.Container:
					Apply(file, flags: 0x00009000, ext: 0x2800);
					break;
				case ProtoKind.Drug:
				case ProtoKind.MiscItem:
				case ProtoKind.Key:
					Apply(file, flags: DefaultItemFlags, ext: 0xB000);
					break;
				case ProtoKind.Armor:
				case ProtoKind.Weapon:
				case ProtoKind.Ammo:
					Apply(file, flags: DefaultItemFlags, ext: 0xA000);
					break;
				case ProtoKind.Critter:
					ApplyCritter(file);
					break;
				case ProtoKind.Wall:
					Apply(file, flags: 0, ext: 0x2000);
					file.TrySet(FieldKeys.ScriptId, -1);
					file.TrySet(FieldKeys.Material, 5);
					break;
				case ProtoKind.Tile:
					file.TrySet(FieldKeys.Material, 5);
					break;
				case ProtoKind.Misc:
					break;
				default:
					if (kind.IsScenery())
					{
						Apply(file, flags: 0, ext: 0x2000);
						file.TrySet(FieldKeys.Material, 3);
						file.TrySet("DoorKeyCode", -1);
					}

					break;
			}

			if (kind.IsItem())
			{
				file.TrySet(FieldKeys.Material, 1);
				file.TrySet(FieldKeys.Size, 1);
			}

			return file;
		}

		private static void Apply(ProtoFile file, Int32 flags, Int32 ext)
		{
			file.TrySet(FieldKeys.Flags, flags);
			file.TrySet(FieldKeys.FlagsExt, ext);
		}

		private static void ApplyCritter(ProtoFile file)
		{
			Apply(file, flags: 0x20000000, ext: 0x6000);
			file.TrySet(FieldKeys.Team, 1);
			file.TrySet(FieldKeys.CritterFlags, 0x2);
			file.TrySet("MaxHp", 30);
			file.TrySet("MaxAp", 7);
			file.TrySet("ArmorClass", 5);
			file.TrySet("MeleeDamage", 1);
			file.TrySet("CarryWeight", 150);
			file.TrySet("Sequence", 10);
			file.TrySet("HealingRate", 1);
			file.TrySet("CriticalChance", 5);
			file.TrySet("DrRadiation", 10);
			file.TrySet("DrPoison", 25);
			file.TrySet("Age", 25);
			file.TrySet(FieldKeys.FrmId, 0x01000000);
		}
	}
}