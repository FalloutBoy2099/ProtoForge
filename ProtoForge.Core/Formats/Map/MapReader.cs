namespace ProtoForge.Core.Formats.Map
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using IO;
	using Pro;

	/// <summary>What a map holds that matters to the prototypes: which prototypes are placed on it and which scripts run on it.</summary>
	public sealed class MapSummary
	{
		public MapSummary(String fileName, String name, Int32 version, Int32 mapScript, Int32 objectCount)
		{
			FileName = fileName;
			Name = name;
			Version = version;
			MapScript = mapScript;
			ObjectCount = objectCount;
		}

		public String FileName { get; }

		/// <summary>The name stored in the header (<c>ARTEMPLE.MAP</c>).</summary>
		public String Name { get; }

		public Int32 Version { get; }

		/// <summary>The line of <c>scripts.lst</c> (counted from 0) of the script of the map itself, or -1 when it has none.</summary>
		public Int32 MapScript { get; }

		/// <summary>Objects on the map, the ones carried in inventories included.</summary>
		public Int32 ObjectCount { get; }

		/// <summary>Prototype id to the number of objects placed on the map.</summary>
		public Dictionary<Int32, Int32> Placed { get; } = [];

		/// <summary>Prototype id to the number of objects found in the inventories of critters and containers.</summary>
		public Dictionary<Int32, Int32> Carried { get; } = [];

		/// <summary>Line of <c>scripts.lst</c> (counted from 0, like the script of a prototype) to the number of script instances of the map that use it.</summary>
		public Dictionary<Int32, Int32> Scripts { get; } = [];
	}

	/// <summary>
	/// Reads the part of a Fallout 1/2 <c>.map</c> file that tells which prototypes are placed on it and which scripts it runs
	/// (the tiles are skipped). The size of the extra data of an object depends on what its prototype is, so the kind of every
	/// prototype has to be given. A map that does not end exactly where the objects end is refused: the layout was misread.
	/// </summary>
	public static class MapReader
	{
		private const Int32 HeaderSize = 0xEC;
		private const Int32 TileBytesPerElevation = 100 * 100 * 4;
		private const Int32 ScriptSlotsPerBlock = 16;
		private const Int32 ScriptSlotBytes = 64;
		private const Int32 ObjectHeaderBytes = 22 * 4;
		private const Int32 CritterExtraBytes = 10 * 4;
		private const Int32 MaxInventoryDepth = 8;
		private const Int32 ElevationLimit = 3;

		/// <summary>The version of the maps of Fallout 1 (Fallout 2 writes 20): its ladders carry one integer less.</summary>
		private const Int32 Fallout1Version = 19;

		public static MapSummary Read(String fileName, Byte[] data, Func<Int32, ProtoKind?> kindOf)
		{
			if (data.Length < HeaderSize)
			{
				throw new InvalidDataException("The map is too short.");
			}

			Int32 version = BigEndian.ReadInt32(data, 0);
			Int32 localVariables = BigEndian.ReadInt32(data, 32);
			Int32 mapScript = BigEndian.ReadInt32(data, 36);
			Int32 flags = BigEndian.ReadInt32(data, 40);
			Int32 globalVariables = BigEndian.ReadInt32(data, 48);
			String name = ReadName(data);

			Int32 elevations = 0;
			foreach (Int32 bit in new[] { 2, 4, 8 })
			{
				if ((flags & bit) == 0)
				{
					elevations++;
				}
			}

			if (localVariables < 0 || globalVariables < 0 || localVariables > 100000 || globalVariables > 100000)
			{
				throw new InvalidDataException("The number of variables is not valid.");
			}

			Reader reader = new(data, HeaderSize + (4 * globalVariables) + (4 * localVariables) + (elevations * TileBytesPerElevation));
			Dictionary<Int32, Int32> scripts = ReadScripts(reader);

			Int32 total = reader.Int();
			// The header counts the script from 1 (0 and -1 mean none), the scripts of the objects count from 0.
			MapSummary map = new(fileName, name, version, mapScript > 0 ? mapScript - 1 : -1, total);
			foreach (KeyValuePair<Int32, Int32> script in scripts)
			{
				map.Scripts[script.Key] = script.Value;
			}

			for (Int32 elevation = 0; elevation < ElevationLimit; elevation++)
			{
				Int32 count = reader.Int();
				if (count < 0 || count > 1_000_000)
				{
					throw new InvalidDataException("The number of objects is not valid.");
				}

				for (Int32 i = 0; i < count; i++)
				{
					ReadObject(reader, map, kindOf, version, carried: false, depth: 0);
				}
			}

			if (reader.Position != data.Length)
			{
				throw new InvalidDataException("The objects do not end where the file ends.");
			}

			return map;
		}

		private static String ReadName(Byte[] data)
		{
			ReadOnlySpan<Byte> raw = data.AsSpan(4, 16);
			Int32 end = raw.IndexOf((Byte)0);
			return System.Text.Encoding.Latin1.GetString(end < 0 ? raw : raw[..end]);
		}

		/// <summary>
		/// Five lists (system, spatial, timed, item, critter), each a count then blocks of 16 slots and a trailer of two integers.
		/// Spatial and timed slots in use are longer than the others by 8 and 4 bytes.
		/// </summary>
		private static Dictionary<Int32, Int32> ReadScripts(Reader reader)
		{
			Dictionary<Int32, Int32> scripts = [];
			for (Int32 type = 0; type < 5; type++)
			{
				Int32 count = reader.Int();
				if (count < 0 || count > 100000)
				{
					throw new InvalidDataException("The number of scripts is not valid.");
				}

				Int32 blocks = (count + ScriptSlotsPerBlock - 1) / ScriptSlotsPerBlock;
				for (Int32 block = 0; block < blocks; block++)
				{
					for (Int32 slot = 0; slot < ScriptSlotsPerBlock; slot++)
					{
						Int32 id = reader.Peek(0);
						Int32 idType = (Int32)((UInt32)id >> 24);
						Int32 extra = idType == 1 ? 8 : idType == 2 ? 4 : 0;
						if (idType == type)
						{
							// After the id and the link come the fields that depend on the type, then the flags and the script number.
							Int32 number = reader.Peek(8 + extra + 4);
							if (number >= 0)
							{
								scripts[number] = scripts.GetValueOrDefault(number) + 1;
							}
						}

						reader.Skip(ScriptSlotBytes + extra);
					}

					reader.Skip(8);
				}
			}

			return scripts;
		}

		private static void ReadObject(Reader reader, MapSummary map, Func<Int32, ProtoKind?> kindOf, Int32 version, Boolean carried, Int32 depth)
		{
			Int32 pid = reader.Peek(44);
			Int32 inventory = reader.Peek(ObjectHeaderBytes - 16);
			reader.Skip(ObjectHeaderBytes);

			Dictionary<Int32, Int32> counts = carried ? map.Carried : map.Placed;
			counts[pid] = counts.GetValueOrDefault(pid) + 1;

			ProtoObjectType type = (ProtoObjectType)((UInt32)pid >> 24);
			ProtoKind? kind = kindOf(pid);
			switch (type)
			{
				case ProtoObjectType.Item:
					reader.Skip(kind switch
					{
						ProtoKind.Weapon => 8,
						ProtoKind.Ammo or ProtoKind.MiscItem or ProtoKind.Key => 4,
						ProtoKind.Armor or ProtoKind.Container or ProtoKind.Drug => 0,
						_ => throw new InvalidDataException($"The item {pid:X8} is not a known prototype."),
					});
					break;
				case ProtoObjectType.Critter:
					reader.Skip(CritterExtraBytes);
					break;
				case ProtoObjectType.Scenery:
					reader.Skip(kind switch
					{
						ProtoKind.SceneryDoor => 4,
						ProtoKind.SceneryStairs or ProtoKind.SceneryElevator => 8,
						ProtoKind.SceneryLadderBottom or ProtoKind.SceneryLadderTop => version == Fallout1Version ? 4 : 8,
						ProtoKind.SceneryGeneric => 0,
						_ => throw new InvalidDataException($"The scenery {pid:X8} is not a known prototype."),
					});
					break;
				case ProtoObjectType.Misc:
					// Only the exit grids carry data: a destination map, tile, elevation and orientation.
					if (pid >= 0x05000010 && pid <= 0x05000017)
					{
						reader.Skip(16);
					}

					break;
				default:
					break;
			}

			if (inventory < 0 || inventory > 100000 || (depth >= MaxInventoryDepth && inventory > 0))
			{
				throw new InvalidDataException("The inventory of an object is not valid.");
			}

			for (Int32 i = 0; i < inventory; i++)
			{
				reader.Skip(4);
				ReadObject(reader, map, kindOf, version, carried: true, depth + 1);
			}
		}

		/// <summary>A position in the file and bounds-checked reads of big-endian integers.</summary>
		private sealed class Reader
		{
			private readonly Byte[] _data;

			public Reader(Byte[] data, Int32 position)
			{
				_data = data;
				Position = position;
			}

			public Int32 Position { get; private set; }

			public Int32 Int()
			{
				Int32 value = Peek(0);
				Position += 4;
				return value;
			}

			public Int32 Peek(Int32 offset)
			{
				if (Position < 0 || Position + offset + 4 > _data.Length)
				{
					throw new InvalidDataException("The map ends too early.");
				}

				return BigEndian.ReadInt32(_data, Position + offset);
			}

			public void Skip(Int32 count)
			{
				Position += count;
				if (Position > _data.Length)
				{
					throw new InvalidDataException("The map ends too early.");
				}
			}
		}
	}
}