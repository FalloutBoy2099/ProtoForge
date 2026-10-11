namespace ProtoForge.Infrastructure.Workspace
{
	using System;
	using System.Collections.Generic;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Stats;

	/// <summary>Per-session choices that do not belong to the game layout.</summary>
	public sealed class WorkspaceOptions
	{
		/// <summary>
		/// The "working set": only these object types are read at start-up. Loading everything takes a couple of seconds
		/// on a full game, but a mod author who only edits items can skip walls and tiles entirely.
		/// </summary>
		public HashSet<ProtoObjectType> LoadedTypes { get; set; } = [.. ProtoObjectTypeExtensions.All];

		/// <summary>Code page of the MSG files; null picks one from the game language.</summary>
		public Int32? MessageCodePage { get; set; }

		/// <summary>Mark saved .pro files as read-only (the engine and some tools expect it).</summary>
		public Boolean ReadOnlyAttribute { get; set; }

		/// <summary>Copy the files that are about to be overwritten into a timestamped backup folder.</summary>
		public Boolean BackupBeforeSave { get; set; } = true;

		public FormulaSet Formula { get; set; } = FormulaSet.Fallout2;

		public WorkspaceOptions Clone()
		{
			return new WorkspaceOptions
			{
				LoadedTypes = [.. LoadedTypes],
				MessageCodePage = MessageCodePage,
				ReadOnlyAttribute = ReadOnlyAttribute,
				BackupBeforeSave = BackupBeforeSave,
				Formula = Formula,
			};
		}
	}

	public sealed record LoadProgress(String Stage, Double Fraction);

	public enum SaveItemKind
	{
		Proto,
		List,
		Message,
		AiPackets,

		/// <summary>A new image or an art list.</summary>
		Art,

		/// <summary>Deletes the override file of the output folder (content is empty).</summary>
		DeleteOverride,
	}

	/// <summary>One file that the save operation will write.</summary>
	public sealed record SaveItem(SaveItemKind Kind, String VirtualPath, String Description, Byte[] Content, Boolean ReplacesExisting, String? AbsolutePath = null);

	/// <summary>
	/// What a save would do, shown to the user before anything is written. ProtoForge never modifies files silently.
	/// </summary>
	public sealed class SavePlan
	{
		public SavePlan(String outputFolder, IReadOnlyList<SaveItem> items)
		{
			OutputFolder = outputFolder;
			Items = items;
		}

		public String OutputFolder { get; }

		public IReadOnlyList<SaveItem> Items { get; }

		public Boolean IsEmpty
		{
			get { return Items.Count == 0; }
		}
	}

	public sealed record SaveResult(Int32 FilesWritten, String? BackupFolder, IReadOnlyList<String> Errors)
	{
		public Boolean Succeeded
		{
			get { return Errors.Count == 0; }
		}
	}
}