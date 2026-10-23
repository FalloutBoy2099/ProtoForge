namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Map;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	/// <summary>One map of the game.</summary>
	public sealed record MapRow(MapSummary Map, String FileName, String Name, String Script, Int32 Objects, Int32 Prototypes);

	/// <summary>A prototype found on a map.</summary>
	public sealed record MapObjectRow(ProtoEntry? Entry, String Name, String Pid, String Kind, Int32 Placed, Int32 Carried);

	/// <summary>A prototype that nothing seems to use.</summary>
	public sealed record UnusedRow(ProtoEntry Entry, String Name, String Pid, String Kind, String FileName, String Source);

	/// <summary>The maps of the game and what is on them, and the prototypes that no map, script or other prototype uses.</summary>
	public sealed partial class MapsViewModel : ObservableObject
	{
		private static readonly ProtoObjectType[] UnusedTypes =
		[
			ProtoObjectType.Item,
			ProtoObjectType.Critter,
			ProtoObjectType.Scenery,
			ProtoObjectType.Wall,
			ProtoObjectType.Misc,
		];

		private readonly WorkspaceService _workspaceService;
		private readonly ShellNavigator _shell;
		private ReferenceIndex? _shown;
		private GameWorkspace? _shownFor;
		private Int32 _version;
		private IReadOnlyList<UnusedRow> _unusedAll = [];
		private IReadOnlyList<ProtoEntry> _unusedEntries = [];

		[ObservableProperty]
		private IReadOnlyList<MapRow> _maps = [];

		[ObservableProperty]
		private MapRow? _selectedMap;

		[ObservableProperty]
		private IReadOnlyList<MapObjectRow> _mapObjects = [];

		[ObservableProperty]
		private IReadOnlyList<UnusedRow> _unused = [];

		/// <summary>0 = every type, then the types of <see cref="UnusedTypes"/>.</summary>
		[ObservableProperty]
		private Int32 _unusedFilterIndex;

		[ObservableProperty]
		[NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
		private Boolean _isLoading;

		[ObservableProperty]
		private String _status = String.Empty;

		[ObservableProperty]
		private String _unusedStatus = String.Empty;

		public MapsViewModel(WorkspaceService workspaceService, ShellNavigator shell)
		{
			_workspaceService = workspaceService;
			_shell = shell;
			_workspaceService.WorkspaceChanged += (_, _) =>
			{
				_shown = null;
				_shownFor = null;
				Maps = [];
				MapObjects = [];
				Unused = [];
				_unusedAll = [];
				Status = String.Empty;
				UnusedStatus = String.Empty;
			};
			UnusedFilterNames =
			[
				Loc.Get("type.all"),
				.. UnusedTypes.Select(t => Loc.Get("type." + t)),
			];
		}

		public IReadOnlyList<String> UnusedFilterNames { get; }

		/// <summary>Shows a prototype in the library.</summary>
		public void Show(ProtoEntry? entry)
		{
			if (entry is not null)
			{
				_shell.ShowProto(entry.Pid);
			}
		}

		/// <summary>Called when the page is shown: the maps are read the first time (in the background), the unused prototypes are worked out again.</summary>
		public async Task LoadAsync()
		{
			if (_workspaceService.Current is not { } workspace)
			{
				return;
			}

			Int32 version = ++_version;
			IsLoading = true;
			Status = Loc.Get("usage.mapsLoading");
			try
			{
				ReferenceIndex index = await workspace.GetReferenceIndexAsync();
				if (version != _version)
				{
					return;
				}

				if (!ReferenceEquals(index, _shown) || !ReferenceEquals(workspace, _shownFor))
				{
					ShowMaps(workspace, index);
					_shown = index;
					_shownFor = workspace;
				}

				IReadOnlyList<ProtoEntry> unused = await Task.Run(() => UnusedPrototypes.Find(workspace.Catalog, workspace.AiPackets, index));
				if (version != _version)
				{
					return;
				}

				_unusedEntries = unused;
				_unusedAll = [.. unused.Select(e => new UnusedRow(e, e.DisplayName, e.Pid.ToString(), e.Kind is { } kind ? Loc.Get("kind." + kind) : String.Empty, e.FileName, e.SourceName))];
				ApplyUnusedFilter();
				Status = Maps.Count == 0 && index.UnreadableMaps.Count == 0
					? Loc.Get("maps.none")
					: Loc.Format("maps.summary", index.Maps.Count, index.Maps.Sum(m => m.Placed.Values.Sum()), index.ScriptCount)
						+ (index.UnreadableMaps.Count > 0 ? " " + Loc.Format("usage.mapsSkipped", index.UnreadableMaps.Count) : String.Empty);
			}
			catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or System.IO.IOException)
			{
				Status = ProtoForge.Core.UserTexts.Describe(ex);
			}
			finally
			{
				if (version == _version)
				{
					IsLoading = false;
				}
			}
		}

		[RelayCommand(CanExecute = nameof(CanRefresh))]
		private async Task Refresh()
		{
			// The maps and the scripts may have changed on the disk: read them again.
			_workspaceService.Current?.InvalidateReferenceIndex();
			await LoadAsync();
		}

		private Boolean CanRefresh()
		{
			return !IsLoading && _workspaceService.Current is not null;
		}

		partial void OnSelectedMapChanged(MapRow? value)
		{
			if (value is null || _workspaceService.Current is not { } workspace)
			{
				MapObjects = [];
				return;
			}

			MapSummary map = value.Map;
			MapObjects =
			[
				.. map.Placed.Keys.Union(map.Carried.Keys)
					.Select(pid =>
					{
						ProtoEntry? entry = workspace.Catalog.Find(pid);
						String kind = entry?.Kind is { } k ? Loc.Get("kind." + k) : String.Empty;
						return new MapObjectRow(entry, entry?.DisplayName ?? "?", new ProtoId(pid).ToString(), kind, map.Placed.GetValueOrDefault(pid), map.Carried.GetValueOrDefault(pid));
					})
					.OrderByDescending(r => r.Placed + r.Carried)
					.ThenBy(r => r.Pid, StringComparer.Ordinal),
			];
		}

		partial void OnUnusedFilterIndexChanged(Int32 value)
		{
			ApplyUnusedFilter();
		}

		private void ShowMaps(GameWorkspace workspace, ReferenceIndex index)
		{
			IReadOnlyList<String> scripts = workspace.ScriptNames;
			Maps =
			[
				.. index.Maps
					.OrderBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
					.Select(m => new MapRow(
						m,
						m.FileName,
						m.Name,
						m.MapScript >= 0 && m.MapScript < scripts.Count ? System.IO.Path.GetFileNameWithoutExtension(scripts[m.MapScript]) : String.Empty,
						m.Placed.Values.Sum() + m.Carried.Values.Sum(),
						m.Placed.Keys.Union(m.Carried.Keys).Count())),
			];
			SelectedMap = null;
		}

		private void ApplyUnusedFilter()
		{
			ProtoObjectType? type = UnusedFilterIndex is > 0 and <= 5 ? UnusedTypes[UnusedFilterIndex - 1] : null;
			HashSet<ProtoEntry> keep = type is null ? [.. _unusedEntries] : [.. _unusedEntries.Where(e => e.Type == type)];
			Unused = [.. _unusedAll.Where(r => keep.Contains(r.Entry))];
			UnusedStatus = Unused.Count == 0 ? Loc.Get("maps.unusedNone") : Loc.Format("maps.unusedCount", Unused.Count);
		}
	}
}