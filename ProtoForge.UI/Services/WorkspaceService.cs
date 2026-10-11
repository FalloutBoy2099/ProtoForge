namespace ProtoForge.UI.Services
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;
	using CommunityToolkit.Mvvm.ComponentModel;
	using ProtoForge.Infrastructure.Layout;
	using ProtoForge.Infrastructure.Settings;
	using ProtoForge.Infrastructure.Workspace;
	using Views;

	/// <summary>
	/// Owns the currently opened game/mod and the operations that cross view boundaries: open, save, close with confirmation.
	/// </summary>
	public sealed partial class WorkspaceService : ObservableObject
	{
		private readonly SettingsService _settings;
		private readonly INotifier _notifier;
		private readonly IDialogService _dialogs;
		private readonly ArtImageService _art;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(HasWorkspace))]
		private GameWorkspace? _current;

		[ObservableProperty]
		private Boolean _isBusy;

		[ObservableProperty]
		private String _busyText = String.Empty;

		[ObservableProperty]
		private Double _busyProgress;

		public WorkspaceService(SettingsService settings, INotifier notifier, IDialogService dialogs, ArtImageService art)
		{
			_settings = settings;
			_notifier = notifier;
			_dialogs = dialogs;
			_art = art;
		}

		public event EventHandler<GameWorkspace?>? WorkspaceChanged;

		public Boolean HasWorkspace
		{
			get { return Current is not null; }
		}

		public SettingsService Settings
		{
			get { return _settings; }
		}

		public WorkspaceOptions CreateOptions()
		{
			AppSettings s = _settings.Current;
			return new WorkspaceOptions
			{
				LoadedTypes = [.. s.LoadedTypes],
				MessageCodePage = s.MessageCodePage,
				ReadOnlyAttribute = s.ReadOnlyAttribute,
				BackupBeforeSave = s.BackupBeforeSave,
				Formula = s.Formula,
			};
		}

		/// <summary>Opens a game folder, a mod folder or a DAT archive, whichever the path is.</summary>
		public async Task<Boolean> OpenPathAsync(String path, String? outputFolder = null)
		{
			switch (GameLayoutDiscovery.Classify(path))
			{
				case OpenTargetKind.GameFolder:
					DiscoveryResult game = GameLayoutDiscovery.FromGameFolder(path);
					if (outputFolder is not null)
					{
						game.Layout.OutputFolder = outputFolder;
					}

					return await OpenAsync(game.Layout, game.Messages);
				case OpenTargetKind.ModFolder:
					DiscoveryResult mod = GameLayoutDiscovery.FromModFolder(path, LoadBaseGame());
					if (outputFolder is not null)
					{
						mod.Layout.OutputFolder = outputFolder;
					}

					return await OpenAsync(mod.Layout, mod.Messages);
				case OpenTargetKind.DatArchive:
					DiscoveryResult dat = GameLayoutDiscovery.FromDatArchive(path, LoadBaseGame());
					String? output = outputFolder ?? _dialogs.PickFolder(Loc.Get("open.chooseOutput"), System.IO.Path.GetDirectoryName(path));
					if (output is null)
					{
						return false;
					}

					dat.Layout.OutputFolder = output;
					return await OpenAsync(dat.Layout, dat.Messages);
				default:
					_notifier.Warning(Loc.Get("open.title"), Loc.Format("open.unknownTarget", path));
					return false;
			}
		}

		public async Task<Boolean> OpenAsync(GameLayout layout, IEnumerable<String>? messages = null)
		{
			if (!await ConfirmCloseAsync())
			{
				return false;
			}

			IsBusy = true;
			BusyProgress = 0;
			BusyText = Loc.Get("busy.opening");
			try
			{
				Progress<LoadProgress> progress = new(p =>
				{
					BusyText = p.Stage;
					BusyProgress = p.Fraction;
				});

				GameWorkspace workspace = await GameWorkspace.OpenAsync(layout, CreateOptions(), progress);
				Install(workspace);
				_settings.RememberProject(layout);

				foreach (String message in (messages ?? []).Concat(workspace.Warnings).Take(3))
				{
					_notifier.Warning(Loc.Get("open.title"), message);
				}

				_notifier.Success(workspace.Layout.DisplayName, Loc.Format("open.loaded", workspace.Catalog.Count, workspace.Files.Layers.Count));
				return true;
			}
			catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
			{
				_notifier.Error(Loc.Get("open.failed"), ex.Message);
				return false;
			}
			finally
			{
				IsBusy = false;
			}
		}

		/// <summary>Reloads the current layout from disk (discarding nothing: the caller already confirmed).</summary>
		public async Task ReloadAsync()
		{
			if (Current is null)
			{
				return;
			}

			GameLayout layout = Current.Layout.Clone();
			await OpenAsync(layout);
		}

		/// <summary>Asks what to do with unsaved changes. Returns false when the user cancels.</summary>
		public async Task<Boolean> ConfirmCloseAsync()
		{
			GameWorkspace? workspace = Current;
			if (workspace is null || !workspace.HasUnsavedChanges)
			{
				return true;
			}

			DialogChoice choice = await _dialogs.AskAsync(
				Loc.Get("save.unsavedTitle"),
				Loc.Get("save.unsavedMessage"),
				Loc.Get("save.saveAndContinue"),
				Loc.Get("save.discard"),
				Loc.Get("common.cancel"));

			return choice switch
			{
				DialogChoice.Primary => await SaveAsync(),
				DialogChoice.Secondary => true,
				_ => false,
			};
		}

		/// <summary>Shows what will be written, asks for confirmation (unless disabled) and saves.</summary>
		public async Task<Boolean> SaveAsync()
		{
			GameWorkspace? workspace = Current;
			if (workspace is null)
			{
				return false;
			}

			if (!workspace.CanSave)
			{
				String? folder = _dialogs.PickFolder(Loc.Get("open.chooseOutput"));
				if (folder is null)
				{
					return false;
				}

				workspace.Layout.OutputFolder = folder;
			}

			SavePlan plan = workspace.CreateSavePlan();
			if (plan.IsEmpty)
			{
				_notifier.Info(Loc.Get("save.title"), Loc.Get("save.nothing"));
				return true;
			}

			if (_settings.Current.ConfirmBeforeSave)
			{
				SavePlanView view = new(plan, workspace.Options.BackupBeforeSave);
				if (!await _dialogs.ShowAsync(Loc.Get("save.confirmTitle"), view, Loc.Get("save.confirmButton"), Loc.Get("common.cancel")))
				{
					return false;
				}
			}

			IsBusy = true;
			BusyText = Loc.Get("busy.saving");
			BusyProgress = 0;
			try
			{
				Progress<Double> progress = new(p => BusyProgress = p);
				SaveResult result = await Task.Run(() => workspace.Save(plan, progress));
				_art.Attach(workspace);
				if (result.Succeeded)
				{
					_notifier.Success(Loc.Get("save.title"), Loc.Format("save.done", result.FilesWritten));
					WorkspaceChanged?.Invoke(this, workspace);
					return true;
				}

				_notifier.Error(Loc.Get("save.failed"), String.Join(Environment.NewLine, result.Errors.Take(3)));
				return false;
			}
			finally
			{
				IsBusy = false;
			}
		}

		public async Task<Boolean> CloseAsync()
		{
			if (!await ConfirmCloseAsync())
			{
				return false;
			}

			Install(null);
			return true;
		}

		private GameLayout? LoadBaseGame()
		{
			String? folder = _settings.Current.BaseGameFolder;
			return !String.IsNullOrEmpty(folder) && GameLayoutDiscovery.Classify(folder) == OpenTargetKind.GameFolder
				? GameLayoutDiscovery.FromGameFolder(folder).Layout
				: null;
		}

		private void Install(GameWorkspace? workspace)
		{
			GameWorkspace? previous = Current;
			Current = workspace;
			_art.Attach(workspace);
			WorkspaceChanged?.Invoke(this, workspace);
			previous?.Dispose();
		}
	}
}