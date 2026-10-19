namespace ProtoForge.Core.Stats
{
	using System;

	/// <summary>The smallest, the average and the largest value of something that varies from hit to hit.</summary>
	public sealed record DamageRange(Double Min, Double Average, Double Max);

	/// <summary>Everything the damage of a weapon against a target depends on.</summary>
	public sealed record CombatInput(
		Int32 MinDamage,
		Int32 MaxDamage,
		Int32 AmmoMultiplier,
		Int32 AmmoDivisor,
		Int32 AmmoResistanceModifier,
		Int32 Threshold,
		Int32 Resistance,
		Int32 Rounds,
		Int32 TargetHitPoints,
		Int32 DamageBonus = 0);

	/// <summary>What one attack does to the target when every round hits.</summary>
	public sealed record CombatResult(
		DamageRange Raw,
		DamageRange PerHit,
		DamageRange PerAttack,
		Int32? AttacksBestCase,
		Int32? AttacksAverage,
		Int32? AttacksWorstCase,
		Int32 EffectiveResistance);

	/// <summary>Where the attacker aims: the penalty on the chance to hit, which the engine also turns into a bonus on the critical chance.</summary>
	public enum AimedLocation
	{
		Uncalled,
		Head,
		Arm,
		Leg,
		Eyes,
		Groin,
	}

	/// <summary>The attacker, the weapon and the situation: what decides whether an attack lands and whether it is a critical hit.</summary>
	public sealed record HitInput(
		Int32 Skill,
		Int32 Perception,
		Int32 Strength,
		Int32 MinStrength,
		Int32 Distance,
		Boolean Ranged,
		Boolean AttackerIsPlayer,
		Int32 SharpshooterRank,
		Int32 WeaponPerk,
		Int32 TargetArmorClass,
		Int32 AmmoArmorClassModifier,
		AimedLocation Location,
		Int32 CriticalChance,
		Boolean Sniper,
		Int32 Luck,
		Double CriticalMultiplier,
		Int32 OtherModifier);

	/// <summary>The chances of an attack, then what it does on average (hits of a burst, critical hits and the armor all taken into account).</summary>
	public sealed record ExpectedResult(
		Int32 ToHit,
		Double HitChance,
		Double CriticalChance,
		Double ExpectedHits,
		Double ExpectedDamage,
		Double? ExpectedAttacksToKill);

	/// <summary>
	/// The damage of one attack, with the rules of the Fallout 2 combat: the ammunition scales the damage (multiplier over divisor),
	/// the damage threshold of the target is subtracted, then the damage resistance (shifted by the ammunition) removes a percentage.
	/// <see cref="Estimate"/> adds the chance to hit (skill, perception and distance, armor class, aimed shots), the critical hits
	/// and the rounds of a burst that reach the target. Not simulated: the critical effects (knock-outs, armor bypass), lighting,
	/// other creatures in the line of fire and the rounds of a burst that go to the sides.
	/// </summary>
	public static class CombatSimulator
	{
		/// <summary>The engine never lets the resistance of a creature go above this value...</summary>
		public const Int32 MaxResistance = 90;

		/// <summary>...but the resistance modifier of the ammunition is added afterwards, and the sum stops at 100.</summary>
		public const Int32 AbsoluteMaxResistance = 100;

		/// <summary>The most the chance to hit can be.</summary>
		public const Int32 MaxToHit = 95;

		public const Int32 PerkLongRange = 58;
		public const Int32 PerkAccurate = 59;
		public const Int32 PerkScopeRange = 64;

		/// <summary>The damage multiplier of an ordinary hit, in the half units of the engine (it divides by 2 at the end).</summary>
		private const Int32 NormalMultiplier = 2;

		public static CombatResult Calculate(CombatInput input)
		{
			Int32 min = Math.Max(0, Math.Min(input.MinDamage, input.MaxDamage));
			Int32 max = Math.Max(0, Math.Max(input.MinDamage, input.MaxDamage));
			Int32 rounds = Math.Max(1, input.Rounds);
			Int32 resistance = GetResistance(input);

			DamageRange perHit = GetPerHit(input, min, max, resistance, NormalMultiplier);
			DamageRange perAttack = new(perHit.Min * rounds, perHit.Average * rounds, perHit.Max * rounds);
			return new CombatResult(
				new DamageRange(min, (min + max) / 2.0, max),
				perHit,
				perAttack,
				Attacks(input.TargetHitPoints, perAttack.Max),
				Attacks(input.TargetHitPoints, perAttack.Average),
				Attacks(input.TargetHitPoints, perAttack.Min),
				resistance);
		}

		/// <summary>The chance to hit, as a number that is at most <see cref="MaxToHit"/> (it can be negative: the attack then never lands).</summary>
		public static Int32 GetToHit(HitInput input)
		{
			Int32 toHit = input.Skill;

			if (input.Ranged)
			{
				Int32 multiplier = 2;
				Int32 minimumDistance = 0;
				if (input.WeaponPerk == PerkLongRange)
				{
					multiplier = 4;
				}
				else if (input.WeaponPerk == PerkScopeRange)
				{
					multiplier = 5;
					minimumDistance = 8;
				}

				Int32 perception = input.Perception + (input.AttackerIsPlayer ? 2 * input.SharpshooterRank : 0);
				Int32 distance = input.Distance;
				if (distance >= minimumDistance)
				{
					distance -= input.AttackerIsPlayer ? multiplier * (perception - 2) : multiplier * perception;
				}
				else
				{
					distance += minimumDistance;
				}

				distance = Math.Max(distance, -2 * perception);
				toHit += distance * -4;
			}

			Int32 missingStrength = input.MinStrength - input.Strength;
			if (missingStrength > 0)
			{
				toHit -= 20 * missingStrength;
			}

			if (input.WeaponPerk == PerkAccurate)
			{
				toHit += 20;
			}

			toHit -= Math.Max(0, input.TargetArmorClass + input.AmmoArmorClassModifier);
			toHit += input.Ranged ? GetLocationPenalty(input.Location) : GetLocationPenalty(input.Location) / 2;
			toHit += input.OtherModifier;
			return Math.Min(toHit, MaxToHit);
		}

		/// <summary>The penalty of aiming at a body part (the engine's default table; it is a bonus on the critical chance as well).</summary>
		public static Int32 GetLocationPenalty(AimedLocation location)
		{
			return location switch
			{
				AimedLocation.Head => -40,
				AimedLocation.Arm => -30,
				AimedLocation.Leg => -20,
				AimedLocation.Eyes => -60,
				AimedLocation.Groin => -30,
				_ => 0,
			};
		}

		/// <summary>
		/// The attack as the engine plays it: a roll of 1 to 100 that has to be at most the chance to hit; the gap between the two, divided by 10,
		/// is added to the critical chance (which the aimed location raises) to decide whether the hit is critical. A burst rolls once for the whole
		/// burst (a critical success adds 20 to the chance of each round), then the rounds aimed at the target roll one by one.
		/// </summary>
		public static ExpectedResult Estimate(CombatInput damage, HitInput hit)
		{
			Int32 toHit = GetToHit(hit);
			Int32 rounds = Math.Max(1, damage.Rounds);
			Int32 min = Math.Max(0, Math.Min(damage.MinDamage, damage.MaxDamage));
			Int32 max = Math.Max(0, Math.Max(damage.MinDamage, damage.MaxDamage));
			Int32 resistance = GetResistance(damage);
			Int32 criticalMultiplier = Math.Max(NormalMultiplier, (Int32)Math.Round(hit.CriticalMultiplier * 2, MidpointRounding.AwayFromZero));
			Double normalDamage = GetPerHit(damage, min, max, resistance, NormalMultiplier).Average;
			Double criticalDamage = GetPerHit(damage, min, max, resistance, criticalMultiplier).Average;

			Int32 chance = Math.Clamp(toHit, 0, 100);
			Int32 criticalBase = hit.CriticalChance - GetLocationPenalty(hit.Location);
			Double forced = rounds == 1 && hit.Sniper && hit.Ranged && hit.AttackerIsPlayer ? Math.Clamp(hit.Luck, 0, 10) / 10.0 : 0;

			Double successful = 0;
			Double critical = 0;
			for (Int32 roll = 1; roll <= chance; roll++)
			{
				Double natural = Math.Clamp(criticalBase + (chance - roll) / 10, 0, 100) / 100.0;
				critical += (forced + ((1 - forced) * natural)) / 100.0;
				successful += 0.01;
			}

			Double plain = successful - critical;
			Double hitsPlain = 1;
			Double hitsCritical = 1;
			if (rounds > 1)
			{
				// Without the hit location penalty: the rounds that follow the first roll are aimed at the torso.
				Int32 torso = Math.Clamp(GetToHit(hit with { Location = AimedLocation.Uncalled }), 0, 100);
				hitsPlain = ExpectedBurstHits(rounds, chance, torso);
				hitsCritical = ExpectedBurstHits(rounds, Math.Min(100, chance + 20), torso);
			}

			Double expectedHits = (plain * hitsPlain) + (critical * hitsCritical);
			Double expectedDamage = (plain * hitsPlain * normalDamage) + (critical * hitsCritical * criticalDamage);
			Double? attacks = expectedDamage > 0 && damage.TargetHitPoints > 0 ? Math.Ceiling(damage.TargetHitPoints / expectedDamage) : null;
			return new ExpectedResult(toHit, successful, critical, expectedHits, expectedDamage, attacks);
		}

		/// <summary>
		/// The rounds of a burst that hit the target once the burst as a whole succeeded: a third of the rounds go to the center,
		/// half of those roll on their own, and each center round that is left follows the line of fire and stops at the first miss.
		/// </summary>
		private static Double ExpectedBurstHits(Int32 rounds, Int32 mainChance, Int32 pathChance)
		{
			Int32 center = Math.Max(1, rounds / 3);
			Int32 main = center / 2;
			if (main == 0)
			{
				main = 1;
				center--;
			}

			Double q = mainChance / 100.0;
			Double p = pathChance / 100.0;
			Double expected = 0;
			for (Int32 hits = 0; hits <= main; hits++)
			{
				Double probability = Binomial(main, hits) * Math.Pow(q, hits) * Math.Pow(1 - q, main - hits);
				Double chain = 0;
				for (Int32 k = 1; k <= Math.Max(0, center - hits); k++)
				{
					chain += Math.Pow(p, k);
				}

				expected += probability * (hits + chain);
			}

			return expected;
		}

		private static Double Binomial(Int32 n, Int32 k)
		{
			Double result = 1;
			for (Int32 i = 1; i <= k; i++)
			{
				result = result * (n - k + i) / i;
			}

			return result;
		}

		private static Int32 GetResistance(CombatInput input)
		{
			return Math.Clamp(Math.Min(Math.Max(0, input.Resistance), MaxResistance) + input.AmmoResistanceModifier, 0, AbsoluteMaxResistance);
		}

		private static DamageRange GetPerHit(CombatInput input, Int32 min, Int32 max, Int32 resistance, Int32 multiplier)
		{
			Double sum = 0;
			Double least = Double.MaxValue;
			Double most = 0;
			for (Int32 raw = min; raw <= max; raw++)
			{
				Int32 hit = PerHit(raw, input, resistance, multiplier);
				sum += hit;
				least = Math.Min(least, hit);
				most = Math.Max(most, hit);
			}

			return new DamageRange(least, sum / (max - min + 1), most);
		}

		/// <summary>One round: the same order as the engine (damage plus bonus, times multiplier, over divisor, halved; minus threshold; then the resistance).</summary>
		private static Int32 PerHit(Int32 raw, CombatInput input, Int32 resistance, Int32 multiplier)
		{
			Int32 divisor = Math.Max(1, input.AmmoDivisor);
			Int32 scaled = (raw + input.DamageBonus) * multiplier * Math.Max(1, input.AmmoMultiplier) / divisor / 2;
			Int32 afterThreshold = Math.Max(0, scaled - Math.Max(0, input.Threshold));
			return afterThreshold - (afterThreshold * resistance / 100);
		}

		/// <summary>How many attacks it takes to bring the target to zero, or null when the attack does nothing (or the hit points are unknown).</summary>
		private static Int32? Attacks(Int32 hitPoints, Double damage)
		{
			return hitPoints <= 0 || damage <= 0 ? null : (Int32)Math.Ceiling(hitPoints / damage);
		}
	}
}