namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using System.Text.RegularExpressions;
	using System.Threading;
	using System.Threading.Tasks;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Editing;
	using ProtoForge.Core.Formats.Frm;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Reference;
	using ProtoForge.Core.Templates;
	using ProtoForge.Infrastructure.Plugins;
	using ProtoForge.Infrastructure.Settings;
	using ProtoForge.Infrastructure.Templates;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	public sealed record ExportScope(String Id, String Label)
	{
		public override String ToString()
		{
			return Label;
		}
	}

	/// <summary>A kind of art (inventory images, map objects, walls...) offered by the art tools.</summary>
	public sealed record ArtKindOption(ArtType? Type, String Label)
	{
		public override String ToString()
		{
			return Label;
		}
	}

	/// <summary>Import/export, bulk creation of walls and tiles, art tools, user templates and plug-ins.</summary>
	public sealed partial class ToolsViewModel : ObservableObject
	{
		private static readonly Regex ArtName = new("^[a-z0-9_\\-]{1,24}\\.frm$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private CancellationTokenSource? _extractCancellation;

		private readonly WorkspaceService _workspaceService;
		private readonly ExchangeService _exchange;
		private readonly SettingsService _settings;
		private readonly UserTemplateStore _templates;
		private readonly IDialogService _dialogs;
		private readonly INotifier _notifier;
		private readonly ShellNavigator _shell;

		[ObservableProperty]
		private ExportScope? _selectedScope;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(IsTileMode))]
		private Int32 _massKindIndex;

		[ObservableProperty]
		private Double? _massArtIndex = 1;

		[ObservableProperty]
		private Double? _massCount = 10;

		[ObservableProperty]
		private Int32 _massMaterial = 5;

		/// <summary>0 = add the prototypes to the loaded game, 1 = write standalone .pro files and a list into a folder.</summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(IsFolderMode))]
		private Int32 _massTargetIndex;

		[ObservableProperty]
		private Double? _massStartNumber = 1;

		/// <summary>Tiles only: also create the "end of group" tile that closes a tile sequence.</summary>
		[ObservableProperty]
		private Boolean _massEndOfGroup;


		[ObservableProperty]
		private IReadOnlyList<ProtoTemplate> _userTemplates = [];

		[ObservableProperty]
		private ProtoTemplate? _selectedTemplate;

		[ObservableProperty]
		private IReadOnlyList<String> _plugins = [];

		[ObservableProperty]
		private ArtKindOption? _selectedAddArtKind;

		[ObservableProperty]
		private ArtKindOption? _selectedExtractKind;

		[ObservableProperty]
		private Boolean _isExtracting;

		[ObservableProperty]
		private Double _extractProgress;

		[ObservableProperty]
		private String _extractStatus = String.Empty;

		public ToolsViewModel(WorkspaceService workspaceService, ExchangeService exchange, SettingsService settings, UserTemplateStore templates, IDialogService dialogs, INotifier notifier, ShellNavigator shell)
		{
			_shell = shell;
			_workspaceService = workspaceService;
			_exchange = exchange;
			_settings = settings;
			_templates = templates;
			_dialogs = dialogs;
			_notifier = notifier;

			Scopes =
			[
				new ExportScope("all", Loc.Get("tools.scopeAll")),
				new ExportScope("modified", Loc.Get("tools.scopeModified")),
				.. ProtoObjectTypeExtensions.All.Select(t => new ExportScope(t.ToString(), Loc.Get("type." + t))),
			];
			_selectedScope = Scopes[0];
			_replaceScope = ReplaceScopes[0];
			Materials = new ChoiceCatalog().Get(ChoiceCatalog.Material);
			AddArtKinds =
			[
				new ArtKindOption(ArtType.Inventory, Loc.Get("art.inventory")),
				new ArtKindOption(ArtType.Item, Loc.Get("art.ground")),
				new ArtKindOption(ArtType.Scenery, Loc.Get("art.scenery")),
				new ArtKindOption(ArtType.Wall, Loc.Get("art.walls")),
				new ArtKindOption(ArtType.Tile, Loc.Get("art.tiles")),
				new ArtKindOption(ArtType.Misc, Loc.Get("art.misc")),
			];
			ExtractKinds =
			[
				.. AddArtKinds,
				new ArtKindOption(ArtType.Critter, Loc.Get("art.critters")),
				new ArtKindOption(ArtType.Heads, Loc.Get("art.heads")),
				new ArtKindOption(null, Loc.Get("art.all")),
			];
			_selectedAddArtKind = AddArtKinds[0];
			_selectedExtractKind = ExtractKinds[0];
			_workspaceService.WorkspaceChanged += (_, _) => Refresh();
			Refresh();
		}

		public WorkspaceService Workspace
		{
			get { return _workspaceService; }
		}

		public IReadOnlyList<ExportScope> Scopes { get; }

		public IReadOnlyList<ChoiceOption> Materials { get; }

		public Boolean IsFolderMode
		{
			get { return MassTargetIndex == 1; }
		}

		public Boolean IsTileMode
		{
			get { return MassKindIndex == 1; }
		}

		public IReadOnlyList<ArtKindOption> AddArtKinds { get; }

		public IReadOnlyList<ArtKindOption> ExtractKinds { get; }

		public String PluginFolder
		{
			get { return PluginLoader.GetPluginFolders().Last(); }
		}

		[RelayCommand]
		private void ExportJson()
		{
			_exchange.ExportJson(GetScopeEntries());
		}

		[RelayCommand]
		private async Task ExportCsv()
		{
			String delimiter = _settings.Current.CsvDelimiter;
			await _exchange.ExportCsvAsync(GetScopeEntries(), delimiter.Length > 0 ? delimiter[0] : ';');
		}

		[RelayCommand]
		private async Task Import()
		{
			String delimiter = _settings.Current.CsvDelimiter;
			await _exchange.ImportAsync(delimiter.Length > 0 ? delimiter[0] : ';');
		}

		[RelayCommand]
		private async Task MassCreate()
		{
			if (MassCount is not { } count || MassArtIndex is not { } art)
			{
				return;
			}

			Boolean wall = MassKindIndex == 0;
			ProtoKind kind = wall ? ProtoKind.Wall : ProtoKind.Tile;
			Int32 total = (Int32)count;
			if (total < 1 || total > 5000)
			{
				_notifier.Warning(Loc.Get("tools.mass"), Loc.Get("tools.massInvalid"));
				return;
			}

			if (IsFolderMode)
			{
				await CreateInFolderAsync(kind, (Int32)art, total);
			}
			else
			{
				await CreateInGameAsync(kind, (Int32)art, total);
			}
		}

		private ProtoFile CreateMassFile(ProtoKind kind, Int32 index, Int32 artIndex)
		{
			ProtoObjectType type = kind.GetObjectType();
			ProtoFile file = BuiltInTemplates.Default(kind).Create(ProtoId.From(type, index));
			file.TrySet(FieldKeys.FrmId, FrameId.Create(type == ProtoObjectType.Wall ? ArtType.Wall : ArtType.Tile, artIndex).Value);
			file.TrySet(FieldKeys.Material, MassMaterial);
			return file;
		}

		/// <summary>The art index of the tile that closes a sequence of tiles ("end of group") in the original game.</summary>
		private const Int32 EndOfGroupTileArt = 20;

		private async Task CreateInGameAsync(ProtoKind kind, Int32 art, Int32 total)
		{
			GameWorkspace? workspace = _workspaceService.Current;
			ProtoObjectType type = kind.GetObjectType();
			if (workspace is null || !workspace.Catalog.IsLoaded(type))
			{
				_notifier.Warning(Loc.Get("tools.mass"), Loc.Get("tools.massInvalid"));
				return;
			}

			Boolean endOfGroup = kind == ProtoKind.Tile && MassEndOfGroup;
			Int32 created = total + (endOfGroup ? 1 : 0);
			if (!await _dialogs.ConfirmAsync(Loc.Get("tools.mass"), Loc.Format("tools.massConfirm", created, Loc.Get("type." + type)), Loc.Get("tools.massCreate")))
			{
				return;
			}

			Int32 first = workspace.Catalog.NextIndex(type);
			List<IUndoableAction> actions = [];
			for (Int32 i = 0; i < created; i++)
			{
				Int32 index = first + i;
				ProtoFile file = CreateMassFile(kind, index, endOfGroup && i == created - 1 ? EndOfGroupTileArt : art + i);
				actions.Add(new AddProtoAction(workspace.Catalog, workspace.Texts, type, file, ProtoDuplicator.FormatFileName(index), workspace.OutputSourceName, String.Empty, String.Empty, ProtoForge.Core.UserTexts.Format("act.createShort", "Create")));
			}

			using (workspace.Catalog.SuspendNotifications())
			{
				workspace.Undo.Execute(new CompositeAction(actions, Loc.Format("tools.massUndo", created)));
			}

			_notifier.Success(Loc.Get("tools.mass"), Loc.Format("tools.massDone", created, new ProtoId(first)));
		}

		/// <summary>
		/// Writes standalone prototype files (numbered from the start number) and their list into a folder, like the old Proto Manager:
		/// the names are appended to <c>Tiles.lst</c> / <c>Walls.lst</c> when the folder already has one and you agree, else <c>Create.lst</c> is written.
		/// </summary>
		private async Task CreateInFolderAsync(ProtoKind kind, Int32 art, Int32 total)
		{
			if (MassStartNumber is not { } startNumber || startNumber < 1 || startNumber > 99999999)
			{
				_notifier.Warning(Loc.Get("tools.mass"), Loc.Get("tools.massInvalid"));
				return;
			}

			String? folder = _dialogs.PickFolder(Loc.Get("tools.massFolder"));
			if (folder is null)
			{
				return;
			}

			Boolean endOfGroup = kind == ProtoKind.Tile && MassEndOfGroup;
			Int32 created = total + (endOfGroup ? 1 : 0);
			Int32 start = (Int32)startNumber;
			List<String> names = [];
			for (Int32 i = 0; i < created; i++)
			{
				names.Add(ProtoDuplicator.FormatFileName(start + i));
			}

			Int32 existing = names.Count(n => File.Exists(Path.Combine(folder, n)));
			if (existing > 0 && !await _dialogs.ConfirmAsync(Loc.Get("tools.mass"), Loc.Format("art.overwriteConfirm", existing, folder), Loc.Get("art.overwrite")))
			{
				return;
			}

			try
			{
				for (Int32 i = 0; i < created; i++)
				{
					ProtoFile file = CreateMassFile(kind, start + i, endOfGroup && i == created - 1 ? EndOfGroupTileArt : art + i);
					File.WriteAllBytes(Path.Combine(folder, names[i]), file.ToArray());
				}

				String listName = kind == ProtoKind.Wall ? "Walls.lst" : "Tiles.lst";
				String listPath = Path.Combine(folder, listName);
				if (File.Exists(listPath) && await _dialogs.ConfirmAsync(Loc.Get("tools.mass"), Loc.Format("tools.massAppend", listName), Loc.Get("tools.massAppendButton")))
				{
					List<String> lines = [.. File.ReadAllLines(listPath)];
					while (lines.Count > 0 && lines[^1].Trim().Length == 0)
					{
						lines.RemoveAt(lines.Count - 1);
					}

					lines.AddRange(names);
					File.WriteAllLines(listPath, lines);
				}
				else
				{
					File.WriteAllLines(Path.Combine(folder, "Create.lst"), names);
				}

				_notifier.Success(Loc.Get("tools.mass"), Loc.Format("tools.massFolderDone", created, folder));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				_notifier.Error(Loc.Get("tools.mass"), ProtoForge.Core.UserTexts.Describe(ex));
			}
		}

		/// <summary>Turns a BMP into a FRM file (the old bmp2frm tool): colours are matched to the palette of the game.</summary>
		[RelayCommand]
		private void ConvertBmpToFrm()
		{
			String? source = _dialogs.PickOpenFile(Loc.Get("art.pickBmp"), "BMP (*.bmp)|*.bmp");
			if (source is null)
			{
				return;
			}

			String? target = _dialogs.PickSaveFile(Loc.Get("art.saveFrm"), "FRM (*.frm)|*.frm", Path.GetFileNameWithoutExtension(source) + ".frm");
			if (target is null)
			{
				return;
			}

			try
			{
				Byte[] frm = BmpToFrm(File.ReadAllBytes(source));
				File.WriteAllBytes(target, frm);
				_notifier.Success(Loc.Get("art.convert"), Loc.Format("art.converted", Path.GetFileName(target)));
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
			{
				_notifier.Error(Loc.Get("art.convert"), ProtoForge.Core.UserTexts.Describe(ex));
			}
		}

		private Byte[] BmpToFrm(Byte[] bmp)
		{
			return ArtImport.ToFrm("image.bmp", bmp, _workspaceService.Current?.Palette ?? Palette.Default);
		}

		/// <summary>
		/// Adds an image (a FRM, or a BMP that is converted) to the art of the game: it is copied to <c>art/&lt;folder&gt;</c> and its name is
		/// appended to the list of the folder. Nothing is written before the next save.
		/// </summary>
		[RelayCommand]
		private async Task AddArt()
		{
			GameWorkspace? workspace = _workspaceService.Current;
			if (workspace is null || SelectedAddArtKind?.Type is not { } type)
			{
				return;
			}

			String? source = _dialogs.PickOpenFile(Loc.Get("art.pickImage"), "FRM / BMP (*.frm;*.bmp)|*.frm;*.bmp");
			if (source is null)
			{
				return;
			}

			Byte[] frm;
			try
			{
				Byte[] data = File.ReadAllBytes(source);
				if (Path.GetExtension(source).Equals(".bmp", StringComparison.OrdinalIgnoreCase))
				{
					frm = BmpToFrm(data);
				}
				else
				{
					FrmFile.Parse(data);
					frm = data;
				}
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
			{
				_notifier.Error(Loc.Get("art.add"), ProtoForge.Core.UserTexts.Describe(ex));
				return;
			}

			String suggested = Path.GetFileNameWithoutExtension(source).ToLowerInvariant() + ".frm";
			String? name = await _dialogs.PromptAsync(Loc.Get("art.add"), Loc.Get("art.fileName"), suggested);
			if (String.IsNullOrWhiteSpace(name))
			{
				return;
			}

			name = name.Trim().ToLowerInvariant();
			if (!name.EndsWith(".frm", StringComparison.Ordinal))
			{
				name += ".frm";
			}

			if (!ArtName.IsMatch(name))
			{
				_notifier.Warning(Loc.Get("art.add"), Loc.Get("art.nameInvalid"));
				return;
			}

			if (workspace.ArtExists(type, name) && !await _dialogs.ConfirmAsync(Loc.Get("art.add"), Loc.Format("art.replaceConfirm", name), Loc.Get("art.replace")))
			{
				return;
			}

			try
			{
				Int32 index = workspace.AddArt(type, name, frm);
				_notifier.Success(Loc.Get("art.add"), Loc.Format("art.added", name, index, FrameId.GetFolderName(type)));
			}
			catch (InvalidOperationException ex)
			{
				_notifier.Error(Loc.Get("art.add"), ProtoForge.Core.UserTexts.Describe(ex));
			}
		}

		/// <summary>Copies the art of the game (from the DAT archives and the mods) to a folder, with a progress bar.</summary>
		[RelayCommand]
		private async Task ExtractArt()
		{
			GameWorkspace? workspace = _workspaceService.Current;
			if (workspace is null || SelectedExtractKind is null)
			{
				return;
			}

			String? destination = _dialogs.PickFolder(Loc.Get("art.pickDestination"));
			if (destination is null)
			{
				return;
			}

			ArtType[] types = SelectedExtractKind.Type is { } one ? [one] : [.. Enum.GetValues<ArtType>()];
			List<String> paths = [];
			foreach (ArtType type in types)
			{
				paths.AddRange(workspace.Files.Enumerate($"art/{FrameId.GetFolderName(type)}"));
			}

			if (paths.Count == 0)
			{
				_notifier.Info(Loc.Get("art.extract"), Loc.Get("art.extractNothing"));
				return;
			}

			Int32 existing = paths.Count(p => File.Exists(ToDiskPath(destination, p)));
			if (existing > 0 && !await _dialogs.ConfirmAsync(Loc.Get("art.extract"), Loc.Format("art.overwriteConfirm", existing, destination), Loc.Get("art.overwrite")))
			{
				return;
			}

			_extractCancellation = new CancellationTokenSource();
			CancellationToken token = _extractCancellation.Token;
			IsExtracting = true;
			ExtractProgress = 0;
			Int32 written = 0;
			try
			{
				await Task.Run(() =>
				{
					for (Int32 i = 0; i < paths.Count; i++)
					{
						token.ThrowIfCancellationRequested();
						Byte[]? bytes = workspace.Files.ReadAllBytes(paths[i]);
						if (bytes is not null)
						{
							String target = ToDiskPath(destination, paths[i]);
							Directory.CreateDirectory(Path.GetDirectoryName(target)!);
							File.WriteAllBytes(target, bytes);
							written++;
						}

						if (i % 25 == 0 || i == paths.Count - 1)
						{
							Double fraction = (Double)(i + 1) / paths.Count;
							String status = Loc.Format("art.extractProgress", i + 1, paths.Count);
							System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
							{
								ExtractProgress = fraction;
								ExtractStatus = status;
							});
						}
					}
				}, token);
				_notifier.Success(Loc.Get("art.extract"), Loc.Format("art.extractDone", written, destination));
			}
			catch (OperationCanceledException)
			{
				_notifier.Info(Loc.Get("art.extract"), Loc.Format("art.extractCancelled", written));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				_notifier.Error(Loc.Get("art.extract"), ProtoForge.Core.UserTexts.Describe(ex));
			}
			finally
			{
				IsExtracting = false;
				ExtractStatus = String.Empty;
				_extractCancellation?.Dispose();
				_extractCancellation = null;
			}
		}

		[RelayCommand]
		private void CancelExtract()
		{
			_extractCancellation?.Cancel();
		}

		private static String ToDiskPath(String root, String virtualPath)
		{
			return Path.Combine(root, virtualPath.Replace('/', Path.DirectorySeparatorChar));
		}

		private Boolean HasTemplateSelected()
		{
			return SelectedTemplate is not null;
		}

		partial void OnSelectedTemplateChanged(ProtoTemplate? value)
		{
			DeleteTemplateCommand.NotifyCanExecuteChanged();
		}

		[RelayCommand(CanExecute = nameof(HasTemplateSelected))]
		private void DeleteTemplate()
		{
			if (SelectedTemplate is { } template)
			{
				_templates.Delete(template.DisplayName);
				Refresh();
				_shell.NotifyTemplatesChanged();
			}
		}

		[RelayCommand]
		private void OpenTemplateFolder()
		{
			OpenFolder(Path.Combine(SettingsService.GetDataFolder(), "templates"));
		}

		[RelayCommand]
		private void OpenPluginFolder()
		{
			OpenFolder(PluginFolder);
		}

		[RelayCommand]
		private void OpenDataFolder()
		{
			OpenFolder(SettingsService.GetDataFolder());
		}

		private IReadOnlyCollection<ProtoEntry> GetScopeEntries()
		{
			GameWorkspace? workspace = _workspaceService.Current;
			if (workspace is null)
			{
				return [];
			}

			String scope = SelectedScope?.Id ?? "all";
			return scope switch
			{
				"all" => [.. workspace.Catalog.All.Where(e => e.File is not null)],
				"modified" => [.. workspace.Catalog.Modified.Where(e => e.File is not null)],
				_ => [.. workspace.Catalog.All.Where(e => e.File is not null && e.Type.ToString() == scope)],
			};
		}

		private void Refresh()
		{
			UserTemplates = _templates.Load();
			Plugins = _workspaceService.Current is { } workspace
				? [.. workspace.ValidationEngine.Rules.Select(r => r.Id)]
				: [];
		}

		private void OpenFolder(String folder)
		{
			try
			{
				Directory.CreateDirectory(folder);
				Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
			{
				_notifier.Error(Loc.Get("tools.title"), ProtoForge.Core.UserTexts.Describe(ex));
			}
		}
	}
}