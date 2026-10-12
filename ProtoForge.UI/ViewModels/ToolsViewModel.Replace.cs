namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Editing;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	/// <summary>Find and replace in the names and descriptions of the prototypes (scope: everything, a type, a range of numbers).</summary>
	public sealed partial class ToolsViewModel
	{
		[ObservableProperty]
		private String _replaceFind = String.Empty;

		[ObservableProperty]
		private String _replaceWith = String.Empty;

		[ObservableProperty]
		private Boolean _replaceInNames = true;

		[ObservableProperty]
		private Boolean _replaceInDescriptions = true;

		[ObservableProperty]
		private Boolean _replaceMatchCase;

		[ObservableProperty]
		private ExportScope? _replaceScope;

		[ObservableProperty]
		private Double? _replaceFromNumber;

		[ObservableProperty]
		private Double? _replaceToNumber;

		[ObservableProperty]
		private String _replaceStatus = String.Empty;

		/// <summary>Everything, or one type of prototype.</summary>
		public IReadOnlyList<ExportScope> ReplaceScopes { get; } =
		[
			new ExportScope("all", Loc.Get("type.all")),
			.. ProtoObjectTypeExtensions.All.Select(t => new ExportScope(t.ToString(), Loc.Get("type." + t))),
		];

		private IReadOnlyList<TextReplacement> PlanReplacement(GameWorkspace workspace)
		{
			IEnumerable<ProtoEntry> entries = workspace.Catalog.All;
			if (ReplaceScope is { Id: not "all" } scope && Enum.TryParse(scope.Id, out ProtoObjectType type))
			{
				entries = entries.Where(e => e.Type == type);
			}

			if (ReplaceFromNumber is { } from)
			{
				entries = entries.Where(e => e.Pid.Index >= (Int32)from);
			}

			if (ReplaceToNumber is { } to)
			{
				entries = entries.Where(e => e.Pid.Index <= (Int32)to);
			}

			return TextReplacer.Plan(entries, workspace.Texts, ReplaceFind, ReplaceWith, ReplaceInNames, ReplaceInDescriptions, ReplaceMatchCase);
		}

		[RelayCommand]
		private void PreviewReplace()
		{
			if (_workspaceService.Current is not { } workspace)
			{
				return;
			}

			IReadOnlyList<TextReplacement> plan = PlanReplacement(workspace);
			ReplaceStatus = plan.Count == 0
				? Loc.Get("tools.replaceNone")
				: Loc.Format("tools.replaceResult", plan.Sum(r => r.Occurrences), plan.Count);
		}

		[RelayCommand]
		private async Task ApplyReplace()
		{
			if (_workspaceService.Current is not { } workspace)
			{
				return;
			}

			IReadOnlyList<TextReplacement> plan = PlanReplacement(workspace);
			if (plan.Count == 0)
			{
				ReplaceStatus = Loc.Get("tools.replaceNone");
				return;
			}

			Int32 occurrences = plan.Sum(r => r.Occurrences);
			if (!await _dialogs.ConfirmAsync(Loc.Get("tools.replace"), Loc.Format("tools.replaceConfirm", occurrences, plan.Count), Loc.Get("tools.replaceApply")))
			{
				return;
			}

			List<IUndoableAction> actions = [.. plan.Select(r => (IUndoableAction)new TextEditAction(workspace.Texts, r.Entry, r.Name, r.Description))];
			workspace.Undo.Execute(new CompositeAction(actions, Loc.Format("tools.replaceUndo", plan.Count)));
			ReplaceStatus = Loc.Format("tools.replaceDone", occurrences, plan.Count);
			_notifier.Success(Loc.Get("tools.replace"), ReplaceStatus);
		}
	}
}