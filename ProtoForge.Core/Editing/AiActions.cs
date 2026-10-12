namespace ProtoForge.Core.Editing
{
	using System;
	using System.Collections.Generic;
	using Formats.Ai;

	/// <summary>Sets (or removes, with a null value) one parameter of an AI packet.</summary>
	public sealed class AiValueAction : IMergeableAction
	{
		private readonly AiFile _file;
		private readonly String _section;
		private readonly String _key;
		private readonly String? _oldValue;
		private String? _newValue;

		public AiValueAction(AiFile file, String section, String key, String? newValue)
		{
			_file = file;
			_section = section;
			_key = key;
			_oldValue = file.GetValue(section, key);
			_newValue = newValue;
			Description = $"{section}: {key}";
		}

		public String Description { get; }

		public Boolean ChangesAnything
		{
			get { return !String.Equals(_oldValue, _newValue, StringComparison.Ordinal); }
		}

		public void Do()
		{
			_file.SetValue(_section, _key, _newValue);
		}

		public void Undo()
		{
			_file.SetValue(_section, _key, _oldValue);
		}

		public Boolean TryMerge(IUndoableAction next)
		{
			if (next is not AiValueAction other || !ReferenceEquals(other._file, _file) || other._section != _section || other._key != _key)
			{
				return false;
			}

			_newValue = other._newValue;
			return true;
		}
	}

	/// <summary>Adds a new packet at the end of the AI file.</summary>
	public sealed class AiAddPacketAction : IUndoableAction
	{
		private readonly AiFile _file;
		private readonly String _name;
		private readonly IReadOnlyList<String> _lines;

		public AiAddPacketAction(AiFile file, String name, IReadOnlyList<String> lines)
		{
			_file = file;
			_name = name;
			_lines = lines;
			Description = UserTexts.Format("act.addAiPacket", "Add AI packet {0}", name);
		}

		public String Description { get; }

		public void Do()
		{
			_previousLineCount = _file.LastSectionLineCount;
			_added = _file.AddSection(_name, _lines);
		}

		public void Undo()
		{
			if (_added is not null)
			{
				_file.RemoveSection(_added);
				_file.TruncateLastSection(_previousLineCount);
			}
		}

		private AiSection? _added;
		private Int32 _previousLineCount;
	}

	/// <summary>Removes a packet from the AI file (it goes back to its place on undo).</summary>
	public sealed class AiRemovePacketAction : IUndoableAction
	{
		private readonly AiFile _file;
		private readonly AiSection _section;
		private Int32 _index = -1;

		public AiRemovePacketAction(AiFile file, AiSection section)
		{
			_file = file;
			_section = section;
			Description = UserTexts.Format("act.deleteAiPacket", "Delete AI packet {0}", section.Name);
		}

		public String Description { get; }

		public void Do()
		{
			_index = _file.RemoveSection(_section);
		}

		public void Undo()
		{
			if (_index >= 0)
			{
				_file.InsertSection(_index, _section);
			}
		}
	}

	/// <summary>Replaces the whole text of a packet (the raw text editor).</summary>
	public sealed class AiRawTextAction : IUndoableAction
	{
		private readonly AiFile _file;
		private readonly String _section;
		private readonly String _oldText;
		private readonly String _newText;

		public AiRawTextAction(AiFile file, String section, String newText)
		{
			_file = file;
			_section = section;
			_oldText = file.GetRawText(section);
			_newText = newText;
			Description = UserTexts.Format("act.editAiPacket", "Edit AI packet {0}", section);
		}

		public String Description { get; }

		public Boolean ChangesAnything
		{
			get { return !String.Equals(_oldText, _newText, StringComparison.Ordinal); }
		}

		public void Do()
		{
			_file.SetRawText(_section, _newText);
		}

		public void Undo()
		{
			_file.SetRawText(_section, _oldText);
		}
	}
}