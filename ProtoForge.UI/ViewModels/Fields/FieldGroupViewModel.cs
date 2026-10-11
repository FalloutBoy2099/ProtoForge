namespace ProtoForge.UI.ViewModels.Fields
{
	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.Linq;
	using CommunityToolkit.Mvvm.ComponentModel;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Stats;
	using Services;

	/// <summary>A base value, its bonus and the resulting total for one critter statistic.</summary>
	public sealed partial class StatRowViewModel : ObservableObject
	{
		[ObservableProperty]
		private String _total = String.Empty;

		public StatRowViewModel(String key, NumberFieldViewModel baseValue, NumberFieldViewModel bonus)
		{
			Key = key;
			Base = baseValue;
			Bonus = bonus;
			baseValue.PropertyChanged += (_, e) => OnChildChanged(e.PropertyName);
			bonus.PropertyChanged += (_, e) => OnChildChanged(e.PropertyName);
		}

		public String Key { get; }

		public String Label
		{
			get { return Loc.Field(Key); }
		}

		public NumberFieldViewModel Base { get; }

		public NumberFieldViewModel Bonus { get; }

		public void Refresh()
		{
			Base.Refresh();
			Bonus.Refresh();
			UpdateTotal();
		}

		private void OnChildChanged(String? property)
		{
			if (property == nameof(NumberFieldViewModel.Number))
			{
				UpdateTotal();
			}
		}

		private void UpdateTotal()
		{
			Total = Base.Number is { } a && Bonus.Number is { } b ? ((Int32)a + (Int32)b).ToString(System.Globalization.CultureInfo.CurrentCulture) : String.Empty;
		}
	}

	/// <summary>
	/// A critter skill: the stored value is an adjustment added to the value the engine computes from SPECIAL, so the editor
	/// also shows that computed value and the resulting total.
	/// </summary>
	public sealed partial class SkillRowViewModel : ObservableObject
	{
		private readonly EditorContext _context;

		[ObservableProperty]
		private String _formula = String.Empty;

		[ObservableProperty]
		private String _total = String.Empty;

		public SkillRowViewModel(String key, NumberFieldViewModel stored, EditorContext context)
		{
			Key = key;
			Stored = stored;
			_context = context;
			stored.PropertyChanged += (_, e) =>
			{
				if (e.PropertyName == nameof(NumberFieldViewModel.Number))
				{
					UpdateTotals();
				}
			};
		}

		public String Key { get; }

		public String Label
		{
			get { return Loc.Field(Key); }
		}

		public NumberFieldViewModel Stored { get; }

		public void Refresh()
		{
			Stored.Refresh();
			UpdateTotals();
		}

		private void UpdateTotals()
		{
			Formula = String.Empty;
			Total = String.Empty;
			if (_context.Entries.Count != 1 || _context.Entries[0].File is not { } file || Stored.Number is not { } stored)
			{
				return;
			}

			Int32[] special = [.. ProtoSchemas.SpecialNames.Select(n => file.Get(n) + file.Get(ProtoSchemas.BonusPrefix + n))];
			if (CharacterFormulas.ComputeSkills(_context.Workspace.Options.Formula, special).TryGetValue(Key, out Int32 computed))
			{
				Formula = computed.ToString(System.Globalization.CultureInfo.CurrentCulture);
				Total = (computed + (Int32)stored).ToString(System.Globalization.CultureInfo.CurrentCulture);
			}
		}
	}

	/// <summary>A titled section of the editor: either a list of fields or a base/bonus/total table.</summary>
	public sealed partial class FieldGroupViewModel : ObservableObject
	{
		[ObservableProperty]
		private Boolean _isExpanded = true;

		[ObservableProperty]
		private Boolean _isVisible = true;

		public FieldGroupViewModel(String key, Boolean isAdvanced)
		{
			Key = key;
			IsAdvanced = isAdvanced;
		}

		public String Key { get; }

		public String Header
		{
			get { return Loc.Group(Key); }
		}

		public Boolean IsAdvanced { get; }

		public ObservableCollection<FieldViewModel> Fields { get; } = [];

		public ObservableCollection<StatRowViewModel> StatRows { get; } = [];

		public ObservableCollection<SkillRowViewModel> SkillRows { get; } = [];

		public Boolean IsSkillTable
		{
			get { return SkillRows.Count > 0; }
		}

		public Boolean IsStatTable
		{
			get { return StatRows.Count > 0; }
		}

		public IEnumerable<FieldViewModel> AllFields
		{
			get { return Fields.Concat(StatRows.SelectMany(r => new FieldViewModel[] { r.Base, r.Bonus })).Concat(SkillRows.Select(r => r.Stored)); }
		}

		public void UpdateVisibility(Boolean showAdvanced)
		{
			foreach (FieldViewModel field in AllFields)
			{
				field.IsVisible = showAdvanced || !field.IsAdvanced;
			}

			IsVisible = (showAdvanced || !IsAdvanced) && (IsStatTable || IsSkillTable || Fields.Any(f => f.IsVisible));
		}

		public void Refresh()
		{
			foreach (FieldViewModel field in Fields)
			{
				field.Refresh();
			}

			foreach (StatRowViewModel row in StatRows)
			{
				row.Refresh();
			}

			foreach (SkillRowViewModel row in SkillRows)
			{
				row.Refresh();
			}
		}
	}

	public static class FieldViewModelFactory
	{
		public static FieldViewModel Create(ProtoField field, EditorContext context)
		{
			return field.Editor switch
			{
				FieldEditor.Choice => new ChoiceFieldViewModel(field, context),
				FieldEditor.Flags => new FlagsFieldViewModel(field, context),
				FieldEditor.Art => new ArtFieldViewModel(field, context),
				FieldEditor.Proto => new ProtoReferenceFieldViewModel(field, context),
				FieldEditor.Script => new ScriptFieldViewModel(field, context),
				FieldEditor.LightIntensity => new LightFieldViewModel(field, context),
				FieldEditor.Character => new CharacterFieldViewModel(field, context),
				_ => new NumberFieldViewModel(field, context),
			};
		}
	}
}