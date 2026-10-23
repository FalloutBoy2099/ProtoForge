namespace ProtoForge.Core.Editing
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Domain;
	using Formats.Pro;

	public enum BulkOperation
	{
		/// <summary>The value becomes the operand.</summary>
		Set,

		/// <summary>The operand is added to the value.</summary>
		Add,

		/// <summary>The value is multiplied by the operand (rounded to the nearest whole number).</summary>
		Multiply,

		/// <summary>Turns on the flag whose mask is the operand (the flags that exclude it are turned off).</summary>
		SetFlag,

		/// <summary>Turns off the flag whose mask is the operand.</summary>
		ClearFlag,
	}

	public enum BulkComparison
	{
		Equal,
		NotEqual,
		Less,
		LessOrEqual,
		Greater,
		GreaterOrEqual,
	}

	/// <summary>Only the prototypes whose <see cref="FieldKey"/> compares to <see cref="Value"/> in this way are edited.</summary>
	public sealed record BulkCondition(String FieldKey, BulkComparison Comparison, Int32 Value);

	/// <summary>The new value of one field of one prototype.</summary>
	public sealed record BulkChange(ProtoEntry Entry, Int32 OldValue, Int32 NewValue);

	/// <summary>Changes one number, choice or flag of many prototypes by a rule ("multiply the damage of every weapon whose range is above 20 by 1.2").</summary>
	public static class BulkEditor
	{
		/// <summary>The value after the operation: numbers are kept inside the range the field accepts, a choice takes the operand, a flag is turned on or off.</summary>
		public static Int32 Compute(ProtoField field, BulkOperation operation, Double operand, Int32 current)
		{
			if (operation is BulkOperation.SetFlag or BulkOperation.ClearFlag)
			{
				UInt32 mask = unchecked((UInt32)(Int64)operand);
				UInt32 value = unchecked((UInt32)current);
				if (operation == BulkOperation.ClearFlag)
				{
					return unchecked((Int32)(value & ~mask));
				}

				UInt32 conflicts = field.FlagSet is { } id && FlagSets.TryGet(id, out FlagSet set) ? set.GetConflicts(mask) : 0;
				return unchecked((Int32)((value | mask) & ~conflicts));
			}

			if (field.Editor != FieldEditor.Number)
			{
				return (Int32)operand;
			}

			Double result = operation switch
			{
				BulkOperation.Set => operand,
				BulkOperation.Add => current + operand,
				_ => current * operand,
			};

			result = Math.Round(result, MidpointRounding.AwayFromZero);
			return (Int32)Math.Clamp(result, field.Min, field.Max);
		}

		public static Boolean Matches(BulkComparison comparison, Int32 value, Int32 reference)
		{
			return comparison switch
			{
				BulkComparison.Equal => value == reference,
				BulkComparison.NotEqual => value != reference,
				BulkComparison.Less => value < reference,
				BulkComparison.LessOrEqual => value <= reference,
				BulkComparison.Greater => value > reference,
				_ => value >= reference,
			};
		}

		/// <summary>The fields a bulk edit can change: the plain numbers, the choices (a damage type, an animation...) and the flags of a prototype.</summary>
		public static IReadOnlyList<ProtoField> GetEditableFields(ProtoKind kind)
		{
			return [.. ProtoSchemas.For(kind).Fields.Where(f => !f.IsReadOnly && f.Key != FieldKeys.ProtoId
				&& ((f.Editor == FieldEditor.Number && !f.IsVirtual) || (f.Editor == FieldEditor.Choice && f.ChoiceSet is not null) || (f.Editor == FieldEditor.Flags && f.FlagSet is not null)))];
		}

		/// <summary>
		/// What the edit would do (nothing is modified here): the prototypes of <paramref name="kind"/> that have the field, match the condition,
		/// and end up with another value.
		/// </summary>
		public static IReadOnlyList<BulkChange> Plan(IEnumerable<ProtoEntry> entries, ProtoKind kind, String fieldKey, BulkOperation operation, Double operand, BulkCondition? condition = null)
		{
			ProtoField? field = ProtoSchemas.For(kind).Find(fieldKey);
			if (field is null || field.Editor is not (FieldEditor.Number or FieldEditor.Choice or FieldEditor.Flags))
			{
				return [];
			}

			List<BulkChange> changes = [];
			foreach (ProtoEntry entry in entries)
			{
				if (entry.File is not { } file || file.Kind != kind || entry.GetValue(field) is not { } current)
				{
					continue;
				}

				if (condition is not null)
				{
					ProtoField? conditionField = file.Schema.Find(condition.FieldKey);
					if (conditionField is null || entry.GetValue(conditionField) is not { } other || !Matches(condition.Comparison, other, condition.Value))
					{
						continue;
					}
				}

				Int32 updated = Compute(field, operation, operand, current);
				if (updated != current)
				{
					changes.Add(new BulkChange(entry, current, updated));
				}
			}

			return changes;
		}
	}
}