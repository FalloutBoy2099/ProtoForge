namespace ProtoForge.Core.Reference
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Linq;

	/// <summary>One selectable value of a list (enumeration, perk, stat, caliber, ...).</summary>
	public readonly record struct ChoiceOption(Int32 Value, String Label)
	{
		public override String ToString()
		{
			return Label;
		}
	}

	/// <summary>
	/// Named lists of values used by the editors. Built-in English defaults are replaced by the labels found in the game
	/// MSG files (stat.msg, perk.msg, proto.msg, ...) when a game is loaded.
	/// </summary>
	public sealed class ChoiceCatalog
	{
		public const String DamageType = "DamageType";
		public const String Material = "Material";
		public const String WeaponAnimation = "WeaponAnimation";
		public const String AttackType = "AttackType";
		public const String Perk = "Perk";
		public const String Stat = "Stat";
		public const String DrugStat = "DrugStat";
		public const String Caliber = "Caliber";
		public const String BodyType = "BodyType";
		public const String KillType = "KillType";
		public const String Gender = "Gender";
		public const String AiPacket = "AiPacket";
		public const String Team = "Team";
		public const String ItemType = "ItemType";
		public const String SceneryType = "SceneryType";
		public const String Skill = "Skill";
		public const String ElevatorType = "ElevatorType";
		public const String CriticalFail = "CriticalFail";
		public const String SoundId = "SoundId";

		private readonly Dictionary<String, List<ChoiceOption>> _sets = new(StringComparer.Ordinal);

		public ChoiceCatalog()
		{
			LoadDefaults();
		}

		public event EventHandler? Changed;

		public IReadOnlyList<ChoiceOption> Get(String setId)
		{
			return _sets.TryGetValue(setId, out List<ChoiceOption>? list) ? list : [];
		}

		public Boolean Contains(String setId, Int32 value)
		{
			return Get(setId).Any(o => o.Value == value);
		}

		/// <summary>Replaces one list (for example with game-localised labels).</summary>
		public void Set(String setId, IEnumerable<ChoiceOption> options)
		{
			_sets[setId] = [.. options];
			Changed?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>Label of a value, or the number itself when the value is not part of the list.</summary>
		public String Describe(String setId, Int32 value)
		{
			foreach (ChoiceOption option in Get(setId))
			{
				if (option.Value == value)
				{
					return option.Label;
				}
			}

			return value.ToString(CultureInfo.InvariantCulture);
		}

		/// <summary>Replaces the labels of an existing list by values read from a message set (value + base offset => message id).</summary>
		public void Relabel(String setId, Func<Int32, String?> labelSource)
		{
			if (!_sets.TryGetValue(setId, out List<ChoiceOption>? list))
			{
				return;
			}

			for (Int32 i = 0; i < list.Count; i++)
			{
				String? label = labelSource(list[i].Value);
				if (!String.IsNullOrWhiteSpace(label))
				{
					list[i] = list[i] with { Label = label };
				}
			}

			Changed?.Invoke(this, EventArgs.Empty);
		}

		private void LoadDefaults()
		{
			_sets[DamageType] = Indexed("Normal", "Laser", "Fire", "Plasma", "Electrical", "EMP", "Explosion");
			_sets[Material] = Indexed("Glass", "Metal", "Plastic", "Wood", "Dirt", "Stone", "Cement", "Leather");
			_sets[ItemType] = Indexed("Armor", "Container", "Drug", "Weapon", "Ammo", "Misc", "Key");
			_sets[SceneryType] = Indexed("Door", "Stairs", "Elevator", "Ladder (bottom)", "Ladder (top)", "Generic");
			_sets[ElevatorType] = Indexed("Elevator 0", "Elevator 1", "Elevator 2", "Elevator 3");
			_sets[WeaponAnimation] = Indexed(
				"None", "Knife (D)", "Club (E)", "Sledgehammer (F)", "Spear (G)", "Pistol (H)", "Submachine gun (I)",
				"Rifle (J)", "Big gun (K)", "Minigun (L)", "Rocket launcher (M)", "Custom (S)", "Custom (O)", "Custom (P)",
				"Custom (Q)", "Custom (T)");
			_sets[AttackType] = Indexed("None", "Punch", "Kick", "Swing", "Thrust", "Throw", "Single", "Burst", "Flame");
			_sets[BodyType] = Indexed("Biped", "Quadruped", "Robotic");
			_sets[Gender] = Indexed("Male", "Female");
			_sets[KillType] = Indexed(
				"Man", "Woman", "Child", "Super Mutant", "Ghoul", "Brahmin", "Radscorpion", "Rat", "Floater", "Centaur",
				"Robot", "Dog", "Manti", "Deathclaw", "Plant", "Gecko", "Alien", "Giant Ant", "Big Boss");

			List<String> stats =
			[
				"Strength", "Perception", "Endurance", "Charisma", "Intelligence", "Agility", "Luck", "Max hit points",
				"Max action points", "Armor class", "Unarmed damage", "Melee damage", "Carry weight", "Sequence",
				"Healing rate", "Critical chance", "Better criticals",
				"DT normal", "DT laser", "DT fire", "DT plasma", "DT electrical", "DT EMP", "DT explosion",
				"DR normal", "DR laser", "DR fire", "DR plasma", "DR electrical", "DR EMP", "DR explosion",
				"Radiation resistance", "Poison resistance", "Age", "Gender", "Current hit points",
				"Current poison level", "Current radiation level",
			];
			_sets[Stat] = Indexed([.. stats]);

			List<ChoiceOption> drugStats =
			[
				new ChoiceOption(-2, "Minimum value of Stat 2"),
				new ChoiceOption(-1, "None"),
				.. stats.Select((name, index) => new ChoiceOption(index, name)),
			];
			_sets[DrugStat] = drugStats;

			_sets[Skill] = Indexed(
				"Small guns", "Big guns", "Energy weapons", "Unarmed", "Melee weapons", "Throwing", "First aid", "Doctor",
				"Sneak", "Lockpick", "Steal", "Traps", "Science", "Repair", "Speech", "Barter", "Gambling", "Outdoorsman");

			List<ChoiceOption> perks = [new ChoiceOption(-1, "None")];
			for (Int32 i = 0; i < 119; i++)
			{
				perks.Add(new ChoiceOption(i, "Perk " + i.ToString(CultureInfo.InvariantCulture)));
			}

			_sets[Perk] = perks;

			List<ChoiceOption> calibers = [];
			for (Int32 i = 0; i < 20; i++)
			{
				calibers.Add(new ChoiceOption(i, i == 0 ? "None" : "Caliber " + i.ToString(CultureInfo.InvariantCulture)));
			}

			_sets[Caliber] = calibers;
			_sets[AiPacket] = [new ChoiceOption(0, "0")];
			_sets[Team] = [new ChoiceOption(0, "Player")];

			_sets[CriticalFail] = Indexed(
				"None",
				"Drop weapon (grenade: explodes close)",
				"Drop weapon, lose ammo",
				"Drop weapon, lose ammo, weapon explodes",
				"Explodes in hand, knockback, shrapnel or dismemberment",
				"Drop weapon, lose ammo, explodes, knockback or dismemberment",
				"Drop weapon, lose ammo, explodes, knockback, dismemberment or incineration");

			_sets[SoundId] = BuildSoundIds();
		}

		/// <summary>
		/// The sound letter of items and weapons (<c>sound/sfx/WA&lt;letter&gt;...</c>). Only letters that exist in the game are
		/// listed; the descriptions are the weapons that use them in the original game.
		/// </summary>
		private static List<ChoiceOption> BuildSoundIds()
		{
			Dictionary<Int32, String> descriptions = new()
			{
				[0] = "Robo melee weapon",
				[35] = "Pulse Pistol",
				[36] = "End Boss Knife",
				[48] = "Ripper, Claw",
				[49] = "Brass Knuckles, Spiked Knuckles, Power Fist, Boxing Gloves",
				[50] = "Rock, Plant Spike, Uranium Ore, Gold Nugget",
				[51] = "Knife, Combat Knife, Shiv, Wakizashi Blade, Switchblade",
				[52] = "Flare, Throwing Knife",
				[53] = "Wrench, Crowbar",
				[54] = "Louisville Slugger, Club",
				[55] = "Sledgehammer, Super Sledge",
				[56] = "Spear, Sharpened Spear, Sharpened Pole",
				[57] = "Cattle Prod",
				[64] = "End Boss Plasma Gun",
				[65] = "10mm Pistol",
				[66] = "Desert Eagle, 9mm Mauser",
				[67] = "Phazer, Laser Pistol, Solar Scorcher",
				[68] = "10mm SMG, Grease Gun M3A1",
				[69] = "14mm Pistol, .223 Pistol, .44 Magnum Revolver",
				[70] = "Plasma Pistol",
				[71] = "Hunting Rifle, Sniper Rifle, Pipe Rifle",
				[72] = "FN FAL, HK G11, HK P90c, Assault Rifle, Tommy Gun, XL70E3",
				[73] = "Flamer, Flame Breath",
				[74] = "Laser Rifle",
				[75] = "Plasma Rifle, Dual Plasma Cannon",
				[76] = "Bozar, Minigun, Light Support Weapon, Dual Minigun",
				[77] = "Gatling Laser",
				[78] = "Rocket Launcher, Robo Rocket Launcher",
				[79] = "Grenade (Frag), Molotov Cocktail",
				[80] = "Grenade (Plasma), Holy Hand Grenade",
				[81] = "Grenade (Pulse)",
				[82] = "H&K CAWS, M60, Shotguns",
				[83] = "Alien Blaster",
				[85] = "Red Ryder BB Gun",
				[86] = "Zip Gun",
				[87] = "M72 Gauss Rifle",
				[88] = "Pulse Rifle",
				[89] = "Gauss Pistol",
				[90] = "Needler Pistol",
			};

			Int32[] letters = [33, 35, 36, 37, 38, 39, 40, 41, 43, 44, 45, 46, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 59, 61, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73,
				74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 93, 94, 95, 96, 123, 125, 126];

			List<ChoiceOption> options = [new ChoiceOption(0, descriptions[0])];
			foreach (Int32 code in letters)
			{
				String letter = ((Char)code).ToString();
				options.Add(new ChoiceOption(code, descriptions.TryGetValue(code, out String? text) ? $"{letter}  {text}" : letter));
			}

			return options;
		}

		private static List<ChoiceOption> Indexed(params String[] labels)
		{
			List<ChoiceOption> list = new(labels.Length);
			for (Int32 i = 0; i < labels.Length; i++)
			{
				list.Add(new ChoiceOption(i, labels[i]));
			}

			return list;
		}
	}
}