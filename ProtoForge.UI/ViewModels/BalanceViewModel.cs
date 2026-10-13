namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using CommunityToolkit.Mvvm.ComponentModel;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Reference;
	using ProtoForge.Core.Stats;
	using ProtoForge.Infrastructure.Settings;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	/// <summary>A prototype of the balance tables.</summary>
	public class BalanceRow
	{
		public BalanceRow(ProtoEntry entry, Int32 cost)
		{
			Entry = entry;
			Name = entry.DisplayName;
			Pid = entry.Pid.ToString();
			Source = entry.SourceName;
			Cost = cost;
		}

		public ProtoEntry Entry { get; }

		public String Name { get; }

		public String Pid { get; }

		public String Source { get; }

		public Int32 Cost { get; }
	}

	/// <summary>A weapon with the figures that matter to compare it with the others.</summary>
	public sealed class WeaponBalanceRow : BalanceRow
	{
		public WeaponBalanceRow(ProtoEntry entry, Int32 min, Int32 max, String damageType, Int32 range, Int32 apCost, Int32 burst, String ammo, Int32 cost, Int32 score)
			: base(entry, cost)
		{
			MinDamage = min;
			MaxDamage = max;
			Average = (min + max) / 2.0;
			DamageType = damageType;
			Range = range;
			ApCost = apCost;
			Burst = burst;
			Ammo = ammo;
			Score = score;
			CostPerDamage = Average > 0 ? cost / Average : null;
			DamagePerAp = apCost > 0 ? Average * Math.Max(1, burst) / apCost : null;
		}

		public Int32 MinDamage { get; }

		public Int32 MaxDamage { get; }

		public Double Average { get; }

		public String DamageType { get; }

		public Int32 Range { get; }

		public Int32 ApCost { get; }

		public Int32 Burst { get; }

		public String Ammo { get; }

		public Int32 Score { get; }

		/// <summary>Price of a point of average damage.</summary>
		public Double? CostPerDamage { get; }

		/// <summary>Average damage of an attack (a whole burst) for each action point it costs.</summary>
		public Double? DamagePerAp { get; }

		public Boolean AverageIsOutlier { get; set; }

		public Boolean CostPerDamageIsOutlier { get; set; }

		public Boolean DamagePerApIsOutlier { get; set; }
	}

	/// <summary>An armor with its protection summed up.</summary>
	public sealed class ArmorBalanceRow : BalanceRow
	{
		public ArmorBalanceRow(ProtoEntry entry, Int32 armorClass, Double averageDr, Double averageDt, Int32 score, Int32 cost)
			: base(entry, cost)
		{
			ArmorClass = armorClass;
			AverageDr = averageDr;
			AverageDt = averageDt;
			Score = score;
			CostPerScore = score > 0 ? (Double)cost / score : null;
		}

		public Int32 ArmorClass { get; }

		public Double AverageDr { get; }

		public Double AverageDt { get; }

		public Int32 Score { get; }

		public Double? CostPerScore { get; }

		public Boolean ScoreIsOutlier { get; set; }

		public Boolean CostPerScoreIsOutlier { get; set; }
	}

	/// <summary>One line of the result of the combat simulation.</summary>
	public sealed record SimulationLine(String Label, String Value);

	/// <summary>Comparison tables of the weapons and armors (to spot what is out of balance) and a small combat calculator.</summary>
	public sealed partial class BalanceViewModel : ObservableObject
	{
		private readonly WorkspaceService _workspaceService;
		private readonly SettingsService _settings;
		private readonly ShellNavigator _shell;
		private Boolean _refreshing;

		[ObservableProperty]
		private IReadOnlyList<WeaponBalanceRow> _weapons = [];

		[ObservableProperty]
		private IReadOnlyList<ArmorBalanceRow> _armors = [];

		[ObservableProperty]
		private Double _maxAverageDamage = 1;

		[ObservableProperty]
		private Double _maxArmorScore = 1;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(ShowWeapons))]
		[NotifyPropertyChangedFor(nameof(ShowArmors))]
		private Int32 _modeIndex;

		[ObservableProperty]
		private IReadOnlyList<ProtoEntry> _weaponEntries = [];

		[ObservableProperty]
		private IReadOnlyList<ProtoEntry> _ammoEntries = [];

		[ObservableProperty]
		private IReadOnlyList<ProtoEntry> _targetEntries = [];

		[ObservableProperty]
		private ProtoEntry? _simWeapon;

		[ObservableProperty]
		private ProtoEntry? _simAmmo;

		[ObservableProperty]
		private ProtoEntry? _simTarget;

		[ObservableProperty]
		private Boolean _simBurst;

		[ObservableProperty]
		private Double? _simThreshold;

		[ObservableProperty]
		private Double? _simResistance;

		[ObservableProperty]
		private Double? _simHitPoints;

		[ObservableProperty]
		private IReadOnlyList<SimulationLine> _simLines = [];

		public BalanceViewModel(WorkspaceService workspaceService, SettingsService settings, ShellNavigator shell)
		{
			_workspaceService = workspaceService;
			_settings = settings;
			_shell = shell;
			_workspaceService.WorkspaceChanged += (_, _) => Refresh();
		}

		public Boolean ShowWeapons
		{
			get { return ModeIndex == 0; }
		}

		public Boolean ShowArmors
		{
			get { return ModeIndex == 1; }
		}

		/// <summary>Shows a prototype of a table in the library.</summary>
		public void Show(BalanceRow row)
		{
			_shell.ShowProto(row.Entry.Pid);
		}

		/// <summary>Reads the tables again from the open game (also called when the page is shown, after edits made elsewhere).</summary>
		public void Refresh()
		{
			if (_workspaceService.Current is not { } workspace)
			{
				Weapons = [];
				Armors = [];
				WeaponEntries = AmmoEntries = TargetEntries = [];
				SimLines = [];
				return;
			}

			_refreshing = true;
			try
			{
				IReadOnlyList<ChoiceOption> damageTypes = workspace.Choices.Get(ChoiceCatalog.DamageType);
				List<WeaponBalanceRow> weapons = [];
				List<ArmorBalanceRow> armors = [];
				foreach (ProtoEntry entry in workspace.Catalog.All)
				{
					if (entry.File is not { } file)
					{
						continue;
					}

					Int32 cost = file.TryGet(FieldKeys.Cost) ?? 0;
					if (entry.Kind == ProtoKind.Weapon)
					{
						Int32 min = file.TryGet(FieldKeys.MinDamage) ?? 0;
						Int32 max = file.TryGet(FieldKeys.MaxDamage) ?? 0;
						Int32 type = file.TryGet("DamageType") ?? 0;
						Int32 ammoPid = file.TryGet(FieldKeys.AmmoPid) ?? -1;
						Boolean hasPerk = (file.TryGet("Perk") ?? -1) != -1;
						weapons.Add(new WeaponBalanceRow(
							entry,
							min,
							max,
							damageTypes.Where(o => o.Value == type).Select(o => o.Label).FirstOrDefault() ?? type.ToString(),
							file.TryGet("MaxRangePrimary") ?? 0,
							file.TryGet("ApCostPrimary") ?? 0,
							file.TryGet("BurstRounds") ?? 0,
							ammoPid > 0 ? workspace.Catalog.Find(ammoPid)?.DisplayName ?? String.Empty : String.Empty,
							cost,
							CharacterFormulas.WeaponScore(min, max, hasPerk, _settings.Current.OriginalWeaponScore)));
					}
					else if (entry.Kind == ProtoKind.Armor)
					{
						List<Int32> dr = [.. ProtoSchemas.DamageSuffixes.Select(s => file.TryGet("Dr" + s) ?? 0)];
						List<Int32> dt = [.. ProtoSchemas.DamageSuffixes.Select(s => file.TryGet("Dt" + s) ?? 0)];
						Int32 armorClass = file.TryGet("ArmorClass") ?? 0;
						armors.Add(new ArmorBalanceRow(entry, armorClass, dr.Average(), dt.Average(), CharacterFormulas.ArmorScore(armorClass, dt, dr), cost));
					}
				}

				MarkOutliers(weapons, w => w.Average, (w, flag) => w.AverageIsOutlier = flag);
				MarkOutliers(weapons, w => w.CostPerDamage, (w, flag) => w.CostPerDamageIsOutlier = flag);
				MarkOutliers(weapons, w => w.DamagePerAp, (w, flag) => w.DamagePerApIsOutlier = flag);
				MarkOutliers(armors, a => a.Score, (a, flag) => a.ScoreIsOutlier = flag);
				MarkOutliers(armors, a => a.CostPerScore, (a, flag) => a.CostPerScoreIsOutlier = flag);
				Weapons = weapons;
				Armors = armors;
				MaxAverageDamage = Math.Max(1, weapons.Select(w => w.Average).DefaultIfEmpty(1).Max());
				MaxArmorScore = Math.Max(1, armors.Select(a => (Double)a.Score).DefaultIfEmpty(1).Max());

				WeaponEntries = [.. workspace.Catalog.All.Where(e => e.Kind == ProtoKind.Weapon && e.File is not null)];
				AmmoEntries = [.. workspace.Catalog.All.Where(e => e.Kind == ProtoKind.Ammo && e.File is not null)];
				TargetEntries = [.. workspace.Catalog.All.Where(e => e.Kind is ProtoKind.Armor or ProtoKind.Critter && e.File is not null)];
				if (SimWeapon is not null && !WeaponEntries.Contains(SimWeapon))
				{
					SimWeapon = null;
				}
			}
			finally
			{
				_refreshing = false;
			}

			Simulate();
		}

		private static void MarkOutliers<T>(IReadOnlyList<T> rows, Func<T, Double?> metric, Action<T, Boolean> mark)
		{
			Boolean[] flags = Outliers.Find([.. rows.Select(metric)]);
			for (Int32 i = 0; i < rows.Count; i++)
			{
				mark(rows[i], flags[i]);
			}
		}

		partial void OnSimWeaponChanged(ProtoEntry? value)
		{
			if (_refreshing)
			{
				return;
			}

			// The ammunition of the weapon is the natural choice (it can be changed afterwards).
			Int32 ammoPid = value?.File?.TryGet(FieldKeys.AmmoPid) ?? -1;
			SimAmmo = ammoPid > 0 ? _workspaceService.Current?.Catalog.Find(ammoPid) : null;
			SimBurst = false;
			Simulate();
		}

		partial void OnSimAmmoChanged(ProtoEntry? value)
		{
			Simulate();
		}

		partial void OnSimTargetChanged(ProtoEntry? value)
		{
			Simulate();
		}

		partial void OnSimBurstChanged(Boolean value)
		{
			Simulate();
		}

		partial void OnSimThresholdChanged(Double? value)
		{
			Simulate();
		}

		partial void OnSimResistanceChanged(Double? value)
		{
			Simulate();
		}

		partial void OnSimHitPointsChanged(Double? value)
		{
			Simulate();
		}

		/// <summary>Calculates the damage of the chosen weapon and ammunition against the chosen target (or the typed values).</summary>
		private void Simulate()
		{
			if (_refreshing || SimWeapon?.File is not { } weapon)
			{
				SimLines = [];
				return;
			}

			ProtoFile? ammo = SimAmmo?.File;
			Int32 typeIndex = Math.Clamp(weapon.TryGet("DamageType") ?? 0, 0, ProtoSchemas.DamageSuffixes.Length - 1);
			String suffix = ProtoSchemas.DamageSuffixes[typeIndex];
			(Int32 targetDt, Int32 targetDr, Int32 targetHp) = ReadTarget(suffix);
			Int32 threshold = SimThreshold is { } t ? (Int32)t : targetDt;
			Int32 resistance = SimResistance is { } r ? (Int32)r : targetDr;
			Int32 hitPoints = SimHitPoints is { } h ? (Int32)h : targetHp;
			Int32 rounds = SimBurst ? Math.Max(1, weapon.TryGet("BurstRounds") ?? 1) : 1;

			CombatResult result = CombatSimulator.Calculate(new CombatInput(
				weapon.TryGet(FieldKeys.MinDamage) ?? 0,
				weapon.TryGet(FieldKeys.MaxDamage) ?? 0,
				ammo?.TryGet(FieldKeys.DamageMultiplier) ?? 1,
				ammo?.TryGet(FieldKeys.DamageDivisor) ?? 1,
				ammo?.TryGet("DamageResistanceModifier") ?? 0,
				threshold,
				resistance,
				rounds,
				hitPoints));

			String damageType = _workspaceService.Current?.Choices.Get(ChoiceCatalog.DamageType).Where(o => o.Value == typeIndex).Select(o => o.Label).FirstOrDefault() ?? suffix;
			SimLines =
			[
				new SimulationLine(Loc.Get("balance.sim.damageType"), damageType),
				new SimulationLine(Loc.Get("balance.sim.raw"), Range(result.Raw)),
				new SimulationLine(Loc.Get("balance.sim.threshold"), threshold.ToString("N0")),
				new SimulationLine(Loc.Get("balance.sim.resistance"), $"{result.EffectiveResistance} %"),
				new SimulationLine(Loc.Get("balance.sim.perHit"), Range(result.PerHit)),
				new SimulationLine(Loc.Format("balance.sim.perAttack", rounds), Range(result.PerAttack)),
				new SimulationLine(Loc.Get("balance.sim.attacks"), hitPoints <= 0 ? Loc.Get("balance.sim.noHp") : Attacks(result)),
			];
		}

		private (Int32 Threshold, Int32 Resistance, Int32 HitPoints) ReadTarget(String suffix)
		{
			if (SimTarget?.File is not { } file)
			{
				return (0, 0, 0);
			}

			if (SimTarget.Kind == ProtoKind.Critter)
			{
				// A critter's own protection and hit points: the stored base values plus their bonus values.
				Int32 Both(String key)
				{
					return (file.TryGet(key) ?? 0) + (file.TryGet(ProtoSchemas.BonusPrefix + key) ?? 0);
				}

				return (Both("Dt" + suffix), Both("Dr" + suffix), Both("MaxHp"));
			}

			return (file.TryGet("Dt" + suffix) ?? 0, file.TryGet("Dr" + suffix) ?? 0, 0);
		}

		private static String Range(DamageRange range)
		{
			return range.Min == range.Max ? range.Max.ToString("0.#") : $"{range.Min:0.#} - {range.Max:0.#}  ({Loc.Get("balance.sim.average")} {range.Average:0.#})";
		}

		private static String Attacks(CombatResult result)
		{
			return result.AttacksAverage is null
				? Loc.Get("balance.sim.never")
				: $"{result.AttacksBestCase} - {result.AttacksWorstCase}  ({Loc.Get("balance.sim.average")} {result.AttacksAverage})";
		}
	}
}