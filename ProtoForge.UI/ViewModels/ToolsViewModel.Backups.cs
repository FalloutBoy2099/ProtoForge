namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Threading.Tasks;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	/// <summary>A backup of the output folder, as the list shows it.</summary>
	public sealed record BackupItem(BackupInfo Info, String Label)
	{
		public override String ToString()
		{
			return Label;
		}
	}

	/// <summary>The backups that saves make of the files they replace, and putting one back.</summary>
	public sealed partial class ToolsViewModel
	{
		[ObservableProperty]
		private IReadOnlyList<BackupItem> _backups = [];

		[ObservableProperty]
		private BackupItem? _selectedBackup;

		public Boolean HasBackups
		{
			get { return Backups.Count > 0; }
		}

		private void RefreshBackups()
		{
			String? output = _workspaceService.Current?.OutputFolder;
			try
			{
				Backups = String.IsNullOrEmpty(output)
					? []
					: [.. BackupManager.List(output).Select(b => new BackupItem(b, Loc.Format("backup.item", b.Time.ToString("G"), b.Entries.Count)))];
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				Backups = [];
			}

			SelectedBackup = null;
			OnPropertyChanged(nameof(HasBackups));
		}

		private Boolean HasBackupSelected()
		{
			return SelectedBackup is not null;
		}

		partial void OnSelectedBackupChanged(BackupItem? value)
		{
			RestoreBackupCommand.NotifyCanExecuteChanged();
		}

		[RelayCommand(CanExecute = nameof(HasBackupSelected))]
		private async Task RestoreBackup()
		{
			GameWorkspace? workspace = _workspaceService.Current;
			if (workspace?.OutputFolder is not { Length: > 0 } output || SelectedBackup is not { } selected)
			{
				return;
			}

			String message = Loc.Format("backup.restoreConfirm", selected.Info.Time.ToString("G"), selected.Info.Entries.Count);
			if (workspace.HasUnsavedChanges)
			{
				message += Environment.NewLine + Environment.NewLine + Loc.Get("backup.restoreUnsaved");
			}

			if (!await _dialogs.ConfirmAsync(Loc.Get("backup.title"), message, Loc.Get("backup.restore")))
			{
				return;
			}

			RestoreResult result = await Task.Run(() => BackupManager.Restore(output, selected.Info));
			if (result.Succeeded)
			{
				_notifier.Success(Loc.Get("backup.title"), Loc.Format("backup.restored", result.Restored, result.Removed));
			}
			else
			{
				_notifier.Error(Loc.Get("backup.failed"), String.Join(Environment.NewLine, result.Errors.Take(3)));
			}

			// What is on disk changed under the loaded game: read it again.
			await _workspaceService.ReloadAsync();
		}

		[RelayCommand]
		private void OpenBackupFolder()
		{
			if (_workspaceService.Current?.OutputFolder is { Length: > 0 } output)
			{
				OpenFolder(Path.Combine(output, BackupManager.FolderName));
			}
		}
	}
}