namespace ProtoForge.Core.Tests.Integration
{
	using System;
	using System.IO;
	using System.Linq;
	using System.Threading.Tasks;
	using Xunit;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Templates;
	using ProtoForge.Infrastructure.Layout;
	using ProtoForge.Infrastructure.Workspace;

	/// <summary>Edits a real game in memory and saves into a temporary output folder (the game files are never touched).</summary>
	public class WorkspaceSaveTests : IDisposable
	{
		private readonly String _output = Path.Combine(Path.GetTempPath(), "protoforge-out-" + Guid.NewGuid().ToString("N"));

		public void Dispose()
		{
			if (Directory.Exists(_output))
			{
				Directory.Delete(_output, recursive: true);
			}
		}

		[SkippableFact]
		public async Task SaveWritesOnlyChangedFiles_AndTheGameCanBeReopened()
		{
			String? game = TestEnvironment.Fallout2Folder;
			Skip.If(game is null, "No Fallout 2 installation found.");

			GameLayout layout = GameLayoutDiscovery.FromGameFolder(game!).Layout;
			layout.OutputFolder = _output;
			WorkspaceOptions options = new() { LoadedTypes = [ProtoObjectType.Item, ProtoObjectType.Critter] };

			ProtoId pistolId;
			ProtoId copyId;
			using (GameWorkspace workspace = await GameWorkspace.OpenAsync(layout, options))
			{
				Assert.False(workspace.HasUnsavedChanges);
				ProtoEntry pistol = workspace.Catalog.GetEntries(ProtoObjectType.Item).First(e => e.Name == "10mm Pistol");
				pistolId = pistol.Pid;
				Int32 originalMax = pistol.File!.Get(FieldKeys.MaxDamage);

				workspace.SetField([pistol], pistol.File.Schema.Require(FieldKeys.MaxDamage), originalMax + 5);
				ProtoEntry copy = workspace.Duplicate(pistol, " (copy)");
				copyId = copy.Pid;
				Assert.True(workspace.HasUnsavedChanges);

				SavePlan plan = workspace.CreateSavePlan();
				Assert.Equal(2, plan.Items.Count(i => i.Kind == SaveItemKind.Proto));
				Assert.Single(plan.Items, i => i.VirtualPath == "proto/items/items.lst");
				Assert.Single(plan.Items, i => i.VirtualPath == "text/english/game/pro_item.msg");
				Assert.Equal(4, plan.Items.Count);

				SaveResult result = workspace.Save(plan);
				Assert.True(result.Succeeded, String.Join("; ", result.Errors));
				Assert.False(workspace.HasUnsavedChanges);
				Assert.False(workspace.Undo.HasUnsavedChanges);
				Assert.Empty(workspace.CreateSavePlan().Items);
			}

			Assert.True(File.Exists(Path.Combine(_output, "proto", "items", "items.lst")));

			// Reopen: the output folder is the highest priority layer, so the edits are visible.
			using GameWorkspace reopened = await GameWorkspace.OpenAsync(layout, options);
			ProtoEntry pistolAgain = reopened.Catalog.Find(pistolId)!;
			ProtoEntry copyAgain = reopened.Catalog.Find(copyId)!;
			Assert.Equal("10mm Pistol (copy)", copyAgain.Name);
			Assert.Equal(pistolAgain.File!.Get(FieldKeys.MaxDamage), copyAgain.File!.Get(FieldKeys.MaxDamage));
			Assert.Equal(reopened.OutputSourceName, pistolAgain.SourceName);
			Assert.Equal(copyAgain.Index * 100, copyAgain.File.MessageId);
		}

		[SkippableFact]
		public async Task VanillaVersion_IsAvailableForOverriddenFiles()
		{
			String? game = TestEnvironment.Fallout2Folder;
			Skip.If(game is null, "No Fallout 2 installation found.");

			GameLayout layout = GameLayoutDiscovery.FromGameFolder(game!).Layout;
			layout.OutputFolder = _output;
			using GameWorkspace workspace = await GameWorkspace.OpenAsync(layout, new WorkspaceOptions { LoadedTypes = [ProtoObjectType.Item] });

			// Find a prototype that a mod (rpu.dat) overrides.
			ProtoEntry? overridden = workspace.Catalog.GetEntries(ProtoObjectType.Item).FirstOrDefault(e => workspace.Locate(e).Count > 1);
			Assert.NotNull(overridden);

			ProtoFile? vanilla = workspace.LoadVanilla(overridden);

			Assert.NotNull(vanilla);
			Assert.Equal(overridden.Kind, vanilla.Kind);
		}

		[SkippableFact]
		public async Task Template_CreatesANewPrototypeAtTheEndOfTheList()
		{
			String? game = TestEnvironment.Fallout2Folder;
			Skip.If(game is null, "No Fallout 2 installation found.");

			GameLayout layout = GameLayoutDiscovery.FromGameFolder(game!).Layout;
			layout.OutputFolder = _output;
			using GameWorkspace workspace = await GameWorkspace.OpenAsync(layout, new WorkspaceOptions { LoadedTypes = [ProtoObjectType.Item] });
			Int32 before = workspace.Catalog.GetEntries(ProtoObjectType.Item).Count;

			ProtoEntry created = workspace.CreateProto(BuiltInTemplates.Default(ProtoKind.Weapon), "Test blaster", "Made by a test");

			Assert.Equal(before + 1, created.Index);
			Assert.Equal(ProtoKind.Weapon, created.Kind);
			Assert.Equal("Test blaster", created.Name);
			Assert.True(workspace.CanDelete(created));

			workspace.Undo.Undo();
			Assert.Equal(before, workspace.Catalog.GetEntries(ProtoObjectType.Item).Count);
			Assert.False(workspace.HasUnsavedChanges);
		}
	}
}