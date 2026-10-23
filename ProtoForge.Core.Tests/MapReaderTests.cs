namespace ProtoForge.Core.Tests
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Threading.Tasks;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Map;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Infrastructure.Layout;
	using ProtoForge.Infrastructure.Workspace;
	using Xunit;
	using Xunit.Abstractions;

	/// <summary>Reading the objects and the scripts of <c>.map</c> files, and the index built from every map and script of a game.</summary>
	public class MapReaderTests
	{
		private readonly ITestOutputHelper _output;

		public MapReaderTests(ITestOutputHelper output)
		{
			_output = output;
		}

		private static Byte[] BuildSimpleMap()
		{
			List<Byte> bytes = [];

			void Int(Int32 value)
			{
				bytes.AddRange([(Byte)(value >> 24), (Byte)(value >> 16), (Byte)(value >> 8), (Byte)value]);
			}

			void Ints(Int32 count, Int32 value = 0)
			{
				for (Int32 i = 0; i < count; i++)
				{
					Int(value);
				}
			}

			void Object(Int32 pid, Int32 inventory = 0, Int32 extra = 0)
			{
				Ints(11);
				Int(pid);
				Ints(5, -1);
				Int(-1);
				Ints(0);
				Int(inventory);
				Ints(3);
				Ints(extra);
			}

			// Header: version, name (16 bytes), entering tile/elevation/orientation, local variables, script, flags, darkness,
			// global variables, map number, time, then 176 reserved bytes.
			Int(20);
			bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("TEST.MAP"));
			bytes.AddRange(new Byte[8]);
			Ints(3);
			Int(0);
			Int(12);
			Int(2 | 4 | 8);
			Ints(1);
			Int(0);
			Int(-1);
			Int(0);
			Ints(44);

			// Scripts: none of the system, spatial (one, a longer slot), timed and item kinds, one critter script.
			Int(0);
			Int(1);
			for (Int32 slot = 0; slot < 16; slot++)
			{
				if (slot == 0)
				{
					Int(0x01000000);
					Int(-1);
					Ints(2);
					Int(0);
					Int(266);
					Ints(12);
				}
				else
				{
					Int(unchecked((Int32)0xCCCCCCCC));
					Ints(15);
				}
			}

			Ints(2);
			Int(0);
			Int(0);
			Int(1);
			for (Int32 slot = 0; slot < 16; slot++)
			{
				if (slot == 0)
				{
					Int(0x04000000);
					Int(-1);
					Int(0);
					Int(7);
					Ints(12);
				}
				else
				{
					Int(unchecked((Int32)0xCCCCCCCC));
					Ints(15);
				}
			}

			Ints(2);

			// Objects: 4 on the first elevation (the third carries one object), none on the others.
			Int(5);
			Int(4);
			Object(0x02000001);
			Object(0x01000002, extra: 10);
			Object(0x00000010, inventory: 1);
			Ints(1);
			Object(0x00000020, extra: 2);
			Object(0x05000010, extra: 4);
			Int(0);
			Int(0);
			return [.. bytes];
		}

		private static ProtoKind? Kind(Int32 pid)
		{
			return pid switch
			{
				0x02000001 => ProtoKind.SceneryGeneric,
				0x00000010 => ProtoKind.Container,
				0x00000020 => ProtoKind.Weapon,
				_ => null,
			};
		}

		[Fact]
		public void Read_ListsThePlacedAndTheCarriedPrototypes_AndTheScriptsOfTheMap()
		{
			MapSummary map = MapReader.Read("test.map", BuildSimpleMap(), Kind);

			Assert.Equal("TEST.MAP", map.Name);
			Assert.Equal(1, map.Placed[0x02000001]);
			Assert.Equal(1, map.Placed[0x01000002]);
			Assert.Equal(1, map.Placed[0x00000010]);
			Assert.Equal(1, map.Placed[0x05000010]);
			Assert.Equal(1, map.Carried[0x00000020]);
			Assert.False(map.Placed.ContainsKey(0x00000020));
			Assert.Equal(1, map.Scripts[266]);
			Assert.Equal(1, map.Scripts[7]);
		}

		[Fact]
		public void Read_RefusesAMapThatDoesNotEndWhereTheObjectsEnd_OrAnItemOfAnUnknownPrototype()
		{
			Byte[] map = BuildSimpleMap();

			Assert.Throws<InvalidDataException>(() => MapReader.Read("test.map", [.. map, 0, 0, 0, 0], Kind));
			Assert.Throws<InvalidDataException>(() => MapReader.Read("test.map", map[..^20], Kind));
			Assert.Throws<InvalidDataException>(() => MapReader.Read("test.map", map, _ => null));
		}

		[Fact]
		public void FindPids_KeepsOnlyTheKnownPrototypesThatAreNotItems()
		{
			Dictionary<Int32, ProtoKind> kinds = new() { [0x01000010] = ProtoKind.Critter, [5] = ProtoKind.Armor, [0x02000007] = ProtoKind.SceneryGeneric };
			Byte[] script =
			[
				0x00, 0x00, 0xC0, 0x01, 0x01, 0x00, 0x00, 0x10,
				0xC0, 0x01, 0x00, 0x00, 0x00, 0x05,
				0xC0, 0x01, 0x01, 0x00, 0x00, 0x99,
				0xC0, 0x01, 0x02, 0x00, 0x00, 0x07,
			];

			Assert.Equal([0x01000010, 0x02000007], ReferenceIndex.FindPids(script, kinds).Order());
		}

		[SkippableFact]
		public async Task Fallout2_EveryMapIsReadToItsLastByte()
		{
			String? game = TestEnvironment.Fallout2Folder;
			Skip.If(game is null, "No Fallout 2 installation found.");
			GameLayout layout = GameLayoutDiscovery.FromGameFolder(game!).Layout;
			using GameWorkspace workspace = await GameWorkspace.OpenAsync(layout, new WorkspaceOptions());
			Dictionary<Int32, ProtoKind> kinds = workspace.Catalog.All.Where(e => e.Kind is not null).ToDictionary(e => e.Pid.Value, e => e.Kind!.Value);

			ReferenceIndex index = ReferenceIndex.Build(workspace.Files, kinds);

			_output.WriteLine($"{index.Maps.Count} maps read, {index.UnreadableMaps.Count} not; {index.ScriptsByPid.Count} prototypes named by {index.ScriptCount} scripts");
			Assert.Empty(index.UnreadableMaps);
			Assert.True(index.Maps.Count > 100);

			// The script of a map is the line of scripts.lst that carries its name.
			MapSummary abbey = index.Maps.First(m => m.FileName.Equals("abbey.map", StringComparison.OrdinalIgnoreCase));
			Assert.Equal("abbey.int", workspace.ScriptNames[abbey.MapScript].Trim(), ignoreCase: true);

			// A prototype that every game has: a lot of the creatures of Arroyo are on its maps.
			Assert.Contains(index.Maps, m => m.Placed.Count > 100);
			Assert.NotEmpty(index.ScriptsByPid);
		}

		[SkippableFact]
		public async Task Fallout1_MapsAreRead_WhenTheLayoutIsTheSame()
		{
			String? game = TestEnvironment.Fallout1Folder;
			Skip.If(game is null, "No Fallout 1 installation found.");
			GameLayout layout = GameLayoutDiscovery.FromGameFolder(game!).Layout;
			using GameWorkspace workspace = await GameWorkspace.OpenAsync(layout, new WorkspaceOptions());
			Dictionary<Int32, ProtoKind> kinds = workspace.Catalog.All.Where(e => e.Kind is not null).ToDictionary(e => e.Pid.Value, e => e.Kind!.Value);

			ReferenceIndex index = ReferenceIndex.Build(workspace.Files, kinds);

			_output.WriteLine($"Fallout 1: {index.Maps.Count} maps read, {index.UnreadableMaps.Count} not ({String.Join(", ", index.UnreadableMaps.Take(8))})");
			Assert.Empty(index.UnreadableMaps);
			Assert.True(index.Maps.Count > 50);
		}
	}
}