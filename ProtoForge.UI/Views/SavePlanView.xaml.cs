namespace ProtoForge.UI.Views
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Windows.Controls;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	public sealed record SavePlanRow(String Glyph, String Path, String Description, String Badge);

	/// <summary>Shows exactly which files a save will create or replace, before anything is written.</summary>
	public partial class SavePlanView : UserControl
	{
		public SavePlanView(SavePlan plan, Boolean backup, String? warning = null)
		{
			Warning = warning ?? String.Empty;
			Summary = Loc.Format("save.summary", plan.Items.Count, plan.Items.Count(i => i.ReplacesExisting));
			OutputFolder = plan.OutputFolder;
			BackupNote = backup ? Loc.Get("save.backupOn") : Loc.Get("save.backupOff");
			Items =
			[
				.. plan.Items.Select(item => new SavePlanRow(
					item.Kind switch
					{
						SaveItemKind.Proto => "▣",
						SaveItemKind.List => "☰",
						SaveItemKind.AiPackets => "⚙",
						SaveItemKind.Art => "▨",
						SaveItemKind.DeleteOverride => "✖",
						_ => "¶",
					},
					item.AbsolutePath ?? item.VirtualPath,
					item.Description,
					item.Kind == SaveItemKind.DeleteOverride ? Loc.Get("save.deletes") : item.ReplacesExisting ? Loc.Get("save.replaces") : Loc.Get("save.creates"))),
			];
			DataContext = this;
			InitializeComponent();
		}

		/// <summary>Shown in the caution color above the files (the output folder is inside the game, for instance).</summary>
		public String Warning { get; }

		public String Summary { get; }

		public String OutputFolder { get; }

		public String BackupNote { get; }

		public IReadOnlyList<SavePlanRow> Items { get; }
	}
}