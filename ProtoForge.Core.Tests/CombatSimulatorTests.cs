namespace ProtoForge.Core.Tests
{
	using System;
	using ProtoForge.Core.Stats;
	using Xunit;

	/// <summary>The chance to hit, the critical hits and the bursts of <see cref="CombatSimulator"/> (figures worked out by hand from the engine rules).</summary>
	public class CombatSimulatorTests
	{
		private static HitInput Hit(
			Int32 skill = 80,
			Int32 perception = 6,
			Int32 distance = 10,
			Boolean ranged = true,
			Boolean player = true,
			Int32 weaponPerk = -1,
			Int32 armorClass = 0,
			AimedLocation location = AimedLocation.Uncalled,
			Int32 criticalChance = 0,
			Boolean sniper = false,
			Int32 luck = 5,
			Int32 minStrength = 0,
			Int32 strength = 5)
		{
			return new HitInput(skill, perception, strength, minStrength, distance, ranged, player, 0, weaponPerk, armorClass, 0, location, criticalChance, sniper, luck, 2, 0);
		}

		private static CombatInput Damage(Int32 min = 10, Int32 max = 10, Int32 rounds = 1, Int32 hitPoints = 100, Int32 threshold = 0, Int32 resistance = 0)
		{
			return new CombatInput(min, max, 1, 1, 0, threshold, resistance, rounds, hitPoints);
		}

		[Fact]
		public void ToHit_OfAPlayerAtDistance_IsSkillPlusPerceptionMinusFourPerHex()
		{
			// Skill + (Perception - 2) * 8 - Distance * 4 = 80 + 32 - 40.
			Assert.Equal(72, CombatSimulator.GetToHit(Hit(skill: 80, perception: 6, distance: 10)));
			Assert.Equal(62, CombatSimulator.GetToHit(Hit(skill: 80, perception: 6, distance: 10, armorClass: 10)));
		}

		[Fact]
		public void ToHit_IsCappedAt95_AndPointBlankGivesTheFullPerceptionBonus()
		{
			Assert.Equal(95, CombatSimulator.GetToHit(Hit(skill: 150, distance: 0)));

			// Perception 5, distance 0: (5 - 2) * 8 = 24 points.
			Assert.Equal(40 + 24, CombatSimulator.GetToHit(Hit(skill: 40, perception: 5, distance: 0)));
		}

		[Fact]
		public void ToHit_LongRangeAndScopeRangeWeaponsReadTheDistanceDifferently()
		{
			// Long range: multiplier 4: 5 - 4 * (8 - 2) = -19, held at -2 * 8 = -16, which is +64.
			Assert.Equal(64, CombatSimulator.GetToHit(Hit(skill: 0, perception: 8, distance: 5, weaponPerk: CombatSimulator.PerkLongRange)));

			// Scope range: closer than 8 hexes the distance counts 8 more.
			Int32 near = CombatSimulator.GetToHit(Hit(skill: 0, perception: 4, distance: 2, weaponPerk: CombatSimulator.PerkScopeRange));
			Int32 far = CombatSimulator.GetToHit(Hit(skill: 0, perception: 4, distance: 12, weaponPerk: CombatSimulator.PerkScopeRange));
			Assert.True(far > near);
		}

		[Fact]
		public void ToHit_AimedShotsCostTheLocationPenalty_HalvedInMelee_AndAccurateWeaponsGainTwenty()
		{
			Assert.Equal(72 - 60, CombatSimulator.GetToHit(Hit(location: AimedLocation.Eyes)));
			Assert.Equal(50 - 15, CombatSimulator.GetToHit(Hit(skill: 50, ranged: false, location: AimedLocation.Arm)));
			Assert.Equal(72 + 20, CombatSimulator.GetToHit(Hit(weaponPerk: CombatSimulator.PerkAccurate)));
		}

		[Fact]
		public void ToHit_AWeaponThatIsTooHeavyCostsTwentyPerMissingPointOfStrength()
		{
			Assert.Equal(72 - 40, CombatSimulator.GetToHit(Hit(minStrength: 7, strength: 5)));
		}

		[Fact]
		public void Estimate_AChanceOfSixtyGivesSixtyPercentOfHits_AndACriticalShareWorkedOutByHand()
		{
			// Melee, skill 70, armor class 10: 60. Critical chance 0: the gap to the roll, divided by 10, is the only critical chance.
			// Gaps 0..59 give 0..5 percent, ten rolls each: (0 + 1 + 2 + 3 + 4 + 5) * 10 / 100 / 100 = 0.015.
			ExpectedResult result = CombatSimulator.Estimate(Damage(), Hit(skill: 70, ranged: false, armorClass: 10));

			Assert.Equal(60, result.ToHit);
			Assert.Equal(0.6, result.HitChance, 6);
			Assert.Equal(0.015, result.CriticalChance, 6);
			Assert.Equal(0.6, result.ExpectedHits, 6);
		}

		[Fact]
		public void Estimate_ACriticalHitDoesMoreDamage_SoTheExpectationBeatsThePlainOne()
		{
			ExpectedResult plain = CombatSimulator.Estimate(Damage(), Hit(skill: 70, ranged: false, criticalChance: 0));
			ExpectedResult lucky = CombatSimulator.Estimate(Damage(), Hit(skill: 70, ranged: false, criticalChance: 50));

			Assert.True(lucky.CriticalChance > plain.CriticalChance);
			Assert.True(lucky.ExpectedDamage > plain.ExpectedDamage);
			Assert.Equal(plain.HitChance, lucky.HitChance, 6);
		}

		[Fact]
		public void Estimate_AimingAtTheEyesRaisesTheCriticalChance()
		{
			ExpectedResult uncalled = CombatSimulator.Estimate(Damage(), Hit(skill: 100, distance: 0));
			ExpectedResult eyes = CombatSimulator.Estimate(Damage(), Hit(skill: 100, distance: 0, location: AimedLocation.Eyes));

			Assert.True(eyes.HitChance < uncalled.HitChance);
			Assert.True(eyes.CriticalChance / eyes.HitChance > uncalled.CriticalChance / uncalled.HitChance);
		}

		[Fact]
		public void Estimate_SniperWithMaximumLuckMakesEveryHitCritical_ButOnlyForSingleShots()
		{
			ExpectedResult single = CombatSimulator.Estimate(Damage(), Hit(sniper: true, luck: 10));
			ExpectedResult burst = CombatSimulator.Estimate(Damage(rounds: 9), Hit(sniper: true, luck: 10));

			Assert.Equal(single.HitChance, single.CriticalChance, 6);
			Assert.True(burst.CriticalChance < burst.HitChance);
		}

		[Fact]
		public void Estimate_ABurstHitsMoreThanOneRoundButLessThanAllOfThem()
		{
			// Nine rounds: three go to the center, one rolls on its own, the two others follow the line of fire.
			ExpectedResult result = CombatSimulator.Estimate(Damage(rounds: 9), Hit(skill: 150, distance: 0));

			Assert.Equal(95, result.ToHit);
			Assert.InRange(result.ExpectedHits, 2.0, 3.5);
			Assert.True(result.ExpectedHits < 9 * result.HitChance);
		}

		[Fact]
		public void Estimate_AttacksToKillUseTheExpectedDamage()
		{
			ExpectedResult sure = CombatSimulator.Estimate(Damage(min: 10, max: 10, hitPoints: 100), Hit(skill: 150, distance: 0, ranged: false, criticalChance: 0));
			ExpectedResult unlikely = CombatSimulator.Estimate(Damage(min: 10, max: 10, hitPoints: 100), Hit(skill: 20, ranged: false));

			Assert.True(unlikely.ExpectedAttacksToKill > sure.ExpectedAttacksToKill);
			Assert.Null(CombatSimulator.Estimate(Damage(hitPoints: 0), Hit()).ExpectedAttacksToKill);
		}

		[Fact]
		public void Resistance_RoundsTheWayTheEngineDoes_AndTheAmmoCanPushItPastNinety()
		{
			// 5 damage with 50 % resistance: the engine subtracts the truncated 2, so 3 remain (not 2).
			Assert.Equal(3, CombatSimulator.Calculate(new CombatInput(5, 5, 1, 1, 0, 0, 50, 1, 0)).PerHit.Max);

			// The creature stops at 90 % but ammunition adds 20 afterwards: 100 %, then nothing gets through.
			CombatResult capped = CombatSimulator.Calculate(new CombatInput(10, 10, 1, 1, 20, 0, 120, 1, 0));
			Assert.Equal(100, capped.EffectiveResistance);
			Assert.Equal(0, capped.PerHit.Max);
		}

		[Fact]
		public void DamageBonus_IsAddedBeforeTheAmmunitionScalesTheDamage()
		{
			CombatResult result = CombatSimulator.Calculate(new CombatInput(10, 10, 2, 1, 0, 0, 0, 1, 0, DamageBonus: 4));

			Assert.Equal(28, result.PerHit.Max);
		}
	}
}