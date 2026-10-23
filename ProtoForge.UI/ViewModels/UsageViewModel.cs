namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using ProtoForge.Core.Domain;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	/// <summary>One line of the "Usage" tab.</summary>
	public sealed record UsageRow(String Relation, String Field, String Name, String Pid, String Source, ProtoEntry? Entry);

	/// <summary>Where the selected prototype is used: by other prototypes, by AI packets, and what it shares with others.</summary>
	public sealed partial class UsageViewModel : ObservableObject
	{
		private readonly GameWorkspace _workspace;
		private readonly ShellNavigator _shell;
		private ProtoEntry? _entry;
		private Int32 _version;

		[ObservableProperty]
		private IReadOnlyList<UsageRow> _rows = [];

		[ObservableProperty]
		private String _summary = String.Empty;

		/// <summary>True while the tab is shown: the search only runs then (it reads the whole catalog).</summary>
		[ObservableProperty]
		private Boolean _isActive;

		public UsageViewModel(GameWorkspace workspace, ShellNavigator shell)
		{
			_workspace = workspace;
			_shell = shell;
		}

		/// <summary>The prototype to look at (null for none or for a multiple selection).</summary>
		public void Load(ProtoEntry? entry)
		{
			_entry = entry;
			if (IsActive)
			{
				_ = RefreshAsync();
			}
			else
			{
				Rows = [];
				Summary = String.Empty;
			}
		}

		/// <summary>Reads the maps and the scripts again (they may have changed on the disk) and searches again.</summary>
		[RelayCommand]
		private async Task RefreshReferences()
		{
			_workspace.InvalidateReferenceIndex();
			if (IsActive)
			{
				await RefreshAsync();
			}
		}

		partial void OnIsActiveChanged(Boolean value)
		{
			if (value)
			{
				_ = RefreshAsync();
			}
		}

		/// <summary>Shows the prototype of a line in the library.</summary>
		public void Show(UsageRow row)
		{
			if (row.Entry is { } entry)
			{
				_shell.ShowProto(entry.Pid);
			}
		}

		private async Task RefreshAsync()
		{
			ProtoEntry? entry = _entry;
			Int32 version = ++_version;
			if (entry is null)
			{
				Rows = [];
				Summary = Loc.Get("usage.select");
				return;
			}

			IReadOnlyList<String> scripts = _workspace.ScriptNames;
			IReadOnlyList<UsageRef> found = await Task.Run(() => UsageFinder.Find(_workspace.Catalog, entry, _workspace.AiPackets, null, scripts));
			if (version != _version)
			{
				return;
			}

			// The prototypes answer at once; the maps and the scripts take a moment the first time they are read.
			Task<ReferenceIndex> pending = _workspace.GetReferenceIndexAsync();
			Display(found, pending.IsCompleted ? null : Loc.Get("usage.mapsLoading"));
			if (pending.IsCompleted && pending.IsFaulted)
			{
				return;
			}

			ReferenceIndex index;
			try
			{
				index = await pending;
			}
			catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or System.IO.IOException)
			{
				Display(found, null);
				return;
			}

			if (version != _version)
			{
				return;
			}

			IReadOnlyList<UsageRef> complete = await Task.Run(() => UsageFinder.Find(_workspace.Catalog, entry, _workspace.AiPackets, index, scripts));
			if (version == _version)
			{
				Display(complete, index.UnreadableMaps.Count > 0 ? Loc.Format("usage.mapsSkipped", index.UnreadableMaps.Count) : null);
			}
		}

		private void Display(IReadOnlyList<UsageRef> found, String? note)
		{
			Rows =
			[
				.. found
					.OrderBy(u => u.Kind)
					.ThenBy(u => u.Entry?.Pid.Value ?? Int32.MaxValue)
					.ThenBy(u => u.Detail, StringComparer.OrdinalIgnoreCase)
					.Select(ToRow),
			];
			String summary = found.Count == 0 ? Loc.Get("usage.none") : Loc.Format("usage.count", found.Count);
			Summary = note is null ? summary : summary + " " + note;
		}

		private static UsageRow ToRow(UsageRef u)
		{
			// A map or a script is named in the "name" column, the counts are the detail.
			String detail = u.Kind switch
			{
				UsageKind.PlacedOnMap when u.Count > 0 && u.SecondCount > 0 => Loc.Format("usage.mapBoth", u.Count, u.SecondCount),
				UsageKind.PlacedOnMap when u.Count > 0 => Loc.Format("usage.mapPlaced", u.Count),
				UsageKind.PlacedOnMap => Loc.Format("usage.mapCarried", u.SecondCount),
				UsageKind.ScriptOnMap => Loc.Format("usage.mapScript", u.Count),
				_ => u.FieldKey.Length == 0 ? String.Empty : Loc.Field(u.FieldKey),
			};
			return new UsageRow(
				Loc.Get("usage." + u.Kind),
				detail,
				u.Entry is null ? u.Detail : u.Entry.DisplayName,
				u.Entry is null ? String.Empty : u.Entry.Pid.ToString(),
				u.Entry?.SourceName ?? String.Empty,
				u.Entry);
		}
	}
}