namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;
	using CommunityToolkit.Mvvm.ComponentModel;
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

			IReadOnlyList<UsageRef> found = await Task.Run(() => UsageFinder.Find(_workspace.Catalog, entry, _workspace.AiPackets));
			if (version != _version)
			{
				return;
			}

			Rows =
			[
				.. found
					.OrderBy(u => u.Kind)
					.ThenBy(u => u.Entry?.Pid.Value ?? Int32.MaxValue)
					.Select(u => new UsageRow(
						Loc.Get("usage." + u.Kind),
						Loc.Field(u.FieldKey),
						u.Entry is null ? u.Detail : u.Entry.DisplayName,
						u.Entry is null ? String.Empty : u.Entry.Pid.ToString(),
						u.Entry?.SourceName ?? String.Empty,
						u.Entry)),
			];
			Summary = found.Count == 0 ? Loc.Get("usage.none") : Loc.Format("usage.count", found.Count);
		}
	}
}