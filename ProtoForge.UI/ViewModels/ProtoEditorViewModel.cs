namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.Linq;
	using System.Threading.Tasks;
	using System.Windows;
	using System.Windows.Media;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Editing;
	using ProtoForge.Core.Formats.Audio;
	using ProtoForge.Core.Formats.Frm;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Reference;
	using ProtoForge.Core.Stats;
	using ProtoForge.Core.Templates;
	using ProtoForge.Infrastructure.Settings;
	using ProtoForge.Infrastructure.Templates;
	using ProtoForge.Infrastructure.Workspace;
	using Fields;
	using Services;

	/// <summary>
	/// The right-hand panel: builds the field editors that match the kind of the selected prototype(s) (weapon, armor,
	/// critter...), applies edits to the whole selection and shows previews, texts and the raw bytes.
	/// </summary>
	/// <summary>A kind a prototype can be converted to, with its display name.</summary>
	public sealed record KindOption(ProtoKind Kind, String Label)
	{
		public override String ToString()
		{
			return Label;
		}
	}

	public sealed partial class ProtoEditorViewModel : ObservableObject, IDisposable
	{
		private readonly GameWorkspace _workspace;
		private readonly EditorContext _context;
		private readonly SettingsService _settings;
		private readonly INotifier _notifier;
		private readonly IDialogService _dialogs;
		private readonly UserTemplateStore _templates;
		private readonly ShellNavigator _shell;
		private Boolean _refreshing;
		private Boolean _refreshQueued;
		private Int32 _previewDirection = -1;
		private Int32 _previewVersion;
		private Int32 _directionCount = 1;
		private Int32 _soundLetter;
		private Int32 _soundStep;
		private String _groupsSignature = String.Empty;

		[ObservableProperty]
		private ObservableCollection<FieldGroupViewModel> _groups = [];

		[ObservableProperty]
		private String _title = String.Empty;

		[ObservableProperty]
		private String _subtitle = String.Empty;

		[ObservableProperty]
		private Boolean _hasSelection;

		[ObservableProperty]
		private Boolean _isMultiple;

		[ObservableProperty]
		private Boolean _canEditText;

		[ObservableProperty]
		private String _name = String.Empty;

		[ObservableProperty]
		private String _description = String.Empty;

		[ObservableProperty]
		private Boolean _showAdvanced;

		[ObservableProperty]
		private String _problem = String.Empty;

		[ObservableProperty]
		private ImageSource? _groundImage;

		/// <summary>The frames of the ground image (several for a critter); the view plays them in a loop.</summary>
		[ObservableProperty]
		private IReadOnlyList<ImageSource>? _groundFrames;

		[ObservableProperty]
		private Int32 _groundFramesPerSecond = 10;

		[ObservableProperty]
		private ImageSource? _inventoryImage;

		[ObservableProperty]
		private ImageSource? _portraitImage;

		[ObservableProperty]
		private Boolean _isCritter;

		[ObservableProperty]
		private IReadOnlyList<KeyValuePair<String, String>> _aiDetails = [];

		[ObservableProperty]
		private String _aiTitle = String.Empty;

		[ObservableProperty]
		private Double _weaponScore;

		[ObservableProperty]
		private Boolean _isWeapon;

		/// <summary>0 = fixed formula (sfall), 1 = original formula of the game.</summary>
		[ObservableProperty]
		private Int32 _weaponScoreType;

		[ObservableProperty]
		private Boolean _isArmor;

		[ObservableProperty]
		private Boolean _canChangeKind;

		[ObservableProperty]
		private IReadOnlyList<KindOption> _kindOptions = [];

		[ObservableProperty]
		private KindOption? _selectedKind;

		[ObservableProperty]
		private Int32 _armorScore;

		[ObservableProperty]
		private Boolean _canRestoreOriginal;

		[ObservableProperty]
		private Boolean _canRemoveOverride;

		[ObservableProperty]
		private IReadOnlyList<ReferenceOption> _armorOptions = [];

		[ObservableProperty]
		private ReferenceOption? _selectedArmor;

		[ObservableProperty]
		private String _armorInfo = String.Empty;

		public ProtoEditorViewModel(
			GameWorkspace workspace,
			ArtImageService art,
			SettingsService settings,
			INotifier notifier,
			IDialogService dialogs,
			UserTemplateStore templates,
			ShellNavigator shell)
		{
			_workspace = workspace;
			_settings = settings;
			_notifier = notifier;
			_dialogs = dialogs;
			_templates = templates;
			_shell = shell;
			_context = new EditorContext(workspace, art);
			Hex = new HexViewModel(workspace, settings, dialogs, notifier);
			Table = new FieldTableViewModel(workspace);
			Usage = new UsageViewModel(workspace, shell);
			CritterPreview = new CritterPreviewViewModel(workspace, art);
			_showAdvanced = settings.Current.AdvancedMode;
			_weaponScoreType = settings.Current.OriginalWeaponScore ? 1 : 0;
			workspace.Undo.Changed += OnUndoChanged;
			workspace.Choices.Changed += OnChoicesChanged;
		}

		public HexViewModel Hex { get; }

		public FieldTableViewModel Table { get; }

		public UsageViewModel Usage { get; }

		public CritterPreviewViewModel CritterPreview { get; }

		public IReadOnlyList<ProtoEntry> Selection
		{
			get { return _context.Entries; }
		}

		public ProtoEntry? Single
		{
			get { return Selection.Count == 1 ? Selection[0] : null; }
		}

		public void SetSelection(IReadOnlyList<ProtoEntry> entries)
		{
			foreach (ProtoEntry entry in _context.Entries)
			{
				entry.FieldChanged -= OnEntryChanged;
			}

			_context.Entries = [.. entries.Where(e => e.File is not null)];
			foreach (ProtoEntry entry in _context.Entries)
			{
				entry.FieldChanged += OnEntryChanged;
			}

			HasSelection = entries.Count > 0;
			IsMultiple = _context.Entries.Count > 1;
			Problem = entries.Count == 1 && entries[0].File is null ? DescribeProblem(entries[0]) : String.Empty;

			// The editors read the selection through the context: the same fields (and the same controls) serve the next prototype of the
			// same kind, so changing the selected row only reloads their values. They are rebuilt only when the kind of the selection changes.
			String signature = GetSignature();
			if (Groups.Count == 0 || signature != _groupsSignature)
			{
				BuildGroups();
				_groupsSignature = signature;
			}

			RefreshAll();
			OnPropertyChanged(nameof(Selection));
			OnPropertyChanged(nameof(Single));
		}

		partial void OnShowAdvancedChanged(Boolean value)
		{
			_settings.Current.AdvancedMode = value;
			foreach (FieldGroupViewModel group in Groups)
			{
				group.UpdateVisibility(value);
			}
		}

		partial void OnWeaponScoreTypeChanged(Int32 value)
		{
			_settings.Current.OriginalWeaponScore = value == 1;
			_settings.Save();
			UpdateScores();
		}

		/// <summary>The user picked another kind in the list: converts the selection after a confirmation.</summary>
		partial void OnSelectedKindChanged(KindOption? value)
		{
			if (_refreshing || value is null || !CanChangeKind)
			{
				return;
			}

			_ = ObserveAsync(ChangeKindAsync(value));
		}

		/// <summary>Runs a task that nobody awaits so that its failure is reported instead of being lost.</summary>
		private async Task ObserveAsync(Task task)
		{
			try
			{
				await task;
			}
			catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException)
			{
				_notifier.Error(Loc.Get("kind.changeTitle"), ProtoForge.Core.UserTexts.Describe(ex));
			}
		}

		private async Task ChangeKindAsync(KindOption target)
		{
			Boolean confirmed = Selection.All(e => e.Kind == target.Kind)
				|| await _dialogs.ConfirmAsync(Loc.Get("kind.changeTitle"), Loc.Format("kind.changeConfirm", target.Label), Loc.Get("kind.changeButton"));
			if (confirmed)
			{
				_workspace.ChangeKind(Selection, target.Kind, Loc.Get("kind.changeTitle"));
			}

			QueueRefresh();
		}

		partial void OnSelectedArmorChanged(ReferenceOption? value)
		{
			ArmorInfo = value is null ? String.Empty : DescribeArmor(value.Value);
			ApplyArmorCommand.NotifyCanExecuteChanged();
		}

		partial void OnNameChanged(String value)
		{
			ApplyText();
		}

		partial void OnDescriptionChanged(String value)
		{
			if (value.Contains('\r', StringComparison.Ordinal))
			{
				// The box uses CRLF for a line break, the texts use LF.
				Description = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
				return;
			}

			ApplyText();
		}

		private void OnChoicesChanged(Object? sender, EventArgs e)
		{
			QueueRefresh();
		}

		public void Dispose()
		{
			_workspace.Undo.Changed -= OnUndoChanged;
			_workspace.Choices.Changed -= OnChoicesChanged;
			Hex.Dispose();
			foreach (ProtoEntry entry in _context.Entries)
			{
				entry.FieldChanged -= OnEntryChanged;
			}
		}

		[RelayCommand]
		private void Revert()
		{
			List<IUndoableAction> actions = [];
			foreach (ProtoEntry entry in Selection.Where(e => e.HasFileChanges && e.Baseline is not null))
			{
				actions.Add(new ReplaceFileAction(entry, entry.Baseline!, Loc.Get("edit.revert")));
			}

			if (actions.Count > 0)
			{
				_workspace.Undo.Execute(new CompositeAction(actions, Loc.Get("edit.revert")));
			}
		}

		[RelayCommand]
		private async Task SaveAsTemplate()
		{
			if (Single?.File is not { } file)
			{
				return;
			}

			String? name = await _dialogs.PromptAsync(Loc.Get("template.saveTitle"), Loc.Get("template.name"), Single.Name);
			if (String.IsNullOrWhiteSpace(name))
			{
				return;
			}

			if (_templates.Exists(name) && !await _dialogs.ConfirmAsync(Loc.Get("template.saveTitle"), Loc.Format("template.overwrite", name), Loc.Get("template.replace")))
			{
				return;
			}

			try
			{
				_templates.Save(name, file);
			}
			catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
			{
				_notifier.Error(Loc.Get("template.saveTitle"), ProtoForge.Core.UserTexts.Describe(ex));
				return;
			}

			_shell.NotifyTemplatesChanged();
			_notifier.Success(Loc.Get("template.saveTitle"), Loc.Format("template.saved", name));
		}

		[RelayCommand]
		private void CompareWithVanilla()
		{
			if (Single is { } entry)
			{
				_shell.CompareWithVanilla(entry);
			}
		}

		/// <summary>
		/// Sets the derived statistics (hit points, action points, armor class, ...) to what the formulas give for the SPECIAL values.
		/// The skills are not touched: their stored value is an adjustment added to the formula by the engine.
		/// </summary>
		[RelayCommand]
		private void ComputeDerivedStats()
		{
			List<IUndoableAction> actions = [];
			foreach (ProtoEntry entry in Selection.Where(e => e.Kind == ProtoKind.Critter))
			{
				ProtoFile file = entry.File!;
				Int32[] special = [.. ProtoSchemas.SpecialNames.Select(n => file.Get(n))];
				foreach (KeyValuePair<String, Int32> pair in CharacterFormulas.ComputeDerived(special))
				{
					actions.Add(new FieldEditAction([entry], pair.Key, pair.Value, pair.Key));
				}
			}

			if (actions.Count > 0)
			{
				_workspace.Undo.Execute(new CompositeAction(actions, Loc.Get("critter.computeStats")));
			}
		}

		[RelayCommand(CanExecute = nameof(CanRestoreOriginal))]
		private void RestoreOriginal()
		{
			Int32 count = _workspace.RestoreOriginal(Selection, Loc.Get("edit.restoreOriginal"));
			if (count == 0)
			{
				_notifier.Info(Loc.Get("edit.restoreOriginal"), Loc.Get("edit.nothingToRestore"));
			}
		}

		[RelayCommand(CanExecute = nameof(CanRemoveOverride))]
		private void RemoveOverride()
		{
			Int32 count = _workspace.RemoveOverrides(Selection, Loc.Get("edit.removeOverride"));
			_notifier.Info(Loc.Get("edit.removeOverride"), count == 0 ? Loc.Get("edit.noOverride") : Loc.Format("edit.overrideRemoved", count));
		}

		/// <summary>
		/// Plays the sound of the weapon: <c>sound/sfx/WA&lt;letter&gt;&lt;1|2&gt;XXX&lt;1|2&gt;.acm</c>. Each click plays the next
		/// variant (primary attack, primary hit, secondary attack, secondary hit).
		/// </summary>
		[RelayCommand]
		private void PlaySound()
		{
			if (Single?.File is not { } file || !IsWeapon)
			{
				return;
			}

			Int32 id = file.Get("WeaponSoundId");
			if (id == 0)
			{
				_notifier.Info(Loc.Get("sound.title"), Loc.Get("sound.noLetter"));
				return;
			}

			if (id != _soundLetter)
			{
				_soundLetter = id;
				_soundStep = 0;
			}

			String name = $"WA{(Char)id}{_soundStep / 2 + 1}XXX{_soundStep % 2 + 1}.acm";
			_soundStep = (_soundStep + 1) % 4;
			Byte[]? bytes = _workspace.Files.ReadAllBytes("sound/sfx/" + name);
			if (bytes is null)
			{
				_notifier.Warning(Loc.Get("sound.title"), Loc.Format("sound.notFound", name));
				return;
			}

			try
			{
				SoundPreview.Play(AcmFile.Read(bytes).ToWav());
				_notifier.Info(Loc.Get("sound.title"), Loc.Format("sound.playing", name));
			}
			catch (System.IO.InvalidDataException)
			{
				_notifier.Error(Loc.Get("sound.title"), Loc.Format("sound.invalid", name));
			}
		}

		/// <summary>Saves one of the previews (<c>ground</c>, <c>inventory</c> or <c>portrait</c>) as a bitmap: <c>8</c> keeps the palette of the game, <c>32</c> has an alpha channel.</summary>
		[RelayCommand]
		private void SaveImage(String? spec)
		{
			String[] parts = (spec ?? String.Empty).Split(':');
			if (parts.Length != 2 || Single?.File is not { } file)
			{
				return;
			}

			String key = parts[0] switch
			{
				"inventory" => FieldKeys.InventoryFrmId,
				"portrait" => FieldKeys.HeadFrmId,
				_ => FieldKeys.FrmId,
			};
			Int32? value = file.TryGet(key);
			String? path = value is null || value.Value == -1 ? null : _workspace.Art.ResolvePath(new FrameId(value.Value));
			Byte[]? bytes = path is null ? null : _workspace.Files.ReadAllBytes(path);
			if (bytes is null)
			{
				_notifier.Warning(Loc.Get("image.saveTitle"), Loc.Get("image.none"));
				return;
			}

			try
			{
				FrmFile frm = FrmFile.Parse(bytes);
				FrmFrame frame = frm.Directions[frm.Directions.Count > 3 ? 3 : 0][0];
				Boolean alpha = parts[1] == "32";
				String? target = _dialogs.PickSaveFile(Loc.Get("image.saveTitle"), "BMP (*.bmp)|*.bmp", System.IO.Path.GetFileNameWithoutExtension(path) + ".bmp");
				if (target is null)
				{
					return;
				}

				System.IO.File.WriteAllBytes(target, alpha ? FrmConverter.ToBmp32(frame, _workspace.Palette) : FrmConverter.ToBmp8(frame, _workspace.Palette));
				_notifier.Success(Loc.Get("image.saveTitle"), Loc.Format("image.saved", System.IO.Path.GetFileName(target)));
			}
			catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or ArgumentOutOfRangeException)
			{
				_notifier.Error(Loc.Get("image.saveTitle"), ProtoForge.Core.UserTexts.Describe(ex));
			}
		}

		private Boolean CanApplyArmor()
		{
			return SelectedArmor is not null && IsCritter;
		}

		/// <summary>Copies the damage thresholds and resistances of an armor into the bonus stats of the selected critters.</summary>
		[RelayCommand(CanExecute = nameof(CanApplyArmor))]
		private void ApplyArmor()
		{
			if (SelectedArmor is null || _workspace.Catalog.Find(SelectedArmor.Value)?.File is not { } armor)
			{
				return;
			}

			List<IUndoableAction> actions = [];
			foreach (ProtoEntry entry in Selection.Where(e => e.Kind == ProtoKind.Critter))
			{
				foreach (String suffix in ProtoSchemas.DamageSuffixes)
				{
					actions.Add(new FieldEditAction([entry], ProtoSchemas.BonusPrefix + "Dt" + suffix, armor.Get("Dt" + suffix), suffix));
					actions.Add(new FieldEditAction([entry], ProtoSchemas.BonusPrefix + "Dr" + suffix, armor.Get("Dr" + suffix), suffix));
				}
			}

			if (actions.Count > 0)
			{
				_workspace.Undo.Execute(new CompositeAction(actions, Loc.Get("critter.applyArmor")));
			}
		}

		private String DescribeArmor(Int32 pid)
		{
			if (_workspace.Catalog.Find(pid)?.File is not { } armor)
			{
				return String.Empty;
			}

			return String.Join(Environment.NewLine, ProtoSchemas.DamageSuffixes.Select((suffix, index) =>
				$"{_workspace.Choices.Describe(ChoiceCatalog.DamageType, index)}: DT {armor.Get("Dt" + suffix)} | DR {armor.Get("Dr" + suffix)}%"));
		}

		private void BuildArmorOptions()
		{
			ReferenceOption? previous = SelectedArmor;
			ArmorOptions =
			[
				.. _workspace.Catalog.GetEntries(ProtoObjectType.Item)
					.Where(e => e.Kind == ProtoKind.Armor && e.File is not null)
					.Select(e => new ReferenceOption(e.Pid.Value, $"{e.DisplayName}  [{e.Pid}]")),
			];
			SelectedArmor = previous is null ? null : ArmorOptions.FirstOrDefault(o => o.Value == previous.Value);
		}

		private void ApplyText()
		{
			if (_refreshing || Single is not { } entry || !CanEditText)
			{
				return;
			}

			_workspace.SetTexts(entry, Name, Description);
		}

		private static String DescribeProblem(ProtoEntry entry)
		{
			return entry.LoadError switch
			{
				"missing" => Loc.Format("problem.missing", entry.FileName),
				"blank" => Loc.Get("problem.blank"),
				null => Loc.Get("problem.unreadable"),
				_ => Loc.Format("problem.parse", entry.LoadError),
			};
		}

		private void BuildGroups()
		{
			foreach (FieldGroupViewModel group in Groups)
			{
				foreach (FieldViewModel field in group.AllFields)
				{
					field.Detach();
				}
			}

			List<FieldGroupViewModel> groups = [];
			if (_context.Entries.Count > 0)
			{
				ProtoSchema schema = _context.Entries[0].File!.Schema;
				HashSet<String> available = GetCommonKeys();
				HashSet<String> consumed = [];

				foreach (String groupKey in schema.Groups)
				{
					List<ProtoField> fields = [.. schema.GetGroup(groupKey).Where(f => available.Contains(f.Key))];
					if (fields.Count == 0 || consumed.Contains(groupKey))
					{
						continue;
					}

					Boolean isCritterStats = schema.Kind == ProtoKind.Critter && groupKey is "Special" or "Derived" or "Thresholds" or "Resistances";
					if (isCritterStats)
					{
						groups.Add(BuildStatTable(schema, groupKey, fields, available, consumed));
						continue;
					}

					if (schema.Kind == ProtoKind.Critter && groupKey == "Skills")
					{
						FieldGroupViewModel skills = new(groupKey, isAdvanced: false);
						foreach (ProtoField field in fields)
						{
							skills.SkillRows.Add(new SkillRowViewModel(field.Key, new NumberFieldViewModel(field, _context), _context));
						}

						groups.Add(skills);
						continue;
					}

					Boolean advancedGroup = groupKey.StartsWith(ProtoSchemas.BonusPrefix, StringComparison.Ordinal);
					FieldGroupViewModel group = new(groupKey, advancedGroup);
					foreach (ProtoField field in fields)
					{
						group.Fields.Add(FieldViewModelFactory.Create(field, _context));
					}

					groups.Add(group);
				}
			}

			Groups = [.. groups];
			foreach (FieldGroupViewModel group in Groups)
			{
				group.UpdateVisibility(ShowAdvanced);
			}
		}

		private FieldGroupViewModel BuildStatTable(ProtoSchema schema, String groupKey, List<ProtoField> fields, HashSet<String> available, HashSet<String> consumed)
		{
			String bonusGroup = ProtoSchemas.BonusPrefix + groupKey;
			consumed.Add(bonusGroup);
			FieldGroupViewModel group = new(groupKey, isAdvanced: false);
			foreach (ProtoField baseField in fields)
			{
				ProtoField? bonusField = schema.Find(ProtoSchemas.BonusPrefix + baseField.Key);
				if (bonusField is null || !available.Contains(bonusField.Key))
				{
					group.Fields.Add(FieldViewModelFactory.Create(baseField, _context));
					continue;
				}

				group.StatRows.Add(new StatRowViewModel(
					baseField.Key,
					new NumberFieldViewModel(baseField, _context),
					new NumberFieldViewModel(bonusField, _context)));
			}

			return group;
		}

		/// <summary>Keys of the fields that every selected prototype has with the same editor (the intersection for mixed kinds).</summary>
		private HashSet<String> GetCommonKeys()
		{
			ProtoSchema first = _context.Entries[0].File!.Schema;
			HashSet<String> keys = [.. first.Fields.Select(f => f.Key)];
			foreach (ProtoEntry entry in _context.Entries.Skip(1))
			{
				ProtoSchema schema = entry.File!.Schema;
				keys.RemoveWhere(key =>
				{
					ProtoField? other = schema.Find(key);
					ProtoField mine = first.Require(key);
					return other is null || other.Editor != mine.Editor || other.FlagSet != mine.FlagSet || other.ChoiceSet != mine.ChoiceSet || other.IsVirtual != mine.IsVirtual;
				});
			}

			return keys;
		}

		private void OnEntryChanged(Object? sender, FieldChangedEventArgs e)
		{
			QueueRefresh();
		}

		private void OnUndoChanged(Object? sender, EventArgs e)
		{
			QueueRefresh();
		}

		private void QueueRefresh()
		{
			if (_refreshQueued)
			{
				return;
			}

			_refreshQueued = true;
			Application.Current.Dispatcher.BeginInvoke(() =>
			{
				_refreshQueued = false;
				RefreshAll();
			});
		}

		private String GetSignature()
		{
			return String.Join(",", _context.Entries.Select(e => e.Kind));
		}

		private void RefreshAll()
		{
			_refreshing = true;
			try
			{
				if (GetSignature() != _groupsSignature)
				{
					// A prototype changed kind (undo of a conversion, restore of the original...): its fields are not the same any more.
					BuildGroups();
					_groupsSignature = GetSignature();
				}

				foreach (FieldGroupViewModel group in Groups)
				{
					group.Refresh();
				}

				UpdateHeader();
				Hex.Load(Single);
				Table.Load(Single);
				Usage.Load(Single);
				CritterPreview.Load(Single);
			}
			finally
			{
				_refreshing = false;
			}
		}

		private void UpdateHeader()
		{
			IReadOnlyList<ProtoEntry> entries = _context.Entries;
			IsCritter = entries.Count > 0 && entries.All(e => e.Kind == ProtoKind.Critter);
			IsWeapon = entries.Count == 1 && entries[0].Kind == ProtoKind.Weapon;
			IsArmor = entries.Count == 1 && entries[0].Kind == ProtoKind.Armor;
			UpdateKindOptions(entries);
			CanRestoreOriginal = entries.Any(e => !e.IsNew);
			CanRemoveOverride = entries.Any(e => e.SourceName == _workspace.OutputSourceName || e.PendingOverrideRemoval);
			RestoreOriginalCommand.NotifyCanExecuteChanged();
			RemoveOverrideCommand.NotifyCanExecuteChanged();
			ApplyArmorCommand.NotifyCanExecuteChanged();
			if (IsCritter && ArmorOptions.Count == 0)
			{
				BuildArmorOptions();
			}
			CanEditText = entries.Count == 1 && _workspace.Texts.CanEditTexts(entries[0]);

			if (entries.Count == 0)
			{
				Title = Problem.Length > 0 ? Loc.Get("editor.unavailable") : Loc.Get("editor.nothing");
				Subtitle = Problem;
				Name = String.Empty;
				Description = String.Empty;
				GroundImage = InventoryImage = PortraitImage = null;
				GroundFrames = null;
				AiDetails = [];
				return;
			}

			if (entries.Count > 1)
			{
				Title = Loc.Format("editor.multiple", entries.Count);
				Subtitle = String.Join(", ", entries.Select(e => e.Kind!.Value.ToString()).Distinct().Select(k => Loc.Get("kind." + k)));
				Name = String.Empty;
				Description = String.Empty;
				GroundImage = InventoryImage = PortraitImage = null;
				GroundFrames = null;
				AiDetails = [];
				return;
			}

			ProtoEntry entry = entries[0];
			Title = entry.Name.Length > 0 ? entry.Name : entry.FileName;
			IReadOnlyList<String> scripts = _workspace.ScriptNames;
			String script = entry.ScriptIndex >= 0 && entry.ScriptIndex < scripts.Count ? $"  ·  {Loc.Get("col.script")} {System.IO.Path.GetFileNameWithoutExtension(scripts[entry.ScriptIndex])}" : String.Empty;
			Subtitle = $"{Loc.Get("kind." + entry.Kind)}  ·  {entry.Pid}  ·  {entry.FileName}  ·  {entry.SourceName}{script}";
			Name = entry.Name;
			Description = entry.Description;
			UpdatePreviews(entry);
			UpdateAi(entry);
			UpdateScores();
		}

		private void UpdateKindOptions(IReadOnlyList<ProtoEntry> entries)
		{
			ProtoKind? first = entries.Count > 0 ? entries[0].Kind : null;
			Boolean family = first is { } kind
				&& ProtoConversion.GetTargets(kind).Count > 0
				&& entries.All(e => e.Kind is { } k && k.GetObjectType() == kind.GetObjectType());
			CanChangeKind = family;
			if (!family)
			{
				KindOptions = [];
				SelectedKind = null;
				return;
			}

			ProtoKind current = first!.Value;
			IReadOnlyList<ProtoKind> targets = ProtoConversion.GetTargets(current);
			if (KindOptions.Count != targets.Count || KindOptions[0].Kind != targets[0])
			{
				KindOptions = [.. targets.Select(k => new KindOption(k, Loc.Get("kind." + k)))];
			}

			SelectedKind = entries.All(e => e.Kind == current) ? KindOptions.FirstOrDefault(o => o.Kind == current) : null;
		}

		private void UpdateScores()
		{
			if (Single?.File is not { } file)
			{
				WeaponScore = 0;
				ArmorScore = 0;
				return;
			}

			WeaponScore = IsWeapon ? CharacterFormulas.WeaponScore(file.Get(FieldKeys.MinDamage), file.Get(FieldKeys.MaxDamage), file.Get("Perk", -1) != -1, WeaponScoreType == 1) : 0;
			ArmorScore = IsArmor
				? CharacterFormulas.ArmorScore(
					file.Get("ArmorClass"),
					ProtoSchemas.DamageSuffixes.Select(x => file.Get("Dt" + x)),
					ProtoSchemas.DamageSuffixes.Select(x => file.Get("Dr" + x)))
				: 0;
		}

		/// <summary>Click on the ground image of a critter: shows the next of its six directions.</summary>
		[RelayCommand]
		private void RotatePreview()
		{
			if (_directionCount <= 1 || Single is null)
			{
				return;
			}

			_previewDirection = ((_previewDirection < 0 ? 3 : _previewDirection) + 1) % _directionCount;
			UpdatePreviews(Single);
		}

		private async void UpdatePreviews(ProtoEntry entry)
		{
			ProtoFile file = entry.File!;
			ProtoEntry expected = entry;
			Int32 version = ++_previewVersion;
			Int32? groundValue = file.TryGet(FieldKeys.FrmId);
			Task<ArtAnimation?> groundTask = groundValue is null || groundValue.Value == -1
				? Task.FromResult<ArtAnimation?>(null)
				: _context.Art.GetAnimationAsync(new FrameId(groundValue.Value), _previewDirection);
			Task<ImageSource?> inventoryTask = LoadAsync(file, FieldKeys.InventoryFrmId);
			Task<ImageSource?> portraitTask = LoadAsync(file, FieldKeys.HeadFrmId);
			ArtAnimation? animation = await groundTask;
			ImageSource? ground = animation?.Frames[0];
			ImageSource? inventory = await inventoryTask;
			ImageSource? portrait = await portraitTask;
			if (ReferenceEquals(Single, expected) && version == _previewVersion)
			{
				_directionCount = animation?.DirectionCount ?? 1;
				GroundFrames = animation?.Frames.Cast<ImageSource>().ToList();
				GroundFramesPerSecond = animation?.FramesPerSecond ?? 10;
				GroundImage = ground;
				InventoryImage = inventory;
				PortraitImage = portrait;
			}
		}

		/// <summary>Decodes the images of prototypes the user is likely to select next, so that selecting them is instant.</summary>
		public void Prefetch(IReadOnlyList<ProtoEntry> entries)
		{
			ArtImageService art = _context.Art;
			List<(Int32 Ground, Int32 Inventory, Int32 Head)> ids = [];
			foreach (ProtoEntry entry in entries)
			{
				if (entry.File is { } file)
				{
					ids.Add((file.TryGet(FieldKeys.FrmId) ?? -1, file.TryGet(FieldKeys.InventoryFrmId) ?? -1, file.TryGet(FieldKeys.HeadFrmId) ?? -1));
				}
			}

			_ = Task.Run(async () =>
			{
				foreach ((Int32 ground, Int32 inventory, Int32 head) in ids)
				{
					try
					{
						if (ground != -1)
						{
							await art.GetAnimationAsync(new FrameId(ground), _previewDirection);
						}

						foreach (Int32 value in new[] { inventory, head })
						{
							if (value != -1)
							{
								await art.GetAsync(new FrameId(value));
							}
						}
					}
					catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or System.IO.InvalidDataException)
					{
						// A broken image is reported when its prototype is selected: here it only means nothing is cached.
					}
				}
			});
		}

		private async Task<ImageSource?> LoadAsync(ProtoFile file, String key)
		{
			Int32? value = file.TryGet(key);
			return value is null || value.Value == -1 ? null : await _context.Art.GetAsync(new ProtoForge.Core.Formats.Frm.FrameId(value.Value));
		}

		private void UpdateAi(ProtoEntry entry)
		{
			AiDetails = [];
			AiTitle = String.Empty;
			if (entry.Kind != ProtoKind.Critter)
			{
				return;
			}

			Int32 number = entry.File!.Get(FieldKeys.AiPacket);
			ProtoForge.Core.Formats.Ai.AiPacket? packet = _workspace.AiPackets.Find(number);
			if (packet is null)
			{
				AiTitle = Loc.Format("ai.notFound", number);
				return;
			}

			AiTitle = packet.DisplayName;
			AiDetails =
			[
				.. packet.Properties
					.Where(p => !p.Key.StartsWith("hit_", StringComparison.OrdinalIgnoreCase)
						&& !p.Key.EndsWith("_start", StringComparison.OrdinalIgnoreCase)
						&& !p.Key.EndsWith("_end", StringComparison.OrdinalIgnoreCase)
						&& p.Key != "packet_num")
					.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase),
			];
		}
	}
}