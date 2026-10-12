namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.Linq;
	using System.Threading.Tasks;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using ProtoForge.Core.Formats.Msg;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Stats;
	using ProtoForge.Infrastructure.Layout;
	using ProtoForge.Infrastructure.Settings;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	public sealed partial class LayerItemViewModel : ObservableObject
	{
		[ObservableProperty]
		private Boolean _isEnabled;

		public LayerItemViewModel(LayerDefinition definition)
		{
			Definition = definition;
			_isEnabled = definition.IsEnabled;
		}

		public LayerDefinition Definition { get; }

		public String Name
		{
			get { return Definition.Name; }
		}

		public String Path
		{
			get { return Definition.Path; }
		}

		public String Kind
		{
			get { return Definition.Kind == LayerKind.DatArchive ? "DAT" : Loc.Get("layer.folder"); }
		}

		public String Role
		{
			get { return Loc.Get("layer.role." + Definition.Role); }
		}

		/// <summary>Called when the user ticks or unticks the layer (so that the "apply" button appears).</summary>
		public Action? Changed { get; set; }

		partial void OnIsEnabledChanged(Boolean value)
		{
			Definition.IsEnabled = value;
			Changed?.Invoke();
		}
	}

	public sealed partial class WorkingSetItem : ObservableObject
	{
		[ObservableProperty]
		private Boolean _isSelected;

		public WorkingSetItem(ProtoObjectType type, Boolean isSelected)
		{
			Type = type;
			_isSelected = isSelected;
		}

		public ProtoObjectType Type { get; }

		public String Label
		{
			get { return Loc.Get("type." + Type); }
		}
	}

	public sealed record CodePageOption(Int32? CodePage, String Label)
	{
		public override String ToString()
		{
			return Label;
		}
	}

	/// <summary>Application preferences and the list of data sources of the opened game.</summary>
	public sealed partial class SettingsViewModel : ObservableObject
	{
		private readonly SettingsService _settings;
		private readonly WorkspaceService _workspace;
		private readonly IDialogService _dialogs;
		private readonly INotifier _notifier;
		private Boolean _loading;

		[ObservableProperty]
		private String _theme;

		[ObservableProperty]
		private String _uiLanguage;

		[ObservableProperty]
		private Boolean _readOnlyAttribute;

		[ObservableProperty]
		private Boolean _backupBeforeSave;

		[ObservableProperty]
		private Boolean _confirmBeforeSave;

		[ObservableProperty]
		private Int32 _formulaIndex;

		[ObservableProperty]
		private String _csvDelimiter;

		[ObservableProperty]
		private String _hexEditorPath;

		[ObservableProperty]
		private CodePageOption? _selectedCodePage;

		[ObservableProperty]
		private String _baseGameFolder;

		[ObservableProperty]
		private String _outputFolder = String.Empty;

		[ObservableProperty]
		private String _gameLanguage = String.Empty;

		/// <summary>The language folders found in the game (<c>text/&lt;language&gt;/game</c>), to pick from or to type over.</summary>
		[ObservableProperty]
		private IReadOnlyList<String> _gameLanguages = [];

		[ObservableProperty]
		private ObservableCollection<LayerItemViewModel> _layers = [];

		[ObservableProperty]
		private LayerItemViewModel? _selectedLayer;

		[ObservableProperty]
		private Boolean _hasWorkspace;

		[ObservableProperty]
		private Boolean _layoutDirty;

		public SettingsViewModel(SettingsService settings, WorkspaceService workspace, IDialogService dialogs, INotifier notifier)
		{
			_settings = settings;
			_workspace = workspace;
			_dialogs = dialogs;
			_notifier = notifier;

			AppSettings s = settings.Current;
			_theme = s.Theme;
			_uiLanguage = s.UiLanguage;
			ThemeHelper.Applied += (_, name) => Theme = name;
			_readOnlyAttribute = s.ReadOnlyAttribute;
			_backupBeforeSave = s.BackupBeforeSave;
			_confirmBeforeSave = s.ConfirmBeforeSave;
			_formulaIndex = (Int32)s.Formula;
			_csvDelimiter = s.CsvDelimiter;
			_hexEditorPath = s.ExternalHexEditor;
			_baseGameFolder = s.BaseGameFolder ?? String.Empty;

			CodePages =
			[
				new CodePageOption(null, Loc.Get("settings.codePageAuto")),
				.. MsgEncodings.Common.Select(c => new CodePageOption(c.Key, c.Value)),
			];
			_selectedCodePage = CodePages.FirstOrDefault(c => c.CodePage == s.MessageCodePage) ?? CodePages[0];
			WorkingSet = [.. ProtoObjectTypeExtensions.All.Select(t => new WorkingSetItem(t, s.LoadedTypes.Contains(t)))];
			foreach (WorkingSetItem item in WorkingSet)
			{
				item.PropertyChanged += (_, _) => SaveWorkingSet();
			}

			LoadLayout(workspace.Current);
			workspace.WorkspaceChanged += (_, ws) => LoadLayout(ws);
		}

		public IReadOnlyList<String> Themes { get; } = ["Dark", "Light", "System"];

		public IReadOnlyList<KeyValuePair<String, String>> Languages { get; } =
		[
			new(String.Empty, Loc.Get("settings.languageSystem")),
			.. Loc.AvailableLanguages,
		];

		public IReadOnlyList<CodePageOption> CodePages { get; }

		public IReadOnlyList<WorkingSetItem> WorkingSet { get; }

		partial void OnThemeChanged(String value)
		{
			_settings.Current.Theme = value;
			_settings.Save();
			ThemeHelper.Apply(value);
		}

		partial void OnUiLanguageChanged(String value)
		{
			_settings.Current.UiLanguage = value;
			_settings.Save();
			if (!_loading)
			{
				_notifier.Info(Loc.Get("settings.title"), Loc.Get("settings.restartNeeded"));
			}
		}

		partial void OnReadOnlyAttributeChanged(Boolean value)
		{
			_settings.Current.ReadOnlyAttribute = value;
			_settings.Save();
			if (_workspace.Current is { } ws)
			{
				ws.Options.ReadOnlyAttribute = value;
			}
		}

		partial void OnBackupBeforeSaveChanged(Boolean value)
		{
			_settings.Current.BackupBeforeSave = value;
			_settings.Save();
			if (_workspace.Current is { } ws)
			{
				ws.Options.BackupBeforeSave = value;
			}
		}

		partial void OnConfirmBeforeSaveChanged(Boolean value)
		{
			_settings.Current.ConfirmBeforeSave = value;
			_settings.Save();
		}

		partial void OnFormulaIndexChanged(Int32 value)
		{
			_settings.Current.Formula = (FormulaSet)value;
			_settings.Save();
			if (_workspace.Current is { } ws)
			{
				ws.Options.Formula = (FormulaSet)value;
			}
		}

		partial void OnCsvDelimiterChanged(String value)
		{
			_settings.Current.CsvDelimiter = value.Length > 0 ? value[..1] : ";";
			_settings.Save();
		}

		partial void OnHexEditorPathChanged(String value)
		{
			_settings.Current.ExternalHexEditor = value.Trim();
			_settings.Save();
		}

		[RelayCommand]
		private void BrowseHexEditor()
		{
			String? path = _dialogs.PickOpenFile(Loc.Get("hex.pickEditor"), "*.exe|*.exe|*.*|*.*");
			if (path is not null)
			{
				HexEditorPath = path;
			}
		}

		partial void OnSelectedCodePageChanged(CodePageOption? value)
		{
			_settings.Current.MessageCodePage = value?.CodePage;
			_settings.Save();
			LayoutDirty = LayoutDirty || !_loading;
		}

		partial void OnBaseGameFolderChanged(String value)
		{
			_settings.Current.BaseGameFolder = value.Length > 0 ? value : null;
			_settings.Save();
		}

		partial void OnOutputFolderChanged(String value)
		{
			LayoutDirty = LayoutDirty || !_loading;
		}

		partial void OnGameLanguageChanged(String value)
		{
			LayoutDirty = LayoutDirty || !_loading;
		}

		[RelayCommand]
		private void BrowseBaseGame()
		{
			String? folder = _dialogs.PickFolder(Loc.Get("open.gameFolder"), BaseGameFolder);
			if (folder is not null)
			{
				BaseGameFolder = folder;
			}
		}

		[RelayCommand]
		private void BrowseOutput()
		{
			String? folder = _dialogs.PickFolder(Loc.Get("open.chooseOutput"), OutputFolder);
			if (folder is not null)
			{
				OutputFolder = folder;
			}
		}

		[RelayCommand]
		private void AddFolderLayer()
		{
			String? folder = _dialogs.PickFolder(Loc.Get("layer.addFolder"));
			if (folder is not null)
			{
				AddLayer(new LayerDefinition { Path = folder, Kind = LayerKind.Directory, Name = new System.IO.DirectoryInfo(folder).Name, Role = LayerRole.Mod });
			}
		}

		[RelayCommand]
		private void AddDatLayer()
		{
			String? file = _dialogs.PickOpenFile(Loc.Get("layer.addDat"), "DAT (*.dat)|*.dat");
			if (file is not null)
			{
				AddLayer(new LayerDefinition { Path = file, Kind = LayerKind.DatArchive, Name = System.IO.Path.GetFileName(file), Role = LayerRole.Mod });
			}
		}

		[RelayCommand]
		private void MoveLayerUp(LayerItemViewModel? layer)
		{
			Move(layer, -1);
		}

		[RelayCommand]
		private void MoveLayerDown(LayerItemViewModel? layer)
		{
			Move(layer, 1);
		}

		[RelayCommand]
		private void RemoveLayer(LayerItemViewModel? layer)
		{
			if (layer is not null && Layers.Remove(layer))
			{
				LayoutDirty = true;
			}
		}

		[RelayCommand]
		private async Task ApplyLayout()
		{
			GameWorkspace? workspace = _workspace.Current;
			if (workspace is null)
			{
				return;
			}

			GameLayout layout = workspace.Layout.Clone();
			layout.Layers = [.. Layers.Select(l => l.Definition)];
			layout.OutputFolder = String.IsNullOrWhiteSpace(OutputFolder) ? null : OutputFolder;
			layout.Language = GameLanguage.Length > 0 ? GameLanguage : "english";
			if (await _workspace.OpenAsync(layout))
			{
				LayoutDirty = false;
			}
		}

		private void AddLayer(LayerDefinition definition)
		{
			Layers.Insert(0, new LayerItemViewModel(definition) { Changed = MarkLayoutDirty });
			LayoutDirty = true;
		}

		private void Move(LayerItemViewModel? layer, Int32 delta)
		{
			if (layer is null)
			{
				return;
			}

			Int32 index = Layers.IndexOf(layer);
			Int32 target = index + delta;
			if (index >= 0 && target >= 0 && target < Layers.Count)
			{
				Layers.Move(index, target);
				SelectedLayer = layer;
				LayoutDirty = true;
			}
		}

		private void SaveWorkingSet()
		{
			_settings.Current.LoadedTypes = [.. WorkingSet.Where(w => w.IsSelected).Select(w => w.Type)];
			_settings.Save();
			LayoutDirty = LayoutDirty || !_loading;
		}

		private void MarkLayoutDirty()
		{
			if (!_loading)
			{
				LayoutDirty = true;
			}
		}

		private GameWorkspace? _layoutWorkspace;

		private void LoadLayout(GameWorkspace? workspace)
		{
			if (ReferenceEquals(workspace, _layoutWorkspace) && workspace is not null && LayoutDirty)
			{
				// Saving raises the same notification as opening a game: unapplied edits of the sources must survive it.
				return;
			}

			_layoutWorkspace = workspace;
			_loading = true;
			try
			{
				HasWorkspace = workspace is not null;
				Layers = workspace is null ? [] : new ObservableCollection<LayerItemViewModel>(workspace.Layout.Layers.Select(l => new LayerItemViewModel(l.Clone()) { Changed = MarkLayoutDirty }));
				OutputFolder = workspace?.Layout.OutputFolder ?? String.Empty;
				GameLanguages = workspace is null
					? []
					: [.. workspace.Files.EnumerateDirectories("text").Where(d => workspace.Files.Enumerate($"text/{d}/game").Count > 0)];
				GameLanguage = workspace?.Layout.Language ?? String.Empty;
				LayoutDirty = false;
			}
			finally
			{
				_loading = false;
			}
		}
	}
}