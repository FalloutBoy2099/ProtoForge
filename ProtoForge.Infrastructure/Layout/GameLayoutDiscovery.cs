namespace ProtoForge.Infrastructure.Layout
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.IO;
	using System.Linq;
	using ProtoForge.Core.Formats.Ai;

	public enum OpenTargetKind
	{
		Unknown,
		GameFolder,
		ModFolder,
		DatArchive,
	}

	public sealed record DiscoveryResult(GameLayout Layout, IReadOnlyList<String> Messages);

	/// <summary>
	/// Builds a <see cref="GameLayout"/> from what the user points at: a game folder (read from <c>fallout2.cfg</c>, <c>ddraw.ini</c>
	/// and <c>mods/mods_order.txt</c>), a mod folder or a single DAT archive.
	/// </summary>
	public static class GameLayoutDiscovery
	{
		public static OpenTargetKind Classify(String path)
		{
			if (File.Exists(path))
			{
				return path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ? OpenTargetKind.DatArchive : OpenTargetKind.Unknown;
			}

			if (!Directory.Exists(path))
			{
				return OpenTargetKind.Unknown;
			}

			if (File.Exists(Path.Combine(path, "fallout2.cfg")) || File.Exists(Path.Combine(path, "fallout.cfg"))
				|| File.Exists(Path.Combine(path, "master.dat")) || Directory.Exists(Path.Combine(path, "master.dat")))
			{
				return OpenTargetKind.GameFolder;
			}

			if (Directory.Exists(Path.Combine(path, "proto")) || Directory.Exists(Path.Combine(path, "text")))
			{
				return OpenTargetKind.ModFolder;
			}

			// A folder holding a data subfolder is most likely a game folder without cfg.
			return Directory.Exists(Path.Combine(path, "data")) ? OpenTargetKind.GameFolder : OpenTargetKind.Unknown;
		}

		public static DiscoveryResult FromGameFolder(String gameFolder)
		{
			List<String> messages = [];
			GameLayout layout = new() { GameFolder = Path.GetFullPath(gameFolder) };
			String root = layout.GameFolder;

			String? cfgPath = new[] { "fallout2.cfg", "fallout.cfg" }.Select(n => Path.Combine(root, n)).FirstOrDefault(File.Exists);
			IniFile cfg = cfgPath is null ? IniFile.Parse(String.Empty) : IniFile.Parse(File.ReadAllBytes(cfgPath));
			if (cfgPath is null)
			{
				messages.Add("No fallout2.cfg / fallout.cfg found: default file names were assumed.");
			}

			String masterDat = cfg.GetString("system", "master_dat", "master.dat");
			String critterDat = cfg.GetString("system", "critter_dat", "critter.dat");
			String masterPatches = cfg.GetString("system", "master_patches", "data");
			String critterPatches = cfg.GetString("system", "critter_patches", masterPatches);
			layout.Language = cfg.GetString("system", "language", "english");

			HashSet<String> seen = new(StringComparer.OrdinalIgnoreCase);
			void Add(String? path, LayerRole role, String? name = null)
			{
				if (String.IsNullOrWhiteSpace(path))
				{
					return;
				}

				String full = ResolveInside(root, path);
				if (!seen.Add(full))
				{
					return;
				}

				Boolean isDirectory = Directory.Exists(full);
				if (!isDirectory && !File.Exists(full))
				{
					return;
				}

				layout.Layers.Add(new LayerDefinition
				{
					Path = full,
					Kind = isDirectory ? LayerKind.Directory : LayerKind.DatArchive,
					Name = name ?? Path.GetFileName(full),
					Role = role,
				});
			}

			// Priority documented by sfall: patches > mods_order > PatchFileXX > patchXXX.dat > sfall.dat > critter > f2_res > master.
			Add(masterPatches, LayerRole.Patches);
			Add(critterPatches, LayerRole.Patches);
			AddMods(root, Add, messages);
			AddExtraPatches(root, Add);
			AddPatchArchives(root, Add);
			Add("sfall.dat", LayerRole.Engine);
			Add(critterDat, LayerRole.Base);
			Add("f2_res.dat", LayerRole.Engine);
			Add(masterDat, LayerRole.Base);

			if (!layout.Layers.Any(l => l.Role == LayerRole.Base))
			{
				messages.Add("master.dat / critter.dat were not found in the game folder.");
			}

			String dataFolder = ResolveInside(root, masterPatches);
			layout.OutputFolder = dataFolder;
			return new DiscoveryResult(layout, messages);
		}

		/// <summary>A mod folder, optionally layered over an existing game installation.</summary>
		public static DiscoveryResult FromModFolder(String modFolder, GameLayout? baseGame)
		{
			String full = Path.GetFullPath(modFolder);
			List<String> messages = [];
			GameLayout layout = baseGame?.Clone() ?? new GameLayout();
			layout.Layers.RemoveAll(l => String.Equals(l.Path, full, StringComparison.OrdinalIgnoreCase));
			layout.Layers.Insert(0, new LayerDefinition
			{
				Path = full,
				Kind = LayerKind.Directory,
				Name = new DirectoryInfo(full).Name,
				Role = LayerRole.Mod,
			});
			layout.OutputFolder = full;
			if (baseGame is null)
			{
				messages.Add("Only the mod folder is loaded. Choose a game folder in the settings to see vanilla data and names.");
			}

			return new DiscoveryResult(layout, messages);
		}

		public static DiscoveryResult FromDatArchive(String datFile, GameLayout? baseGame)
		{
			String full = Path.GetFullPath(datFile);
			GameLayout layout = baseGame?.Clone() ?? new GameLayout();
			layout.Layers.RemoveAll(l => String.Equals(l.Path, full, StringComparison.OrdinalIgnoreCase));
			layout.Layers.Insert(0, new LayerDefinition
			{
				Path = full,
				Kind = LayerKind.DatArchive,
				Name = Path.GetFileName(full),
				Role = LayerRole.Mod,
			});
			return new DiscoveryResult(layout, ["The archive is read-only: choose an output folder before saving."]);
		}

		/// <summary>
		/// Resolves a path of a configuration file. Absolute paths are kept; relative ones are rooted in the game folder.
		/// </summary>
		internal static String ResolveInside(String root, String path)
		{
			String cleaned = path.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
			return Path.GetFullPath(Path.IsPathRooted(cleaned) ? cleaned : Path.Combine(root, cleaned));
		}

		private static void AddMods(String root, Action<String?, LayerRole, String?> add, List<String> messages)
		{
			String modsFolder = Path.Combine(root, "mods");
			if (!Directory.Exists(modsFolder))
			{
				return;
			}

			List<String> ordered = [];
			String orderFile = Path.Combine(modsFolder, "mods_order.txt");
			if (File.Exists(orderFile))
			{
				foreach (String raw in File.ReadAllLines(orderFile))
				{
					String line = StripComment(raw);
					if (line.Length == 0 || line.Contains(':', StringComparison.Ordinal) || line.Contains("..", StringComparison.Ordinal))
					{
						continue;
					}

					ordered.Add(line.TrimStart('\\', '/'));
				}
			}
			else
			{
				ordered.AddRange(Directory.EnumerateFileSystemEntries(modsFolder, "*.dat")
					.Select(Path.GetFileName)
					.Where(n => n is not null)
					.Select(n => n!)
					.Order(StringComparer.OrdinalIgnoreCase));
				messages.Add("mods_order.txt not found: mods are ordered alphabetically.");
			}

			// In mods_order.txt later entries override earlier ones.
			ordered.Reverse();
			foreach (String entry in ordered)
			{
				add(Path.Combine("mods", entry), LayerRole.Mod, null);
			}
		}

		private static void AddExtraPatches(String root, Action<String?, LayerRole, String?> add)
		{
			String ddraw = Path.Combine(root, "ddraw.ini");
			if (!File.Exists(ddraw))
			{
				return;
			}

			IniFile ini = IniFile.Parse(File.ReadAllBytes(ddraw));
			for (Int32 i = 99; i >= 0; i--)
			{
				String? value = ini.GetString("ExtraPatches", "PatchFile" + i.ToString(CultureInfo.InvariantCulture));
				if (!String.IsNullOrWhiteSpace(value))
				{
					add(value, LayerRole.ExtraPatch, null);
				}
			}
		}

		private static void AddPatchArchives(String root, Action<String?, LayerRole, String?> add)
		{
			foreach (String file in Directory.EnumerateFiles(root, "patch*.dat").OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase))
			{
				add(Path.GetFileName(file), LayerRole.PatchArchive, null);
			}
		}

		private static String StripComment(String line)
		{
			Int32 cut = line.IndexOfAny([';', '#']);
			return (cut >= 0 ? line[..cut] : line).Trim();
		}
	}
}