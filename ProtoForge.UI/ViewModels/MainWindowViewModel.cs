namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using CommunityToolkit.Mvvm.Messaging;
	using ProtoForge.Core.Domain;
	using ProtoForge.Infrastructure.Layout;
	using ProtoForge.Infrastructure.Settings;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	/// <summary>State shared by the shell: file commands, undo/redo, global search, busy overlay and status bar.</summary>
	public sealed partial class MainWindowViewModel : ObservableObject
	{
		private readonly WorkspaceService _workspaceService;
		private readonly SettingsService _settings;
		private readonly IDialogService _dialogs;
		private readonly INotifier _notifier;
		private GameWorkspace? _workspace;

		[ObservableProperty]
		private String _title = "ProtoForge";

		[ObservableProperty]
		private String _searchText = String.Empty;

		[ObservableProperty]
		private String _statusText = String.Empty;

		[ObservableProperty]
		private String _modifiedText = String.Empty;

		[ObservableProperty]
		private Boolean _hasUnsavedChanges;

		[ObservableProperty]
		private String _undoTooltip = String.Empty;

		[ObservableProperty]
		private String _redoTooltip = String.Empty;

		[ObservableProperty]
		private IReadOnlyList<RecentProject> _recentProjects = [];

		public MainWindowViewModel(WorkspaceService workspaceService, SettingsService settings, IDialogService dialogs, INotifier notifier)
		{
			_workspaceService = workspaceService;
			_settings = settings;
			_dialogs = dialogs;
			_notifier = notifier;
			_workspaceService.WorkspaceChanged += (_, workspace) => OnWorkspaceChanged(workspace);
			_workspaceService.PropertyChanged += (_, e) =>
			{
				if (e.PropertyName is nameof(WorkspaceService.HasWorkspace))
				{
					OnPropertyChanged(nameof(HasWorkspace));
					SaveCommand.NotifyCanExecuteChanged();
					CloseProjectCommand.NotifyCanExecuteChanged();
					ReloadCommand.NotifyCanExecuteChanged();
				}
			};
			RecentProjects = _settings.Current.RecentProjects;
			StatusText = Loc.Get("status.ready");
		}

		public WorkspaceService Workspace
		{
			get { return _workspaceService; }
		}

		public Boolean HasWorkspace
		{
			get { return _workspaceService.HasWorkspace; }
		}

		public event EventHandler? FocusSearchRequested;

		public Task<Boolean> OpenPathAsync(String path, String? outputFolder = null)
		{
			return _workspaceService.OpenPathAsync(path, outputFolder);
		}

		partial void OnSearchTextChanged(String value)
		{
			WeakReferenceMessenger.Default.Send(new SearchChangedMessage(value));
		}

		[RelayCommand]
		public void FocusSearch()
		{
			FocusSearchRequested?.Invoke(this, EventArgs.Empty);
		}

		[RelayCommand]
		private async Task OpenGameFolder()
		{
			String? folder = _dialogs.PickFolder(Loc.Get("open.gameFolder"), _settings.Current.BaseGameFolder);
			if (folder is not null)
			{
				await OpenAndRemember(folder);
			}
		}

		[RelayCommand]
		private async Task OpenModFolder()
		{
			String? folder = _dialogs.PickFolder(Loc.Get("open.modFolder"));
			if (folder is not null)
			{
				await OpenAndRemember(folder);
			}
		}

		[RelayCommand]
		private async Task OpenDatArchive()
		{
			String? file = _dialogs.PickOpenFile(Loc.Get("open.datFile"), "DAT (*.dat)|*.dat");
			if (file is not null)
			{
				await _workspaceService.OpenPathAsync(file);
			}
		}

		[RelayCommand]
		private async Task OpenRecent(RecentProject? project)
		{
			if (project is null)
			{
				return;
			}

			if (!await _workspaceService.OpenAsync(project.Layout.Clone()))
			{
				return;
			}
		}

		[RelayCommand]
		private void ForgetRecent(RecentProject? project)
		{
			if (project is not null)
			{
				_settings.ForgetProject(project);
				RecentProjects = [.. _settings.Current.RecentProjects];
			}
		}

		private Boolean CanSave()
		{
			return _workspaceService.HasWorkspace;
		}

		[RelayCommand(CanExecute = nameof(CanSave))]
		private async Task Save()
		{
			await _workspaceService.SaveAsync();
		}

		[RelayCommand(CanExecute = nameof(CanSave))]
		private async Task CloseProject()
		{
			await _workspaceService.CloseAsync();
		}

		[RelayCommand(CanExecute = nameof(CanSave))]
		private async Task Reload()
		{
			if (await _workspaceService.ConfirmCloseAsync())
			{
				await _workspaceService.ReloadAsync();
			}
		}

		private Boolean CanUndo()
		{
			return _workspace?.Undo.CanUndo == true;
		}

		private Boolean CanRedo()
		{
			return _workspace?.Undo.CanRedo == true;
		}

		[RelayCommand(CanExecute = nameof(CanUndo))]
		private void Undo()
		{
			_workspace?.Undo.Undo();
		}

		[RelayCommand(CanExecute = nameof(CanRedo))]
		private void Redo()
		{
			_workspace?.Undo.Redo();
		}

		private async Task OpenAndRemember(String folder)
		{
			if (GameLayoutDiscovery.Classify(folder) == OpenTargetKind.GameFolder)
			{
				_settings.Current.BaseGameFolder = folder;
				_settings.Save();
			}

			await _workspaceService.OpenPathAsync(folder);
		}

		private void OnWorkspaceChanged(GameWorkspace? workspace)
		{
			if (!ReferenceEquals(workspace, _workspace))
			{
				if (_workspace is not null)
				{
					_workspace.Undo.Changed -= OnUndoChanged;
					_workspace.Catalog.Changed -= OnCatalogChanged;
				}

				_workspace = workspace;
				if (workspace is not null)
				{
					workspace.Undo.Changed += OnUndoChanged;
					workspace.Catalog.Changed += OnCatalogChanged;
				}
			}

			RecentProjects = [.. _settings.Current.RecentProjects];
			OnUndoChanged(null, EventArgs.Empty);
		}

		private void OnCatalogChanged(Object? sender, CatalogChangedEventArgs e)
		{
			System.Windows.Application.Current.Dispatcher.BeginInvoke(UpdateStatus);
		}

		private void OnUndoChanged(Object? sender, EventArgs e)
		{
			System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
			{
				UndoCommand.NotifyCanExecuteChanged();
				RedoCommand.NotifyCanExecuteChanged();
				UndoTooltip = _workspace?.Undo.UndoDescription is { } undo ? Loc.Format("edit.undoTip", undo) : Loc.Get("edit.undo");
				RedoTooltip = _workspace?.Undo.RedoDescription is { } redo ? Loc.Format("edit.redoTip", redo) : Loc.Get("edit.redo");
				UpdateStatus();
			});
		}

		private void UpdateStatus()
		{
			GameWorkspace? workspace = _workspace;
			if (workspace is null)
			{
				Title = "ProtoForge";
				StatusText = Loc.Get("status.ready");
				ModifiedText = String.Empty;
				HasUnsavedChanges = false;
				return;
			}

			Int32 modified = workspace.Catalog.Modified.Count();
			HasUnsavedChanges = workspace.HasUnsavedChanges;
			Title = $"ProtoForge - {workspace.Layout.DisplayName}{(HasUnsavedChanges ? " *" : String.Empty)}";
			StatusText = Loc.Format("status.summary", workspace.Catalog.Count, workspace.Files.Layers.Count, workspace.OutputFolder ?? Loc.Get("status.noOutput"));
			ModifiedText = HasUnsavedChanges ? Loc.Format("status.modified", modified) : Loc.Get("status.saved");
		}
	}
}