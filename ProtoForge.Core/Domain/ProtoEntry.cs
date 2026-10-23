namespace ProtoForge.Core.Domain
{
	using System;
	using Formats.Pro;
	using Validation;

	public sealed class FieldChangedEventArgs : EventArgs
	{
		public FieldChangedEventArgs(ProtoField? field)
		{
			Field = field;
		}

		/// <summary>The modified field, or null when the whole file changed.</summary>
		public ProtoField? Field { get; }
	}

	/// <summary>
	/// One prototype of the game: its identity (taken from its position in the LST file), the parsed <c>.pro</c> content
	/// and the texts found in the MSG files. Edits are tracked against the state that was loaded from disk.
	/// </summary>
	public sealed class ProtoEntry : ObservableBase
	{
		private ProtoFile? _file;
		private ProtoFile? _baseline;
		private String _fileName;
		private String _name = String.Empty;
		private String _description = String.Empty;
		private String _baselineName = String.Empty;
		private String _baselineDescription = String.Empty;
		private String _sourceName;
		private Boolean _isNew;
		private Int32 _revision;
		private Severity? _worstIssue;
		private Boolean _isReadOnly;
		private Boolean _pendingOverrideRemoval;
		private ProtoFile? _overrideFallback;

		public ProtoEntry(ProtoId pid, String fileName, ProtoFile? file, String? loadError, String sourceName)
		{
			Pid = pid;
			_fileName = fileName;
			_file = file;
			_baseline = file?.Clone();
			LoadError = loadError;
			_sourceName = sourceName;
		}

		public ProtoId Pid { get; }

		public ProtoObjectType Type
		{
			get { return Pid.Type; }
		}

		/// <summary>1-based index in the LST file.</summary>
		public Int32 Index
		{
			get { return Pid.Index; }
		}

		/// <summary>File name as listed in the LST (for example <c>00000012.pro</c>).</summary>
		public String FileName
		{
			get { return _fileName; }
			set
			{
				if (SetProperty(ref _fileName, value))
				{
					OnPropertyChanged(nameof(DisplayName));
				}
			}
		}

		/// <summary>The parsed content, or null when the file is missing or unreadable (see <see cref="LoadError"/>).</summary>
		public ProtoFile? File
		{
			get { return _file; }
		}

		public ProtoKind? Kind
		{
			get { return _file?.Kind; }
		}

		public String? LoadError { get; private set; }

		public Boolean IsBroken
		{
			get { return _file is null; }
		}

		public String Name
		{
			get { return _name; }
			private set
			{
				if (SetProperty(ref _name, value))
				{
					OnPropertyChanged(nameof(DisplayName));
				}
			}
		}

		/// <summary>The name, or the file name in angle brackets when the prototype has no text.</summary>
		public String DisplayName
		{
			get { return _name.Length > 0 ? _name : $"<{_fileName}>"; }
		}

		/// <summary>The line of <c>scripts.lst</c> (counted from 0) of the script of the prototype, or -1 when it has none.</summary>
		public Int32 ScriptIndex
		{
			get { return _file?.TryGet(FieldKeys.ScriptId) is { } id && id != -1 ? id & 0xFFFFFF : -1; }
		}

		public String Description
		{
			get { return _description; }
			private set { SetProperty(ref _description, value); }
		}

		/// <summary>The name as it was when loaded or last saved.</summary>
		public String BaselineName
		{
			get { return _baselineName; }
		}

		/// <summary>The description as it was when loaded or last saved.</summary>
		public String BaselineDescription
		{
			get { return _baselineDescription; }
		}

		/// <summary>Display name of the layer providing the file (master.dat, a mod, the output folder...).</summary>
		public String SourceName
		{
			get { return _sourceName; }
			set { SetProperty(ref _sourceName, value); }
		}

		/// <summary>Created in this session and not saved yet.</summary>
		public Boolean IsNew
		{
			get { return _isNew; }
			set
			{
				if (SetProperty(ref _isNew, value))
				{
					OnPropertyChanged(nameof(IsModified));
				}
			}
		}

		public Boolean HasFileChanges
		{
			get { return _file is not null && (_baseline is null || !_file.ContentEquals(_baseline)); }
		}

		public Boolean HasTextChanges
		{
			get { return _name != _baselineName || _description != _baselineDescription; }
		}

		public Boolean IsModified
		{
			get { return _isNew || HasFileChanges || HasTextChanges || _pendingOverrideRemoval; }
		}

		/// <summary>The file on disk has the read-only attribute (shown as "R/O" in the list).</summary>
		public Boolean IsReadOnly
		{
			get { return _isReadOnly; }
			set { SetProperty(ref _isReadOnly, value); }
		}

		/// <summary>Hit points of a critter (base plus bonus), shown in the list; null for other prototypes.</summary>
		public Int32? CritterHp
		{
			get { return _file is { Kind: ProtoKind.Critter } file ? file.Get("MaxHp") + file.Get("BonusMaxHp") : null; }
		}

		/// <summary>Fallout 1 critter layout (shown as "F1" in the list).</summary>
		public Boolean IsFallout1Format
		{
			get { return _file?.IsFallout1Critter == true; }
		}

		/// <summary>
		/// The override file of the output folder will be deleted by the next save, so that the version of a lower layer
		/// (usually <c>master.dat</c>) is used again.
		/// </summary>
		public Boolean PendingOverrideRemoval
		{
			get { return _pendingOverrideRemoval; }
		}

		/// <summary>The content of the lower layer that replaces the override while <see cref="PendingOverrideRemoval"/> is set.</summary>
		public ProtoFile? OverrideFallback
		{
			get { return _overrideFallback; }
		}

		/// <summary>True when the pending override removal still applies (the content was not edited since).</summary>
		public Boolean RemovesOverride
		{
			get { return _pendingOverrideRemoval && _file is not null && _overrideFallback is not null && _file.ContentEquals(_overrideFallback); }
		}

		public void SetOverrideRemoval(Boolean pending, ProtoFile? fallback)
		{
			_pendingOverrideRemoval = pending;
			_overrideFallback = pending ? fallback : null;
			OnPropertyChanged(nameof(PendingOverrideRemoval));
			OnPropertyChanged(nameof(IsModified));
		}

		/// <summary>Worst problem found by the last validation run (null when none or not validated).</summary>
		public Severity? WorstIssue
		{
			get { return _worstIssue; }
			set { SetProperty(ref _worstIssue, value); }
		}

		/// <summary>Incremented after each modification; lets views detect stale data cheaply.</summary>
		public Int32 Revision
		{
			get { return _revision; }
		}

		/// <summary>The content as it was when loaded or last saved (used for diff and revert).</summary>
		public ProtoFile? Baseline
		{
			get { return _baseline; }
		}

		public event EventHandler<FieldChangedEventArgs>? FieldChanged;

		public Int32? GetValue(ProtoField field)
		{
			return _file?.TryGet(field);
		}

		/// <summary>Writes one field value. Returns false when the file is missing the field or the value did not change.</summary>
		public Boolean TrySetValue(ProtoField field, Int32 value)
		{
			if (_file is null)
			{
				return false;
			}

			Int32? current = _file.TryGet(field);
			if (current is null || current.Value == value)
			{
				return false;
			}

			if (!_file.TrySet(field, value))
			{
				return false;
			}

			RaiseChanged(field);
			return true;
		}

		/// <summary>Replaces the whole file content (undo of structural operations, JSON import, size repair...).</summary>
		public void ReplaceFile(ProtoFile file)
		{
			_file = file;
			LoadError = null;
			OnPropertyChanged(nameof(File));
			OnPropertyChanged(nameof(Kind));
			OnPropertyChanged(nameof(IsFallout1Format));
			OnPropertyChanged(nameof(IsBroken));
			RaiseChanged(null);
		}

		/// <summary>Called by the text service when the MSG texts are (re)loaded or edited.</summary>
		public void SetTexts(String name, String description, Boolean asBaseline)
		{
			Name = name;
			Description = description;
			if (asBaseline)
			{
				_baselineName = name;
				_baselineDescription = description;
			}

			OnPropertyChanged(nameof(HasTextChanges));
			OnPropertyChanged(nameof(IsModified));
			_revision++;
		}

		/// <summary>Forgets the pending changes: the current state becomes the reference state.</summary>
		public void AcceptChanges(String sourceName)
		{
			_baseline = _file?.Clone();
			_baselineName = _name;
			_baselineDescription = _description;
			_isNew = false;
			_pendingOverrideRemoval = false;
			_overrideFallback = null;
			SourceName = sourceName;
			OnPropertyChanged(nameof(IsNew));
			OnPropertyChanged(nameof(HasFileChanges));
			OnPropertyChanged(nameof(HasTextChanges));
			OnPropertyChanged(nameof(PendingOverrideRemoval));
			OnPropertyChanged(nameof(IsModified));
			OnPropertyChanged(nameof(Baseline));
		}

		private void RaiseChanged(ProtoField? field)
		{
			_revision++;
			FieldChanged?.Invoke(this, new FieldChangedEventArgs(field));
			OnPropertyChanged(nameof(HasFileChanges));
			OnPropertyChanged(nameof(IsModified));
			OnPropertyChanged(nameof(CritterHp));
			OnPropertyChanged(nameof(Revision));
		}

		public override String ToString()
		{
			return $"{Pid} {FileName} {Name}";
		}
	}
}