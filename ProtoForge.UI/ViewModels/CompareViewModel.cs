namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.ComponentModel;
	using System.Globalization;
	using System.Linq;
	using System.Windows.Data;
	using System.Windows.Media;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using Microsoft.Extensions.DependencyInjection;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Frm;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Infrastructure.Settings;
	using ProtoForge.Infrastructure.Workspace;
	using Services;
	using Views;

	public enum VersionKind
	{
		Current,
		Loaded,
		Vanilla,
	}

	public sealed record VersionOption(VersionKind Kind, String Label)
	{
		public override String ToString()
		{
			return Label;
		}
	}

	/// <summary>One field compared between the left and right prototypes.</summary>
	public sealed class CompareRow
	{
		public CompareRow(FieldDiff diff, String valueA, String valueB)
		{
			Diff = diff;
			Key = diff.Key;
			Label = Loc.Field(diff.Key);
			GroupLabel = Loc.Group(diff.Group);
			ValueA = valueA;
			ValueB = valueB;
		}

		public FieldDiff? Diff { get; }

		public String Key { get; }

		public String Label { get; }

		public String GroupLabel { get; }

		public String ValueA { get; }

		public String ValueB { get; }

		public Boolean IsDifferent { get; init; }

		/// <summary>Text rows (name, description) have no schema field.</summary>
		public static CompareRow ForText(String key, String group, String a, String b)
		{
			return new CompareRow(new FieldDiff(key, group, null, null, null, null), a, b) { IsDifferent = a != b };
		}
	}

	/// <summary>One side of the comparison: a prototype and which version of it to look at.</summary>
	public sealed partial class CompareSideViewModel : ObservableObject
	{
		[ObservableProperty]
		private ProtoEntry? _entry;

		[ObservableProperty]
		private VersionOption _version;

		[ObservableProperty]
		private ImageSource? _image;

		[ObservableProperty]
		private String _title = String.Empty;

		[ObservableProperty]
		private String _subtitle = String.Empty;

		[ObservableProperty]
		private Byte[] _data = [];

		[ObservableProperty]
		private IReadOnlyList<Int32> _changed = [];

		public CompareSideViewModel(VersionOption version)
		{
			_version = version;
		}

		public ProtoFile? File { get; set; }

		public String? Name { get; set; }

		public String? Description { get; set; }
	}

	/// <summary>
	/// Side-by-side comparison of two prototypes, or of the same prototype before/after an edit or against the original game.
	/// </summary>
	public sealed partial class CompareViewModel : ObservableObject
	{
		private readonly WorkspaceService _workspaceService;
		private readonly ArtImageService _art;
		private readonly SettingsService _settings;
		private Boolean _updating;

		[ObservableProperty]
		private ICollectionView? _rowsView;

		[ObservableProperty]
		private Boolean _differencesOnly = true;

		[ObservableProperty]
		private String _summary = String.Empty;

		[ObservableProperty]
		private IReadOnlyList<ProtoEntry> _entries = [];

		public CompareViewModel(WorkspaceService workspaceService, ArtImageService art, SettingsService settings)
		{
			_workspaceService = workspaceService;
			_art = art;
			_settings = settings;
			Versions =
			[
				new VersionOption(VersionKind.Current, Loc.Get("compare.current")),
				new VersionOption(VersionKind.Loaded, Loc.Get("compare.loaded")),
				new VersionOption(VersionKind.Vanilla, Loc.Get("compare.vanilla")),
			];
			Left = new CompareSideViewModel(Versions[1]);
			Right = new CompareSideViewModel(Versions[0]);
			Left.PropertyChanged += OnSideChanged;
			Right.PropertyChanged += OnSideChanged;
			_workspaceService.WorkspaceChanged += (_, workspace) =>
			{
				Entries = workspace is null ? [] : [.. workspace.Catalog.All];
				if (workspace is null)
				{
					Left.Entry = null;
					Right.Entry = null;
				}
				else
				{
					Rebuild();
				}
			};
			Entries = _workspaceService.Current is { } current ? [.. current.Catalog.All] : [];
		}

		public IReadOnlyList<VersionOption> Versions { get; }

		public CompareSideViewModel Left { get; }

		public CompareSideViewModel Right { get; }

		public WorkspaceService Workspace
		{
			get { return _workspaceService; }
		}

		partial void OnDifferencesOnlyChanged(Boolean value)
		{
			RowsView?.Refresh();
		}

		/// <summary>The same prototype: the original game on the left, the current state on the right.</summary>
		public void SetVanillaVsCurrent(ProtoEntry entry)
		{
			_updating = true;
			Left.Entry = entry;
			Left.Version = Versions[(Int32)VersionKind.Vanilla];
			Right.Entry = entry;
			Right.Version = Versions[(Int32)VersionKind.Current];
			_updating = false;
			Rebuild();
		}

		public void SetBaselineVsCurrent(ProtoEntry entry)
		{
			_updating = true;
			Left.Entry = entry;
			Left.Version = Versions[(Int32)VersionKind.Loaded];
			Right.Entry = entry;
			Right.Version = Versions[(Int32)VersionKind.Current];
			_updating = false;
			Rebuild();
		}

		public void SetPair(ProtoEntry a, ProtoEntry b)
		{
			_updating = true;
			Left.Entry = a;
			Left.Version = Versions[(Int32)VersionKind.Current];
			Right.Entry = b;
			Right.Version = Versions[(Int32)VersionKind.Current];
			_updating = false;
			Rebuild();
		}

		public void CopyFrom(CompareViewModel other)
		{
			_updating = true;
			Left.Entry = other.Left.Entry;
			Left.Version = other.Left.Version;
			Right.Entry = other.Right.Entry;
			Right.Version = other.Right.Version;
			_updating = false;
			Rebuild();
		}

		[RelayCommand]
		private void Swap()
		{
			_updating = true;
			(Left.Entry, Right.Entry) = (Right.Entry, Left.Entry);
			(Left.Version, Right.Version) = (Right.Version, Left.Version);
			_updating = false;
			Rebuild();
		}

		[RelayCommand]
		private void OpenInWindow()
		{
			CompareWindow window = App.Services.GetRequiredService<CompareWindow>();
			window.ViewModel.CopyFrom(this);
			window.Show();
		}

		private void OnSideChanged(Object? sender, PropertyChangedEventArgs e)
		{
			if (!_updating && e.PropertyName is nameof(CompareSideViewModel.Entry) or nameof(CompareSideViewModel.Version))
			{
				Rebuild();
			}
		}

		private void Rebuild()
		{
			GameWorkspace? workspace = _workspaceService.Current;
			if (workspace is null)
			{
				RowsView = null;
				Summary = String.Empty;
				return;
			}

			Resolve(workspace, Left);
			Resolve(workspace, Right);

			IReadOnlyList<FieldDiff> diffs = ProtoDiff.Compare(Left.File, Right.File);
			List<CompareRow> rows = [];
			if (Left.File is not null || Right.File is not null)
			{
				rows.Add(CompareRow.ForText("Name", "Identity", Left.Name ?? String.Empty, Right.Name ?? String.Empty));
				rows.Add(CompareRow.ForText("Description", "Identity", Left.Description ?? String.Empty, Right.Description ?? String.Empty));
			}

			foreach (FieldDiff diff in diffs.Where(d => d.Field is { IsVirtual: false }))
			{
				rows.Add(new CompareRow(diff, Format(workspace, diff.FieldA, diff.ValueA), Format(workspace, diff.FieldB, diff.ValueB)) { IsDifferent = diff.IsDifferent });
			}

			ListCollectionView view = new(new ObservableCollection<CompareRow>(rows)) { Filter = item => !DifferencesOnly || ((CompareRow)item).IsDifferent };
			view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CompareRow.GroupLabel)));
			RowsView = view;

			Int32 different = rows.Count(r => r.IsDifferent);
			Summary = Left.File is null || Right.File is null
				? Loc.Get("compare.pickTwo")
				: different == 0 ? Loc.Get("compare.identical") : Loc.Format("compare.differences", different, rows.Count);

			Left.Data = Left.File?.ToArray() ?? [];
			Right.Data = Right.File?.ToArray() ?? [];
			IReadOnlyList<Int32> offsets = ProtoDiff.GetDifferentOffsets(Left.File, Right.File);
			Left.Changed = offsets;
			Right.Changed = offsets;
		}

		private void Resolve(GameWorkspace workspace, CompareSideViewModel side)
		{
			ProtoEntry? entry = side.Entry;
			side.File = null;
			side.Name = null;
			side.Description = null;
			side.Image = null;
			side.Title = entry?.DisplayName ?? Loc.Get("compare.nothing");
			side.Subtitle = String.Empty;
			if (entry is null)
			{
				return;
			}

			switch (side.Version.Kind)
			{
				case VersionKind.Current:
					side.File = entry.File;
					side.Name = entry.Name;
					side.Description = entry.Description;
					break;
				case VersionKind.Loaded:
					side.File = entry.Baseline;
					side.Name = entry.BaselineName;
					side.Description = entry.BaselineDescription;
					break;
				default:
					side.File = workspace.LoadVanilla(entry);
					side.Name = entry.BaselineName;
					side.Description = entry.BaselineDescription;
					break;
			}

			side.Subtitle = $"{entry.Pid} · {entry.FileName} · {side.Version.Label}";
			if (side.File?.TryGet(FieldKeys.FrmId) is { } fid && fid != -1)
			{
				LoadImage(side, new FrameId(fid));
			}
		}

		private async void LoadImage(CompareSideViewModel side, FrameId fid)
		{
			ProtoEntry? entry = side.Entry;
			ImageSource? image = await _art.GetAsync(fid);
			if (ReferenceEquals(entry, side.Entry))
			{
				side.Image = image;
			}
		}

		private static String Format(GameWorkspace workspace, ProtoField? field, Int32? value)
		{
			if (field is null || value is null)
			{
				return "-";
			}

			Int32 v = value.Value;
			switch (field.Editor)
			{
				case FieldEditor.Choice when field.ChoiceSet is not null:
					return $"{workspace.Choices.Describe(field.ChoiceSet, v)} ({v})";
				case FieldEditor.Flags when field.FlagSet is not null:
					String names = String.Join(", ", FlagSets.Get(field.FlagSet).GetActive(v).Select(b => Loc.Flag(b.Name)));
					return $"0x{v:X8}" + (names.Length > 0 ? " · " + names : String.Empty);
				case FieldEditor.Art:
				case FieldEditor.Script:
					return v == -1 ? Loc.Get("field.none") : $"0x{v:X8}";
				case FieldEditor.Proto:
					return v == -1 ? Loc.Get("field.none") : workspace.Catalog.Find(v) is { } target ? $"{target.DisplayName} [{new ProtoId(v)}]" : new ProtoId(v).ToString();
				case FieldEditor.LightIntensity:
					return $"{v * 100.0 / 65536.0:0.#}% ({v})";
				default:
					return v.ToString(CultureInfo.CurrentCulture);
			}
		}
	}
}