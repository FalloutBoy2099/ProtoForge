namespace ProtoForge.Core.Domain
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Threading;
	using Formats.Map;
	using Formats.Pro;
	using IO;
	using Vfs;

	/// <summary>
	/// What the maps and the scripts of the game say about the prototypes: where each one is placed, which scripts run on which map,
	/// and which scripts name a prototype. Reading every map and script takes a moment, so this is built once, in the background.
	/// </summary>
	public sealed class ReferenceIndex
	{
		public ReferenceIndex(IReadOnlyList<MapSummary> maps, IReadOnlyList<String> unreadableMaps, IReadOnlyDictionary<Int32, List<String>> scriptsByPid, Int32 scriptCount)
		{
			Maps = maps;
			UnreadableMaps = unreadableMaps;
			ScriptsByPid = scriptsByPid;
			ScriptCount = scriptCount;
		}

		public IReadOnlyList<MapSummary> Maps { get; }

		/// <summary>The maps that could not be read (a layout that is not understood, or a prototype that is not loaded).</summary>
		public IReadOnlyList<String> UnreadableMaps { get; }

		/// <summary>Prototype id to the compiled scripts that contain it (only creatures, scenery, walls and misc objects: an item number looks like any small number).</summary>
		public IReadOnlyDictionary<Int32, List<String>> ScriptsByPid { get; }

		public Int32 ScriptCount { get; }

		/// <summary>An index with nothing in it (no game open).</summary>
		public static ReferenceIndex Empty { get; } = new([], [], new Dictionary<Int32, List<String>>(), 0);

		public static ReferenceIndex Build(IGameFileSystem files, IReadOnlyDictionary<Int32, ProtoKind> kinds, IProgress<Double>? progress = null, CancellationToken cancellationToken = default)
		{
			List<String> mapPaths = [.. files.Enumerate("maps").Where(p => p.EndsWith(".map", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase)];
			List<String> scriptPaths = [.. files.Enumerate("scripts").Where(p => p.EndsWith(".int", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase)];
			Int32 steps = Math.Max(1, mapPaths.Count + scriptPaths.Count);
			Int32 done = 0;

			Func<Int32, ProtoKind?> kindOf = pid => kinds.TryGetValue(pid, out ProtoKind kind) ? kind : null;
			List<MapSummary> maps = [];
			List<String> unreadable = [];
			foreach (String path in mapPaths)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					if (files.ReadAllBytes(path) is { } bytes)
					{
						maps.Add(MapReader.Read(GamePath.GetFileName(path), bytes, kindOf));
					}
				}
				catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
				{
					unreadable.Add(GamePath.GetFileName(path));
				}

				progress?.Report((Double)(++done) / steps);
			}

			Dictionary<Int32, List<String>> scriptsByPid = [];
			foreach (String path in scriptPaths)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					if (files.ReadAllBytes(path) is { } bytes)
					{
						String name = GamePath.GetFileName(path);
						foreach (Int32 pid in FindPids(bytes, kinds))
						{
							if (!scriptsByPid.TryGetValue(pid, out List<String>? list))
							{
								scriptsByPid[pid] = list = [];
							}

							list.Add(name);
						}
					}
				}
				catch (Exception ex) when (ex is IOException or ArgumentException)
				{
					// A script that cannot be read is not searched.
				}

				progress?.Report((Double)(++done) / steps);
			}

			return new ReferenceIndex(maps, unreadable, scriptsByPid, scriptPaths.Count);
		}

		/// <summary>
		/// The prototype ids that a compiled script pushes as constants (opcode <c>C001</c> then a 32-bit integer). Only ids of prototypes that exist
		/// and are not items count: the id of an item is a small number, which a script uses for a thousand other reasons.
		/// </summary>
		public static IReadOnlyCollection<Int32> FindPids(Byte[] script, IReadOnlyDictionary<Int32, ProtoKind> kinds)
		{
			HashSet<Int32> found = [];
			for (Int32 i = 0; i + 6 <= script.Length; i += 2)
			{
				if (script[i] == 0xC0 && script[i + 1] == 0x01)
				{
					Int32 value = BigEndian.ReadInt32(script, i + 2);
					if ((value >>> 24) is >= 1 and <= 5 && kinds.ContainsKey(value))
					{
						found.Add(value);
					}
				}
			}

			return found;
		}
	}
}