namespace ProtoForge.Core.Tests
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Ai;
	using ProtoForge.Core.Formats.Lst;
	using ProtoForge.Core.Formats.Map;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Templates;
	using Xunit;

	public class UnusedPrototypesTests
	{
		private static ProtoEntry Add(List<ProtoEntry> entries, ProtoObjectType type, ProtoKind kind, Int32 index)
		{
			ProtoId id = ProtoId.From(type, index);
			ProtoEntry entry = new(id, ProtoDuplicator.FormatFileName(index), BuiltInTemplates.Default(kind).Create(id), null, "master.dat");
			entries.Add(entry);
			return entry;
		}

		[Fact]
		public void Find_KeepsWhatNothingPlacesNamesOrPointsAt()
		{
			List<ProtoEntry> items = [];
			ProtoEntry weapon = Add(items, ProtoObjectType.Item, ProtoKind.Weapon, 1);
			ProtoEntry ammo = Add(items, ProtoObjectType.Item, ProtoKind.Ammo, 2);
			ProtoEntry spareAmmo = Add(items, ProtoObjectType.Item, ProtoKind.Ammo, 3);
			ProtoEntry carried = Add(items, ProtoObjectType.Item, ProtoKind.MiscItem, 4);
			ProtoEntry armor = Add(items, ProtoObjectType.Item, ProtoKind.Armor, 5);
			weapon.File!.TrySet(FieldKeys.AmmoPid, ammo.Pid.Value);
			List<ProtoEntry> critters = [];
			ProtoEntry placed = Add(critters, ProtoObjectType.Critter, ProtoKind.Critter, 1);
			ProtoEntry created = Add(critters, ProtoObjectType.Critter, ProtoKind.Critter, 2);
			ProtoEntry nobody = Add(critters, ProtoObjectType.Critter, ProtoKind.Critter, 3);
			List<ProtoEntry> tiles = [];
			Add(tiles, ProtoObjectType.Tile, ProtoKind.Tile, 1);

			ProtoCatalog catalog = new();
			catalog.Load(ProtoObjectType.Item, new LstFile(items.Select(e => e.FileName)), items);
			catalog.Load(ProtoObjectType.Critter, new LstFile(critters.Select(e => e.FileName)), critters);
			catalog.Load(ProtoObjectType.Tile, new LstFile(tiles.Select(e => e.FileName)), tiles);

			MapSummary map = new("a.map", "A.MAP", 20, 0, 3);
			map.Placed[placed.Pid.Value] = 2;
			map.Carried[carried.Pid.Value] = 1;
			ReferenceIndex index = new([map], [], new Dictionary<Int32, List<String>> { [created.Pid.Value] = ["spawn.int"] }, 1);

			IReadOnlyList<ProtoEntry> unused = UnusedPrototypes.Find(catalog, AiFile.CreateEmpty().ToCatalog(), index);

			// The weapon itself is unused, its ammunition is not; tiles are never judged.
			Assert.Equal([weapon, spareAmmo, armor, nobody], unused);
			Assert.DoesNotContain(unused, e => e.Type == ProtoObjectType.Tile);
		}
	}
}