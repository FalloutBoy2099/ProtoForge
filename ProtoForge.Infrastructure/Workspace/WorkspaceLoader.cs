namespace ProtoForge.Infrastructure.Workspace
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using System.Threading;
	using System.Threading.Tasks;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Ai;
	using ProtoForge.Core.Formats.Frm;
	using ProtoForge.Core.Formats.Lst;
	using ProtoForge.Core.Formats.Msg;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Reference;
	using ProtoForge.Core.Validation;
	using ProtoForge.Core.Vfs;
	using Layers;
	using Layout;
	using Plugins;

	/// <summary>Builds a <see cref="GameWorkspace"/>: opens the layers, reads the LST/PRO/MSG files and prepares the reference lists.</summary>
	internal static class WorkspaceLoader
	{
		public static GameWorkspace Load(GameLayout layout, WorkspaceOptions options, IProgress<LoadProgress>? progress, CancellationToken cancellationToken)
		{
			List<String> warnings = [];
			progress?.Report(new LoadProgress(UserTexts.Format("busy.stage.sources", "Opening data sources"), 0));
			LayeredFileSystem files = LayeredFileSystem.Open(layout, warnings);

			try
			{
				if (files.HasFallout1Archive)
				{
					// Fallout 1 uses other skill formulas and has fewer perks: follow the game that was opened.
					options.Formula = ProtoForge.Core.Stats.FormulaSet.Fallout1;
				}

				String language = ResolveLanguage(files, layout.Language, warnings);
				Encoding encoding = GameWorkspace.ResolveEncoding(options, language);
				MessageStore messages = new(name => LoadMessages(files, language, name, encoding), encoding);

				ProtoCatalog catalog = new();
				ProtoObjectType[] types = [.. ProtoObjectTypeExtensions.All.Where(options.LoadedTypes.Contains)];
				for (Int32 t = 0; t < types.Length; t++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					progress?.Report(new LoadProgress(UserTexts.Format("busy.stage.reading", "Reading {0}", UserTexts.Format("type." + types[t], types[t].GetFolderName())), 0.1 + (0.7 * t / types.Length)));
					LoadType(files, catalog, types[t], warnings, cancellationToken);
				}

				progress?.Report(new LoadProgress(UserTexts.Format("busy.stage.texts", "Reading texts"), 0.85));
				ProtoTextService texts = new(messages);
				foreach (ProtoEntry entry in catalog.All)
				{
					texts.Load(entry);
				}

				progress?.Report(new LoadProgress(UserTexts.Format("busy.stage.reference", "Preparing reference data"), 0.92));
				ChoiceCatalog choices = new();
				AiFile ai = LoadAi(files);
				ConfigureChoices(choices, messages, ai.ToCatalog());
				IReadOnlyList<String> scripts = LoadScripts(files);
				Palette palette = LoadPalette(files);

				PluginLoader loader = new();
				ValidationEngine validation = ValidationEngine.CreateDefault(loader.LoadRules(warnings));

				progress?.Report(new LoadProgress(UserTexts.Format("busy.stage.done", "Done"), 1));
				return new GameWorkspace(layout, options, files, catalog, messages, choices, ai, scripts, palette, language, warnings, validation);
			}
			catch
			{
				files.Dispose();
				throw;
			}
		}

		private static String ResolveLanguage(LayeredFileSystem files, String requested, List<String> warnings)
		{
			if (!System.Text.RegularExpressions.Regex.IsMatch(requested, "^[A-Za-z0-9_-]{1,32}$"))
			{
				warnings.Add(UserTexts.Format("warn.languageInvalid", "The language '{0}' is not a valid folder name; 'english' is used instead.", requested));
				requested = "english";
			}

			if (files.Enumerate($"text/{requested}/game").Count > 0)
			{
				return requested;
			}

			foreach (String candidate in new[] { "english" })
			{
				if (files.Enumerate($"text/{candidate}/game").Count > 0)
				{
					warnings.Add(UserTexts.Format("warn.languageFallback", "No text found for language '{0}'; '{1}' is used instead.", requested, candidate));
					return candidate;
				}
			}

			warnings.Add(UserTexts.Format("warn.languageMissing", "No MSG files found for language '{0}': names and descriptions will be empty.", requested));
			return requested;
		}

		private static MsgFile? LoadMessages(LayeredFileSystem files, String language, String name, Encoding encoding)
		{
			Byte[]? bytes = files.ReadAllBytes($"text/{language}/game/{name}");
			return bytes is null ? null : MsgFile.Parse(bytes, encoding);
		}

		private static void LoadType(LayeredFileSystem files, ProtoCatalog catalog, ProtoObjectType type, List<String> warnings, CancellationToken cancellationToken)
		{
			Byte[]? listBytes = files.ReadAllBytes(type.GetListPath());
			if (listBytes is null)
			{
				// Not loading the type at all: editing an empty list would write an LST that replaces the one of the base game.
				warnings.Add(UserTexts.Format("warn.listMissing", "{0} was not found: {1} prototypes are not available.", type.GetListPath(), UserTexts.Format("type." + type, type.ToString())));
				return;
			}

			LstFile list = LstFile.Parse(listBytes);
			ProtoEntry[] entries = new ProtoEntry[list.Count];
			ParallelOptions parallel = new() { CancellationToken = cancellationToken };
			Parallel.For(0, list.Count, parallel, i => entries[i] = LoadEntry(files, list, type, i));
			catalog.Load(type, list, entries);
		}

		private static ProtoEntry LoadEntry(LayeredFileSystem files, LstFile list, ProtoObjectType type, Int32 zeroBasedIndex)
		{
			ProtoId pid = ProtoId.From(type, zeroBasedIndex + 1);
			String fileName = list.GetName(zeroBasedIndex);
			if (fileName.Length == 0)
			{
				return new ProtoEntry(pid, String.Empty, null, "blank", String.Empty);
			}

			String path = $"{type.GetProtoDirectory()}/{fileName}";
			if (!GamePath.IsSafe(path))
			{
				// A name such as ..\..\x.pro would reach outside the prototype folder when the file is saved or read.
				return new ProtoEntry(pid, fileName, null, "The file name is not allowed (it leaves the prototype folder).", String.Empty);
			}

			GameFileLocation? location = files.Locate(path);
			if (location is null)
			{
				return new ProtoEntry(pid, fileName, null, "missing", String.Empty);
			}

			Byte[]? bytes = files.ReadFromLayer(path, location.Layer);
			if (bytes is null)
			{
				return new ProtoEntry(pid, fileName, null, "unreadable", location.Layer.Name);
			}

			ProtoFile? file = ProtoFile.TryParse(bytes, type, out String? error);
			return new ProtoEntry(pid, fileName, file, error, location.Layer.Name) { IsReadOnly = !location.Layer.IsDat && files.IsReadOnly(path, location.Layer) };
		}

		private static AiFile LoadAi(LayeredFileSystem files)
		{
			Byte[]? bytes = files.ReadAllBytes("data/ai.txt");
			return bytes is null ? AiFile.CreateEmpty() : AiFile.Parse(bytes);
		}

		private static IReadOnlyList<String> LoadScripts(LayeredFileSystem files)
		{
			Byte[]? bytes = files.ReadAllBytes("scripts/scripts.lst");
			if (bytes is null)
			{
				return [];
			}

			LstFile list = LstFile.Parse(bytes);
			return [.. Enumerable.Range(0, list.Count).Select(list.GetName)];
		}

		private static Palette LoadPalette(LayeredFileSystem files)
		{
			Byte[]? bytes = files.ReadAllBytes("color.pal");
			if (bytes is not null && bytes.Length >= 768)
			{
				return new Palette(bytes);
			}

			using System.IO.Stream stream = typeof(ArtResolver).Assembly.GetManifestResourceStream(
				typeof(ArtResolver).Assembly.GetManifestResourceNames().First(n => n.EndsWith("color.pal", StringComparison.OrdinalIgnoreCase)))!;
			Byte[] embedded = new Byte[768];
			stream.ReadExactly(embedded);
			return new Palette(embedded);
		}

		/// <summary>Some game texts are internal identifiers (<c>normal_dam</c>): keep the built-in label for those.</summary>
		private static String? ReadableLabel(String? text)
		{
			if (String.IsNullOrWhiteSpace(text) || (!text.Contains(' ', StringComparison.Ordinal) && text.Contains('_', StringComparison.Ordinal)))
			{
				return null;
			}

			return text;
		}

		/// <summary>Fallout 1 has 64 perks, Fallout 2 has 119: the list ends with the last perk that has a name in perk.msg.</summary>
		private static void TrimPerks(ChoiceCatalog choices, MsgFile perk)
		{
			Int32 last = -1;
			foreach (ChoiceOption option in choices.Get(ChoiceCatalog.Perk))
			{
				if (option.Value >= 0 && !String.IsNullOrWhiteSpace(perk.GetText(101 + option.Value)))
				{
					last = Math.Max(last, option.Value);
				}
			}

			if (last >= 0 && last < choices.Get(ChoiceCatalog.Perk).Max(o => o.Value))
			{
				choices.Set(ChoiceCatalog.Perk, choices.Get(ChoiceCatalog.Perk).Where(o => o.Value <= last).ToList());
			}
		}

		/// <summary>Replaces the built-in English labels with the texts of the game (stat.msg, perk.msg, proto.msg, ...).</summary>
		internal static void ConfigureChoices(ChoiceCatalog choices, MessageStore messages, AiPacketCatalog ai)
		{
			MsgFile proto = messages.GetOrCreate("proto.msg");
			MsgFile stat = messages.GetOrCreate("stat.msg");
			MsgFile perk = messages.GetOrCreate("perk.msg");
			MsgFile skill = messages.GetOrCreate("skill.msg");

			choices.Relabel(ChoiceCatalog.Material, v => ReadableLabel(proto.GetText(100 + v)));
			choices.Relabel(ChoiceCatalog.DamageType, v => ReadableLabel(proto.GetText(250 + v)));
			choices.Relabel(ChoiceCatalog.Stat, v => stat.GetText(100 + v));
			choices.Relabel(ChoiceCatalog.DrugStat, v => v >= 0 ? stat.GetText(100 + v) : null);
			choices.Relabel(ChoiceCatalog.Perk, v => v >= 0 ? perk.GetText(101 + v) : null);
			TrimPerks(choices, perk);
			choices.Relabel(ChoiceCatalog.Skill, v => skill.GetText(100 + v));

			List<ChoiceOption> calibers = [];
			for (Int32 id = 300; id < 350; id++)
			{
				String? text = proto.GetText(id);
				if (text is null)
				{
					break;
				}

				calibers.Add(new ChoiceOption(id - 300, text));
			}

			if (calibers.Count > 0)
			{
				choices.Set(ChoiceCatalog.Caliber, calibers);
			}

			choices.Set(ChoiceCatalog.Team, TeamCatalog.LoadDefault());
			if (ai.Packets.Count > 0)
			{
				choices.Set(ChoiceCatalog.AiPacket, ai.Packets.Select(p => new ChoiceOption(p.Number, p.DisplayName)));
			}
		}
	}
}