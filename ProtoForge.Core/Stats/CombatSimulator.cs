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
		Int32 TargetHitPoints);

	/// <summary>What one attack does to the target.</summary>
	public sealed record CombatResult(
		DamageRange Raw,
		DamageRange PerHit,
		DamageRange PerAttack,
		Int32? AttacksBestCase,
		Int32? AttacksAverage,
		Int32? AttacksWorstCase,
		Int32 EffectiveResistance);

	/// <summary>
	/// The damage of one attack, with the rules of the Fallout 2 combat: the ammunition scales the damage (multiplier over divisor),
	/// the damage threshold of the target is subtracted, then the damage resistance (shifted by the ammunition) removes a percentage.
	/// Every hit of a burst is supposed to land. A sketch of the engine, good enough to compare weapons, armors and ammunition.
	/// </summary>
	public static class CombatSimulator
	{
		/// <summary>The engine never lets a resistance go above this value.</summary>
		public const Int32 MaxResistance = 90;

		public static CombatResult Calculate(CombatInput input)
		{
			Int32 min = Math.Max(0, Math.Min(input.MinDamage, input.MaxDamage));
			Int32 max = Math.Max(0, Math.Max(input.MinDamage, input.MaxDamage));
			Int32 rounds = Math.Max(1, input.Rounds);
			Int32 resistance = Math.Clamp(input.Resistance + input.AmmoResistanceModifier, 0, MaxResistance);

			Double sum = 0;
			Double least = Double.MaxValue;
			Double most = 0;
			for (Int32 raw = min; raw <= max; raw++)
			{
				Int32 hit = PerHit(raw, input, resistance);
				sum += hit;
				least = Math.Min(least, hit);
				most = Math.Max(most, hit);
			}

			Double average = sum / (max - min + 1);
			DamageRange perHit = new(least, average, most);
			DamageRange perAttack = new(least * rounds, average * rounds, most * rounds);
			return new CombatResult(
				new DamageRange(min, (min + max) / 2.0, max),
				perHit,
				perAttack,
				Attacks(input.TargetHitPoints, perAttack.Max),
				Attacks(input.TargetHitPoints, perAttack.Average),
				Attacks(input.TargetHitPoints, perAttack.Min),
				resistance);
		}

		private static Int32 PerHit(Int32 raw, CombatInput input, Int32 resistance)
		{
			Int32 divisor = Math.Max(1, input.AmmoDivisor);
			Int32 scaled = raw * Math.Max(1, input.AmmoMultiplier) / divisor;
			Int32 afterThreshold = Math.Max(0, scaled - Math.Max(0, input.Threshold));
			return afterThreshold * (100 - resistance) / 100;
		}

		/// <summary>How many attacks it takes to bring the target to zero, or null when the attack does nothing (or the hit points are unknown).</summary>
		private static Int32? Attacks(Int32 hitPoints, Double damage)
		{
			return hitPoints <= 0 || damage <= 0 ? null : (Int32)Math.Ceiling(hitPoints / damage);
		}
	}
}