namespace ProtoForge.Core.Tests.Integration
{
	using System;
	using System.IO;
	using System.Linq;
	using System.Threading.Tasks;
	using Xunit;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Frm;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Infrastructure.Layout;
	using ProtoForge.Infrastructure.Workspace;

	/// <summary>Editing features that need a real game: restore, override removal, art, AI packets, kind change. Output goes to a temporary folder.</summary>
	public class WorkspaceFeatureTests : IDisposable
	{
		private readonly String _output = Path.Combine(Path.GetTempPath(), "protoforge-feat-" + Guid.NewGuid().ToString("N"));

		public void Dispose()
		{
			if (Directory.Exists(_output))
			{
				Directory.Delete(_output, recursive: true);
			}
		}

		private async Task<(GameWorkspace Workspace, GameLayout Layout)> OpenAsync(params ProtoObjectType[] types)
		{
			String? game = TestEnvironment.Fallout2Folder;
			Skip.If(game is null, "No Fallout 2 installation found.");
			GameLayout layout = GameLayoutDiscovery.FromGameFolder(game!).Layout;
			layout.OutputFolder = _output;
			return (await GameWorkspace.OpenAsync(layout, new WorkspaceOptions { LoadedTypes = [.. types] }), layout);
		}

		[SkippableFact]
		public async Task RestoreOriginal_PutsTheGameVersionBack_AndCanBeUndone()
		{
			(GameWorkspace workspace, _) = await OpenAsync(ProtoObjectType.Item);
			using GameWorkspace owner = workspace;
			ProtoEntry pistol = workspace.Catalog.GetEntries(ProtoObjectType.Item).First(e => e.Name == "10mm Pistol");
			Int32 original = pistol.File!.Get(FieldKeys.MaxDamage);
			workspace.SetField([pistol], FieldKeys.MaxDamage, original + 7);

			Assert.NotNull(workspace.GetRestorableOriginal(pistol));
			Int32 restored = workspace.RestoreOriginal([pistol], "Restore");

			Assert.Equal(1, restored);
			Assert.Equal(original, pistol.File!.Get(FieldKeys.MaxDamage));
			workspace.Undo.Undo();
			Assert.Equal(original + 7, pistol.File!.Get(FieldKeys.MaxDamage));
		}

		[SkippableFact]
		public async Task RemoveOverride_DeletesTheOutputFile_AndKeepsABackup()
		{
			(GameWorkspace workspace, GameLayout layout) = await OpenAsync(ProtoObjectType.Item);
			ProtoId id;
			using (workspace)
			{
				ProtoEntry pistol = workspace.Catalog.GetEntries(ProtoObjectType.Item).First(e => e.Name == "10mm Pistol");
				id = pistol.Pid;
				workspace.SetField([pistol], FieldKeys.MaxDamage, pistol.File!.Get(FieldKeys.MaxDamage) + 3);
				Assert.True(workspace.Save(workspace.CreateSavePlan()).Succeeded);
				String path = Path.Combine(_output, "proto", "items", pistol.FileName);
				Assert.True(File.Exists(path));

				Assert.Equal(1, workspace.RemoveOverrides([pistol], "Remove"));
				SavePlan plan = workspace.CreateSavePlan();
				Assert.Single(plan.Items, i => i.Kind == SaveItemKind.DeleteOverride);
				Assert.DoesNotContain(plan.Items, i => i.Kind == SaveItemKind.Proto);

				Assert.True(workspace.Save(plan).Succeeded);
				Assert.False(File.Exists(path));
				Assert.NotEmpty(Directory.GetFiles(Path.Combine(_output, ".protoforge-backup"), pistol.FileName, SearchOption.AllDirectories));
				Assert.False(workspace.HasUnsavedChanges);
			}

			using GameWorkspace reopened = await GameWorkspace.OpenAsync(layout, new WorkspaceOptions { LoadedTypes = [ProtoObjectType.Item] });
			Assert.NotEqual(reopened.OutputSourceName, reopened.Catalog.Find(id)!.SourceName);
		}

