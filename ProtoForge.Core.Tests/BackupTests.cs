namespace ProtoForge.Core.Tests
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Threading.Tasks;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Infrastructure.Layout;
	using ProtoForge.Infrastructure.Workspace;
	using Xunit;

	/// <summary>The backups of a save and the way back.</summary>
	public class BackupTests : IDisposable
	{
		private readonly String _output = Path.Combine(Path.GetTempPath(), "protoforge-backup-" + Guid.NewGuid().ToString("N"));

		public void Dispose()
		{
			if (Directory.Exists(_output))
			{
				Directory.Delete(_output, recursive: true);
			}
		}

		[Fact]
		public void Restore_PutsBackTheReplacedFile_AndRemovesTheCreatedOne_AndCanBeUndone()
		{
			Directory.CreateDirectory(Path.Combine(_output, "proto", "items"));
			String replaced = Path.Combine(_output, "proto", "items", "a.pro");
			String created = Path.Combine(_output, "proto", "items", "b.pro");
			File.WriteAllText(replaced, "new");
			File.WriteAllText(created, "created");

			String folder = Path.Combine(_output, BackupManager.FolderName, "20260101-120000");
			Directory.CreateDirectory(Path.Combine(folder, "proto", "items"));
			File.WriteAllText(Path.Combine(folder, "proto", "items", "a.pro"), "old");
			BackupInfo backup = new(folder, "20260101-120000", new DateTime(2026, 1, 1, 12, 0, 0), [new BackupEntry("proto/items/a.pro", BackupAction.Replaced), new BackupEntry("proto/items/b.pro", BackupAction.Created)], HasManifest: true);

			RestoreResult result = BackupManager.Restore(_output, backup);

			Assert.True(result.Succeeded);
			Assert.Equal(1, result.Restored);
			Assert.Equal(1, result.Removed);
			Assert.Equal("old", File.ReadAllText(replaced));
			Assert.False(File.Exists(created));

			// The restore made a backup of what it overwrote: restoring that one brings the files back.
			BackupInfo safety = Assert.Single(BackupManager.List(_output), b => b.Folder == result.SafetyBackupFolder);
			Assert.True(BackupManager.Restore(_output, safety).Succeeded);
			Assert.Equal("new", File.ReadAllText(replaced));
			Assert.Equal("created", File.ReadAllText(created));
		}

		[Fact]
		public void Restore_RefusesAPathOutsideTheOutputFolder()
		{
			String folder = Path.Combine(_output, BackupManager.FolderName, "20260101-120000");
			Directory.CreateDirectory(folder);
			BackupInfo backup = new(folder, "20260101-120000", DateTime.Now, [new BackupEntry("../outside.pro", BackupAction.Replaced)], HasManifest: true);

			RestoreResult result = BackupManager.Restore(_output, backup);

			Assert.False(result.Succeeded);
			Assert.False(File.Exists(Path.Combine(Directory.GetParent(_output)!.FullName, "outside.pro")));
		}

		[Fact]
		public void List_ReadsABackupWithoutManifest_AndPruneKeepsTheNewest()
		{
			foreach (String name in new[] { "20260101-100000", "20260102-100000", "20260103-100000" })
			{
				Directory.CreateDirectory(Path.Combine(_output, BackupManager.FolderName, name, "proto", "items"));
				File.WriteAllText(Path.Combine(_output, BackupManager.FolderName, name, "proto", "items", "a.pro"), name);
			}

			IReadOnlyList<BackupInfo> list = BackupManager.List(_output);

			Assert.Equal("20260103-100000", list[0].Name);
			Assert.False(list[0].HasManifest);
			Assert.Equal("proto/items/a.pro", Assert.Single(list[0].Entries).Path);

			Assert.Equal(2, BackupManager.Prune(_output, keep: 1));
			Assert.Equal(["20260103-100000"], BackupManager.List(_output).Select(b => b.Name));
		}

		[SkippableFact]
		public async Task Save_WritesAManifest_AndRestoringTheLatestBackupBringsTheOldFileBack()
		{
			String? game = TestEnvironment.Fallout2Folder;
			Skip.If(game is null, "No Fallout 2 installation found.");
			GameLayout layout = GameLayoutDiscovery.FromGameFolder(game!).Layout;
			layout.OutputFolder = _output;
			using GameWorkspace workspace = await GameWorkspace.OpenAsync(layout, new WorkspaceOptions { LoadedTypes = [ProtoObjectType.Item] });
			ProtoEntry pistol = workspace.Catalog.GetEntries(ProtoObjectType.Item).First(e => e.Name == "10mm Pistol");
			String path = Path.Combine(_output, "proto", "items", pistol.FileName);

			workspace.SetField([pistol], FieldKeys.MaxDamage, pistol.File!.Get(FieldKeys.MaxDamage) + 3);
			Assert.True(workspace.Save(workspace.CreateSavePlan()).Succeeded);
			Byte[] first = File.ReadAllBytes(path);
			BackupInfo created = Assert.Single(BackupManager.List(_output));
			Assert.Contains(created.Entries, e => e.Path == "proto/items/" + pistol.FileName && e.Action == BackupAction.Created);

			workspace.SetField([pistol], FieldKeys.MaxDamage, pistol.File!.Get(FieldKeys.MaxDamage) + 5);
			SaveResult second = workspace.Save(workspace.CreateSavePlan());
			Assert.True(second.Succeeded);
			Assert.NotEqual(first, File.ReadAllBytes(path));

			BackupInfo latest = BackupManager.List(_output).First(b => b.Folder == second.BackupFolder);
			Assert.Contains(latest.Entries, e => e.Path == "proto/items/" + pistol.FileName && e.Action == BackupAction.Replaced);
			Assert.True(BackupManager.Restore(_output, latest).Succeeded);
			Assert.Equal(first, File.ReadAllBytes(path));

			// The first backup says the file did not exist before that save: restoring it removes the file.
			Assert.True(BackupManager.Restore(_output, created).Succeeded);
			Assert.False(File.Exists(path));
		}
	}
}