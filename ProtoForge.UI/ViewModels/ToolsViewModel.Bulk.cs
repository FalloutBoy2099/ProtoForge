namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Linq;
	using System.Threading.Tasks;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Editing;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Reference;
	using ProtoForge.Infrastructure.Rules;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	/// <summary>A kind of prototype offered by the bulk edit.</summary>
	public sealed record BulkKindOption(ProtoKind Kind, String Label)
	{
		public override String ToString()
		{
			return Label;
		}
	}

	/// <summary>A field offered by the bulk edit (null field: "no condition").</summary>
	public sealed record BulkFieldOption(ProtoField? Field, String Label)
	{
		public override String ToString()
		{
			return Label;
		}
	}

	/// <summary>What the bulk edit does to the field: set, add, multiply, turn a flag on or off.</summary>
	public sealed record BulkOperationOption(BulkOperation Operation, String Label)
	{
		public override String ToString()
		{
			return Label;
		}
	}

	/// <summary>A value that is picked from a list: a named choice, or a flag (then <see cref="Value"/> is its mask).</summary>
	public sealed record BulkValueOption(Int32 Value, String Label)
	{
		public override String ToString()
		{
			return Label;
		}
	}

	/// <summary>
	/// Changes one number, choice or flag of many prototypes by a rule: set, add, multiply, turn a flag on or off, optionally only where another field
	/// has a given value. A rule can be saved and played again later.
	/// </summary>
	public sealed partial class ToolsViewModel
	{
		private static readonly BulkComparison[] Comparisons =
		[
			BulkComparison.Equal,
			BulkComparison.NotEqual,
			BulkComparison.Less,
			BulkComparison.LessOrEqual,
			BulkComparison.Greater,
			BulkComparison.GreaterOrEqual,
		];

		private readonly ChoiceCatalog _bulkChoices = new();
		private Boolean _loadingBulkRule;

		[ObservableProperty]
		private BulkKindOption? _selectedBulkKind;

		[ObservableProperty]
		private IReadOnlyList<BulkFieldOption> _bulkFields = [];

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(IsBulkNumber))]
		private BulkFieldOption? _selectedBulkField;

		[ObservableProperty]
		private IReadOnlyList<BulkOperationOption> _bulkOperations = [];

		[ObservableProperty]
		private BulkOperationOption? _selectedBulkOperation;

		[ObservableProperty]
		private Double? _bulkValue = 1;

		/// <summary>The named values of a choice, or the flags, of the field.</summary>
		[ObservableProperty]
		private IReadOnlyList<BulkValueOption> _bulkChoiceList = [];

		[ObservableProperty]
		private BulkValueOption? _selectedBulkChoice;

		[ObservableProperty]
		private IReadOnlyList<BulkFieldOption> _bulkConditionFields = [];

		[ObservableProperty]
		private BulkFieldOption? _selectedConditionField;

		[ObservableProperty]
		private Int32 _bulkComparisonIndex;

		[ObservableProperty]
		private Double? _bulkConditionValue = 0;

		/// <summary>The named values of the field of the condition, when it is a choice.</summary>
		[ObservableProperty]
		private IReadOnlyList<BulkValueOption> _conditionChoiceList = [];

		[ObservableProperty]
		private BulkValueOption? _selectedConditionChoice;

		[ObservableProperty]
		private String _bulkStatus = String.Empty;

		[ObservableProperty]
		private IReadOnlyList<BulkRule> _bulkRules = [];

		[ObservableProperty]
		private BulkRule? _selectedBulkRule;

		public IReadOnlyList<BulkKindOption> BulkKinds { get; } =
		[
			.. ProtoKindExtensions.All.Select(k => new BulkKindOption(k, Loc.Get("kind." + k))),
		];

		/// <summary>True for a plain number (the value is typed), false for a choice or a flag (the value is picked).</summary>
		public Boolean IsBulkNumber
		{
			get { return SelectedBulkField?.Field is not { Editor: FieldEditor.Choice or FieldEditor.Flags }; }
		}

		public Boolean HasBulkCondition
		{
			get { return SelectedConditionField?.Field is not null; }
		}

		/// <summary>The condition compares a number that is typed.</summary>
		public Boolean IsConditionNumber
		{
			get { return HasBulkCondition && SelectedConditionField?.Field is { Editor: not FieldEditor.Choice }; }
		}

		/// <summary>The condition compares a choice that is picked.</summary>
		public Boolean IsConditionChoice
		{
			get { return SelectedConditionField?.Field is { Editor: FieldEditor.Choice }; }
		}

		public Boolean HasBulkRules
		{
			get { return BulkRules.Count > 0; }
		}

		private void InitializeBulk()
		{
			SelectedBulkKind = BulkKinds.First(k => k.Kind == ProtoKind.Weapon);
			RefreshBulkRules();
		}

		partial void OnSelectedBulkKindChanged(BulkKindOption? value)
		{
			ProtoKind kind = value?.Kind ?? ProtoKind.Weapon;
			BulkFields = [.. BulkEditor.GetEditableFields(kind).Select(f => new BulkFieldOption(f, Loc.Field(f.Key)))];
			SelectedBulkField = BulkFields.FirstOrDefault(f => f.Field?.Key == FieldKeys.MaxDamage) ?? BulkFields.FirstOrDefault();
			BulkConditionFields =
			[
				new BulkFieldOption(null, Loc.Get("tools.bulkNoCondition")),
				.. ProtoSchemas.For(kind).Fields
					.Where(f => f.Editor is FieldEditor.Number or FieldEditor.Choice && f.Key != FieldKeys.ProtoId && (f.Editor != FieldEditor.Choice || f.ChoiceSet is not null))
					.Select(f => new BulkFieldOption(f, Loc.Field(f.Key))),
			];
			SelectedConditionField = BulkConditionFields[0];
			BulkStatus = String.Empty;
		}

		partial void OnSelectedBulkFieldChanged(BulkFieldOption? value)
		{
			ProtoField? field = value?.Field;
			if (field is { Editor: FieldEditor.Flags, FlagSet: { } setId } && FlagSets.TryGet(setId, out FlagSet set))
			{
				BulkOperations =
				[
					new BulkOperationOption(BulkOperation.SetFlag, Loc.Get("tools.bulkFlagOn")),
					new BulkOperationOption(BulkOperation.ClearFlag, Loc.Get("tools.bulkFlagOff")),
				];
				BulkChoiceList = [.. set.Bits.Select(b => new BulkValueOption(b.SignedMask, Loc.Flag(b.Name)))];
			}
			else if (field is { Editor: FieldEditor.Choice, ChoiceSet: { } choiceSet })
			{
				BulkOperations = [new BulkOperationOption(BulkOperation.Set, Loc.Get("tools.bulkSet"))];
				BulkChoiceList = [.. _bulkChoices.Get(choiceSet).Select(o => new BulkValueOption(o.Value, o.Label))];
			}
			else
			{
				BulkOperations =
				[
					new BulkOperationOption(BulkOperation.Set, Loc.Get("tools.bulkSet")),
					new BulkOperationOption(BulkOperation.Add, Loc.Get("tools.bulkAdd")),
					new BulkOperationOption(BulkOperation.Multiply, Loc.Get("tools.bulkMultiply")),
				];
				BulkChoiceList = [];
			}

			SelectedBulkOperation = BulkOperations[^1].Operation == BulkOperation.Multiply ? BulkOperations[^1] : BulkOperations[0];
			SelectedBulkChoice = BulkChoiceList.FirstOrDefault();
			BulkStatus = String.Empty;
		}

		partial void OnSelectedConditionFieldChanged(BulkFieldOption? value)
		{
			OnPropertyChanged(nameof(HasBulkCondition));
			OnPropertyChanged(nameof(IsConditionNumber));
			OnPropertyChanged(nameof(IsConditionChoice));
			if (value?.Field is { Editor: FieldEditor.Choice, ChoiceSet: { } set })
			{
				ConditionChoiceList = [.. _bulkChoices.Get(set).Select(o => new BulkValueOption(o.Value, o.Label))];
				SelectedConditionChoice = ConditionChoiceList.FirstOrDefault();
			}
			else
			{
				ConditionChoiceList = [];
				SelectedConditionChoice = null;
			}
		}

		partial void OnSelectedConditionChoiceChanged(BulkValueOption? value)
		{
			if (value is not null && !_loadingBulkRule)
			{
				BulkConditionValue = value.Value;
			}
		}

		private (BulkKindOption Kind, BulkFieldOption Field, BulkOperation Operation, Double Operand, BulkCondition? Condition)? ReadBulk()
		{
			if (SelectedBulkKind is not { } kind || SelectedBulkField is not { Field: { } field } fieldOption || SelectedBulkOperation is not { } operation)
			{
				return null;
			}

			Double operand;
			if (field.Editor == FieldEditor.Number)
			{
				if (BulkValue is not { } typed)
				{
					return null;
				}

				operand = typed;
			}
			else if (SelectedBulkChoice is { } picked)
			{
				// A flag is stored as its mask; the mask of the top bit does not fit in a signed integer, hence the unsigned conversion.
				operand = field.Editor == FieldEditor.Flags ? unchecked((UInt32)picked.Value) : picked.Value;
			}
			else
			{
				return null;
			}

			BulkCondition? condition = SelectedConditionField?.Field is { } conditionField && BulkConditionValue is { } reference
				? new BulkCondition(conditionField.Key, Comparisons[Math.Clamp(BulkComparisonIndex, 0, Comparisons.Length - 1)], (Int32)reference)
				: null;
			return (kind, fieldOption, operation.Operation, operand, condition);
		}

		/// <summary>A value as the user knows it: a number, the name of a choice, the bits of a flag word.</summary>
		private String DescribeValue(ProtoField field, Int32 value)
		{
			return field.Editor switch
			{
				FieldEditor.Choice when field.ChoiceSet is { } set => _bulkChoices.Get(set).Where(o => o.Value == value).Select(o => o.Label).FirstOrDefault() ?? value.ToString(CultureInfo.CurrentCulture),
				FieldEditor.Flags => "0x" + unchecked((UInt32)value).ToString("X8", CultureInfo.InvariantCulture),
				_ => value.ToString(CultureInfo.CurrentCulture),
			};
		}

		private String DescribeChange(ProtoField field, BulkChange change)
		{
			String name = change.Entry.Name.Length > 0 ? change.Entry.Name : change.Entry.Pid.ToString();
			return $"{name}: {DescribeValue(field, change.OldValue)} → {DescribeValue(field, change.NewValue)}";
		}

		[RelayCommand]
		private void PreviewBulk()
		{
			if (_workspaceService.Current is not { } workspace || ReadBulk() is not { } rule)
			{
				return;
			}

			ProtoField field = rule.Field.Field!;
			IReadOnlyList<BulkChange> plan = BulkEditor.Plan(workspace.Catalog.All, rule.Kind.Kind, field.Key, rule.Operation, rule.Operand, rule.Condition);
			BulkStatus = plan.Count == 0
				? Loc.Get("tools.bulkNone")
				: Loc.Format("tools.bulkResult", plan.Count, String.Join("; ", plan.Take(3).Select(c => DescribeChange(field, c))));
		}

		[RelayCommand]
		private async Task ApplyBulk()
		{
			if (_workspaceService.Current is not { } workspace || ReadBulk() is not { } rule)
			{
				return;
			}

			ProtoField field = rule.Field.Field!;
			IReadOnlyList<BulkChange> plan = BulkEditor.Plan(workspace.Catalog.All, rule.Kind.Kind, field.Key, rule.Operation, rule.Operand, rule.Condition);
			if (plan.Count == 0)
			{
				BulkStatus = Loc.Get("tools.bulkNone");
				return;
			}

			String message = Loc.Format("tools.bulkConfirm", plan.Count, rule.Field.Label, String.Join(Environment.NewLine, plan.Take(5).Select(c => DescribeChange(field, c))));
			if (!await _dialogs.ConfirmAsync(Loc.Get("tools.bulk"), message, Loc.Get("tools.bulkApply")))
			{
				return;
			}

			List<ProtoEntry> entries = [.. plan.Select(c => c.Entry)];
			workspace.ModifyField(entries, field.Key, value => BulkEditor.Compute(field, rule.Operation, rule.Operand, value));
			BulkStatus = Loc.Format("tools.bulkDone", plan.Count);
			_notifier.Success(Loc.Get("tools.bulk"), BulkStatus);
		}

		private void RefreshBulkRules()
		{
			BulkRules = _bulkRuleStore.Load();
			SelectedBulkRule = null;
			OnPropertyChanged(nameof(HasBulkRules));
		}

		partial void OnSelectedBulkRuleChanged(BulkRule? value)
		{
			LoadBulkRuleCommand.NotifyCanExecuteChanged();
			DeleteBulkRuleCommand.NotifyCanExecuteChanged();
		}

		[RelayCommand]
		private async Task SaveBulkRule()
		{
			if (ReadBulk() is not { } rule)
			{
				return;
			}

			String suggested = SelectedBulkRule?.Name ?? $"{rule.Kind.Label} · {rule.Field.Label}";
			String? name = await _dialogs.PromptAsync(Loc.Get("tools.bulkRules"), Loc.Get("tools.bulkRuleName"), suggested);
			if (String.IsNullOrWhiteSpace(name))
			{
				return;
			}

			name = name.Trim();
			if (_bulkRuleStore.Exists(name) && !await _dialogs.ConfirmAsync(Loc.Get("tools.bulkRules"), Loc.Format("tools.bulkRuleReplace", name), Loc.Get("tools.bulkRuleSave")))
			{
				return;
			}

			_bulkRuleStore.Save(new BulkRule(
				name,
				rule.Kind.Kind.ToString(),
				rule.Field.Field!.Key,
				rule.Operation,
				rule.Operand,
				rule.Condition?.FieldKey,
				rule.Condition?.Comparison ?? BulkComparison.Equal,
				rule.Condition?.Value ?? 0));
			RefreshBulkRules();
			SelectedBulkRule = BulkRules.FirstOrDefault(r => String.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
			_notifier.Success(Loc.Get("tools.bulkRules"), Loc.Format("tools.bulkRuleSaved", name));
		}

		private Boolean HasBulkRuleSelected()
		{
			return SelectedBulkRule is not null;
		}

		/// <summary>Puts a saved rule back in the form (it is not applied: the preview and the confirmation come first).</summary>
		[RelayCommand(CanExecute = nameof(HasBulkRuleSelected))]
		private void LoadBulkRule()
		{
			if (SelectedBulkRule is not { } rule)
			{
				return;
			}

			BulkKindOption? kind = BulkKinds.FirstOrDefault(k => k.Kind.ToString() == rule.Kind);
			if (kind is null)
			{
				_notifier.Warning(Loc.Get("tools.bulkRules"), Loc.Format("tools.bulkRuleInvalid", rule.Name));
				return;
			}

			_loadingBulkRule = true;
			try
			{
				SelectedBulkKind = kind;
				BulkFieldOption? field = BulkFields.FirstOrDefault(f => f.Field?.Key == rule.Field);
				if (field is null)
				{
					_notifier.Warning(Loc.Get("tools.bulkRules"), Loc.Format("tools.bulkRuleInvalid", rule.Name));
					return;
				}

				SelectedBulkField = field;
				SelectedBulkOperation = BulkOperations.FirstOrDefault(o => o.Operation == rule.Operation) ?? BulkOperations[0];
				if (field.Field!.Editor == FieldEditor.Number)
				{
					BulkValue = rule.Operand;
				}
				else
				{
					Int32 stored = field.Field.Editor == FieldEditor.Flags ? unchecked((Int32)(UInt32)rule.Operand) : (Int32)rule.Operand;
					SelectedBulkChoice = BulkChoiceList.FirstOrDefault(c => c.Value == stored) ?? BulkChoiceList.FirstOrDefault();
				}

				SelectedConditionField = BulkConditionFields.FirstOrDefault(f => rule.ConditionField is not null && f.Field?.Key == rule.ConditionField) ?? BulkConditionFields[0];
				if (SelectedConditionField.Field is not null)
				{
					BulkComparisonIndex = Math.Max(0, Array.IndexOf(Comparisons, rule.ConditionComparison));
					BulkConditionValue = rule.ConditionValue;
					SelectedConditionChoice = ConditionChoiceList.FirstOrDefault(c => c.Value == rule.ConditionValue) ?? SelectedConditionChoice;
				}
			}
			finally
			{
				_loadingBulkRule = false;
			}

			BulkStatus = Loc.Format("tools.bulkRuleLoaded", rule.Name);
		}

		[RelayCommand(CanExecute = nameof(HasBulkRuleSelected))]
		private async Task DeleteBulkRule()
		{
			if (SelectedBulkRule is not { } rule)
			{
				return;
			}

			if (await _dialogs.ConfirmAsync(Loc.Get("tools.bulkRules"), Loc.Format("tools.bulkRuleDelete", rule.Name), Loc.Get("cmd.delete")))
			{
				_bulkRuleStore.Delete(rule.Name);
				RefreshBulkRules();
			}
		}
	}
}