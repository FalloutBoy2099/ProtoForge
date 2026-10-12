namespace ProtoForge.Core.Editing
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Domain;
	using Formats.Msg;
	using Formats.Pro;

	/// <summary>
	/// Sets one field on one or several prototypes (mass edit is just a longer entry list). The field is resolved by key in the
	/// schema of each entry, so prototypes of different kinds can be edited together as long as they share the field.
	/// </summary>
	public sealed class FieldEditAction : IMergeableAction
	{
		private sealed class Target
		{
			public Target(ProtoEntry entry, ProtoField field, Int32 oldValue, Int32 newValue)
			{
				Entry = entry;
				Field = field;
				OldValue = oldValue;
				NewValue = newValue;
			}

			public ProtoEntry Entry { get; }

			public ProtoField Field { get; }

			public Int32 OldValue { get; }

			public Int32 NewValue { get; set; }
		}

		private readonly List<Target> _targets = [];
		private readonly String _key;

		public FieldEditAction(IReadOnlyList<ProtoEntry> entries, String key, Func<Int32, Int32> transform, String description)
		{
			_key = key;
			Description = description;
			foreach (ProtoEntry entry in entries)
			{
				ProtoField? field = entry.File?.Schema.Find(key);
				Int32? current = field is null ? null : entry.GetValue(field);
				if (field is not null && current is not null)
				{
					_targets.Add(new Target(entry, field, current.Value, transform(current.Value)));
				}
			}
		}

		public FieldEditAction(IReadOnlyList<ProtoEntry> entries, String key, Int32 newValue, String description)
			: this(entries, key, _ => newValue, description)
		{
		}

		public FieldEditAction(IReadOnlyList<ProtoEntry> entries, ProtoField field, Int32 newValue, String description)
			: this(entries, field.Key, _ => newValue, description)
		{
		}

		public String Description { get; }

		public Boolean ChangesAnything
		{
			get { return _targets.Any(t => t.OldValue != t.NewValue); }
		}

		public void Do()
		{
			foreach (Target target in _targets)
			{
				target.Entry.TrySetValue(target.Field, target.NewValue);
			}
		}

		public void Undo()
		{
			foreach (Target target in _targets)
			{
				target.Entry.TrySetValue(target.Field, target.OldValue);
			}
		}

		public Boolean TryMerge(IUndoableAction next)
		{
			if (next is not FieldEditAction other || !String.Equals(other._key, _key, StringComparison.Ordinal)
				|| other._targets.Count != _targets.Count)
			{
				return false;
			}

			for (Int32 i = 0; i < _targets.Count; i++)
			{
				if (!ReferenceEquals(_targets[i].Entry, other._targets[i].Entry))
				{
					return false;
				}
			}

			for (Int32 i = 0; i < _targets.Count; i++)
			{
				_targets[i].NewValue = other._targets[i].NewValue;
			}

			return true;
		}
	}

	/// <summary>Edits the name and description (MSG texts) of a prototype.</summary>
	public sealed class TextEditAction : IMergeableAction
	{
		private readonly ProtoTextService _texts;
		private readonly ProtoEntry _entry;
		private readonly String _oldName;
		private readonly String _oldDescription;
		private String _newName;
		private String _newDescription;

		public TextEditAction(ProtoTextService texts, ProtoEntry entry, String newName, String newDescription)
		{
			_texts = texts;
			_entry = entry;
			_oldName = entry.Name;
			_oldDescription = entry.Description;
			_newName = newName;
			_newDescription = newDescription;
		}

		public String Description
		{
			get { return UserTexts.Format("act.editText", "Edit text"); }
		}

		public void Do()
		{
			_texts.Write(_entry, _newName, _newDescription);
		}

		public void Undo()
		{
			_texts.Write(_entry, _oldName, _oldDescription);
		}

		public Boolean TryMerge(IUndoableAction next)
		{
			if (next is not TextEditAction other || !ReferenceEquals(other._entry, _entry))
			{
				return false;
			}

			_newName = other._newName;
			_newDescription = other._newDescription;
			return true;
		}
	}

	/// <summary>Swaps the whole binary content of a prototype (hex edits, JSON import, repair of a wrong size).</summary>
	public sealed class ReplaceFileAction : IUndoableAction
	{
		private readonly ProtoEntry _entry;
		private readonly ProtoFile _oldFile;
		private readonly ProtoFile _newFile;

		public ReplaceFileAction(ProtoEntry entry, ProtoFile newFile, String description)
		{
			_entry = entry;
			_oldFile = entry.File?.Clone() ?? throw new InvalidOperationException(UserTexts.Format("err.noContentReplace", "The prototype has no content to replace."));
			_newFile = newFile;
			Description = description;
		}

		public String Description { get; }

		public void Do()
		{
			_entry.ReplaceFile(_newFile.Clone());
		}

		public void Undo()
		{
			_entry.ReplaceFile(_oldFile.Clone());
		}
	}

	/// <summary>
	/// Goes back to the version of a lower layer (usually <c>master.dat</c>) and deletes the override file of the output
	/// folder when the change is saved.
	/// </summary>
	public sealed class RemoveOverrideAction : IUndoableAction
	{
		private readonly ProtoEntry _entry;
		private readonly ProtoFile _fallback;
		private readonly String _fallbackSource;
		private readonly ProtoFile _oldFile;
		private readonly String _oldSource;
		private readonly Boolean _wasPending;
		private readonly ProtoFile? _oldFallback;

		public RemoveOverrideAction(ProtoEntry entry, ProtoFile fallback, String fallbackSource, String description)
		{
			_entry = entry;
			_fallback = fallback;
			_fallbackSource = fallbackSource;
			_oldFile = entry.File?.Clone() ?? throw new InvalidOperationException(UserTexts.Format("err.noContent", "The prototype has no content."));
			_oldSource = entry.SourceName;
			_wasPending = entry.PendingOverrideRemoval;
			_oldFallback = entry.OverrideFallback;
			Description = description;
		}

		public String Description { get; }

		public void Do()
		{
			_entry.ReplaceFile(_fallback.Clone());
			_entry.SourceName = _fallbackSource;
			_entry.SetOverrideRemoval(true, _fallback.Clone());
		}

		public void Undo()
		{
			_entry.ReplaceFile(_oldFile.Clone());
			_entry.SourceName = _oldSource;
			_entry.SetOverrideRemoval(_wasPending, _oldFallback);
		}
	}

	/// <summary>Appends a new prototype (create, duplicate, import) at the end of its LST file.</summary>
	public sealed class AddProtoAction : IUndoableAction
	{
		private readonly ProtoCatalog _catalog;
		private readonly ProtoTextService _texts;
		private readonly ProtoObjectType _type;
		private readonly ProtoFile _file;
		private readonly String _fileName;
		private readonly String _sourceName;
		private readonly String _name;
		private readonly String _description;

		public AddProtoAction(
			ProtoCatalog catalog,
			ProtoTextService texts,
			ProtoObjectType type,
			ProtoFile file,
			String fileName,
			String sourceName,
			String name,
			String description,
			String actionDescription)
		{
			_catalog = catalog;
			_texts = texts;
			_type = type;
			_file = file;
			_fileName = fileName;
			_sourceName = sourceName;
			_name = name;
			_description = description;
			Description = actionDescription;
		}

		public String Description { get; }

		/// <summary>The entry created by <see cref="Do"/> (kept identical across undo/redo so views keep their references).</summary>
		public ProtoEntry? Entry { get; private set; }

		public void Do()
		{
			if (Entry is null)
			{
				Entry = _catalog.Append(_type, _file, _fileName, _sourceName);
			}
			else
			{
				_catalog.Restore(Entry);
			}

			Int32 messageId = Entry.File?.MessageId ?? 0;
			MsgFile messages = _texts.GetFile(_type);
			_previousName = messageId > 0 ? messages.Find(messageId)?.Text : null;
			_previousDescription = messageId > 0 ? messages.Find(messageId + 1)?.Text : null;
			_texts.Write(Entry, _name, _description);
		}

		public void Undo()
		{
			if (Entry is null)
			{
				return;
			}

			IReadOnlyList<ProtoEntry> list = _catalog.GetEntries(_type);
			if (list.Count == 0 || !ReferenceEquals(list[^1], Entry))
			{
				throw new InvalidOperationException(UserTexts.Format("err.laterAdded", "Another prototype was added after this one; undo them first."));
			}

			_catalog.RemoveLast(_type);

			// Do not leave orphan texts behind in the MSG file, and give back the texts that this action overwrote.
			Int32 messageId = Entry.File?.MessageId ?? 0;
			if (messageId > 0)
			{
				MsgFile messages = _texts.GetFile(_type);
				RestoreText(messages, messageId, _previousName);
				RestoreText(messages, messageId + 1, _previousDescription);
			}
		}

		private String? _previousName;
		private String? _previousDescription;

		private static void RestoreText(MsgFile messages, Int32 id, String? previous)
		{
			if (previous is null)
			{
				messages.Remove(id);
			}
			else
			{
				messages.SetText(id, previous);
			}
		}
	}

	/// <summary>Removes the last prototype of a type from its LST.</summary>
	public sealed class RemoveLastProtoAction : IUndoableAction
	{
		private readonly ProtoCatalog _catalog;
		private readonly ProtoEntry _entry;

		public RemoveLastProtoAction(ProtoCatalog catalog, ProtoEntry entry, String description)
		{
			_catalog = catalog;
			_entry = entry;
			Description = description;
		}

		public String Description { get; }

		public void Do()
		{
			IReadOnlyList<ProtoEntry> list = _catalog.GetEntries(_entry.Type);
			if (list.Count == 0 || !ReferenceEquals(list[^1], _entry))
			{
				throw new InvalidOperationException(UserTexts.Format("err.onlyLastRemove", "Only the last prototype of a list can be removed."));
			}

			_catalog.RemoveLast(_entry.Type);
		}

		public void Undo()
		{
			_catalog.Restore(_entry);
		}
	}

	/// <summary>Groups several actions into one undo step.</summary>
	public sealed class CompositeAction : IUndoableAction
	{
		private readonly IReadOnlyList<IUndoableAction> _actions;

		public CompositeAction(IReadOnlyList<IUndoableAction> actions, String description)
		{
			_actions = actions;
			Description = description;
		}

		public String Description { get; }

		public Int32 Count
		{
			get { return _actions.Count; }
		}

		public void Do()
		{
			foreach (IUndoableAction action in _actions)
			{
				action.Do();
			}
		}

		public void Undo()
		{
			for (Int32 i = _actions.Count - 1; i >= 0; i--)
			{
				_actions[i].Undo();
			}
		}
	}
}