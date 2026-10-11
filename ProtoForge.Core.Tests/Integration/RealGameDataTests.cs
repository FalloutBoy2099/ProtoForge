namespace ProtoForge.Core.Tests.Integration
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using System.Text;
	using Xunit;
	using Xunit.Abstractions;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Dat;
	using ProtoForge.Core.Formats.Frm;
	using ProtoForge.Core.Formats.Lst;
	using ProtoForge.Core.Formats.Msg;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Validation;
	using ProtoForge.Infrastructure.Layout;
	using ProtoForge.Infrastructure.Workspace;

	/// <summary>
	/// Runs the parsers against an installed Fallout 2 (read-only). Skipped when no installation is available.
	/// </summary>
	public class RealGameDataTests
	{
		private readonly ITestOutputHelper _output;

		public RealGameDataTests(ITestOutputHelper output)
		{
			_output = output;
		}

		[SkippableFact]
		public void Dat2_ListsAndReadsFiles()
		{
			String? game = TestEnvironment.Fallout2Folder;
			Skip.If(game is null, "No Fallout 2 installation found.");

			using DatArchive master = DatArchive.Open(Path.Combine(game!, "master.dat"));

			Assert.Equal(DatFormat.Fallout2, master.Format);
			Assert.True(master.Count > 10000);
			Byte[]? list = master.Read("proto/items/items.lst");
			Assert.NotNull(list);
			Assert.True(LstFile.Parse(list).Count > 400);
			Assert.NotNull(master.Find("PROTO\\ITEMS\\ITEMS.LST"));
		}

		[SkippableFact]
		public void Dat1_ReadsFallout1Archive()
		{
			String? path = TestEnvironment.Fallout1MasterDat;
			Skip.If(path is null, "No Fallout 1 master.dat found.");

			using DatArchive master = DatArchive.Open(path!);

			Assert.Equal(DatFormat.Fallout1, master.Format);
			DatEntry? entry = master.Entries.FirstOrDefault(e => e.Path.EndsWith("items.lst", StringComparison.Ordinal) && e.Path.StartsWith("proto", StringComparison.Ordinal));
			Assert.NotNull(entry);
			Byte[] data = master.Read(entry);
			Assert.Equal(entry.UnpackedSize, data.Length);
			Assert.True(LstFile.Parse(data).Count > 100);

			// Every compressed file must decode to its declared size.
			Int32 decoded = 0;
			foreach (DatEntry e in master.Entries.Where(e => e.IsCompressed).Take(300))
			{
				Assert.Equal(e.UnpackedSize, master.Read(e).Length);
				decoded++;
			}

			_output.WriteLine($"Decoded {decoded} LZSS files.");
		}

		[SkippableFact]
		public void AllItemAndCritterProtos_ParseWithTheExpectedLayout()
		{
			String? game = TestEnvironment.Fallout2Folder;
			Skip.If(game is null, "No Fallout 2 installation found.");

			using DatArchive master = DatArchive.Open(Path.Combine(game!, "master.dat"));
			Int32 checkedCount = 0;
			foreach (DatEntry entry in master.Entries.Where(e => e.Path.StartsWith("proto/", StringComparison.Ordinal) && e.Path.EndsWith(".pro", StringComparison.Ordinal)))
			{
				ProtoObjectType type = entry.Path.Split('/')[1] switch
				{
					"items" => ProtoObjectType.Item,
					"critters" => ProtoObjectType.Critter,
					"scenery" => ProtoObjectType.Scenery,
					"walls" => ProtoObjectType.Wall,
					"tiles" => ProtoObjectType.Tile,
					"misc" => ProtoObjectType.Misc,
					_ => throw new InvalidDataException(entry.Path),
				};

				Byte[] bytes = master.Read(entry);
				ProtoFile? file = ProtoFile.TryParse(bytes, type, out String? error);
				Assert.True(file is not null, $"{entry.Path}: {error}");
				Assert.True(file.HasExpectedLength, $"{entry.Path}: {file.Kind} has {bytes.Length} bytes, expected {file.Kind.GetExpectedSize()}.");
				Assert.Equal(type, file.ObjectType);

				// Unmodified round trip must be byte exact.
				Assert.Equal(bytes, file.ToArray());
				checkedCount++;
			}

			_output.WriteLine($"Parsed {checkedCount} prototypes.");
			Assert.True(checkedCount > 5000);
		}

		[SkippableFact]
		public void MsgFile_RoundTripsByteForByte_AndEditsOnlyTheChangedEntry()
		{
			String? game = TestEnvironment.Fallout2Folder;
			Skip.If(game is null, "No Fallout 2 installation found.");

			using DatArchive master = DatArchive.Open(Path.Combine(game!, "master.dat"));
			Encoding encoding = MsgEncodings.Get(1252);
			foreach (String name in new[] { "pro_item.msg", "pro_crit.msg", "proto.msg", "perk.msg", "stat.msg", "pro_scen.msg" })
			{
				Byte[] original = master.Read($"text/english/game/{name}")!;
				MsgFile file = MsgFile.Parse(original, encoding);

				Assert.True(file.Count > 10, name);
				Assert.Equal(original, file.ToBytes());
				Assert.False(file.IsDirty);
			}

			Byte[] bytes = master.Read("text/english/game/pro_item.msg")!;
			MsgFile items = MsgFile.Parse(bytes, encoding);
			String before = items.ToText();
			items.SetText(100, "Edited name");
			String after = items.ToText();

			Assert.True(items.IsDirty);
			Assert.Equal("Edited name", items.GetText(100));
			// Only one line differs.
			String[] beforeLines = before.Split('\n');
			String[] afterLines = after.Split('\n');
			Assert.Equal(beforeLines.Length, afterLines.Length);
			Assert.Single(beforeLines.Zip(afterLines), p => p.First != p.Second);
		}

		[SkippableFact]
		public void Frm_DecodesItemAndCritterArt()
		{
			String? game = TestEnvironment.Fallout2Folder;
			Skip.If(game is null, "No Fallout 2 installation found.");

			using DatArchive master = DatArchive.Open(Path.Combine(game!, "master.dat"));
			using DatArchive critter = DatArchive.Open(Path.Combine(game!, "critter.dat"));
			Palette palette = new(master.Read("color.pal")!);

			FrmFile item = FrmFile.Parse(master.Read("art/inven/pistol.frm") ?? master.Read("art/inven/uzi.frm")!);
			FrmFile critterArt = FrmFile.Parse(critter.Read("art/critters/hmwarraa.frm")!);

			Assert.Single(item.Directories());
			Assert.Equal(6, critterArt.Directories().Count);
			FrmFrame frame = item.Directories()[0][0];
			Assert.True(frame.Width > 10 && frame.Height > 10);
			Assert.Equal(frame.Width * frame.Height * 4, palette.ToBgra(frame).Length);
			Assert.Contains(frame.Pixels, p => p != 0);
		}

		[SkippableFact]
		public void GameLayoutDiscovery_FindsTheLayersInEnginePriorityOrder()
		{
			String? game = TestEnvironment.Fallout2Folder;
			Skip.If(game is null, "No Fallout 2 installation found.");

			DiscoveryResult result = GameLayoutDiscovery.FromGameFolder(game!);
			List<String> names = [.. result.Layout.Layers.Select(l => l.Name)];
			foreach (String name in names)
			{
				_output.WriteLine(name);
			}

			Assert.Equal("data", names[0]);
			Assert.True(names.IndexOf("rpu.dat") > 0);
			Assert.True(names.IndexOf("rpu.dat") < names.IndexOf("critter.dat"));
			Assert.True(names.IndexOf("critter.dat") < names.IndexOf("master.dat"));
			Assert.Equal(Path.Combine(game!, "data"), result.Layout.OutputFolder);
			Assert.Equal(OpenTargetKind.GameFolder, GameLayoutDiscovery.Classify(game!));
		}

		[SkippableFact]
		public async System.Threading.Tasks.Task Workspace_LoadsTheWholeGame_AndValidates()
		{
			String? game = TestEnvironment.Fallout2Folder;
			Skip.If(game is null, "No Fallout 2 installation found.");

			DiscoveryResult discovery = GameLayoutDiscovery.FromGameFolder(game!);
			Stopwatch watch = Stopwatch.StartNew();
			using GameWorkspace workspace = await GameWorkspace.OpenAsync(discovery.Layout, new WorkspaceOptions());
			watch.Stop();
			_output.WriteLine($"Loaded {workspace.Catalog.Count} prototypes in {watch.ElapsedMilliseconds} ms ({workspace.Files.Layers.Count} layers).");
			foreach (String warning in workspace.Warnings)
			{
				_output.WriteLine("warning: " + warning);
			}

			Assert.True(workspace.Catalog.Count > 5000);
			Assert.True(workspace.Catalog.GetEntries(ProtoObjectType.Item).Count > 400);
			Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20));

			// The Restoration Project data folder overrides some prototypes: sources must reflect the layer that provided each file.
			ProtoEntry pistol = workspace.Catalog.GetEntries(ProtoObjectType.Item).First(e => e.Name.Contains("10mm pistol", StringComparison.OrdinalIgnoreCase));
			Assert.Equal(ProtoKind.Weapon, pistol.Kind);
			_output.WriteLine($"{pistol.Pid} {pistol.Name} from {pistol.SourceName}");
			Assert.False(String.IsNullOrEmpty(pistol.Description));

			Stopwatch validation = Stopwatch.StartNew();
			IReadOnlyList<ValidationIssue> issues = workspace.Validate();
			validation.Stop();
			_output.WriteLine($"Validation: {issues.Count} issues in {validation.ElapsedMilliseconds} ms");
			foreach (IGrouping<String, ValidationIssue> group in issues.GroupBy(i => i.Code).OrderByDescending(g => g.Count()))
			{
				_output.WriteLine($"  {group.Key}: {group.Count()}  e.g. {group.First().Message}");
			}

			// Every PRO file of the game can be read (the validator may still report data oddities, which exist in real mods).
			Assert.DoesNotContain(issues, i => i.Code is "FileUnreadable" or "BadSize");

			// Choices come from the game texts.
			Assert.Equal("Strength", workspace.Choices.Describe("Stat", 0));
			Assert.Contains(workspace.Choices.Get("Caliber"), c => c.Label.Contains("mm", StringComparison.OrdinalIgnoreCase));
		}
	}

	internal static class FrmTestExtensions
	{
		public static IReadOnlyList<IReadOnlyList<FrmFrame>> Directories(this FrmFile file)
		{
			return file.Directions;
		}
	}
}