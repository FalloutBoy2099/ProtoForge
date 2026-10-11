namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.ComponentModel;
	using System.Globalization;
	using System.Linq;
	using System.Threading.Tasks;
	using System.Windows.Data;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using ProtoForge.Core.Editing;
	using ProtoForge.Core.Formats.Msg;
	using ProtoForge.Core.Vfs;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	/// <summary>One message line; editing the text or the audio name goes through the undo history.</summary>
	public sealed partial class MsgRow : ObservableObject
	{
		private readonly MessagesViewModel _owner;
		private Boolean _loading = true;

		[ObservableProperty]
		private String _text;

		[ObservableProperty]
		private String _audio;

		[ObservableProperty]
		private String _description;

		public MsgRow(MessagesViewModel owner, Int32 id, String audio, String text, String? description = null)
		{
			_owner = owner;
			Id = id;
			_audio = audio;
			_text = text;
			HasDescription = description is not null;
			_description = description ?? String.Empty;
			_loading = false;
		}

		public Int32 Id { get; }

		/// <summary>In the prototype texts (<c>pro_*.msg</c>) the id N is the name and N + 1 the description: they share one row.</summary>
		public Boolean HasDescription { get; }

		partial void OnDescriptionChanged(String value)
		{
			if (!_loading && HasDescription)
			{
				_owner.CommitDescription(this);
			}
		}

		partial void OnTextChanged(String value)
		{
			if (!_loading)
			{
				_owner.Commit(this);
			}
		}

		partial void OnAudioChanged(String value)
		{
			if (!_loading)
			{
				_owner.Commit(this);
			}
		}
	}

	public sealed record MsgFileItem(String Name, Boolean IsDirty)
	{
		public String Display
		{
			get { return IsDirty ? Name + " *" : Name; }
		}
	}

	/// <summary>Editor for the game text files (names, descriptions, dialog lines...).</summary>
	public sealed partial class MessagesViewModel : ObservableObject
	{
		private readonly WorkspaceService _workspaceService;
		private readonly IDialogService _dialogs;
		private readonly INotifier _notifier;
		private MsgFile? _file;
		private Boolean _applying;

		[ObservableProperty]
		private IReadOnlyList<MsgFileItem> _files = [];

		[ObservableProperty]
		private MsgFileItem? _selectedFile;

		[ObservableProperty]
		private ICollectionView? _view;

		[ObservableProperty]
		private String _filterText = String.Empty;

		[ObservableProperty]
		private MsgRow? _selectedRow;

		[ObservableProperty]
		private String _status = String.Empty;

		/// <summary>True for the prototype texts: names and descriptions are shown side by side.</summary>
		[ObservableProperty]
		private Boolean _isPaired;

		public MessagesViewModel(WorkspaceService workspaceService, ShellNavigator shell, IDialogService dialogs, INotifier notifier)
		{
			_workspaceService = workspaceService;
			_dialogs = dialogs;
			_notifier = notifier;
			_workspaceService.WorkspaceChanged += (_, ws) => OnWorkspaceChanged(ws);
			shell.MessageRequested += (_, request) => ShowMessage(request.File, request.Id);
			OnWorkspaceChanged(_workspaceService.Current);
		}

		public WorkspaceService Workspace
		{
			get { return _workspaceService; }
		}

		partial void OnSelectedFileChanged(MsgFileItem? value)
		{
			// While the list of files is only refreshed (dirty marks), the rows must stay as they are.
			if (!_applying)
			{
				LoadFile(value);
			}
		}

		partial void OnFilterTextChanged(String value)
		{
			View?.Refresh();
		}

		public void ShowMessage(String fileName, Int32 id)
		{
			MsgFileItem? item = Files.FirstOrDefault(f => String.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase));
			if (item is null)
			{
				return;
			}

			SelectedFile = item;
			SelectedRow = (View?.SourceCollection as IEnumerable<MsgRow>)?.FirstOrDefault(r => r.Id == id || (r.HasDescription && r.Id + 1 == id));
		}

		/// <summary>Called by a row after the user edited its description (the message that follows its id).</summary>
		internal void CommitDescription(MsgRow row)
		{
			GameWorkspace? workspace = _workspaceService.Current;
			if (workspace is null || _file is null || _applying)
			{
				return;
			}

			_applying = true;
			try
			{
				String audio = _file.Find(row.Id + 1)?.Audio ?? String.Empty;
				workspace.Undo.Execute(new MessageEditAction(_file, row.Id + 1, row.Description, audio, workspace.RefreshTexts));
				RefreshFileList(keepSelection: true);
			}
			finally
			{
				_applying = false;
			}
		}

		private static Boolean IsPairedFile(String name)
		{
			return name.StartsWith("pro_", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>The rows of a file; in a prototype file the description (id + 1) of a name (id) goes into the row of the name.</summary>
		private List<MsgRow> BuildRows(MsgFile file, Boolean paired)
		{
			List<MsgEntry> entries = [.. file.Entries];
			if (!paired)
			{
				return [.. entries.Select(e => new MsgRow(this, e.Id, e.Audio, e.Text))];
			}

			Dictionary<Int32, MsgEntry> byId = [];
			foreach (MsgEntry entry in entries)
			{
				byId[entry.Id] = entry;
			}

			List<MsgRow> rows = [];
			foreach (MsgEntry entry in entries)
			{
				if (entry.Id % 100 == 1 && byId.ContainsKey(entry.Id - 1))
				{
					continue;
				}

				String? description = entry.Id % 100 == 0 && byId.TryGetValue(entry.Id + 1, out MsgEntry? next) ? ProtoForge.Core.Domain.ProtoTextService.ToDisplay(next.Text) : null;
				rows.Add(new MsgRow(this, entry.Id, entry.Audio, entry.Text, description));
			}

			return rows;
		}

		/// <summary>Called by a row after the user edited it.</summary>
		internal void Commit(MsgRow row)
		{
			GameWorkspace? workspace = _workspaceService.Current;
			if (workspace is null || _file is null || _applying)
			{
				return;
			}

			_applying = true;
			try
			{
				workspace.Undo.Execute(new MessageEditAction(_file, row.Id, row.Text, row.Audio, workspace.RefreshTexts));
				RefreshFileList(keepSelection: true);
			}
			finally
			{
				_applying = false;
			}
		}

		[RelayCommand]
		private async Task AddMessage()
		{
			GameWorkspace? workspace = _workspaceService.Current;
			if (workspace is null || _file is null)
			{
				return;
			}

			Int32 suggested = (_file.Entries.Select(e => e.Id).DefaultIfEmpty(0).Max() / 100 + 1) * 100;
			String? input = await _dialogs.PromptAsync(Loc.Get("messages.addTitle"), Loc.Get("messages.addId"), suggested.ToString(CultureInfo.InvariantCulture));
			if (input is null)
			{
				return;
			}

			if (!Int32.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out Int32 id) || id < 0)
			{
				_notifier.Warning(Loc.Get("messages.addTitle"), Loc.Get("messages.invalidId"));
				return;
			}

			if (_file.Find(id) is not null)
			{
				_notifier.Warning(Loc.Get("messages.addTitle"), Loc.Format("messages.exists", id));
				ShowMessage(SelectedFile!.Name, id);
				return;
			}

			_applying = true;
			try
			{
				workspace.Undo.Execute(new MessageEditAction(_file, id, String.Empty, String.Empty, workspace.RefreshTexts));
			}
			finally
			{
				_applying = false;
			}

			LoadFile(SelectedFile);
			SelectedRow = (View?.SourceCollection as IEnumerable<MsgRow>)?.FirstOrDefault(r => r.Id == id);
		}

		private Boolean HasRowSelected()
		{
			return SelectedRow is not null;
		}

		[RelayCommand(CanExecute = nameof(HasRowSelected))]
		private async Task DeleteMessage()
		{
			GameWorkspace? workspace = _workspaceService.Current;
			if (workspace is null || _file is null || SelectedRow is not { } row)
			{
				return;
			}

			if (!await _dialogs.ConfirmAsync(Loc.Get("messages.deleteTitle"), Loc.Format("messages.deleteConfirm", row.Id), Loc.Get("delete.button")))
			{
				return;
			}

			_applying = true;
			try
			{
				List<IUndoableAction> removals = [new MessageRemoveAction(_file, row.Id, workspace.RefreshTexts)];
				if (row.HasDescription)
				{
					removals.Add(new MessageRemoveAction(_file, row.Id + 1, workspace.RefreshTexts));
				}

				workspace.Undo.Execute(removals.Count == 1 ? removals[0] : new CompositeAction(removals, Loc.Format("messages.deleteTitle")));
			}
			finally
			{
				_applying = false;
			}

			LoadFile(SelectedFile);
		}

		partial void OnSelectedRowChanged(MsgRow? value)
		{
			DeleteMessageCommand.NotifyCanExecuteChanged();
		}

		private GameWorkspace? _current;

		private void OnWorkspaceChanged(GameWorkspace? workspace)
		{
			Boolean same = ReferenceEquals(workspace, _current) && workspace is not null;
			_current = workspace;
			if (workspace is not null)
			{
				workspace.Undo.Changed -= OnUndoChanged;
				workspace.Undo.Changed += OnUndoChanged;
			}

			// A save raises the same notification as a new game: stay on the file that is being edited.
			RefreshFileList(keepSelection: same);
		}

		private void OnUndoChanged(Object? sender, EventArgs e)
		{
			if (_applying)
			{
				return;
			}

			System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
			{
				if (!_applying)
				{
					// Edits made elsewhere (a prototype, another page) raise this too: rebuild the rows only when they really changed.
					if (!RowsMatchFile())
					{
						LoadFile(SelectedFile);
					}

					RefreshFileList(keepSelection: true);
				}
			});
		}

		private Boolean RowsMatchFile()
		{
			if (_file is null || View?.SourceCollection is not IList<MsgRow> rows)
			{
				return false;
			}

			List<MsgRow> expected = BuildRows(_file, IsPaired);
			if (rows.Count != expected.Count)
			{
				return false;
			}

			for (Int32 i = 0; i < rows.Count; i++)
			{
				MsgRow shown = rows[i];
				MsgRow current = expected[i];
				if (shown.Id != current.Id || shown.Text != current.Text || shown.Audio != current.Audio || shown.Description != current.Description)
				{
					return false;
				}
			}

			return true;
		}

		private void RefreshFileList(Boolean keepSelection)
		{
			GameWorkspace? workspace = _workspaceService.Current;
			if (workspace is null)
			{
				Files = [];
				SelectedFile = null;
				View = null;
				return;
			}

			String? previous = keepSelection ? SelectedFile?.Name : null;
			HashSet<String> names = new(
				workspace.Files.Enumerate($"text/{workspace.Language}/game")
					.Where(p => GamePath.GetExtension(p) == ".msg")
					.Select(GamePath.GetFileName),
				StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<String, MsgFile> loaded in workspace.Messages.Loaded)
			{
				names.Add(loaded.Key);
			}

			HashSet<String> dirty = [.. workspace.Messages.DirtyFiles];
			Files = [.. names.Order(StringComparer.OrdinalIgnoreCase).Select(n => new MsgFileItem(n, dirty.Contains(n)))];
			_applying = true;
			try
			{
				SelectedFile = previous is null
					? Files.FirstOrDefault(f => String.Equals(f.Name, "pro_item.msg", StringComparison.OrdinalIgnoreCase)) ?? Files.FirstOrDefault()
					: Files.FirstOrDefault(f => String.Equals(f.Name, previous, StringComparison.OrdinalIgnoreCase));
			}
			finally
			{
				_applying = false;
			}

			if (previous is null)
			{
				LoadFile(SelectedFile);
			}
		}

		private void LoadFile(MsgFileItem? item)
		{
			GameWorkspace? workspace = _workspaceService.Current;
			if (workspace is null || item is null)
			{
				_file = null;
				View = null;
				Status = String.Empty;
				return;
			}

			_file = workspace.GetMessageFile(item.Name);
			IsPaired = IsPairedFile(item.Name);
			ObservableCollection<MsgRow> rows = new(BuildRows(_file, IsPaired));
			ListCollectionView view = new(rows)
			{
				Filter = row =>
				{
					if (FilterText.Length == 0)
					{
						return true;
					}

					MsgRow r = (MsgRow)row;
					return r.Text.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
						|| r.Description.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
						|| r.Id.ToString(CultureInfo.InvariantCulture).Contains(FilterText, StringComparison.Ordinal);
				},
			};
			View = view;
			Status = Loc.Format("messages.count", rows.Count, workspace.Messages.Encoding.WebName);
		}
	}
}