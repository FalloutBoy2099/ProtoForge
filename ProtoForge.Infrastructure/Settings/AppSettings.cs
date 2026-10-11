namespace ProtoForge.Infrastructure.Settings
{
	using System;
	using System.Collections.Generic;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Stats;
	using Layout;

	/// <summary>A game or mod previously opened, with everything needed to reopen it.</summary>
	public sealed class RecentProject
	{
		public String Name { get; set; } = String.Empty;

		public DateTime LastOpened { get; set; }

		public GameLayout Layout { get; set; } = new();
	}

	/// <summary>User preferences, stored as JSON in the application data folder.</summary>
	public sealed class AppSettings
	{
		/// <summary>"Dark", "Light" or "System".</summary>
		public String Theme { get; set; } = "Dark";

		/// <summary>UI language code ("en", "fr"); empty follows the operating system.</summary>
		public String UiLanguage { get; set; } = String.Empty;

		public List<RecentProject> RecentProjects { get; set; } = [];

		/// <summary>Game folder used as the vanilla base when opening a mod folder or a DAT archive on its own.</summary>
		public String? BaseGameFolder { get; set; }

		public List<ProtoObjectType> LoadedTypes { get; set; } = [.. ProtoObjectTypeExtensions.All];

		public Int32? MessageCodePage { get; set; }

		public Boolean ReadOnlyAttribute { get; set; }

		public Boolean BackupBeforeSave { get; set; } = true;

		public FormulaSet Formula { get; set; } = FormulaSet.Fallout2;

		public Boolean AdvancedMode { get; set; }

		/// <summary>Use the original game formula for the weapon score instead of the fixed one (sfall).</summary>
		public Boolean OriginalWeaponScore { get; set; }

		/// <summary>Path of the hex editor started by the "external editor" button of the hex view (empty: ask when used).</summary>
		public String ExternalHexEditor { get; set; } = String.Empty;

		public Boolean ConfirmBeforeSave { get; set; } = true;

		public String CsvDelimiter { get; set; } = ";";

		public Boolean HideIdenticalFieldsInCompare { get; set; }

		public Double WindowWidth { get; set; } = 1500;

		public Double WindowHeight { get; set; } = 900;

		public Boolean WindowMaximized { get; set; }
	}
}