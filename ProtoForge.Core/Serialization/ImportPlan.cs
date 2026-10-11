namespace ProtoForge.Core.Serialization
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Domain;
	using Editing;
	using Formats.Pro;

	public sealed record FieldChange(ProtoEntry Entry, ProtoField Field, Int32 OldValue, Int32 NewValue);

	public sealed record TextChange(ProtoEntry Entry, String Name, String Description);

	/// <summary>
	/// The result of reading a JSON/CSV file: what would change, computed without touching any prototype.
	/// The user reviews the plan, then applies it as one undoable step.
	/// </summary>
	public sealed class ImportPlan
	{
		public List<FieldChange> FieldChanges { get; } = [];

		public List<TextChange> TextChanges { get; } = [];

		public List<String> Warnings { get; } = [];

		public Int32 ProtoCount
		{
			get { return FieldChanges.Select(c => c.Entry).Concat(TextChanges.Select(c => c.Entry)).Distinct().Count(); }
		}

		public Boolean IsEmpty
		{
			get { return FieldChanges.Count == 0 && TextChanges.Count == 0; }
		}

		public IUndoableAction ToAction(ProtoTextService texts, String description)
		{
			List<IUndoableAction> actions = [];
			foreach (FieldChange change in FieldChanges)
			{
				actions.Add(new FieldEditAction([change.Entry], change.Field, change.NewValue, description));
			}

			foreach (TextChange change in TextChanges)
			{
				actions.Add(new TextEditAction(texts, change.Entry, change.Name, change.Description));
			}

			return new CompositeAction(actions, description);
		}

		/// <summary>Adds a field change when the value really differs.</summary>
		internal void AddField(ProtoEntry entry, ProtoField field, Int32 value)
		{
			Int32? current = entry.GetValue(field);
			if (current is null)
			{
				Warnings.Add($"{entry.Pid}: field '{field.Key}' does not exist in this file layout.");
				return;
			}

			Boolean fits = field.IsVirtual
				? value >= 0 && ((unchecked((UInt32)value) << field.Shift) & ~field.Mask) == 0
				: field.Size != 1 || value is >= 0 and <= 255;
			if (!fits)
			{
				// A one-byte field or a bit field would silently store another number than the one in the file.
				Warnings.Add($"{entry.Pid}: the value {value} does not fit the field '{field.Key}'; skipped.");
				return;
			}

			if (current.Value != value)
			{
				FieldChanges.Add(new FieldChange(entry, field, current.Value, value));
			}
		}

		internal void AddText(ProtoEntry entry, String? name, String? description)
		{
			if (entry.File is not { MessageId: > 0 })
			{
				if (name is not null || description is not null)
				{
					Warnings.Add($"{entry.Pid}: this prototype has no message id, its texts cannot be changed.");
				}

				return;
			}

			String newName = name ?? entry.Name;
			String newDescription = description ?? entry.Description;
			if (newName != entry.Name || newDescription != entry.Description)
			{
				TextChanges.Add(new TextChange(entry, newName, newDescription));
			}
		}
	}
}