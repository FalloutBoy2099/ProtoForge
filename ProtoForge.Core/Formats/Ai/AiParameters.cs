namespace ProtoForge.Core.Formats.Ai
{
	using System;
	using System.Collections.Generic;
	using System.Linq;

	public enum AiParameterKind
	{
		Number,

		/// <summary>One of a fixed list of keywords (a value of -1 means "not set").</summary>
		Choice,

		/// <summary>A keyword list that may also hold several comma separated keywords.</summary>
		MultiChoice,

		/// <summary>A taunt message set: the name of a section of <c>aigenmsg.txt</c> or <c>aibdymsg.txt</c>.</summary>
		TauntSet,
	}

	/// <summary>Describes one parameter of an AI packet: how it is edited and which values the engine accepts.</summary>
	public sealed record AiParameter(String Key, AiParameterKind Kind, String Group, Int32 Min = 0, Int32 Max = 99999, IReadOnlyList<String>? Options = null);

	/// <summary>The parameters of <c>AI.txt</c> known to the editor (anything else is still shown and editable as plain text).</summary>
	public static class AiParameters
	{
		public const String NotSet = "-1";

		public const String PacketNumberKey = "packet_num";

		public const String DrugsKey = "chem_primary_desire";

		public const String GeneralType = "general_type";

		public const String BodyType = "body_type";

		private static readonly IReadOnlyList<AiParameter> Known =
		[
			new(PacketNumberKey, AiParameterKind.Number, "Identity", 0, 1000),

			new("disposition", AiParameterKind.Choice, "Behavior", Options: ["none", "coward", "defensive", "aggressive", "berserk", "custom"]),
			new("attack_who", AiParameterKind.Choice, "Behavior", Options: ["whomever_attacking_me", "strongest", "weakest", "whomever", "closest"]),
			new("best_weapon", AiParameterKind.Choice, "Behavior", Options: ["no_pref", "unarmed", "unarmed_over_thrown", "melee", "melee_over_ranged", "ranged", "ranged_over_melee", "random"]),
			new("distance", AiParameterKind.Choice, "Behavior", Options: ["charge", "on_your_own", "stay", "stay_close", "snipe"]),
			new("chem_use", AiParameterKind.Choice, "Behavior", Options: ["clean", "sometimes", "stims_when_hurt_little", "stims_when_hurt_lots", "anytime", "always"]),
			new("area_attack_mode", AiParameterKind.Choice, "Behavior", Options: ["always", "sometimes", "be_sure", "be_careful", "be_absolutely_sure"]),
			new("run_away_mode", AiParameterKind.Choice, "Behavior", Options: ["none", "coward", "finger_hurts", "bleeding", "not_feeling_good", "tourniquet", "never"]),
			new("hurt_too_much", AiParameterKind.MultiChoice, "Behavior", Options: ["blind", "crippled", "crippled_arms", "crippled_legs"]),
			new("aggression", AiParameterKind.Number, "Behavior", -999, 999),
			new("min_hp", AiParameterKind.Number, "Behavior", 0, 999),
			new("min_to_hit", AiParameterKind.Number, "Behavior", 0, 999),
			new("max_dist", AiParameterKind.Number, "Behavior", 0, 999),
			new("called_freq", AiParameterKind.Number, "Behavior", 1, 1000000),
			new("secondary_freq", AiParameterKind.Number, "Behavior", 1, 1000000),

			new("chance", AiParameterKind.Number, "Taunts", 0, 100),
			new(GeneralType, AiParameterKind.TauntSet, "Taunts"),
			new(BodyType, AiParameterKind.TauntSet, "Taunts"),
			new("attack_start", AiParameterKind.Number, "Taunts"),
			new("attack_end", AiParameterKind.Number, "Taunts"),
			new("miss_start", AiParameterKind.Number, "Taunts"),
			new("miss_end", AiParameterKind.Number, "Taunts"),
			new("move_start", AiParameterKind.Number, "Taunts"),
			new("move_end", AiParameterKind.Number, "Taunts"),
			new("run_start", AiParameterKind.Number, "Taunts"),
			new("run_end", AiParameterKind.Number, "Taunts"),
			new("hit_head_start", AiParameterKind.Number, "Taunts"),
			new("hit_head_end", AiParameterKind.Number, "Taunts"),
			new("hit_eyes_start", AiParameterKind.Number, "Taunts"),
			new("hit_eyes_end", AiParameterKind.Number, "Taunts"),
			new("hit_groin_start", AiParameterKind.Number, "Taunts"),
			new("hit_groin_end", AiParameterKind.Number, "Taunts"),
			new("hit_torso_start", AiParameterKind.Number, "Taunts"),
			new("hit_torso_end", AiParameterKind.Number, "Taunts"),
			new("hit_left_arm_start", AiParameterKind.Number, "Taunts"),
			new("hit_left_arm_end", AiParameterKind.Number, "Taunts"),
			new("hit_right_arm_start", AiParameterKind.Number, "Taunts"),
			new("hit_right_arm_end", AiParameterKind.Number, "Taunts"),
			new("hit_left_leg_start", AiParameterKind.Number, "Taunts"),
			new("hit_left_leg_end", AiParameterKind.Number, "Taunts"),
			new("hit_right_leg_start", AiParameterKind.Number, "Taunts"),
			new("hit_right_leg_end", AiParameterKind.Number, "Taunts"),

			new("font", AiParameterKind.Number, "Display"),
			new("color", AiParameterKind.Number, "Display"),
			new("outline_color", AiParameterKind.Number, "Display"),
		];

		private static readonly HashSet<String> HandledElsewhere = new(StringComparer.OrdinalIgnoreCase) { DrugsKey };

		public static IReadOnlyList<AiParameter> All
		{
			get { return Known; }
		}

		public static IEnumerable<String> Groups
		{
			get { return Known.Select(p => p.Group).Distinct(); }
		}

		public static AiParameter? Find(String key)
		{
			return Known.FirstOrDefault(p => String.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>True for the keys that have a dedicated editor (including the drug list).</summary>
		public static Boolean IsKnown(String key)
		{
			return Find(key) is not null || HandledElsewhere.Contains(key);
		}

		/// <summary>The drug prototype ids of <c>chem_primary_desire</c> (at most three, -1 and empty entries ignored).</summary>
		public static IReadOnlyList<Int32> ParseDrugs(String? value)
		{
			List<Int32> drugs = [];
			if (value is null)
			{
				return drugs;
			}

			foreach (String part in value.Split(','))
			{
				if (Int32.TryParse(part.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out Int32 pid) && pid != -1)
				{
					drugs.Add(pid);
				}
			}

			return [.. drugs.Take(3)];
		}

		public static String FormatDrugs(IEnumerable<Int32> pids)
		{
			String text = String.Join(",", pids.Take(3).Select(p => p.ToString(System.Globalization.CultureInfo.InvariantCulture)));
			return text.Length == 0 ? NotSet : text;
		}
	}
}