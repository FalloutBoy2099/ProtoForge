namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Editing;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Reference;
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

	/// <summary>Changes one number of many prototypes by a rule: set, add, or multiply, optionally only where another field has a given value.</summary>
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

		[ObservableProperty]
		private BulkKindOption? _selectedBulkKind;

		[ObservableProperty]
		private IReadOnlyList<BulkFieldOption> _bulkFields = [];

		[ObservableProperty]
		private BulkFieldOption? _selectedBulkField;

		/// <summary>0 = set, 1 = add, 2 = multiply.</summary>
		[ObservableProperty]
		private Int32 _bulkOperationIndex = 2;

		[ObservableProperty]
		private Double? _bulkValue = 1;

		[ObservableProperty]
		private IReadOnlyList<BulkFieldOption> _bulkConditionFields = [];

		[ObservableProperty]
		private BulkFieldOption? _selectedConditionField;

		[ObservableProperty]
		private Int32 _bulkComparisonIndex;

		[ObservableProperty]
		private Double? _bulkConditionValue = 0;

		[ObservableProperty]
		private String _bulkConditionHint = String.Empty;

		[ObservableProperty]
		private String _bulkStatus = String.Empty;

		public IReadOnlyList<BulkKindOption> BulkKinds { get; } =
		[
			.. ProtoKindExtensions.All.Select(k => new BulkKindOption(k, Loc.Get("kind." + k))),
		];

		public Boolean HasBulkCondition
		{
			get { return SelectedConditionField?.Field is not null; }
		}

		private void InitializeBulk()
		{
			SelectedBulkKind = BulkKinds.First(k => k.Kind == ProtoKind.Weapon);
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
					.Where(f => f.Editor is FieldEditor.Number or FieldEditor.Choice && f.Key != FieldKeys.ProtoId)
					.Select(f => new BulkFieldOption(f, Loc.Field(f.Key))),
			];
			SelectedConditionField = BulkConditionFields[0];
			BulkStatus = String.Empty;
		}

		partial void OnSelectedConditionFieldChanged(BulkFieldOption? value)
		{
			OnPropertyChanged(nameof(HasBulkCondition));
			UpdateConditionHint();
		}

		partial void OnBulkConditionValueChanged(Double? value)
		{
			UpdateConditionHint();
		}

		/// <summary>For a field that lists named values (a damage type, an animation) the name of the number that was typed.</summary>
		private void UpdateConditionHint()
		{
			BulkConditionHint = SelectedConditionField?.Field is { Editor: FieldEditor.Choice, ChoiceSet: { } set } && BulkConditionValue is { } value
				? _bulkChoices.Get(set).Where(o => o.Value == (Int32)value).Select(o => o.Label).FirstOrDefault() ?? String.Empty
				: String.Empty;
		}

		private (BulkKindOption Kind, BulkFieldOption Field, BulkOperation Operation, Double Operand, BulkCondition? Condition)? ReadBulk()
		{
			if (SelectedBulkKind is not { } kind || SelectedBulkField is not { Field: not null } field || BulkValue is not { } operand)
			{
				return null;
			}

			BulkCondition? condition = SelectedConditionField?.Field is { } conditionField && BulkConditionValue is { } reference
				? new BulkCondition(conditionField.Key, Comparisons[Math.Clamp(BulkComparisonIndex, 0, Comparisons.Length - 1)], (Int32)reference)
				: null;
			return (kind, field, (BulkOperation)Math.Clamp(BulkOperationIndex, 0, 2), operand, condition);
		}

		private String DescribeChange(BulkChange change)
		{
			String name = change.Entry.Name.Length > 0 ? change.Entry.Name : change.Entry.Pid.ToString();
			return $"{name}: {change.OldValue} → {change.NewValue}";
		}

		[RelayCommand]
		private void PreviewBulk()
		{
			if (_workspaceService.Current is not { } workspace || ReadBulk() is not { } rule)
			{
				return;
			}

			IReadOnlyList<BulkChange> plan = BulkEditor.Plan(workspace.Catalog.All, rule.Kind.Kind, rule.Field.Field!.Key, rule.Operation, rule.Operand, rule.Condition);
			BulkStatus = plan.Count == 0
				? Loc.Get("tools.bulkNone")
				: Loc.Format("tools.bulkResult", plan.Count, String.Join("; ", plan.Take(3).Select(DescribeChange)));
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

			String message = Loc.Format("tools.bulkConfirm", plan.Count, rule.Field.Label, String.Join(Environment.NewLine, plan.Take(5).Select(DescribeChange)));
			if (!await _dialogs.ConfirmAsync(Loc.Get("tools.bulk"), message, Loc.Get("tools.bulkApply")))
			{
				return;
			}

			List<ProtoEntry> entries = [.. plan.Select(c => c.Entry)];
			workspace.ModifyField(entries, field.Key, value => BulkEditor.Compute(field, rule.Operation, rule.Operand, value));
			BulkStatus = Loc.Format("tools.bulkDone", plan.Count);
			_notifier.Success(Loc.Get("tools.bulk"), BulkStatus);
		}
	}
}