		[SkippableFact]
		public async Task AddArt_AppearsInTheListAndIsWrittenWithTheSave()
		{
			(GameWorkspace workspace, GameLayout layout) = await OpenAsync(ProtoObjectType.Item);
			using (workspace)
			{
				Int32 before = workspace.Art.GetNames(ArtType.Inventory).Count;
				Byte[] frm = FrmWriter.WriteStatic(new FrmFrame(2, 2, 0, 0, [1, 2, 3, 4]));

				Int32 index = workspace.AddArt(ArtType.Inventory, "pftest.frm", frm);

				Assert.Equal(before, index);
				Assert.Equal("pftest.frm", workspace.Art.GetNames(ArtType.Inventory)[index]);
				Assert.True(workspace.Art.Exists(FrameId.Create(ArtType.Inventory, index)));
				Assert.True(workspace.HasUnsavedChanges);

				workspace.Undo.Undo();
				Assert.Equal(before, workspace.Art.GetNames(ArtType.Inventory).Count);
				Assert.False(workspace.HasUnsavedChanges);

				workspace.Undo.Redo();
				SavePlan plan = workspace.CreateSavePlan();
				Assert.Equal(2, plan.Items.Count(i => i.Kind == SaveItemKind.Art));
				Assert.True(workspace.Save(plan).Succeeded);
				Assert.True(File.Exists(Path.Combine(_output, "art", "inven", "pftest.frm")));
				Assert.False(workspace.HasUnsavedChanges);
			}

			using GameWorkspace reopened = await GameWorkspace.OpenAsync(layout, new WorkspaceOptions { LoadedTypes = [ProtoObjectType.Item] });
			Assert.Contains("pftest.frm", reopened.Art.GetNames(ArtType.Inventory));
		}

		[SkippableFact]
		public async Task AiPackets_AreEditedAndSavedToTheOutputFolder()
		{
			(GameWorkspace workspace, GameLayout layout) = await OpenAsync(ProtoObjectType.Critter);
			Int32 count;
			using (workspace)
			{
				Assert.NotEmpty(workspace.Ai.Sections);
				count = workspace.Ai.Sections.Count;
				ProtoForge.Core.Formats.Ai.AiSection first = workspace.Ai.Sections[0];
				workspace.SetAiValue(workspace.Ai, first.Name, "aggression", "77");
				Int32 number = workspace.AddAiPacket(workspace.Ai, "ProtoForge test packet");
				Assert.True(workspace.HasUnsavedChanges);
				Assert.Equal(number, workspace.AiPackets.Find(number)!.Number);

				SavePlan plan = workspace.CreateSavePlan();
				Assert.Single(plan.Items, i => i.Kind == SaveItemKind.AiPackets);
				Assert.True(workspace.Save(plan).Succeeded);
				Assert.False(workspace.HasUnsavedChanges);
			}

			using GameWorkspace reopened = await GameWorkspace.OpenAsync(layout, new WorkspaceOptions { LoadedTypes = [ProtoObjectType.Critter] });
			Assert.Equal(count + 1, reopened.Ai.Sections.Count);
			Assert.Equal("77", reopened.Ai.Sections[0].GetValue("aggression"));
		}

		[SkippableFact]
		public async Task ChangeKind_TurnsAWeaponIntoAMiscItem_AndBack()
		{
			(GameWorkspace workspace, _) = await OpenAsync(ProtoObjectType.Item);
			using GameWorkspace owner = workspace;
			ProtoEntry pistol = workspace.Catalog.GetEntries(ProtoObjectType.Item).First(e => e.Name == "10mm Pistol");
			Int32 weight = pistol.File!.Get(FieldKeys.Weight);

			Assert.Equal(1, workspace.ChangeKind([pistol], ProtoKind.MiscItem, "Change type"));

			Assert.Equal(ProtoKind.MiscItem, pistol.Kind);
			Assert.Equal(weight, pistol.File!.Get(FieldKeys.Weight));
			Assert.Equal(pistol.Pid.Value, pistol.File.StoredProtoId);
			workspace.Undo.Undo();
			Assert.Equal(ProtoKind.Weapon, pistol.Kind);
		}

		[SkippableFact]
		public async Task SetFlag_TransparencyModesExcludeEachOther()
		{
			(GameWorkspace workspace, _) = await OpenAsync(ProtoObjectType.Item);
			using GameWorkspace owner = workspace;
			ProtoEntry pistol = workspace.Catalog.GetEntries(ProtoObjectType.Item).First(e => e.Name == "10mm Pistol");

			workspace.SetFlag([pistol], FieldKeys.Flags, 0x00008000, true);
			workspace.SetFlag([pistol], FieldKeys.Flags, 0x00010000, true);

			UInt32 flags = unchecked((UInt32)pistol.File!.Get(FieldKeys.Flags));
			Assert.Equal(0x00010000u, flags & FlagSets.TransparencyModes);
		}
	}
}