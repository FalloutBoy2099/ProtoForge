namespace ProtoForge.Infrastructure.Settings
{
	using System;
	using System.IO;
	using System.Linq;
	using System.Text.Json;
	using System.Text.Json.Serialization;
	using Layout;

	/// <summary>Reads and writes <see cref="AppSettings"/>. A corrupt or missing file never prevents the application from starting.</summary>
	public sealed class SettingsService
	{
		private const Int32 MaxRecentProjects = 12;

		private static readonly JsonSerializerOptions Options = new()
		{
			WriteIndented = true,
			Converters = { new JsonStringEnumConverter() },
		};

		private readonly String _path;

		public SettingsService(String? path = null)
		{
			_path = path ?? Path.Combine(GetDataFolder(), "settings.json");
			Current = Load();
		}

		public AppSettings Current { get; private set; }

		public static String GetDataFolder()
		{
			String folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProtoForge");
			Directory.CreateDirectory(folder);
			return folder;
		}

		public void Save()
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
				String temp = _path + ".tmp";
				File.WriteAllText(temp, JsonSerializer.Serialize(Current, Options));
				File.Move(temp, _path, overwrite: true);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				// Preferences are a convenience: failing to persist them must not interrupt editing.
			}
		}

		public void RememberProject(GameLayout layout)
		{
			String key = layout.OutputFolder ?? layout.GameFolder ?? String.Empty;
			Current.RecentProjects.RemoveAll(p => String.Equals(p.Layout.OutputFolder ?? p.Layout.GameFolder, key, StringComparison.OrdinalIgnoreCase));
			Current.RecentProjects.Insert(0, new RecentProject { Name = layout.DisplayName, LastOpened = DateTime.Now, Layout = layout.Clone() });
			if (Current.RecentProjects.Count > MaxRecentProjects)
			{
				Current.RecentProjects.RemoveRange(MaxRecentProjects, Current.RecentProjects.Count - MaxRecentProjects);
			}

			Save();
		}

		public void ForgetProject(RecentProject project)
		{
			Current.RecentProjects.Remove(project);
			Save();
		}

		private AppSettings Load()
		{
			try
			{
				if (File.Exists(_path))
				{
					AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options);
					if (loaded is not null)
					{
						loaded.RecentProjects = [.. (loaded.RecentProjects ?? []).Where(p => p?.Layout is not null)];
						loaded.LoadedTypes ??= [];
						return loaded;
					}
				}
			}
			catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NullReferenceException)
			{
				// Keep a copy of the unreadable file (the next save would replace it), then fall through to defaults.
				try
				{
					File.Copy(_path, _path + ".bad", overwrite: true);
				}
				catch (Exception copy) when (copy is IOException or UnauthorizedAccessException)
				{
					// Nothing more can be done.
				}
			}

			return new AppSettings();
		}
	}
}