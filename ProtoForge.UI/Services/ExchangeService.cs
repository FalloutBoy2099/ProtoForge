namespace ProtoForge.UI.Services
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text;
	using System.Text.Json;
	using System.Threading.Tasks;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Serialization;
	using ProtoForge.Infrastructure.Workspace;

	/// <summary>JSON and CSV export/import shared by the library and the tools pages.</summary>
	public sealed class ExchangeService
	{
		private readonly WorkspaceService _workspace;
		private readonly IDialogService _dialogs;
		private readonly INotifier _notifier;

		public ExchangeService(WorkspaceService workspace, IDialogService dialogs, INotifier notifier)
		{
			_workspace = workspace;
			_dialogs = dialogs;
			_notifier = notifier;
		}

		public const String Producer = "ProtoForge 1.0 (by Kain)";

		public Boolean ExportJson(IReadOnlyCollection<ProtoEntry> entries)
		{
			if (entries.Count == 0)
			{
				_notifier.Warning(Loc.Get("export.title"), Loc.Get("export.nothing"));
				return false;
			}

			String? path = _dialogs.PickSaveFile(Loc.Get("export.jsonTitle"), "JSON (*.json)|*.json", "protos.json");
			if (path is null)
			{
				return false;
			}

			File.WriteAllText(path, ProtoJson.Export(entries, Producer), new UTF8Encoding(false));
			_notifier.Success(Loc.Get("export.title"), Loc.Format("export.done", entries.Count, Path.GetFileName(path)));
			return true;
		}

		/// <summary>Exports one CSV file per prototype kind into a folder chosen by the user (after asking which columns and which kind of values).</summary>
		public async Task<Boolean> ExportCsvAsync(IReadOnlyCollection<ProtoEntry> entries, Char delimiter)
		{
			if (entries.Count == 0)
			{
				_notifier.Warning(Loc.Get("export.title"), Loc.Get("export.nothing"));
				return false;
			}

			IEnumerable<ProtoKind> kinds = entries.Where(e => e.File is not null).Select(e => e.File!.Kind).Distinct();
			Views.CsvOptionsViewModel options = new(ProtoCsv.GetFieldKeys(kinds), Loc.Format("csv.summary", entries.Count));
			if (!await _dialogs.ShowAsync(Loc.Get("csv.title"), new Views.CsvOptionsView(options), Loc.Get("csv.export"), Loc.Get("common.cancel")))
			{
				return false;
			}

			String? folder = _dialogs.PickFolder(Loc.Get("export.csvTitle"));
			if (folder is null)
			{
				return false;
			}

			CsvExportOptions export = new()
			{
				Columns = options.GetSelectedColumns(),
				Readable = options.Readable,
				Choices = _workspace.Current?.Choices,
			};
			IReadOnlyDictionary<ProtoKind, String> files = ProtoCsv.Export(entries, delimiter, export);
			foreach (KeyValuePair<ProtoKind, String> file in files)
			{
				// UTF-8 with BOM so that spreadsheets detect accents correctly.
				File.WriteAllText(Path.Combine(folder, file.Key + ".csv"), file.Value, new UTF8Encoding(true));
			}

			_notifier.Success(Loc.Get("export.title"), Loc.Format("export.csvDone", files.Count, folder));
			return true;
		}

		public async Task<Boolean> ImportAsync(Char delimiter)
		{
			GameWorkspace? workspace = _workspace.Current;
			if (workspace is null)
			{
				return false;
			}

			String? path = _dialogs.PickOpenFile(Loc.Get("import.title"), "JSON / CSV (*.json;*.csv)|*.json;*.csv");
			if (path is null)
			{
				return false;
			}

			ImportPlan plan;
			try
			{
				String text = await File.ReadAllTextAsync(path);
				plan = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
					? ProtoCsv.CreatePlan(text, workspace.Catalog, delimiter, workspace.Choices)
					: ProtoJson.CreatePlan(text, workspace.Catalog);
			}
			catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
			{
				_notifier.Error(Loc.Get("import.title"), ex.Message);
				return false;
			}

			if (plan.IsEmpty)
			{
				_notifier.Info(Loc.Get("import.title"), plan.Warnings.Count > 0
					? Loc.Format("import.nothingWithWarnings", plan.Warnings.Count)
					: Loc.Get("import.nothing"));
				return false;
			}

			StringBuilder summary = new();
			summary.AppendLine(Loc.Format("import.summary", plan.ProtoCount, plan.FieldChanges.Count, plan.TextChanges.Count));
			foreach (String warning in plan.Warnings.Take(8))
			{
				summary.AppendLine("• " + warning);
			}

			if (plan.Warnings.Count > 8)
			{
				summary.AppendLine(Loc.Format("import.moreWarnings", plan.Warnings.Count - 8));
			}

			if (!await _dialogs.ConfirmAsync(Loc.Get("import.title"), summary.ToString(), Loc.Get("import.apply")))
			{
				return false;
			}

			workspace.Apply(plan, Loc.Format("import.undo", Path.GetFileName(path)));
			_notifier.Success(Loc.Get("import.title"), Loc.Format("import.done", plan.ProtoCount));
			return true;
		}
	}
}