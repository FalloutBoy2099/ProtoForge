namespace ProtoForge.Core.Editing
{
	using System;
	using System.Collections.Generic;
	using Domain;

	/// <summary>A reversible edit. <see cref="Do"/> is called when the action is executed and again when it is redone.</summary>
	public interface IUndoableAction
	{
		/// <summary>Short human readable description (shown in the undo history and the status bar).</summary>
		String Description { get; }

		void Do();

		void Undo();
	}

	/// <summary>An action that can absorb a following similar action (typing in a numeric box produces one undo step, not ten).</summary>
	public interface IMergeableAction : IUndoableAction
	{
		Boolean TryMerge(IUndoableAction next);
	}

	/// <summary>Linear undo/redo history shared by every editor of a workspace.</summary>
	public sealed class UndoManager : ObservableBase
	{
		private readonly List<IUndoableAction> _undo = [];
		private readonly List<IUndoableAction> _redo = [];
		private DateTime _lastExecuted = DateTime.MinValue;
		private Int32 _version;
		private Int32 _savedVersion;

		public TimeSpan MergeWindow { get; set; } = TimeSpan.FromSeconds(1.5);

		public Boolean CanUndo
		{
			get { return _undo.Count > 0; }
		}

		public Boolean CanRedo
		{
			get { return _redo.Count > 0; }
		}

		public String? UndoDescription
		{
			get { return _undo.Count > 0 ? _undo[^1].Description : null; }
		}

		public String? RedoDescription
		{
			get { return _redo.Count > 0 ? _redo[^1].Description : null; }
		}

		/// <summary>True when the history position differs from the last saved state.</summary>
		public Boolean HasUnsavedChanges
		{
			get { return _version != _savedVersion; }
		}

		public IReadOnlyList<IUndoableAction> UndoHistory
		{
			get { return _undo; }
		}

		public event EventHandler? Changed;

		public void Execute(IUndoableAction action)
		{
			action.Do();
			Push(action);
		}

		/// <summary>Records an action whose effect has already been applied.</summary>
		public void Record(IUndoableAction action)
		{
			Push(action);
		}

		public Boolean Undo()
		{
			if (_undo.Count == 0)
			{
				return false;
			}

			IUndoableAction action = _undo[^1];
			_undo.RemoveAt(_undo.Count - 1);
			action.Undo();
			_redo.Add(action);
			_version--;
			_lastExecuted = DateTime.MinValue;
			RaiseChanged();
			return true;
		}

		public Boolean Redo()
		{
			if (_redo.Count == 0)
			{
				return false;
			}

			IUndoableAction action = _redo[^1];
			_redo.RemoveAt(_redo.Count - 1);
			action.Do();
			_undo.Add(action);
			_version++;
			_lastExecuted = DateTime.MinValue;
			RaiseChanged();
			return true;
		}

		public void MarkSaved()
		{
			_savedVersion = _version;
			RaiseChanged();
		}

		public void Clear()
		{
			_undo.Clear();
			_redo.Clear();
			_version = 0;
			_savedVersion = 0;
			RaiseChanged();
		}

		private void Push(IUndoableAction action)
		{
			DateTime now = DateTime.UtcNow;
			_redo.Clear();
			if (_undo.Count > 0 && now - _lastExecuted <= MergeWindow
				&& _undo[^1] is IMergeableAction mergeable && mergeable.TryMerge(action))
			{
				_lastExecuted = now;
				RaiseChanged();
				return;
			}

			_undo.Add(action);
			_version++;
			_lastExecuted = now;
			RaiseChanged();
		}

		private void RaiseChanged()
		{
			OnPropertyChanged(nameof(CanUndo));
			OnPropertyChanged(nameof(CanRedo));
			OnPropertyChanged(nameof(UndoDescription));
			OnPropertyChanged(nameof(RedoDescription));
			OnPropertyChanged(nameof(HasUnsavedChanges));
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}
}