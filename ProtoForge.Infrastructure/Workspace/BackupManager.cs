namespace ProtoForge.Infrastructure.Workspace
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.IO;
	using System.Linq;
	using System.Text.Json;
	using ProtoForge.Core;
	using ProtoForge.Core.Vfs;

	/// <summary>What a save did to one file.</summary>
	public enum BackupAction
	{
		/// <summary>The file existed and was overwritten: the backup holds the previous content.</summary>
		Replaced,

		/// <summary>The file did not exist: restoring deletes it.</summary>
		Created,

		/// <summary>The file was deleted: the backup holds the content it had.</summary>
		Deleted,
	}

	/// <summary>One file of a backup. <see cref="Absolute"/> is set for the files that live outside the output folder.</summary>
	public sealed record BackupEntry(String Path, BackupAction Action, String? Absolute = null);

	/// <summary>One timestamped backup folder: when it was made and which files it covers.</summary>
	public sealed record BackupInfo(String Folder, String Name, DateTime Time, IReadOnlyList<BackupEntry> Entries, Boolean HasManifest);

	public sealed record RestoreResult(Int32 Restored, Int32 Removed, String? SafetyBackupFolder, IReadOnlyList<String> Errors)
	{
		public Boolean Succeeded
		{
			get { return Errors.Count == 0; }
		}
	}

	/// <summary>
	/// The copies of the files a save replaces, and the way back. Each save writes a timestamped folder under <c>.protoforge-backup</c>
	/// with the previous version of every file it touches and a <c>manifest.json</c> that also lists the files it created,
	/// so restoring puts the output folder back exactly as it was before that save.
	/// </summary>
	public static class BackupManager
	{
		public const String FolderName = ".protoforge-backup";

		private const String ManifestName = "manifest.json";
		private const String ExternalFolder = "external";

		/// <summary>The newest backups that are kept; older ones are deleted after a save.</summary>
		public const Int32 DefaultKeep = 30;

		private static readonly JsonSerializerOptions Json = new()
		{
			WriteIndented = true,
			Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
		};

		/// <summary>The backups of an output folder, newest first.</summary>
		public static IReadOnlyList<BackupInfo> List(String outputFolder)
		{
			String root = Path.Combine(outputFolder, FolderName);
			if (!Directory.Exists(root))
			{
				return [];
			}

			List<BackupInfo> result = [];
			foreach (String folder in Directory.EnumerateDirectories(root))
			{
				try
				{
					result.Add(Read(folder));
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
				{
					// A folder that cannot be read is not offered; it is still there for a manual recovery.
				}
			}

			return [.. result.OrderByDescending(b => b.Time).ThenByDescending(b => b.Name, StringComparer.Ordinal)];
		}

		/// <summary>Deletes the oldest backups so that at most <paramref name="keep"/> remain.</summary>
		public static Int32 Prune(String outputFolder, Int32 keep = DefaultKeep)
		{
			Int32 removed = 0;
			foreach (BackupInfo old in List(outputFolder).Skip(Math.Max(1, keep)))
			{
				try
				{
					Directory.Delete(old.Folder, recursive: true);
					removed++;
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					// A backup that is in use stays; the next save tries again.
				}
			}

			return removed;
		}

		/// <summary>
		/// Puts the files of a backup back. The current state of every file it touches is backed up first, so a restore can itself be undone.
		/// </summary>
		public static RestoreResult Restore(String outputFolder, BackupInfo backup)
		{
			List<String> errors = [];
			Int32 restored = 0;
			Int32 removed = 0;
			BackupRecorder safety = new(outputFolder);

			foreach (BackupEntry entry in backup.Entries)
			{
				String? target = entry.Absolute ?? GamePath.ResolveInside(outputFolder, entry.Path);
				if (target is null)
				{
					errors.Add(UserTexts.Format("err.pathNotAllowed", "{0}: the path is not allowed.", entry.Path));
					continue;
				}

				try
				{
					if (entry.Action == BackupAction.Created)
					{
						if (File.Exists(target))
						{
							safety.Keep(target, entry.Path, entry.Absolute, BackupAction.Deleted);
							File.SetAttributes(target, FileAttributes.Normal);
							File.Delete(target);
							removed++;
						}

						continue;
					}

					String source = GetCopyPath(backup.Folder, entry);
					if (!File.Exists(source))
					{
						errors.Add(UserTexts.Format("backup.missingCopy", "{0}: the copy is missing from the backup.", entry.Path));
						continue;
					}

					if (File.Exists(target))
					{
						safety.Keep(target, entry.Path, entry.Absolute, BackupAction.Replaced);
						File.SetAttributes(target, FileAttributes.Normal);
					}
					else
					{
						safety.Created(entry.Path, entry.Absolute);
					}

					Directory.CreateDirectory(Path.GetDirectoryName(target)!);
					File.Copy(source, target, overwrite: true);
					restored++;
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					errors.Add($"{entry.Path}: {UserTexts.Describe(ex)}");
				}
			}

			safety.Commit();
			return new RestoreResult(restored, removed, safety.Folder, errors);
		}

		internal static String GetCopyPath(String backupFolder, BackupEntry entry)
		{
			return entry.Absolute is null
				? Path.Combine(backupFolder, entry.Path.Replace('/', Path.DirectorySeparatorChar))
				: Path.Combine(backupFolder, ExternalFolder, Path.GetFileName(entry.Absolute));
		}

		internal static String CreateFolder(String outputFolder)
		{
			String stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
			String folder = Path.Combine(outputFolder, FolderName, stamp);
			for (Int32 suffix = 2; Directory.Exists(folder); suffix++)
			{
				folder = Path.Combine(outputFolder, FolderName, $"{stamp}-{suffix}");
			}

			return folder;
		}

		internal static void WriteManifest(String folder, IReadOnlyList<BackupEntry> entries)
		{
			Directory.CreateDirectory(folder);
			File.WriteAllText(Path.Combine(folder, ManifestName), JsonSerializer.Serialize(entries, Json));
		}

		private static BackupInfo Read(String folder)
		{
			String name = Path.GetFileName(folder);
			DateTime time = DateTime.TryParseExact(name.Length >= 15 ? name[..15] : name, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
				? parsed
				: Directory.GetCreationTime(folder);

			String manifest = Path.Combine(folder, ManifestName);
			if (File.Exists(manifest))
			{
				List<BackupEntry> entries = JsonSerializer.Deserialize<List<BackupEntry>>(File.ReadAllText(manifest), Json) ?? [];
				return new BackupInfo(folder, name, time, entries, HasManifest: true);
			}

			// A backup from a version that wrote no manifest: every copy in it is a replaced file.
			List<BackupEntry> legacy = [];
			foreach (String file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
			{
				String relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
				if (!relative.StartsWith(ExternalFolder + "/", StringComparison.Ordinal))
				{
					legacy.Add(new BackupEntry(relative, BackupAction.Replaced));
				}
			}

			return new BackupInfo(folder, name, time, legacy, HasManifest: false);
		}
	}

	/// <summary>Collects the backup of one save (or one restore): copies each file before it is touched, then writes the manifest.</summary>
	internal sealed class BackupRecorder
	{
		private readonly String _output;
		private readonly List<BackupEntry> _entries = [];
		private String? _folder;

		public BackupRecorder(String outputFolder)
		{
			_output = outputFolder;
		}

		/// <summary>The backup folder, or null while nothing was recorded.</summary>
		public String? Folder
		{
			get { return _folder; }
		}

		/// <summary>Copies the current content of <paramref name="target"/> into the backup.</summary>
		public void Keep(String target, String virtualPath, String? absolute, BackupAction action)
		{
			BackupEntry entry = new(virtualPath, action, absolute);
			_folder ??= BackupManager.CreateFolder(_output);
			String copy = BackupManager.GetCopyPath(_folder, entry);
			Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
			File.Copy(target, copy, overwrite: true);
			_entries.Add(entry);
		}

		/// <summary>Notes a file that did not exist: restoring will delete it.</summary>
		public void Created(String virtualPath, String? absolute)
		{
			_folder ??= BackupManager.CreateFolder(_output);
			_entries.Add(new BackupEntry(virtualPath, BackupAction.Created, absolute));
		}

		public void Commit()
		{
			if (_folder is not null && _entries.Count > 0)
			{
				BackupManager.WriteManifest(_folder, _entries);
			}
		}
	}
}