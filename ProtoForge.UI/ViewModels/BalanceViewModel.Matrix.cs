namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using CommunityToolkit.Mvvm.ComponentModel;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Stats;
	using Services;

	/// <summary>
	/// One weapon of the table of weapons against armors: the figure for each of the six reference targets
	/// (the columns of the grid are fixed, their titles are the names of the targets).
	/// </summary>
	public sealed class MatrixRow
	{
		public MatrixRow(ProtoEntry entry, IReadOnlyList<Double> values)
		{
			Entry = entry;
			Name = entry.DisplayName;
			V0 = values[0];
			V1 = values[1];
			V2 = values[2];
			V3 = values[3];
			V4 = values[4];
			V5 = values[5];
		}

		public ProtoEntry Entry { get; }

		public String Name { get; }

		public Double V0 { get; }

		public Double V1 { get; }

		public Double V2 { get; }

		public Double V3 { get; }

		public Double V4 { get; }

		public Double V5 { get; }
	}

	/// <summary>The weapons against a few armors that go from nothing to the best the game has, with the attacker of the simulation.</summary>
	public sealed partial class BalanceViewModel
	{
		public const Int32 MatrixColumns = 6;

		/// <summary>0 = expected damage of an attack, 1 = expected damage per action point.</summary>
		[ObservableProperty]
		private Int32 _matrixMetricIndex;

		/// <summary>Weapons that have a burst fire it (and the action points of the burst count).</summary>
		[ObservableProperty]
		private Boolean _matrixBurst;

		[ObservableProperty]
		private IReadOnlyList<MatrixRow> _matrixRows = [];

		[ObservableProperty]
		private IReadOnlyList<String> _matrixHeaders = [.. Enumerable.Repeat(String.Empty, MatrixColumns)];

		/// <summary>The damage and the chance to hit of an attack with the attacker described in the simulation.</summary>
		private (CombatInput Damage, HitInput Hit) BuildCombat(ProtoFile weapon, ProtoFile? ammo, Int32 threshold, Int32 resistance, Int32 hitPoints, Int32 armorClass, Boolean burst, Int32 weaponPerk)
		{
			Int32 rounds = burst ? Math.Max(1, weapon.TryGet("BurstRounds") ?? 1) : 1;
			CombatInput damage = new(
				weapon.TryGet(FieldKeys.MinDamage) ?? 0,
				weapon.TryGet(FieldKeys.MaxDamage) ?? 0,
				ammo?.TryGet(FieldKeys.DamageMultiplier) ?? 1,
				ammo?.TryGet(FieldKeys.DamageDivisor) ?? 1,
				ammo?.TryGet("DamageResistanceModifier") ?? 0,
				threshold,
				resistance,
				rounds,
				hitPoints,
				(Int32)(SimDamageBonus ?? 0));

			// The attack mode of the weapon decides whether the distance matters: throwing and firing are ranged, punching and swinging are not.
			Boolean ranged = burst || (weapon.TryGet("PrimaryAttackMode") ?? 0) >= 5;
			Int32 luck = (Int32)(SimLuck ?? 0);
			HitInput hit = new(
				(Int32)(SimSkill ?? 0),
				(Int32)(SimPerception ?? 0),
				(Int32)(SimStrength ?? 0),
				weapon.TryGet("MinStrength") ?? 0,
				(Int32)(SimDistance ?? 0),
				ranged,
				SimPlayer,
				(Int32)(SimSharpshooter ?? 0),
				weaponPerk,
				armorClass,
				ammo?.TryGet("ArmorClassModifier") ?? 0,
				(AimedLocation)Math.Clamp(SimLocationIndex, 0, 5),
				(Int32)(SimCriticalChance ?? luck),
				SimSniper,
				luck,
				SimCriticalMultiplier ?? 2,
				0);
			return (damage, hit);
		}

		private void RefreshMatrix()
		{
			if (_refreshing || _workspaceService.Current is not { } workspace || Weapons.Count == 0)
			{
				MatrixRows = [];
				return;
			}

			// No protection, then five armors spread over the range of the armor scores (the weakest to the strongest).
			List<ArmorBalanceRow> sorted = [.. Armors.Where(a => a.AverageDr > 0 || a.AverageDt > 0).OrderBy(a => a.Score)];
			List<ArmorBalanceRow> picked = [];
			if (sorted.Count > 0)
			{
				for (Int32 i = 0; i < MatrixColumns - 1; i++)
				{
					ArmorBalanceRow armor = sorted[(Int32)Math.Round((sorted.Count - 1) * (i / (Double)(MatrixColumns - 2)))];
					if (!picked.Contains(armor))
					{
						picked.Add(armor);
					}
				}
			}

			String[] headers = new String[MatrixColumns];
			headers[0] = Loc.Get("balance.matrix.none");
			for (Int32 i = 1; i < MatrixColumns; i++)
			{
				headers[i] = i - 1 < picked.Count ? picked[i - 1].Name : String.Empty;
			}

			Boolean perAp = MatrixMetricIndex == 1;
			List<MatrixRow> rows = [];
			foreach (WeaponBalanceRow weapon in Weapons)
			{
				if (weapon.Entry.File is not { } file)
				{
					continue;
				}

				Int32 ammoPid = file.TryGet(FieldKeys.AmmoPid) ?? -1;
				ProtoFile? ammo = ammoPid > 0 ? workspace.Catalog.Find(ammoPid)?.File : null;
				Boolean burst = MatrixBurst && (file.TryGet("BurstRounds") ?? 0) > 1;
				Int32 ap = burst && (file.TryGet("ApCostSecondary") ?? 0) > 0 ? file.TryGet("ApCostSecondary") ?? 0 : file.TryGet("ApCostPrimary") ?? 0;
				Int32 perk = (file.TryGet("Perk") ?? -1) switch
				{
					CombatSimulator.PerkAccurate => CombatSimulator.PerkAccurate,
					CombatSimulator.PerkLongRange => CombatSimulator.PerkLongRange,
					CombatSimulator.PerkScopeRange => CombatSimulator.PerkScopeRange,
					_ => -1,
				};
				Int32 typeIndex = Math.Clamp(file.TryGet("DamageType") ?? 0, 0, ProtoSchemas.DamageSuffixes.Length - 1);
				String suffix = ProtoSchemas.DamageSuffixes[typeIndex];

				Double[] values = new Double[MatrixColumns];
				for (Int32 column = 0; column < MatrixColumns; column++)
				{
					ProtoFile? armor = column == 0 ? null : column - 1 < picked.Count ? picked[column - 1].Entry.File : null;
					if (column > 0 && armor is null)
					{
						continue;
					}

					(CombatInput damage, HitInput hit) = BuildCombat(
						file,
						ammo,
						armor?.TryGet("Dt" + suffix) ?? 0,
						armor?.TryGet("Dr" + suffix) ?? 0,
						0,
						armor?.TryGet("ArmorClass") ?? 0,
						burst,
						perk);
					Double expected = CombatSimulator.Estimate(damage, hit).ExpectedDamage;
					values[column] = perAp ? (ap > 0 ? expected / ap : 0) : expected;
				}

				rows.Add(new MatrixRow(weapon.Entry, values));
			}

			MatrixHeaders = headers;
			MatrixRows = rows;
		}
	}
}