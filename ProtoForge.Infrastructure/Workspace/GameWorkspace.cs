namespace ProtoForge.Infrastructure.Workspace
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text;
	using System.Threading;
	using System.Threading.Tasks;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Editing;
	using ProtoForge.Core.Formats.Ai;
	using ProtoForge.Core.Formats.Frm;
	using ProtoForge.Core.Formats.Msg;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Reference;
	using ProtoForge.Core.Serialization;
	using ProtoForge.Core.Templates;
	using ProtoForge.Core.Validation;
	using ProtoForge.Core.Vfs;
	using Infrastructure.Layers;
	using Layout;

	/// <summary>
	/// A loaded game or mod: the layered file system, every prototype, the texts and the reference lists, plus the editing
	/// operations (all undoable) and the save pipeline.
	/// </summary>
	public sealed class GameWorkspace : IDisposable
	{
		private readonly ValidationEngine _validation;
		private ValidationContext? _validationContext;

		internal GameWorkspace(
			GameLayout layout,
			WorkspaceOptions options,
			LayeredFileSystem files,
			ProtoCatalog catalog,
			MessageStore messages,
			ChoiceCatalog choices,
			AiFile ai,
			IReadOnlyList<String> scriptNames,
			Palette palette,
			String language,
			IReadOnlyList<String> warnings,
			ValidationEngine validation)
		{
			Layout = layout;
			Options = options;
			Files = files;
			Catalog = catalog;
			Messages = messages;
			Choices = choices;
			Ai = ai;
			Ai.Changed += OnAiChanged;
			ScriptNames = scriptNames;
			Palette = palette;
			Language = language;
			Warnings = warnings;
			_validation = validation;
			Texts = new ProtoTextService(messages);
			Art = new ArtResolver(files);
			Undo = new UndoManager();
		}

		public GameLayout Layout { get; }

		public WorkspaceOptions Options { get; }

		public LayeredFileSystem Files { get; }

		public ProtoCatalog Catalog { get; }

		public MessageStore Messages { get; }

		public ProtoTextService Texts { get; }

		public ChoiceCatalog Choices { get; }

		public ArtResolver Art { get; }

		/// <summary>The combat AI packets (<c>data/ai.txt</c>), editable.</summary>
		public AiFile Ai { get; }

		/// <summary>Another AI file opened from disk for editing (saved back to the same place), or null.</summary>
		public AiFile? CustomAi { get; private set; }

		public String? CustomAiPath { get; private set; }

		private AiPacketCatalog? _aiCatalog;

		/// <summary>The packets of <see cref="Ai"/> (rebuilt after each edit).</summary>
		public AiPacketCatalog AiPackets
		{
			get { return _aiCatalog ??= Ai.ToCatalog(); }
		}

		/// <summary>Names of the taunt sets of <c>aigenmsg.txt</c> (the <c>general_type</c> values).</summary>
		public IReadOnlyList<String> AiGeneralTypes
		{
			get { return _aiGeneralTypes ??= ReadSectionNames("data/aigenmsg.txt"); }
		}

		/// <summary>Names of the taunt sets of <c>aibdymsg.txt</c> (the <c>body_type</c> values).</summary>
		public IReadOnlyList<String> AiBodyTypes
		{
			get { return _aiBodyTypes ??= ReadSectionNames("data/aibdymsg.txt"); }
		}

		private IReadOnlyList<String>? _aiGeneralTypes;
		private IReadOnlyList<String>? _aiBodyTypes;

		public IReadOnlyList<String> ScriptNames { get; }

		public Palette Palette { get; }

		public UndoManager Undo { get; }

		/// <summary>Game language folder used for <c>text/&lt;language&gt;/game</c>.</summary>
		public String Language { get; }

		public IReadOnlyList<String> Warnings { get; }

		public String? OutputFolder
		{
			get { return Layout.OutputFolder; }
		}

		public Boolean CanSave
		{
			get { return !String.IsNullOrEmpty(OutputFolder); }
		}

		public String OutputSourceName
		{
			get { return Files.Layers.FirstOrDefault(l => l.IsOutput)?.Name ?? "Output"; }
		}

		public Boolean HasUnsavedChanges
		{
			get
			{
				return Catalog.All.Any(e => e.IsModified) || Catalog.ModifiedLists.Any() || Messages.DirtyFiles.Any() || Ai.IsDirty || CustomAi?.IsDirty == true || Files.HasPending;
			}
		}

		public static Task<GameWorkspace> OpenAsync(
			GameLayout layout,
			WorkspaceOptions options,
			IProgress<LoadProgress>? progress = null,
			CancellationToken cancellationToken = default)
		{
			return Task.Run(() => WorkspaceLoader.Load(layout, options, progress, cancellationToken), cancellationToken);
		}

		public String GetMessagePath(String fileName)
		{
			return $"text/{Language}/game/{fileName}";
		}

		#region Editing operations

		/// <summary>Sets a field on every entry that has it (identified by key, so different kinds can be edited together).</summary>
		public void SetField(IReadOnlyList<ProtoEntry> entries, String key, Int32 value)
		{
			Run(new FieldEditAction(entries, key, value, DescribeEdit(entries, key)));
		}

		public void SetField(IReadOnlyList<ProtoEntry> entries, ProtoField field, Int32 value)
		{
			SetField(entries, field.Key, value);
		}

		/// <summary>Computes each entry's new value from its current one (bit toggles, art index changes...).</summary>
		public void ModifyField(IReadOnlyList<ProtoEntry> entries, String key, Func<Int32, Int32> transform)
		{
			Run(new FieldEditAction(entries, key, transform, DescribeEdit(entries, key)));
		}

		/// <summary>Sets or clears a flag. Turning on one of several alternative bits (transparency modes) clears the others.</summary>
		public void SetFlag(IReadOnlyList<ProtoEntry> entries, String key, UInt32 mask, Boolean on)
		{
			UInt32 conflicts = on ? GetFlagConflicts(entries, key, mask) : 0;
			ModifyField(entries, key, value => unchecked((Int32)(on ? ((UInt32)value | mask) & ~conflicts : (UInt32)value & ~mask)));
		}

		private static UInt32 GetFlagConflicts(IReadOnlyList<ProtoEntry> entries, String key, UInt32 mask)
		{
			UInt32 conflicts = 0;
			foreach (ProtoEntry entry in entries)
			{
				String? setId = entry.File?.Schema.Find(key)?.FlagSet;
				if (setId is not null && FlagSets.TryGet(setId, out FlagSet set))
				{
					conflicts |= set.GetConflicts(mask);
				}
			}

			return conflicts;
		}

		private void Run(FieldEditAction action)
		{
			if (action.ChangesAnything)
			{
				Undo.Execute(action);
			}
		}

		private static String DescribeEdit(IReadOnlyList<ProtoEntry> entries, String key)
		{
			return entries.Count == 1 ? $"{key} ({entries[0].Pid})" : $"{key} ({entries.Count})";
		}

		public void SetTexts(ProtoEntry entry, String name, String description)
		{
			if (!Texts.CanEditTexts(entry) || (entry.Name == name && entry.Description == description))
			{
				return;
			}

			Undo.Execute(new TextEditAction(Texts, entry, name, description));
		}

		/// <summary>Edits a message of any MSG file (undoable); names shown in the lists are refreshed.</summary>
		public MsgFile GetMessageFile(String fileName)
		{
			return Messages.GetOrCreate(fileName);
		}

		/// <summary>Reloads the names and descriptions of the list and the labels of the choice lists (perks, calibers, materials...) after a message edit.</summary>
		public void RefreshTexts()
		{
			Texts.Refresh(Catalog.All);
			WorkspaceLoader.ConfigureChoices(Choices, Messages, AiPackets);
		}

		public ProtoEntry CreateProto(ProtoTemplate template, String name, String description)
		{
			ProtoObjectType type = template.Kind.GetObjectType();
			if (!Catalog.IsLoaded(type))
			{
				throw new InvalidOperationException($"The {type} prototypes are not part of the working set.");
			}

			Int32 index = Catalog.NextIndex(type);
			ProtoFile file = template.Create(ProtoId.From(type, index));
			AddProtoAction action = new(Catalog, Texts, type, file, ProtoDuplicator.FormatFileName(index), OutputSourceName, name, description, $"Create {template.DisplayName}");
			Undo.Execute(action);
			return action.Entry!;
		}

		public ProtoEntry Duplicate(ProtoEntry source, String copySuffix)
		{
			Int32 index = Catalog.NextIndex(source.Type);
			(ProtoFile file, String fileName, String name, String description) = ProtoDuplicator.Duplicate(source, index, copySuffix);
			AddProtoAction action = new(Catalog, Texts, source.Type, file, fileName, OutputSourceName, name, description, $"Duplicate {source.Pid}");
			Undo.Execute(action);
			return action.Entry!;
		}

		/// <summary>Only the last prototype of a list can be removed: removing another one would renumber (and break) every later PID.</summary>
		public Boolean CanDelete(ProtoEntry entry)
		{
			IReadOnlyList<ProtoEntry> list = Catalog.GetEntries(entry.Type);
			return list.Count > 0 && ReferenceEquals(list[^1], entry);
		}

		public void DeleteLast(ProtoEntry entry)
		{
			if (!CanDelete(entry))
			{
				throw new InvalidOperationException("Only the last prototype of a list can be deleted.");
			}

			Undo.Execute(new RemoveLastProtoAction(Catalog, entry, $"Delete {entry.Pid}"));
		}

		public void ReplaceFile(ProtoEntry entry, ProtoFile file, String description)
		{
			if (entry.File is null)
			{
				throw new InvalidOperationException("The prototype has no content to replace.");
			}

			Undo.Execute(new ReplaceFileAction(entry, file, description));
		}

		/// <summary>The original (lowest layer) version of the prototype when it differs from the current content.</summary>
		public ProtoFile? GetRestorableOriginal(ProtoEntry entry)
		{
			if (entry.File is null)
			{
				return null;
			}

			ProtoFile? vanilla = LoadVanilla(entry);
			return vanilla is not null && vanilla.Kind == entry.File.Kind && !vanilla.ContentEquals(entry.File) ? vanilla : null;
		}

		/// <summary>Puts the original game version back into the editor (undoable, nothing is written until the next save).</summary>
		public Int32 RestoreOriginal(IReadOnlyList<ProtoEntry> entries, String description)
		{
			List<IUndoableAction> actions = [];
			foreach (ProtoEntry entry in entries)
			{
				if (GetRestorableOriginal(entry) is { } original)
				{
					actions.Add(new ReplaceFileAction(entry, original, description));
				}
			}

			if (actions.Count > 0)
			{
				Undo.Execute(new CompositeAction(actions, description));
			}

			return actions.Count;
		}

		/// <summary>
		/// The version that would be used if the override of the output folder was deleted: the first other layer that has
		/// the file. Null when there is no override or nothing below it.
		/// </summary>
		public (ProtoFile File, String Source)? GetOverrideFallback(ProtoEntry entry)
		{
			if (!CanSave || entry.File is null)
			{
				return null;
			}

			IReadOnlyList<GameFileLocation> locations = Locate(entry);
			if (!locations.Any(l => l.Layer.IsOutput))
			{
				return null;
			}

			GameFileLocation? lower = locations.FirstOrDefault(l => !l.Layer.IsOutput);
			Byte[]? bytes = lower is null ? null : Files.ReadFromLayer(GetProtoPath(entry), lower.Layer);
			ProtoFile? file = bytes is null ? null : ProtoFile.TryParse(bytes, entry.Type, out _);
			return file is null || lower is null ? null : (file, lower.Layer.Name);
		}

		/// <summary>Goes back to the lower layer version and deletes the override file on the next save (undoable until then).</summary>
		public Int32 RemoveOverrides(IReadOnlyList<ProtoEntry> entries, String description)
		{
			List<IUndoableAction> actions = [];
			foreach (ProtoEntry entry in entries)
			{
				if (!entry.PendingOverrideRemoval && GetOverrideFallback(entry) is { } fallback)
				{
					actions.Add(new RemoveOverrideAction(entry, fallback.File, fallback.Source, description));
				}
			}

			if (actions.Count > 0)
			{
				Undo.Execute(new CompositeAction(actions, description));
			}

			return actions.Count;
		}

		/// <summary>Turns items into another kind of item (or scenery into another kind of scenery). Returns the number of converted prototypes.</summary>
		public Int32 ChangeKind(IReadOnlyList<ProtoEntry> entries, ProtoKind target, String description)
		{
			List<IUndoableAction> actions = [];
			foreach (ProtoEntry entry in entries)
			{
				if (entry.File is { } file && ProtoConversion.CanConvert(file.Kind, target))
				{
					actions.Add(new ReplaceFileAction(entry, ProtoConversion.Convert(file, target), description));
				}
			}

			if (actions.Count > 0)
			{
				Undo.Execute(new CompositeAction(actions, description));
			}

			return actions.Count;
		}

		public void Apply(ImportPlan plan, String description)
		{
			if (!plan.IsEmpty)
			{
				Undo.Execute(plan.ToAction(Texts, description));
			}
		}

		#endregion

		#region Art

		/// <summary>Raised when art files or art lists were added or removed (the pickers must reload their lists).</summary>
		public event EventHandler? ArtChanged;

		/// <summary>True when the name is already in the art list of the type.</summary>
		public Boolean ArtExists(ArtType type, String fileName)
		{
			return Art.GetList(type)?.IndexOf(fileName) >= 0;
		}

		/// <summary>
		/// Adds an image to the game art (undoable, written with the next save): the FRM goes to <c>art/&lt;folder&gt;</c> and its
		/// name to the list of the folder. When the name already exists, only the image is replaced. Returns the index in the list.
		/// </summary>
		public Int32 AddArt(ArtType type, String fileName, Byte[] frm)
		{
			AddArtAction action = new(Files, Art, type, fileName, frm, () => ArtChanged?.Invoke(this, EventArgs.Empty));
			Undo.Execute(action);
			return action.Index;
		}

		#endregion

		#region AI packets

		private void OnAiChanged(Object? sender, EventArgs e)
		{
			_aiCatalog = null;
			_validationContext = null;
			if (AiPackets.Packets.Count > 0)
			{
				Choices.Set(ChoiceCatalog.AiPacket, AiPackets.Packets.Select(p => new ChoiceOption(p.Number, p.DisplayName)));
			}
		}

		private IReadOnlyList<String> ReadSectionNames(String path)
		{
			Byte[]? bytes = Files.ReadAllBytes(path);
			return bytes is null ? [] : [.. AiFile.Parse(bytes).Sections.Select(s => s.Name)];
		}

		public void SetAiValue(AiFile file, String section, String key, String? value)
		{
			AiValueAction action = new(file, section, key, value);
			if (action.ChangesAnything)
			{
				Undo.Execute(action);
			}
		}

		public void SetAiRawText(AiFile file, String section, String text)
		{
			AiRawTextAction action = new(file, section, text);
			if (action.ChangesAnything)
			{
				Undo.Execute(action);
			}
		}

		/// <summary>Adds a packet built from the default template with the next free number. Returns its number.</summary>
		public Int32 AddAiPacket(AiFile file, String name)
		{
			if (file.Find(name) is not null)
			{
				throw new InvalidOperationException($"An AI packet named '{name}' already exists.");
			}

			Int32 number = file.NextPacketNumber();
			Undo.Execute(new AiAddPacketAction(file, name, AiFile.CreateDefaultPacketLines(number)));
			return number;
		}

		public void RemoveAiPacket(AiFile file, String name)
		{
			Undo.Execute(new AiRemovePacketAction(file, name));
		}

		/// <summary>Opens an AI file from disk next to the game one; its changes are saved back to the same path.</summary>
		public AiFile OpenCustomAi(String path)
		{
			AiFile file = AiFile.Parse(File.ReadAllBytes(path));
			if (file.Sections.Count == 0 || !file.Sections.Any(s => s.GetValue(AiParameters.PacketNumberKey) is not null))
			{
				throw new InvalidDataException("The file does not contain any AI packet.");
			}

			CustomAi = file;
			CustomAiPath = path;
			return file;
		}

		public void CloseCustomAi()
		{
			CustomAi = null;
			CustomAiPath = null;
		}

		/// <summary>The critters of the loaded game that use a packet number.</summary>
		public IEnumerable<ProtoEntry> GetPacketUsers(Int32 packetNumber)
		{
			return Catalog.GetEntries(ProtoObjectType.Critter).Where(c => c.File?.TryGet(FieldKeys.AiPacket) == packetNumber);
		}

		#endregion

		#region Reading helpers

		public String GetProtoPath(ProtoEntry entry)
		{
			return $"{entry.Type.GetProtoDirectory()}/{entry.FileName}";
		}

		/// <summary>Every layer that provides this prototype file, overrides first.</summary>
		public IReadOnlyList<GameFileLocation> Locate(ProtoEntry entry)
		{
			return Files.LocateAll(GetProtoPath(entry));
		}

		/// <summary>The version of the prototype from the lowest priority layer (usually the original game), if it exists.</summary>
		public ProtoFile? LoadVanilla(ProtoEntry entry)
		{
			IReadOnlyList<GameFileLocation> locations = Locate(entry);
			if (locations.Count == 0)
			{
				return null;
			}

			Byte[]? bytes = Files.ReadFromLayer(GetProtoPath(entry), locations[^1].Layer);
			return bytes is null ? null : ProtoFile.TryParse(bytes, entry.Type, out _);
		}

		#endregion

		#region Validation

		public IReadOnlyList<ValidationIssue> Validate(IEnumerable<ProtoEntry>? entries = null, IProgress<Double>? progress = null, CancellationToken cancellationToken = default)
		{
			ValidationContext context = _validationContext ??= new ValidationContext(Catalog, Files, Messages, Texts, Art, Choices, AiPackets, ScriptNames.Count);
			return _validation.Run(context, entries ?? Catalog.All, progress, cancellationToken);
		}

		public ValidationEngine ValidationEngine
		{
			get { return _validation; }
		}

		#endregion

		#region Saving

		public SavePlan CreateSavePlan()
		{
			if (!CanSave)
			{
				throw new InvalidOperationException("No output folder is configured.");
			}

			String output = OutputFolder!;
			List<SaveItem> items = [];

			foreach (ProtoEntry entry in Catalog.All.Where(e => e.File is not null))
			{
				String path = GetProtoPath(entry);
				if (entry.RemovesOverride)
				{
					if (FileExistsInOutput(output, path))
					{
						items.Add(new SaveItem(SaveItemKind.DeleteOverride, path, $"{entry.Pid} {entry.Name}".Trim(), [], ReplacesExisting: true));
					}
				}
				else if (entry.IsNew || entry.HasFileChanges)
				{
					items.Add(new SaveItem(SaveItemKind.Proto, path, $"{entry.Pid} {entry.Name}".Trim(), entry.File!.ToArray(), FileExistsInOutput(output, path)));
				}
			}

			foreach (ProtoObjectType type in Catalog.ModifiedLists)
			{
				String path = type.GetListPath();
				items.Add(new SaveItem(SaveItemKind.List, path, $"{type} list ({Catalog.GetList(type).Count} entries)", Catalog.GetList(type).ToBytes(), FileExistsInOutput(output, path)));
			}

			foreach (KeyValuePair<String, MsgFile> pair in Messages.Loaded.Where(p => p.Value.IsDirty))
			{
				String path = GetMessagePath(pair.Key);
				items.Add(new SaveItem(SaveItemKind.Message, path, pair.Key, pair.Value.ToBytes(), FileExistsInOutput(output, path)));
			}

			foreach (KeyValuePair<String, Byte[]> pending in Files.Pending.OrderBy(p => p.Key, StringComparer.Ordinal))
			{
				items.Add(new SaveItem(SaveItemKind.Art, pending.Key, pending.Key.EndsWith(".lst", StringComparison.Ordinal) ? "Art list" : "Art image", pending.Value, FileExistsInOutput(output, pending.Key)));
			}

			if (Ai.IsDirty)
			{
				items.Add(new SaveItem(SaveItemKind.AiPackets, "data/ai.txt", "AI packets", Ai.ToBytes(), FileExistsInOutput(output, "data/ai.txt")));
			}

			if (CustomAi is { IsDirty: true } custom && CustomAiPath is not null)
			{
				items.Add(new SaveItem(SaveItemKind.AiPackets, CustomAiPath, "AI packets (external file)", custom.ToBytes(), ReplacesExisting: true, AbsolutePath: CustomAiPath));
			}

			return new SavePlan(output, items);
		}

		public SaveResult Save(SavePlan plan, IProgress<Double>? progress = null)
		{
			List<String> errors = [];
			String? backupFolder = null;
			Int32 written = 0;

			for (Int32 i = 0; i < plan.Items.Count; i++)
			{
				SaveItem item = plan.Items[i];
				String target = item.AbsolutePath ?? Path.Combine(plan.OutputFolder, item.VirtualPath.Replace('/', Path.DirectorySeparatorChar));
				try
				{
					if (File.Exists(target))
					{
						File.SetAttributes(target, FileAttributes.Normal);
						if (Options.BackupBeforeSave || item.Kind == SaveItemKind.DeleteOverride)
						{
							// A deleted override is always backed up: it is the only copy of that work.
							backupFolder ??= Path.Combine(plan.OutputFolder, ".protoforge-backup", DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
							String backup = Path.Combine(backupFolder, item.AbsolutePath is null ? item.VirtualPath.Replace('/', Path.DirectorySeparatorChar) : Path.Combine("external", Path.GetFileName(item.AbsolutePath)));
							Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
							File.Copy(target, backup, overwrite: true);
						}
					}

					if (item.Kind == SaveItemKind.DeleteOverride)
					{
						File.Delete(target);
						written++;
						progress?.Report((Double)(i + 1) / plan.Items.Count);
						continue;
					}

					Directory.CreateDirectory(Path.GetDirectoryName(target)!);
					String temp = target + ".protoforge-tmp";
					File.WriteAllBytes(temp, item.Content);
					File.Move(temp, target, overwrite: true);
					if (item.Kind == SaveItemKind.Proto && Options.ReadOnlyAttribute)
					{
						File.SetAttributes(target, FileAttributes.ReadOnly);
					}

					written++;
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					errors.Add($"{item.VirtualPath}: {ex.Message}");
				}

				progress?.Report((Double)(i + 1) / plan.Items.Count);
			}

			if (errors.Count == 0)
			{
				HashSet<String> protoPaths = [.. plan.Items.Where(item => item.Kind == SaveItemKind.Proto).Select(item => item.VirtualPath)];
				foreach (ProtoEntry entry in Catalog.All.Where(e => e.IsModified))
				{
					Boolean wasWritten = protoPaths.Contains(GetProtoPath(entry));
					entry.AcceptChanges(entry.PendingOverrideRemoval && !wasWritten ? entry.SourceName : OutputSourceName);
					if (wasWritten)
					{
						entry.IsReadOnly = Options.ReadOnlyAttribute;
					}
				}

				Catalog.AcceptListChanges();
				Messages.AcceptChanges();
				Ai.AcceptChanges();
				CustomAi?.AcceptChanges();
				Files.ClearPending();
				Art.Invalidate();
				Undo.MarkSaved();
			}

			Files.Invalidate();
			return new SaveResult(written, backupFolder, errors);
		}

		private static Boolean FileExistsInOutput(String output, String virtualPath)
		{
			return File.Exists(Path.Combine(output, virtualPath.Replace('/', Path.DirectorySeparatorChar)));
		}

		#endregion

		public void Dispose()
		{
			Files.Dispose();
		}

		internal static Encoding ResolveEncoding(WorkspaceOptions options, String language)
		{
			return options.MessageCodePage is { } codePage ? MsgEncodings.Get(codePage) : MsgEncodings.ForLanguage(language);
		}
	}
}