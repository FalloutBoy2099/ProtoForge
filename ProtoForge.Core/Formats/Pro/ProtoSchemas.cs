namespace ProtoForge.Core.Formats.Pro
{
	using System;
	using System.Collections.Generic;
	using Reference;

	/// <summary>
	/// Registry of the binary layouts of every prototype kind. Adding a new layout (or extending an existing one,
	/// for example with fields introduced by a source port) only requires describing it here.
	/// </summary>
	public static class ProtoSchemas
	{
		public static readonly String[] DamageSuffixes = ["Normal", "Laser", "Fire", "Plasma", "Electrical", "Emp", "Explosion"];

		public static readonly String[] SpecialNames =
			["Strength", "Perception", "Endurance", "Charisma", "Intelligence", "Agility", "Luck"];

		public static readonly String[] DerivedNames =
		[
			"MaxHp", "MaxAp", "ArmorClass", "UnarmedDamage", "MeleeDamage", "CarryWeight", "Sequence", "HealingRate",
			"CriticalChance", "BetterCriticals",
		];

		public static readonly String[] SkillNames =
		[
			"SmallGuns", "BigGuns", "EnergyWeapons", "Unarmed", "MeleeWeapons", "Throwing", "FirstAid", "Doctor", "Sneak",
			"Lockpick", "Steal", "Traps", "Science", "Repair", "Speech", "Barter", "Gambling", "Outdoorsman",
		];

		public const String BonusPrefix = "Bonus";

		private static readonly Dictionary<ProtoKind, ProtoSchema> Cache = BuildAll();

		public static ProtoSchema For(ProtoKind kind)
		{
			return Cache[kind];
		}

		private static Dictionary<ProtoKind, ProtoSchema> BuildAll()
		{
			Dictionary<ProtoKind, ProtoSchema> map = new();
			foreach (ProtoKind kind in ProtoKindExtensions.All)
			{
				map[kind] = Build(kind);
			}

			return map;
		}

		private static ProtoSchema Build(ProtoKind kind)
		{
			SchemaBuilder b = new(kind);
			switch (kind)
			{
				case ProtoKind.Armor:
					ItemHeader(b, kind);
					b.Group("Armor").Number("ArmorClass", 0, 999);
					Damage(b, "Dr", 0, 999);
					Damage(b, "Dt", 0, 999);
					b.Choice("Perk", ChoiceCatalog.Perk, -1)
						.Art("MaleFrmId", ArtType.Critter, 0x01000000)
						.Art("FemaleFrmId", ArtType.Critter, 0x01000000);
					break;
				case ProtoKind.Container:
					ItemHeader(b, kind);
					b.Group("Container").Number("MaxSize", 0, 9999).Flags("OpenFlags", FlagSets.ContainerFlags);
					break;
				case ProtoKind.Drug:
					ItemHeader(b, kind);
					DrugFields(b);
					break;
				case ProtoKind.Weapon:
					ItemHeader(b, kind);
					WeaponFields(b);
					break;
				case ProtoKind.Ammo:
					ItemHeader(b, kind);
					b.Group("Ammo")
						.Choice("Caliber", ChoiceCatalog.Caliber)
						.Number("Quantity", 0, 9999)
						.Number("ArmorClassModifier", -999, 999)
						.Number("DamageResistanceModifier", -999, 999)
						.Number("DamageMultiplier", 1, 999, 1)
						.Number("DamageDivisor", 1, 999, 1);
					break;
				case ProtoKind.MiscItem:
					ItemHeader(b, kind);
					b.Group("MiscItem")
						.Proto("PowerPid", ProtoObjectType.Item, null, -1)
						.Choice("PowerType", ChoiceCatalog.Caliber)
						.Number("Charges", -1, 9999);
					break;
				case ProtoKind.Key:
					ItemHeader(b, kind);
					b.Group("Key").Number("KeyCode");
					break;
				case ProtoKind.Critter:
					CritterFields(b);
					break;
				case ProtoKind.SceneryDoor:
				case ProtoKind.SceneryStairs:
				case ProtoKind.SceneryElevator:
				case ProtoKind.SceneryLadderBottom:
				case ProtoKind.SceneryLadderTop:
				case ProtoKind.SceneryGeneric:
					SceneryFields(b, kind);
					break;
				case ProtoKind.Wall:
					FullHeader(b, ArtType.Wall, FlagSets.ExtFlags);
					b.Group("Wall").Choice(FieldKeys.Material, ChoiceCatalog.Material);
					break;
				case ProtoKind.Tile:
					b.Group("Identity").Pid(FieldKeys.ProtoId).Message(FieldKeys.MessageId);
					b.Group("Appearance").Art(FieldKeys.FrmId, ArtType.Tile, 0);
					b.Group("Flags").Flags(FieldKeys.Flags, FlagSets.ObjectFlags).Flags(FieldKeys.FlagsExt, FlagSets.ExtFlags);
					b.Group("Tile").Number("Unknown", advanced: true, defaultValue: -1).Choice(FieldKeys.Material, ChoiceCatalog.Material);
					break;
				case ProtoKind.Misc:
					b.Group("Identity").Pid(FieldKeys.ProtoId).Message(FieldKeys.MessageId);
					b.Group("Appearance").Art(FieldKeys.FrmId, ArtType.Misc, 0)
						.Number(FieldKeys.LightRadius, 0, 8)
						.Light(FieldKeys.LightIntensity);
					b.Group("Flags").Flags(FieldKeys.Flags, FlagSets.ObjectFlags).Flags(FieldKeys.FlagsExt, FlagSets.ExtFlags);
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(kind));
			}

			return b.Build();
		}

		/// <summary>Identity, appearance, flags and script: shared by items, critters, scenery and walls (8 dwords).</summary>
		private static void FullHeader(SchemaBuilder b, ArtType artType, String extFlagSet)
		{
			b.Group("Identity").Pid(FieldKeys.ProtoId).Message(FieldKeys.MessageId);
			b.Group("Appearance")
				.Art(FieldKeys.FrmId, artType, 0)
				.Number(FieldKeys.LightRadius, 0, 8)
				.Light(FieldKeys.LightIntensity);
			b.Group("Flags")
				.Flags(FieldKeys.Flags, FlagSets.ObjectFlags)
				.Flags(FieldKeys.FlagsExt, extFlagSet);
			b.Group("Identity").Script(FieldKeys.ScriptId);
		}

		private static void ItemHeader(SchemaBuilder b, ProtoKind kind)
		{
			FullHeader(b, ArtType.Item, kind == ProtoKind.Weapon ? FlagSets.WeaponExtFlags : FlagSets.ExtFlags);
			b.Group("Item")
				.Choice(FieldKeys.SubType, ChoiceCatalog.ItemType, kind.GetSubTypeValue() ?? 0, readOnly: true)
				.Choice(FieldKeys.Material, ChoiceCatalog.Material)
				.Number(FieldKeys.Size, 0, 999)
				.Number(FieldKeys.Weight, 0, 99999)
				.Number(FieldKeys.Cost, 0, 9999999);
			b.Group("Appearance").Art(FieldKeys.InventoryFrmId, ArtType.Inventory, 0x07000000);
			b.Group("Item").ByteChoice(FieldKeys.SoundId, ChoiceCatalog.SoundId, '0');
		}

		private static void Damage(SchemaBuilder b, String prefix, Int32 min, Int32 max)
		{
			b.Group(prefix == "Dr" ? "Resistances" : "Thresholds");
			foreach (String suffix in DamageSuffixes)
			{
				b.Number(prefix + suffix, min, max);
			}
		}

		private static void DrugFields(SchemaBuilder b)
		{
			b.Group("DrugImmediate");
			for (Int32 i = 1; i <= 3; i++)
			{
				b.Choice("Stat" + i, ChoiceCatalog.DrugStat, -1);
			}

			for (Int32 i = 1; i <= 3; i++)
			{
				b.Number("ImmediateAmount" + i);
			}

			b.Group("DrugDelayed1").Number("Duration1", 0, 99999);
			for (Int32 i = 1; i <= 3; i++)
			{
				b.Number("Delayed1Amount" + i);
			}

			b.Group("DrugDelayed2").Number("Duration2", 0, 99999);
			for (Int32 i = 1; i <= 3; i++)
			{
				b.Number("Delayed2Amount" + i);
			}

			b.Group("DrugAddiction")
				.Number("AddictionChance", 0, 100)
				.Choice("WithdrawalEffect", ChoiceCatalog.Perk, -1)
				.Number("WithdrawalOnset", 0, 99999);
		}

		private static void WeaponFields(SchemaBuilder b)
		{
			// The attack modes live in the low byte of the extended flags (primary: bits 0-3, secondary: bits 4-7).
			ProtoField ext = b.Find(FieldKeys.FlagsExt);
			b.Group("Weapon")
				.SubField("PrimaryAttackMode", ext, 0x0F, 0, ChoiceCatalog.AttackType)
				.SubField("SecondaryAttackMode", ext, 0xF0, 4, ChoiceCatalog.AttackType);
			b.Choice("AnimationCode", ChoiceCatalog.WeaponAnimation);
			b.Number(FieldKeys.MinDamage, 0, 9999).Number(FieldKeys.MaxDamage, 0, 9999);
			b.Choice("DamageType", ChoiceCatalog.DamageType);
			b.Number("MaxRangePrimary", 0, 999).Number("MaxRangeSecondary", 0, 999);
			b.Proto(FieldKeys.ProjectilePid, ProtoObjectType.Misc, null, -1);
			b.Number("MinStrength", 0, 10);
			b.Number("ApCostPrimary", 0, 99).Number("ApCostSecondary", 0, 99);
			b.Choice("CriticalFail", ChoiceCatalog.CriticalFail);
			b.Choice("Perk", ChoiceCatalog.Perk, -1);
			b.Number("BurstRounds", 0, 99);
			b.Choice(FieldKeys.Caliber, ChoiceCatalog.Caliber);
			b.Proto(FieldKeys.AmmoPid, ProtoObjectType.Item, ProtoKind.Ammo, -1);
			b.Number("MaxAmmo", 0, 9999);
			b.ByteChoice("WeaponSoundId", ChoiceCatalog.SoundId, '0');
		}

		private static void CritterFields(SchemaBuilder b)
		{
			FullHeader(b, ArtType.Critter, FlagSets.ExtFlags);

			b.Group("Critter")
				.Art(FieldKeys.HeadFrmId, ArtType.Heads, -1)
				.Choice(FieldKeys.AiPacket, ChoiceCatalog.AiPacket)
				.Choice(FieldKeys.Team, ChoiceCatalog.Team)
				.Flags(FieldKeys.CritterFlags, FlagSets.CritterFlags);

			StatBlock(b, String.Empty);
			StatBlock(b, BonusPrefix);

			b.Group("Skills");
			foreach (String skill in SkillNames)
			{
				b.Number(skill, -999, 999);
			}

			b.Group("Critter")
				.Choice("BodyType", ChoiceCatalog.BodyType)
				.Number("Experience", 0, Int32.MaxValue)
				.Choice("KillType", ChoiceCatalog.KillType)
				.Choice("DamageType", ChoiceCatalog.DamageType);
		}

		/// <summary>The engine stores 35 stats twice: base values, then bonus values added on top of them.</summary>
		private static void StatBlock(SchemaBuilder b, String prefix)
		{
			Boolean bonus = prefix.Length > 0;
			b.Group(prefix + "Special");
			foreach (String name in SpecialNames)
			{
				b.Number(prefix + name, bonus ? -99 : 1, 99, bonus ? 0 : 5);
			}

			b.Group(prefix + "Derived");
			foreach (String name in DerivedNames)
			{
				b.Number(prefix + name, -9999, 99999);
			}

			b.Group(prefix + "Thresholds");
			foreach (String suffix in DamageSuffixes)
			{
				b.Number(prefix + "Dt" + suffix, -999, 999);
			}

			b.Group(prefix + "Resistances");
			foreach (String suffix in DamageSuffixes)
			{
				b.Number(prefix + "Dr" + suffix, -999, 999);
			}

			b.Number(prefix + "DrRadiation", -999, 999).Number(prefix + "DrPoison", -999, 999);

			b.Group(prefix + "Character")
				.Number(prefix + "Age", 0, 999, bonus ? 0 : 25)
				.Choice(prefix + "Gender", ChoiceCatalog.Gender);
		}

		private static void SceneryFields(SchemaBuilder b, ProtoKind kind)
		{
			FullHeader(b, ArtType.Scenery, FlagSets.ExtFlags);
			b.Group("Scenery")
				.Choice(FieldKeys.SubType, ChoiceCatalog.SceneryType, kind.GetSubTypeValue() ?? 0, readOnly: true)
				.Choice(FieldKeys.Material, ChoiceCatalog.Material)
				.Character(FieldKeys.SoundId, 0);

			// Sub type specific data is stored at 0x29 (unaligned) right after the sound byte.
			switch (kind)
			{
				case ProtoKind.SceneryDoor:
					b.Group("Door").Flags("DoorOpenFlags", FlagSets.DoorFlags).Number("DoorKeyCode", -1, 99999, -1);
					break;
				case ProtoKind.SceneryStairs:
					b.Group("Stairs").Number("StairsDestination", advanced: true).Number("StairsMap", -1);
					break;
				case ProtoKind.SceneryElevator:
					b.Group("Elevator").Choice("ElevatorType", ChoiceCatalog.ElevatorType).Number("ElevatorLevel", 0, 3);
					break;
				case ProtoKind.SceneryLadderBottom:
				case ProtoKind.SceneryLadderTop:
					b.Group("Ladder").Number("LadderDestination", advanced: true);
					break;
				default:
					b.Group("Scenery").Number("GenericData", advanced: true);
					break;
			}
		}
	}
}