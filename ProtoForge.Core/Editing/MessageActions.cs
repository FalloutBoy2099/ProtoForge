namespace ProtoForge.Core.Editing
{
	using System;
	using Formats.Msg;

	/// <summary>Creates or edits one message of a MSG file.</summary>
	public sealed class MessageEditAction : IMergeableAction
	{
		private readonly MsgFile _file;
		private readonly Int32 _id;
		private readonly Boolean _existed;
		private readonly String _oldText;
		private readonly String _oldAudio;
		private readonly Action? _afterChange;
		private String _newText;
		private String _newAudio;

		public MessageEditAction(MsgFile file, Int32 id, String newText, String newAudio, Action? afterChange = null)
		{
			_file = file;
			_id = id;
			MsgEntry? current = file.Find(id);
			_existed = current is not null;
			_oldText = current?.Text ?? String.Empty;
			_oldAudio = current?.Audio ?? String.Empty;
			_newText = newText;
			_newAudio = newAudio;
			_afterChange = afterChange;
		}

		public String Description
		{
			get { return $"Message {_id}"; }
		}

		public void Do()
		{
			_file.SetText(_id, _newText, _newAudio);
			_afterChange?.Invoke();
		}

		public void Undo()
		{
			if (_existed)
			{
				_file.SetText(_id, _oldText, _oldAudio);
			}
			else
			{
				_file.Remove(_id);
			}

			_afterChange?.Invoke();
		}

		public Boolean TryMerge(IUndoableAction next)
		{
			if (next is not MessageEditAction other || !ReferenceEquals(other._file, _file) || other._id != _id)
			{
				return false;
			}

			_newText = other._newText;
			_newAudio = other._newAudio;
			return true;
		}
	}

	/// <summary>Deletes one message of a MSG file.</summary>
	public sealed class MessageRemoveAction : IUndoableAction
	{
		private readonly MsgFile _file;
		private readonly Int32 _id;
		private readonly String _text;
		private readonly String _audio;
		private readonly Action? _afterChange;

		public MessageRemoveAction(MsgFile file, Int32 id, Action? afterChange = null)
		{
			_file = file;
			_id = id;
			MsgEntry? current = file.Find(id);
			_text = current?.Text ?? String.Empty;
			_audio = current?.Audio ?? String.Empty;
			_afterChange = afterChange;
		}

		public String Description
		{
			get { return $"Delete message {_id}"; }
		}

		public void Do()
		{
			_file.Remove(_id);
			_afterChange?.Invoke();
		}

		public void Undo()
		{
			_file.SetText(_id, _text, _audio);
			_afterChange?.Invoke();
		}
	}
}