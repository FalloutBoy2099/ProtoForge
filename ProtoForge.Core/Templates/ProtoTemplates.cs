namespace ProtoForge.Core.Templates
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Formats.Pro;

	/// <summary>A named starting point for a new prototype: a kind plus a few field values.</summary>
	public sealed class ProtoTemplate
	{
		public ProtoTemplate(String id, String displayName, ProtoKind kind, IReadOnlyDictionary<String, Int32> values, String category)
		{
			Id = id;
			DisplayName = displayName;
			Kind = kind;
			Values = values;
			Category = category;
		}

		public String Id { get; }

		public String DisplayName { get; }

		public ProtoKind Kind { get; }

		/// <summary>Field values applied on top of the engine defaults (keys are schema keys).</summary>
		public IReadOnlyDictionary<String, Int32> Values { get; }

		public String Category { get; }

		/// <summary>When set, the template is a complete file captured from an existing prototype (user templates).</summary>
		public ProtoFile? Source { get; init; }

		public Boolean IsUserTemplate
		{
			get { return Source is not null; }
		}

		public ProtoFile Create(ProtoId pid)
		{
			ProtoFile file;
			if (Source is not null)
			{
				file = Source.Clone();
				file.StoredProtoId = pid.Value;
				file.TrySet(FieldKeys.MessageId, pid.Index * 100);
				return file;
			}

			file = ProtoFactory.CreateDefault(Kind, pid);
			// Non-virtual fields first, so bit sub-fields refine the stored value.
			ProtoSchema schema = file.Schema;
			foreach (KeyValuePair<String, Int32> pair in Values.OrderBy(p => schema.Find(p.Key)?.IsVirtual == true))
			{
				file.TrySet(pair.Key, pair.Value);
			}

			return file;
		}
	}

	/// <summary>Templates shipped with ProtoForge: the usual starting points of mod authors.</summary>
	public static class BuiltInTemplates
	{
		public static readonly IReadOnlyList<ProtoTemplate> All = Build();

		public static IEnumerable<ProtoTemplate> ForKind(ProtoKind kind)
		{
			return All.Where(t => t.Kind == kind);
		}

		public static ProtoTemplate Default(ProtoKind kind)
		{
			return All.FirstOrDefault(t => t.Kind == kind) ?? new ProtoTemplate("default." + kind, kind.ToString(), kind, new Dictionary<String, Int32>(), "Other");
		}

		private static List<ProtoTemplate> Build()
		{
			const Int32 weaponBase = 0xA000;
			const Int32 bigGun = 0x100;
			const Int32 twoHand = 0x200;
			const Int32 energy = 0x400;

			return
			[
				T("weapon.melee", "Melee weapon", ProtoKind.Weapon, "Weapon", new()
				{
					["AnimationCode"] = 1, ["PrimaryAttackMode"] = 3, ["SecondaryAttackMode"] = 4, ["MinDamage"] = 3, ["MaxDamage"] = 8,
					["MaxRangePrimary"] = 1, ["MaxRangeSecondary"] = 1, ["MinStrength"] = 3, ["ApCostPrimary"] = 3, ["ApCostSecondary"] = 3,
				}),
				T("weapon.pistol", "Pistol", ProtoKind.Weapon, "Weapon", new()
				{
					["AnimationCode"] = 5, ["PrimaryAttackMode"] = 6, ["SecondaryAttackMode"] = 0, ["MinDamage"] = 5, ["MaxDamage"] = 12,
					["MaxRangePrimary"] = 25, ["MinStrength"] = 3, ["ApCostPrimary"] = 5, ["MaxAmmo"] = 12, ["Weight"] = 3, ["Cost"] = 250,
				}),
				T("weapon.rifle", "Rifle", ProtoKind.Weapon, "Weapon", new()
				{
					["AnimationCode"] = 7, ["PrimaryAttackMode"] = 6, ["SecondaryAttackMode"] = 0, ["FlagsExt"] = weaponBase | twoHand,
					["MinDamage"] = 8, ["MaxDamage"] = 20, ["MaxRangePrimary"] = 40, ["MinStrength"] = 5, ["ApCostPrimary"] = 5,
					["MaxAmmo"] = 10, ["Weight"] = 8, ["Cost"] = 600,
				}),
				T("weapon.smg", "Submachine gun", ProtoKind.Weapon, "Weapon", new()
				{
					["AnimationCode"] = 6, ["PrimaryAttackMode"] = 6, ["SecondaryAttackMode"] = 7, ["MinDamage"] = 5, ["MaxDamage"] = 12,
					["MaxRangePrimary"] = 30, ["MaxRangeSecondary"] = 30, ["MinStrength"] = 5, ["ApCostPrimary"] = 5, ["ApCostSecondary"] = 6,
					["BurstRounds"] = 10, ["MaxAmmo"] = 30, ["Weight"] = 5, ["Cost"] = 800,
				}),
				T("weapon.energy", "Energy pistol", ProtoKind.Weapon, "Weapon", new()
				{
					["AnimationCode"] = 5, ["PrimaryAttackMode"] = 6, ["FlagsExt"] = weaponBase | energy, ["DamageType"] = 1,
					["MinDamage"] = 8, ["MaxDamage"] = 16, ["MaxRangePrimary"] = 30, ["MinStrength"] = 3, ["ApCostPrimary"] = 5,
					["MaxAmmo"] = 12, ["Weight"] = 3, ["Cost"] = 1100,
				}),
				T("weapon.biggun", "Big gun", ProtoKind.Weapon, "Weapon", new()
				{
					["AnimationCode"] = 9, ["PrimaryAttackMode"] = 7, ["FlagsExt"] = weaponBase | bigGun | twoHand, ["MinDamage"] = 7,
					["MaxDamage"] = 11, ["MaxRangePrimary"] = 35, ["MaxRangeSecondary"] = 35, ["MinStrength"] = 6, ["ApCostPrimary"] = 6,
					["BurstRounds"] = 30, ["MaxAmmo"] = 120, ["Weight"] = 28, ["Cost"] = 3800,
				}),
				T("weapon.throwing", "Throwing weapon", ProtoKind.Weapon, "Weapon", new()
				{
					["AnimationCode"] = 0, ["PrimaryAttackMode"] = 5, ["MinDamage"] = 5, ["MaxDamage"] = 15, ["MaxRangePrimary"] = 15,
					["MinStrength"] = 1, ["ApCostPrimary"] = 4, ["Weight"] = 1, ["Cost"] = 50,
				}),
				T("armor.light", "Light armor", ProtoKind.Armor, "Armor", new()
				{
					["ArmorClass"] = 15, ["DrNormal"] = 20, ["DrLaser"] = 20, ["DrFire"] = 10, ["DrPlasma"] = 10, ["DrElectrical"] = 20,
					["DrExplosion"] = 10, ["DtNormal"] = 1, ["Weight"] = 8, ["Cost"] = 250,
				}),
				T("armor.heavy", "Heavy armor", ProtoKind.Armor, "Armor", new()
				{
					["ArmorClass"] = 30, ["DrNormal"] = 40, ["DrLaser"] = 60, ["DrFire"] = 40, ["DrPlasma"] = 30, ["DrElectrical"] = 60,
					["DrExplosion"] = 40, ["DtNormal"] = 4, ["DtLaser"] = 6, ["DtFire"] = 4, ["Weight"] = 35, ["Cost"] = 3000,
				}),
				T("ammo.default", "Ammunition", ProtoKind.Ammo, "Ammo", new()
				{
					["Quantity"] = 20, ["DamageMultiplier"] = 1, ["DamageDivisor"] = 1, ["Weight"] = 1, ["Cost"] = 40,
				}),
				T("drug.healing", "Healing drug", ProtoKind.Drug, "Drug", new()
				{
					["Stat1"] = 35, ["ImmediateAmount1"] = 20, ["Weight"] = 1, ["Cost"] = 100,
				}),
				T("container.default", "Container", ProtoKind.Container, "Container", new() { ["MaxSize"] = 100 }),
				T("key.default", "Key", ProtoKind.Key, "Key", new() { ["KeyCode"] = 1 }),
				T("misc.default", "Misc item", ProtoKind.MiscItem, "Misc", new()),
				T("critter.human", "Human NPC", ProtoKind.Critter, "Critter", new()
				{
					["BodyType"] = 0, ["KillType"] = 0, ["Experience"] = 100, ["FrmId"] = 0x01000000,
				}),
				T("critter.animal", "Animal", ProtoKind.Critter, "Critter", new()
				{
					["BodyType"] = 1, ["KillType"] = 5, ["Experience"] = 60, ["CritterFlags"] = 0, ["MaxHp"] = 20, ["MeleeDamage"] = 2,
				}),
				T("critter.robot", "Robot", ProtoKind.Critter, "Critter", new()
				{
					["BodyType"] = 2, ["KillType"] = 10, ["Experience"] = 120, ["CritterFlags"] = 0, ["MaxHp"] = 60, ["DrPoison"] = 100,
					["DrRadiation"] = 100,
				}),
				T("scenery.generic", "Generic scenery", ProtoKind.SceneryGeneric, "Scenery", new()),
				T("scenery.door", "Door", ProtoKind.SceneryDoor, "Scenery", new() { ["FlagsExt"] = 0x2800 }),
				T("wall.default", "Wall", ProtoKind.Wall, "Wall", new()),
				T("tile.default", "Tile", ProtoKind.Tile, "Tile", new()),
				T("misc.object", "Misc object", ProtoKind.Misc, "Misc object", new()),
			];
		}

		private static ProtoTemplate T(String id, String name, ProtoKind kind, String category, Dictionary<String, Int32> values)
		{
			return new ProtoTemplate(id, name, kind, values, category);
		}
	}
}