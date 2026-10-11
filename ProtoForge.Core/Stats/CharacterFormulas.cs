namespace ProtoForge.Core.Stats
{
	using System;
	using System.Collections.Generic;

	public enum FormulaSet
	{
		Fallout2,
		Fallout1,
	}

	/// <summary>
	/// Derived statistics and initial skill values computed from SPECIAL, as in the original character editor.
	/// Used by the "compute from SPECIAL" helper of the critter editor.
	/// </summary>
	public static class CharacterFormulas
	{
		/// <summary>SPECIAL values in the order Strength, Perception, Endurance, Charisma, Intelligence, Agility, Luck.</summary>
		public static IReadOnlyDictionary<String, Int32> ComputeDerived(Int32[] special)
		{
			Int32 st = special[0];
			Int32 pe = special[1];
			Int32 en = special[2];
			Int32 ag = special[5];
			Int32 lk = special[6];

			return new Dictionary<String, Int32>(StringComparer.Ordinal)
			{
				["MaxHp"] = 15 + st + (2 * en),
				["MaxAp"] = 5 + (ag / 2),
				["ArmorClass"] = ag,
				["MeleeDamage"] = Math.Max(1, st - 5),
				["CarryWeight"] = 25 + (25 * st),
				["Sequence"] = 2 * pe,
				["HealingRate"] = Math.Max(1, en / 3),
				["CriticalChance"] = lk,
				["DrRadiation"] = 2 * en,
				["DrPoison"] = 5 * en,
			};
		}

		public static IReadOnlyDictionary<String, Int32> ComputeSkills(FormulaSet set, Int32[] special)
		{
			Int32 st = special[0];
			Int32 pe = special[1];
			Int32 en = special[2];
			Int32 ch = special[3];
			Int32 inl = special[4];
			Int32 ag = special[5];
			Int32 lk = special[6];

			if (set == FormulaSet.Fallout2)
			{
				return new Dictionary<String, Int32>(StringComparer.Ordinal)
				{
					["SmallGuns"] = 5 + (4 * ag),
					["BigGuns"] = 2 * ag,
					["EnergyWeapons"] = 2 * ag,
					["Unarmed"] = 30 + (2 * (ag + st)),
					["MeleeWeapons"] = 20 + (2 * (ag + st)),
					["Throwing"] = 4 * ag,
					["FirstAid"] = 2 * (pe + inl),
					["Doctor"] = 5 + pe + inl,
					["Sneak"] = 5 + (3 * ag),
					["Lockpick"] = 10 + pe + ag,
					["Steal"] = 3 * ag,
					["Traps"] = 10 + pe + ag,
					["Science"] = 4 * inl,
					["Repair"] = 3 * inl,
					["Speech"] = 5 * ch,
					["Barter"] = 4 * ch,
					["Gambling"] = 5 * lk,
					["Outdoorsman"] = 2 * (en + inl),
				};
			}

			return new Dictionary<String, Int32>(StringComparer.Ordinal)
			{
				["SmallGuns"] = 35 + ag,
				["BigGuns"] = 10 + ag,
				["EnergyWeapons"] = 10 + ag,
				["Unarmed"] = 65 + ((ag + st) / 2),
				["MeleeWeapons"] = 55 + ((ag + st) / 2),
				["Throwing"] = 40 + ag,
				["FirstAid"] = 30 + ((pe + inl) / 2),
				["Doctor"] = 15 + ((pe + inl) / 2),
				["Sneak"] = 25 + ag,
				["Lockpick"] = 20 + ((pe + ag) / 2),
				["Steal"] = 20 + ag,
				["Traps"] = 20 + ((pe + ag) / 2),
				["Science"] = 25 + (2 * inl),
				["Repair"] = 20 + inl,
				["Speech"] = 25 + (2 * ch),
				["Barter"] = 20 + (2 * ch),
				["Gambling"] = 20 + (3 * lk),
				["Outdoorsman"] = 5 + ((en + inl) / 2),
			};
		}

		/// <summary>Armor class plus every damage threshold and resistance: the "armor score" of the armor editor.</summary>
		public static Int32 ArmorScore(Int32 armorClass, IEnumerable<Int32> thresholds, IEnumerable<Int32> resistances)
		{
			Int32 score = armorClass;
			foreach (Int32 value in thresholds)
			{
				score += value;
			}

			foreach (Int32 value in resistances)
			{
				score += value;
			}

			return score;
		}

		/// <summary>Weapon quality score shown in the weapon editor: <c>fixed</c> uses the sfall corrected formula.</summary>
		public static Int32 WeaponScore(Int32 minDamage, Int32 maxDamage, Boolean hasPerk, Boolean originalFormula)
		{
			Int32 score;
			if (!originalFormula)
			{
				score = (maxDamage + minDamage) / 4;
				if (hasPerk)
				{
					score *= 2;
				}
			}
			else
			{
				score = Math.Abs(maxDamage - minDamage) / 2;
				if (hasPerk)
				{
					score *= 5;
				}
			}

			return score;
		}
	}
}