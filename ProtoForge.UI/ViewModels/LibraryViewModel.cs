namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.ComponentModel;
	using System.Linq;
	using System.Threading.Tasks;
	using System.Windows;
	using System.Windows.Data;
	using System.Windows.Threading;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using CommunityToolkit.Mvvm.Messaging;
	using CommunityToolkit.Mvvm.Messaging.Messages;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Frm;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Templates;
	using ProtoForge.Core.Validation;
	using ProtoForge.Infrastructure.Settings;
	using ProtoForge.Infrastructure.Templates;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	public sealed class SearchChangedMessage : ValueChangedMessage<String>
	{
		public SearchChangedMessage(String value)
			: base(value)
		{
		}
	}

	/// <summary>A chip of the type filter bar (All, Items, Critters...).</summary>
	public sealed partial class TypeFilterItem : ObservableObject
	{
		[ObservableProperty]
		private Boolean _isSelected;

		[ObservableProperty]
		private Int32 _count;

		public TypeFilterItem(ProtoObjectType? type)
		{
			Type = type;
		}

		public ProtoObjectType? Type { get; }

		public String Label
		{
			get { return Type is null ? Loc.Get("type.all") : Loc.Get("type." + Type); }
		}
	}

	public sealed record FlagFilterOption(String Name, UInt32 Mask, Boolean IsExtended)
	{
		public override String ToString()
		{
			return Loc.Flag(Name);
		}
	}

	public sealed record TemplateGroup(String Category, IReadOnlyList<ProtoTemplate> Templates);

	/// <summary>
	/// The prototype browser: a virtualised list with instant search and filters, plus the editor of the selection.
	/// </summary>
	public sealed partial class LibraryViewModel : ObservableObject
	{
		private readonly WorkspaceService _workspaceService;
		private readonly ArtImageService _art;
		private readonly SettingsService _settings;
		private readonly INotifier _notifier;
		private readonly IDialogService _dialogs;
		private readonly UserTemplateStore _userTemplates;
		private readonly ShellNavigator _shell;
		private readonly ExchangeService _exchange;
		private readonly DispatcherTimer _searchTimer;
		private ObservableCollection<ProtoEntry> _items = [];
		private GameWorkspace? _workspace;

		[ObservableProperty]
		private ProtoEditorViewModel? _editor;

		[ObservableProperty]
		private ICollectionView? _view;

		[ObservableProperty]
		private String _searchText = String.Empty;

		[ObservableProperty]
		private ObservableCollection<TypeFilterItem> _typeFilters = [];

		[ObservableProperty]
		private TypeFilterItem? _selectedTypeFilter;

		[ObservableProperty]
		private ProtoKind? _kindFilter;

		[ObservableProperty]
		private IReadOnlyList<ProtoKind> _availableKinds = [];

		[ObservableProperty]
		private Boolean _modifiedOnly;

		[ObservableProperty]
		private Boolean _errorsOnly;

		[ObservableProperty]
		private Int32 _artFilter;

		[ObservableProperty]
		private String? _sourceFilter;

		[ObservableProperty]
		private IReadOnlyList<String> _sources = [];

		[ObservableProperty]
		private FlagFilterOption? _flagFilter;

		[ObservableProperty]
		private Int32 _flagFilterMode;

		[ObservableProperty]
		private Int32 _shownCount;

		[ObservableProperty]
		private Int32 _totalCount;

		[ObservableProperty]
		private Int32 _selectedCount;

		[ObservableProperty]
		private IReadOnlyList<TemplateGroup> _templateGroups = [];

		public LibraryViewModel(
			WorkspaceService workspaceService,
			ArtImageService art,
			SettingsService settings,
			INotifier notifier,
			IDialogService dialogs,
			UserTemplateStore userTemplates,
			ShellNavigator shell,
			ExchangeService exchange,
			MainWindowViewModel main)
		{
			Main = main;
			_workspaceService = workspaceService;
			_art = art;
			_settings = settings;
			_notifier = notifier;
			_dialogs = dialogs;
			_userTemplates = userTemplates;
			_shell = shell;
			_exchange = exchange;

			_searchTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(140) };
			_searchTimer.Tick += (_, _) =>
			{
				_searchTimer.Stop();
				ApplyFilter();
			};

			_workspaceService.WorkspaceChanged += OnWorkspaceChanged;
			_shell.ProtoRequested += (_, pid) => ShowProtoRequested?.Invoke(this, pid);
			_shell.FocusListRequested += (_, _) => FocusListRequested?.Invoke(this, EventArgs.Empty);
			_shell.TemplatesChanged += (_, _) => UpdateTemplates();
			_shell.SelectionProvider = () => Editor?.Selection ?? [];
			WeakReferenceMessenger.Default.Register<SearchChangedMessage>(this, (recipient, message) => ((LibraryViewModel)recipient).SearchText = message.Value);
			FlagFilters = BuildFlagFilters();
		}

		public MainWindowViewModel Main { get; }

		public WorkspaceService Workspace
		{
			get { return _workspaceService; }
		}

		public IReadOnlyList<FlagFilterOption> FlagFilters { get; }

		/// <summary>Raised when another page asks to display a prototype; the view selects and scrolls to it.</summary>
		public event EventHandler<ProtoId>? ShowProtoRequested;

		/// <summary>Raised when the search box hands the keyboard focus to the list.</summary>
		public event EventHandler? FocusListRequested;

		public void ReloadTemplates()
		{
			UpdateTemplates();
		}

		private const Int32 PrefetchBefore = 3;
		private const Int32 PrefetchAfter = 8;

		/// <summary>Warms the image cache with the rows just above and below the selection (arrow keys and wheel scrolling go that way).</summary>
		private void PrefetchAround(IReadOnlyList<ProtoEntry> selected)
		{
			if (Editor is null || selected.Count != 1 || View is not System.Windows.Data.ListCollectionView list)
			{
				return;
			}

			Int32 index = list.IndexOf(selected[0]);
			if (index < 0)
			{
				return;
			}

			List<ProtoEntry> neighbours = [];
			for (Int32 i = Math.Max(0, index - PrefetchBefore); i <= Math.Min(list.Count - 1, index + PrefetchAfter); i++)
			{
				if (i != index && list.GetItemAt(i) is ProtoEntry entry)
				{
					neighbours.Add(entry);
				}
			}

			Editor.Prefetch(neighbours);
		}

		/// <summary>Called by the grid whenever its selection changes.</summary>
		public void OnSelectionChanged(IList selectedItems)
		{
			List<ProtoEntry> entries = [.. selectedItems.OfType<ProtoEntry>()];
			SelectedCount = entries.Count;
			Editor?.SetSelection(entries);
			PrefetchAround(entries);
			DuplicateCommand.NotifyCanExecuteChanged();
			DeleteCommand.NotifyCanExecuteChanged();
			CompareSelectedCommand.NotifyCanExecuteChanged();
			ExportSelectionJsonCommand.NotifyCanExecuteChanged();
			ExportSelectionCsvCommand.NotifyCanExecuteChanged();
		}

		partial void OnSearchTextChanged(String value)
		{
			_searchTimer.Stop();
			_searchTimer.Start();
		}

		partial void OnSelectedTypeFilterChanged(TypeFilterItem? value)
		{
			foreach (TypeFilterItem item in TypeFilters)
			{
				item.IsSelected = ReferenceEquals(item, value);
			}

			AvailableKinds = value?.Type is { } type ? [.. ProtoKindExtensions.All.Where(k => k.GetObjectType() == type)] : [];
			KindFilter = null;
			ApplyFilter();
		}

		partial void OnKindFilterChanged(ProtoKind? value)
		{
			ApplyFilter();
		}

		partial void OnModifiedOnlyChanged(Boolean value)
		{
			ApplyFilter();
		}

		partial void OnErrorsOnlyChanged(Boolean value)
		{
			ApplyFilter();
		}

		partial void OnArtFilterChanged(Int32 value)
		{
			ApplyFilter();
		}

		partial void OnSourceFilterChanged(String? value)
		{
			ApplyFilter();
		}

		partial void OnFlagFilterChanged(FlagFilterOption? value)
		{
			ApplyFilter();
		}

		partial void OnFlagFilterModeChanged(Int32 value)
		{
			ApplyFilter();
		}

		[RelayCommand]
		private void SelectTypeFilter(TypeFilterItem? item)
		{
			SelectedTypeFilter = item;
		}

		[RelayCommand]
		private void ClearFilters()
		{
			SearchText = String.Empty;
			Main.SearchText = String.Empty;
			KindFilter = null;
			ModifiedOnly = false;
			ErrorsOnly = false;
			ArtFilter = 0;
			SourceFilter = null;
			FlagFilter = null;
			SelectedTypeFilter = TypeFilters.FirstOrDefault();
		}

		[RelayCommand]
		private async Task NewFromTemplate(ProtoTemplate? template)
		{
			GameWorkspace? workspace = _workspace;
			if (workspace is null || template is null)
			{
				return;
			}

			if (!workspace.Catalog.IsLoaded(template.Kind.GetObjectType()))
			{
				_notifier.Warning(Loc.Get("new.title"), Loc.Format("new.notLoaded", Loc.Get("type." + template.Kind.GetObjectType())));
				return;
			}

			String? name = await _dialogs.PromptAsync(Loc.Get("new.title"), Loc.Get("new.nameLabel"), Loc.Has("template." + template.Id) ? Loc.Get("template." + template.Id) : template.DisplayName);
			if (name is null)
			{
				return;
			}

			ProtoEntry created = workspace.CreateProto(template, name, String.Empty);
			_notifier.Success(Loc.Get("new.title"), Loc.Format("new.created", created.Pid, created.FileName));
			ShowProtoRequested?.Invoke(this, created.Pid);
		}

		private Boolean HasSelection()
		{
			return Editor is { Selection.Count: > 0 };
		}

		[RelayCommand(CanExecute = nameof(HasSelection))]
		private void Duplicate()
		{
			GameWorkspace? workspace = _workspace;
			if (workspace is null || Editor is null)
			{
				return;
			}

			ProtoEntry? last = null;
			foreach (ProtoEntry entry in Editor.Selection.OrderBy(e => e.Pid))
			{
				if (!workspace.Catalog.IsLoaded(entry.Type))
				{
					continue;
				}

				last = workspace.Duplicate(entry, Loc.Get("duplicate.suffix"));
			}

			if (last is not null)
			{
				_notifier.Success(Loc.Get("duplicate.title"), Loc.Format("duplicate.done", last.Pid));
				ShowProtoRequested?.Invoke(this, last.Pid);
			}
		}

		[RelayCommand(CanExecute = nameof(HasSelection))]
		private async Task Delete()
		{
			GameWorkspace? workspace = _workspace;
			if (workspace is null || Editor?.Single is not { } entry)
			{
				return;
			}

			if (!workspace.CanDelete(entry))
			{
				_notifier.Warning(Loc.Get("delete.title"), Loc.Get("delete.onlyLast"));
				return;
			}

			if (await _dialogs.ConfirmAsync(Loc.Get("delete.title"), Loc.Format("delete.confirm", entry.Pid, entry.Name), Loc.Get("delete.button")))
			{
				workspace.DeleteLast(entry);
			}
		}

		private Boolean HasTwoSelected()
		{
			return Editor is { Selection.Count: 2 };
		}

		[RelayCommand(CanExecute = nameof(HasTwoSelected))]
		private void CompareSelected()
		{
			if (Editor is { Selection.Count: 2 } editor)
			{
				_shell.ComparePair(editor.Selection[0], editor.Selection[1]);
			}
		}

		[RelayCommand(CanExecute = nameof(HasSelection))]
		private void ExportSelectionJson()
		{
			if (Editor is not null)
			{
				_exchange.ExportJson(Editor.Selection);
			}
		}

		[RelayCommand(CanExecute = nameof(HasSelection))]
		private async Task ExportSelectionCsv()
		{
			if (Editor is not null)
			{
				await _exchange.ExportCsvAsync(Editor.Selection, GetDelimiter());
			}
		}

		[RelayCommand]
		private void CopyPid()
		{
			if (Editor is { Selection.Count: > 0 } editor)
			{
				try
				{
					Clipboard.SetText(String.Join(Environment.NewLine, editor.Selection.Select(e => e.Pid.ToString())));
				}
				catch (System.Runtime.InteropServices.ExternalException)
				{
					_notifier.Warning(Loc.Get("common.clipboard"), Loc.Get("common.clipboardBusy"));
				}
			}
		}

		private Char GetDelimiter()
		{
			String text = _settings.Current.CsvDelimiter;
			return text.Length > 0 ? text[0] : ';';
		}

		private void OnWorkspaceChanged(Object? sender, GameWorkspace? workspace)
		{
			if (ReferenceEquals(workspace, _workspace) && workspace is not null)
			{
				View?.Refresh();
				UpdateCounts();
				return;
			}

			if (_workspace is not null)
			{
				_workspace.Catalog.Changed -= OnCatalogChanged;
			}

			Editor?.Dispose();
			Editor = null;
			_workspace = workspace;
			_items = [];
			View = null;

			if (workspace is null)
			{
				TypeFilters = [];
				TotalCount = ShownCount = SelectedCount = 0;
				Sources = [];
				return;
			}

			workspace.Catalog.Changed += OnCatalogChanged;
			Editor = new ProtoEditorViewModel(workspace, _art, _settings, _notifier, _dialogs, _userTemplates, _shell);
			_items = new ObservableCollection<ProtoEntry>(workspace.Catalog.All);

			ListCollectionView view = new(_items);
			view.SortDescriptions.Add(new SortDescription(nameof(ProtoEntry.Pid), ListSortDirection.Ascending));
			View = view;

			List<TypeFilterItem> filters = [new TypeFilterItem(null)];
			filters.AddRange(ProtoObjectTypeExtensions.All.Where(workspace.Catalog.IsLoaded).Select(t => new TypeFilterItem(t)));
			foreach (TypeFilterItem item in filters)
			{
				item.Count = item.Type is { } type ? workspace.Catalog.GetEntries(type).Count : workspace.Catalog.Count;
			}

			TypeFilters = [.. filters];
			Sources = [.. workspace.Files.Layers.Select(l => l.Name)];
			SelectedTypeFilter = TypeFilters[0];
			UpdateTemplates();
			ApplyFilter();
			TotalCount = _items.Count;
		}

		private void OnCatalogChanged(Object? sender, CatalogChangedEventArgs e)
		{
			Application.Current.Dispatcher.Invoke(() =>
			{
				switch (e.Kind)
				{
					case CatalogChangeKind.Added when e.Entry is not null:
						_items.Add(e.Entry);
						break;
					case CatalogChangeKind.Removed when e.Entry is not null:
						_items.Remove(e.Entry);
						break;
					default:
						_items.Clear();
						foreach (ProtoEntry entry in _workspace!.Catalog.All)
						{
							_items.Add(entry);
						}

						break;
				}

				foreach (TypeFilterItem item in TypeFilters)
				{
					item.Count = item.Type is { } type ? _workspace!.Catalog.GetEntries(type).Count : _workspace!.Catalog.Count;
				}

				TotalCount = _items.Count;
				UpdateCounts();
			});
		}

		private void UpdateTemplates()
		{
			if (_workspace is null)
			{
				TemplateGroups = [];
				return;
			}

			IEnumerable<ProtoTemplate> all = BuiltInTemplates.All.Concat(_userTemplates.Load())
				.Where(t => _workspace.Catalog.IsLoaded(t.Kind.GetObjectType()));
			TemplateGroups = [.. all.GroupBy(t => t.Category).Select(g => new TemplateGroup(g.Key, [.. g]))];
		}

		private void ApplyFilter()
		{
			if (View is not ListCollectionView view || _workspace is null)
			{
				return;
			}

			GameWorkspace workspace = _workspace;
			ProtoFilter filter = new(SearchText)
			{
				Types = SelectedTypeFilter?.Type is { } type ? new HashSet<ProtoObjectType> { type } : null,
				Kinds = KindFilter is { } kind ? new HashSet<ProtoKind> { kind } : null,
				ModifiedOnly = ModifiedOnly,
				ErrorsOnly = ErrorsOnly,
				ErrorChecker = e => e.WorstIssue is Severity.Warning or Severity.Error,
				SourceName = SourceFilter,
				HasArt = ArtFilter == 0 ? null : ArtFilter == 1,
				ArtChecker = e => HasValidArt(workspace, e),
				RequiredFlags = FlagFilter is { IsExtended: false } on && FlagFilterMode == 0 ? on.SignedMask() : 0,
				ForbiddenFlags = FlagFilter is { IsExtended: false } off && FlagFilterMode == 1 ? off.SignedMask() : 0,
				RequiredExtFlags = FlagFilter is { IsExtended: true } onExt && FlagFilterMode == 0 ? onExt.SignedMask() : 0,
				ForbiddenExtFlags = FlagFilter is { IsExtended: true } offExt && FlagFilterMode == 1 ? offExt.SignedMask() : 0,
			};

			view.Filter = filter.IsEmpty ? null : item => filter.Matches((ProtoEntry)item);
			UpdateCounts();
		}

		private void UpdateCounts()
		{
			ShownCount = View?.Cast<Object>().Count() ?? 0;
		}

		private static Boolean? HasValidArt(GameWorkspace workspace, ProtoEntry entry)
		{
			Int32? fid = entry.File?.TryGet(FieldKeys.FrmId);
			return fid is null ? null : workspace.Art.Exists(new FrameId(fid.Value));
		}

		private static IReadOnlyList<FlagFilterOption> BuildFlagFilters()
		{
			List<FlagFilterOption> options = [];
			options.AddRange(FlagSets.Get(FlagSets.ObjectFlags).Bits.Select(b => new FlagFilterOption(b.Name, b.Mask, IsExtended: false)));
			options.AddRange(FlagSets.Get(FlagSets.WeaponExtFlags).Bits.Select(b => new FlagFilterOption(b.Name, b.Mask, IsExtended: true)));
			return options;
		}
	}

	internal static class FlagFilterExtensions
	{
		public static Int32 SignedMask(this FlagFilterOption option)
		{
			return unchecked((Int32)option.Mask);
		}
	}
}