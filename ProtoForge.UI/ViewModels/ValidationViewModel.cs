namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.ComponentModel;
	using System.IO;
	using System.Linq;
	using System.Text;
	using System.Threading.Tasks;
	using System.Windows.Data;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Validation;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	/// <summary>One line of the validation report, with the texts already localised.</summary>
	public sealed class IssueRow
	{
		public IssueRow(ValidationIssue issue, String entryName)
		{
			Issue = issue;
			EntryName = entryName;
			Message = Loc.Has("issue." + issue.Code)
				? String.Format(System.Globalization.CultureInfo.CurrentCulture, Loc.Get("issue." + issue.Code), issue.Arguments.ToArray())
				: issue.Message;
			FieldLabel = issue.FieldKey is null ? String.Empty : Loc.Field(issue.FieldKey);
		}

		public ValidationIssue Issue { get; }

		public Severity Severity
		{
			get { return Issue.Severity; }
		}

		public String PidText
		{
			get { return Issue.PidText; }
		}

		public String EntryName { get; }

		public String Message { get; }

		public String FieldLabel { get; }

		public String Rule
		{
			get { return Issue.RuleId; }
		}
	}

	/// <summary>Runs the validation rules and presents the report with links back to the prototypes.</summary>
	public sealed partial class ValidationViewModel : ObservableObject
	{
		private readonly WorkspaceService _workspaceService;
		private readonly ShellNavigator _shell;
		private readonly IDialogService _dialogs;
		private readonly INotifier _notifier;
		private ObservableCollection<IssueRow> _rows = [];

		[ObservableProperty]
		private ICollectionView? _view;

		[ObservableProperty]
		private Boolean _isRunning;

		[ObservableProperty]
		private Double _progress;

		[ObservableProperty]
		private Int32 _errorCount;

		[ObservableProperty]
		private Int32 _warningCount;

		[ObservableProperty]
		private Int32 _infoCount;

		[ObservableProperty]
		private Boolean _showErrors = true;

		[ObservableProperty]
		private Boolean _showWarnings = true;

		[ObservableProperty]
		private Boolean _showInfo;

		[ObservableProperty]
		private String _filterText = String.Empty;

		[ObservableProperty]
		private String _summary = String.Empty;

		[ObservableProperty]
		private Boolean _hasRun;
		private GameWorkspace? _lastWorkspace;

		[ObservableProperty]
		private IssueRow? _selectedRow;

		public ValidationViewModel(WorkspaceService workspaceService, ShellNavigator shell, IDialogService dialogs, INotifier notifier)
		{
			_workspaceService = workspaceService;
			_shell = shell;
			_dialogs = dialogs;
			_notifier = notifier;
			_workspaceService.WorkspaceChanged += (_, ws) =>
			{
				if (ws is null || !HasRun || !ReferenceEquals(ws, _lastWorkspace))
				{
					Reset();
				}

				_lastWorkspace = ws;
			};
			_lastWorkspace = _workspaceService.Current;
			Summary = Loc.Get("validation.notRun");
			RunCommand.NotifyCanExecuteChanged();
		}

		public WorkspaceService Workspace
		{
			get { return _workspaceService; }
		}

		partial void OnShowErrorsChanged(Boolean value)
		{
			View?.Refresh();
		}

		partial void OnShowWarningsChanged(Boolean value)
		{
			View?.Refresh();
		}

		partial void OnShowInfoChanged(Boolean value)
		{
			View?.Refresh();
		}

		partial void OnFilterTextChanged(String value)
		{
			View?.Refresh();
		}

		private Boolean CanRun()
		{
			return _workspaceService.HasWorkspace && !IsRunning;
		}

		[RelayCommand(CanExecute = nameof(CanRun))]
		public async Task Run()
		{
			GameWorkspace? workspace = _workspaceService.Current;
			if (workspace is null)
			{
				return;
			}

			IsRunning = true;
			Progress = 0;
			Summary = Loc.Get("validation.running");
			RunCommand.NotifyCanExecuteChanged();
			try
			{
				Progress<Double> progress = new(p => Progress = p);
				IReadOnlyList<ValidationIssue> issues = await Task.Run(() => workspace.Validate(null, progress));

				ObservableCollection<IssueRow> rows = new(issues.Select(i => new IssueRow(i, i.Pid is { } pid ? workspace.Catalog.Find(pid)?.DisplayName ?? String.Empty : String.Empty)));
				_rows = rows;
				ApplyToEntries(workspace, issues);

				ListCollectionView view = new(rows) { Filter = Matches };
				View = view;
				ErrorCount = issues.Count(i => i.Severity == Severity.Error);
				WarningCount = issues.Count(i => i.Severity == Severity.Warning);
				InfoCount = issues.Count(i => i.Severity == Severity.Info);
				HasRun = true;
				Summary = Loc.Format("validation.summary", workspace.Catalog.Count, ErrorCount, WarningCount, InfoCount);
			}
			finally
			{
				IsRunning = false;
				RunCommand.NotifyCanExecuteChanged();
			}
		}

		[RelayCommand]
		private void OpenIssue(IssueRow? row)
		{
			row ??= SelectedRow;
			if (row?.Issue.Pid is { } pid)
			{
				_shell.ShowProto(pid);
			}
		}

		[RelayCommand]
		private void ExportReport()
		{
			if (_rows.Count == 0)
			{
				_notifier.Info(Loc.Get("validation.title"), Loc.Get("validation.nothingToExport"));
				return;
			}

			String? path = _dialogs.PickSaveFile(Loc.Get("validation.exportTitle"), "CSV (*.csv)|*.csv|Text (*.txt)|*.txt", "validation-report.csv");
			if (path is null)
			{
				return;
			}

			StringBuilder builder = new();
			Boolean csv = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
			if (csv)
			{
				builder.AppendLine("Severity;PID;Name;Field;Message;Rule");
			}

			foreach (IssueRow row in _rows)
			{
				builder.AppendLine(csv
					? $"{row.Severity};{row.PidText};{Quote(row.EntryName)};{Quote(row.FieldLabel)};{Quote(row.Message)};{row.Rule}"
					: $"[{row.Severity}] {row.PidText} {row.EntryName}: {row.Message}");
			}

			try
			{
				File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
				_notifier.Success(Loc.Get("validation.title"), Loc.Format("export.done", _rows.Count, Path.GetFileName(path)));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				_notifier.Error(Loc.Get("validation.title"), ProtoForge.Core.UserTexts.Describe(ex));
			}
		}

		private static String Quote(String text)
		{
			return "\"" + ProtoForge.Core.Serialization.ProtoCsv.EscapeText(text).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
		}

		private Boolean Matches(Object item)
		{
			IssueRow row = (IssueRow)item;
			Boolean severity = row.Severity switch
			{
				Severity.Error => ShowErrors,
				Severity.Warning => ShowWarnings,
				_ => ShowInfo,
			};
			if (!severity)
			{
				return false;
			}

			return FilterText.Length == 0
				|| row.Message.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
				|| row.EntryName.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
				|| row.PidText.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
				|| row.FieldLabel.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
		}

		private static void ApplyToEntries(GameWorkspace workspace, IReadOnlyList<ValidationIssue> issues)
		{
			foreach (ProtoEntry entry in workspace.Catalog.All)
			{
				entry.WorstIssue = null;
			}

			foreach (IGrouping<ProtoId?, ValidationIssue> group in issues.Where(i => i.Pid is not null).GroupBy(i => i.Pid))
			{
				ProtoEntry? entry = workspace.Catalog.Find(group.Key!.Value);
				if (entry is not null)
				{
					entry.WorstIssue = group.Max(i => i.Severity);
				}
			}
		}

		private void Reset()
		{
			_rows = [];
			View = null;
			ErrorCount = WarningCount = InfoCount = 0;
			HasRun = false;
			Summary = Loc.Get("validation.notRun");
			RunCommand.NotifyCanExecuteChanged();
		}
	}
}