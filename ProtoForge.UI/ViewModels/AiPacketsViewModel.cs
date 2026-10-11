namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.Globalization;
	using System.IO;
	using System.Linq;
	using System.Threading.Tasks;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Ai;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Infrastructure.Workspace;
	using Fields;
	using Services;

	/// <summary>A value of a list with a display text (keyword of an AI parameter, taunt set...).</summary>
	public sealed record AiChoice(String Value, String Label)
	{
		public override String ToString()
		{
			return Label;
		}
	}

	/// <summary>A packet of the list: its section plus the text shown in the list.</summary>
	public sealed record AiPacketItem(AiSection Section, Int32? Number)
	{
		public String Name
		{
			get { return Section.Name; }
		}

		public String DisplayName
		{
			get { return $"{Number?.ToString(CultureInfo.InvariantCulture) ?? "?"}: {Section.Name}"; }
		}
	}

	/// <summary>One editable parameter of the selected AI packet (a number, a keyword, several keywords or a taunt set).</summary>
	public sealed partial class AiParameterViewModel : ObservableObject
	{
		private readonly AiPacketsViewModel _owner;
		private Boolean _refreshing;

		[ObservableProperty]
		private Double? _number;

		[ObservableProperty]
		private String _value = String.Empty;

		[ObservableProperty]
		private Boolean _isSet;

		public AiParameterViewModel(AiPacketsViewModel owner, AiParameter parameter, IReadOnlyList<AiChoice> choices)
		{
			_owner = owner;
			Parameter = parameter;
			Choices = new ObservableCollection<AiChoice>(choices);
		}

		public AiParameter Parameter { get; }

		public ObservableCollection<AiChoice> Choices { get; }

		public String Key
		{
			get { return Parameter.Key; }
		}

		public String Label
		{
			get { return Loc.Has("ai.param." + Key) ? Loc.Get("ai.param." + Key) : Loc.Humanize(Key); }
		}

		public Boolean IsNumber
		{
			get { return Parameter.Kind == AiParameterKind.Number; }
		}

		public Boolean IsChoice
		{
			get { return Parameter.Kind is AiParameterKind.Choice or AiParameterKind.TauntSet; }
		}

		public Boolean IsMultiChoice
		{
			get { return Parameter.Kind == AiParameterKind.MultiChoice; }
		}

		public Double Minimum
		{
			get { return Parameter.Min; }
		}

		public Double Maximum
		{
			get { return Parameter.Max; }
		}

		public void Refresh(String? stored)
		{
			_refreshing = true;
			try
			{
				IsSet = stored is not null;
				if (Parameter.Kind is AiParameterKind.Choice or AiParameterKind.TauntSet && stored is not null && !Choices.Any(c => c.Value == stored))
				{
					// A value the lists do not know (a custom taunt set, a keyword from another version) stays selectable.
					Choices.Add(new AiChoice(stored, stored));
				}

				if (IsNumber)
				{
					Number = Double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out Double number) ? number : 0;
				}
				else
				{
					Value = stored ?? AiParameters.NotSet;
				}
			}
			finally
			{
				_refreshing = false;
			}
		}

		partial void OnNumberChanged(Double? value)
		{
			if (!_refreshing && value is { } number)
			{
				_owner.CommitValue(Key, ((Int32)Math.Round(number)).ToString(CultureInfo.InvariantCulture));
			}
		}

		partial void OnValueChanged(String value)
		{
			if (!_refreshing)
			{
				_owner.CommitValue(Key, value.Trim());
			}
		}
	}

	/// <summary>A section of the parameter form (Behavior, Taunts...).</summary>
	public sealed class AiParameterGroupViewModel
	{
		public AiParameterGroupViewModel(String key, IReadOnlyList<AiParameterViewModel> items)
		{
			Key = key;
			Items = items;
		}

		public String Key { get; }

		public String Header
		{
			get { return Loc.Has("ai.group." + Key) ? Loc.Get("ai.group." + Key) : Key; }
		}

		public IReadOnlyList<AiParameterViewModel> Items { get; }
	}

	/// <summary>A parameter without a dedicated editor (found in the file but unknown to ProtoForge): edited as plain text.</summary>
	public sealed partial class AiOtherParameterViewModel : ObservableObject
	{
		private readonly AiPacketsViewModel _owner;
		private Boolean _refreshing;

		[ObservableProperty]
		private String _value;

		public AiOtherParameterViewModel(AiPacketsViewModel owner, String key, String value)
		{
			_owner = owner;
			Key = key;
			_refreshing = true;
			_value = value;
			_refreshing = false;
		}

		public String Key { get; }

		partial void OnValueChanged(String value)
		{
			if (!_refreshing)
			{
				_owner.CommitValue(Key, value.Trim());
			}
		}
	}

	/// <summary>
	/// The combat AI packets (<c>data/AI.txt</c>): list, parameter form, preferred drugs, raw text and the critters that use
	/// the packet. Everything is edited through undoable actions and written by the usual save plan.
	/// </summary>
	public sealed partial class AiPacketsViewModel : ObservableObject
	{
		private readonly WorkspaceService _workspaceService;
		private readonly ShellNavigator _shell;
		private readonly IDialogService _dialogs;
		private readonly INotifier _notifier;
		private AiFile? _subscribed;
		private Boolean _committing;
		private IReadOnlyList<AiSection> _all = [];

		[ObservableProperty]
		private IReadOnlyList<AiPacketItem> _packets = [];

		[ObservableProperty]
		private AiPacketItem? _selected;

		[ObservableProperty]
		private IReadOnlyList<AiParameterGroupViewModel> _groups = [];

		[ObservableProperty]
		private IReadOnlyList<AiOtherParameterViewModel> _otherParameters = [];

		[ObservableProperty]
		private IReadOnlyList<ReferenceOption> _drugs = [];

		[ObservableProperty]
		private IReadOnlyList<ReferenceOption> _drugOptions = [];

		[ObservableProperty]
		private ReferenceOption? _selectedDrug;

		[ObservableProperty]
		private ReferenceOption? _drugToAdd;

		[ObservableProperty]
		private IReadOnlyList<ProtoEntry> _users = [];

		[ObservableProperty]
		private String _filterText = String.Empty;

		[ObservableProperty]
		private String _summary = String.Empty;

		[ObservableProperty]
		private ProtoEntry? _selectedUser;

		[ObservableProperty]
		private String _rawText = String.Empty;

		[ObservableProperty]
		private Boolean _isCustomFile;

		[ObservableProperty]
		private String _fileLabel = String.Empty;

		[ObservableProperty]
		private Boolean _hasWorkspace;

		public AiPacketsViewModel(WorkspaceService workspaceService, ShellNavigator shell, IDialogService dialogs, INotifier notifier)
		{
			_workspaceService = workspaceService;
			_shell = shell;
			_dialogs = dialogs;
			_notifier = notifier;
			_workspaceService.WorkspaceChanged += (_, ws) => Attach(ws);
			Attach(_workspaceService.Current);
		}

		public WorkspaceService Workspace
		{
			get { return _workspaceService; }
		}

		private AiFile? ActiveFile
		{
			get { return _workspaceService.Current is { } ws ? ws.CustomAi ?? ws.Ai : null; }
		}

		partial void OnFilterTextChanged(String value)
		{
			ApplyFilter();
		}

		partial void OnSelectedChanged(AiPacketItem? value)
		{
			BuildForm();
		}

		private void Attach(GameWorkspace? workspace)
		{
			if (_subscribed is not null)
			{
				_subscribed.Changed -= OnFileChanged;
				_subscribed = null;
			}

			HasWorkspace = workspace is not null;
			if (workspace is null)
			{
				_all = [];
				Packets = [];
				Selected = null;
				Summary = String.Empty;
				return;
			}

			_subscribed = ActiveFile;
			if (_subscribed is not null)
			{
				_subscribed.Changed += OnFileChanged;
			}

			IsCustomFile = workspace.CustomAi is not null;
			FileLabel = workspace.CustomAiPath ?? "data/ai.txt";
			ReloadPackets(keepSelection: false);
		}

		private void OnFileChanged(Object? sender, EventArgs e)
		{
			System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
			{
				if (_committing)
				{
					return;
				}

				Boolean structureChanged = !(ActiveFile?.Sections.SequenceEqual(_all) ?? true);
				if (structureChanged)
				{
					ReloadPackets(keepSelection: true);
				}
				else
				{
					RefreshValues();
				}
			});
		}

		private void ReloadPackets(Boolean keepSelection)
		{
			String? previous = keepSelection ? Selected?.Name : null;
			_all = [.. ActiveFile?.Sections ?? []];
			ApplyFilter();
			Summary = Loc.Format("ai.summary", _all.Count);
			Selected = previous is null ? null : Packets.FirstOrDefault(p => String.Equals(p.Name, previous, StringComparison.OrdinalIgnoreCase));
			if (Selected is null)
			{
				BuildForm();
			}
		}

		private void ApplyFilter()
		{
			AiFile? file = ActiveFile;
			String text = FilterText.Trim();
			Packets =
			[
				.. _all
					.Select(p => new AiPacketItem(p, file?.GetPacketNumber(p)))
					.Where(p => text.Length == 0 || p.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || p.Number?.ToString(CultureInfo.InvariantCulture) == text)
					.OrderBy(p => p.Number ?? Int32.MaxValue),
			];
		}

		private void BuildForm()
		{
			GameWorkspace? workspace = _workspaceService.Current;
			AiSection? section = Selected?.Section;
			if (workspace is null || section is null)
			{
				Groups = [];
				OtherParameters = [];
				Drugs = [];
				Users = [];
				RawText = String.Empty;
				return;
			}

			List<AiParameterGroupViewModel> groups = [];
			foreach (String group in AiParameters.Groups)
			{
				List<AiParameterViewModel> items = [];
				foreach (AiParameter parameter in AiParameters.All.Where(p => p.Group == group))
				{
					items.Add(new AiParameterViewModel(this, parameter, BuildChoices(workspace, parameter)));
				}

				groups.Add(new AiParameterGroupViewModel(group, items));
			}

			Groups = groups;
			DrugOptions =
			[
				.. workspace.Catalog.GetEntries(ProtoObjectType.Item)
					.Where(e => e.Kind == ProtoKind.Drug && e.File is not null)
					.Select(e => new ReferenceOption(e.Pid.Value, $"{e.DisplayName}  [{e.Pid}]")),
			];
			RefreshValues();
		}

		private static IReadOnlyList<AiChoice> BuildChoices(GameWorkspace workspace, AiParameter parameter)
		{
			switch (parameter.Kind)
			{
				case AiParameterKind.Choice:
					return [new AiChoice(AiParameters.NotSet, Loc.Get("ai.notSet")), .. parameter.Options!.Select(o => new AiChoice(o, o))];
				case AiParameterKind.MultiChoice:
					return [.. parameter.Options!.Select(o => new AiChoice(o, o))];
				case AiParameterKind.TauntSet:
					IReadOnlyList<String> names = parameter.Key == AiParameters.GeneralType ? workspace.AiGeneralTypes : workspace.AiBodyTypes;
					return [new AiChoice(AiParameters.NotSet, Loc.Get("ai.notSet")), .. names.Select(n => new AiChoice(n, n))];
				default:
					return [];
			}
		}

		/// <summary>Reloads the values of the form from the file (after a selection change, an undo or a redo).</summary>
		private void RefreshValues()
		{
			GameWorkspace? workspace = _workspaceService.Current;
			AiSection? section = Selected?.Section;
			if (workspace is null || section is null)
			{
				return;
			}

			foreach (AiParameterGroupViewModel group in Groups)
			{
				foreach (AiParameterViewModel item in group.Items)
				{
					item.Refresh(section.GetValue(item.Key));
				}
			}

			OtherParameters = [.. section.GetProperties().Where(p => !AiParameters.IsKnown(p.Key)).Select(p => new AiOtherParameterViewModel(this, p.Key, p.Value))];

			IReadOnlyList<Int32> pids = AiParameters.ParseDrugs(section.GetValue(AiParameters.DrugsKey));
			Drugs = [.. pids.Select(pid => new ReferenceOption(pid, DescribeDrug(workspace, pid)))];

			Int32? number = ActiveFile?.GetPacketNumber(section);
			Users = number is null || !ReferenceEquals(ActiveFile, workspace.Ai) ? [] : [.. workspace.GetPacketUsers(number.Value)];
			RawText = ActiveFile?.GetRawText(section.Name) ?? String.Empty;
			AddDrugCommand.NotifyCanExecuteChanged();
			RemoveDrugCommand.NotifyCanExecuteChanged();
			MoveDrugUpCommand.NotifyCanExecuteChanged();
			MoveDrugDownCommand.NotifyCanExecuteChanged();
			ApplyRawTextCommand.NotifyCanExecuteChanged();
		}

		private static String DescribeDrug(GameWorkspace workspace, Int32 pid)
		{
			ProtoEntry? entry = workspace.Catalog.Find(pid);
			return entry is null ? $"{pid}  <{Loc.Get("ai.errorPid")}>" : $"{entry.DisplayName}  [{entry.Pid}]";
		}

		/// <summary>Writes a parameter value (called by the parameter editors).</summary>
		internal void CommitValue(String key, String value)
		{
			GameWorkspace? workspace = _workspaceService.Current;
			AiFile? file = ActiveFile;
			if (workspace is null || file is null || Selected?.Section is not { } section)
			{
				return;
			}

			String? stored = section.GetValue(key);
			if (value == AiParameters.NotSet && stored is null)
			{
				return;
			}

			_committing = true;
			try
			{
				workspace.SetAiValue(file, section.Name, key, value);
			}
			finally
			{
				_committing = false;
			}

			if (key == AiParameters.PacketNumberKey)
			{
				ApplyFilter();
			}
		}

		#region Drugs

		private Boolean CanAddDrug()
		{
			return DrugToAdd is not null && Drugs.Count < 3;
		}

		partial void OnDrugToAddChanged(ReferenceOption? value)
		{
			AddDrugCommand.NotifyCanExecuteChanged();
		}

		partial void OnSelectedDrugChanged(ReferenceOption? value)
		{
			RemoveDrugCommand.NotifyCanExecuteChanged();
			MoveDrugUpCommand.NotifyCanExecuteChanged();
			MoveDrugDownCommand.NotifyCanExecuteChanged();
		}

		[RelayCommand(CanExecute = nameof(CanAddDrug))]
		private void AddDrug()
		{
			if (DrugToAdd is { } drug)
			{
				WriteDrugs([.. Drugs.Select(d => d.Value), drug.Value]);
			}
		}

		private Boolean CanRemoveDrug()
		{
			return SelectedDrug is not null;
		}

		[RelayCommand(CanExecute = nameof(CanRemoveDrug))]
		private void RemoveDrug()
		{
			if (SelectedDrug is { } drug)
			{
				List<Int32> list = [.. Drugs.Select(d => d.Value)];
				list.RemoveAt(Drugs.ToList().IndexOf(drug));
				WriteDrugs(list);
			}
		}

		[RelayCommand(CanExecute = nameof(CanRemoveDrug))]
		private void MoveDrugUp()
		{
			MoveDrug(-1);
		}

		[RelayCommand(CanExecute = nameof(CanRemoveDrug))]
		private void MoveDrugDown()
		{
			MoveDrug(1);
		}

		private void MoveDrug(Int32 delta)
		{
			if (SelectedDrug is not { } drug)
			{
				return;
			}

			List<Int32> list = [.. Drugs.Select(d => d.Value)];
			Int32 index = Drugs.ToList().IndexOf(drug);
			Int32 target = index + delta;
			if (target < 0 || target >= list.Count)
			{
				return;
			}

			(list[index], list[target]) = (list[target], list[index]);
			WriteDrugs(list);
			SelectedDrug = Drugs.ElementAtOrDefault(target);
		}

		private void WriteDrugs(IEnumerable<Int32> pids)
		{
			CommitValue(AiParameters.DrugsKey, AiParameters.FormatDrugs(pids));
			RefreshValues();
		}

		#endregion

		#region Raw text

		private Boolean CanApplyRawText()
		{
			return Selected is not null && ActiveFile is { } file && !String.Equals(RawText, file.GetRawText(Selected.Name), StringComparison.Ordinal);
		}

		partial void OnRawTextChanged(String value)
		{
			ApplyRawTextCommand.NotifyCanExecuteChanged();
		}

		/// <summary>Replaces the text of the packet with what was typed in the raw text editor.</summary>
		[RelayCommand(CanExecute = nameof(CanApplyRawText))]
		private void ApplyRawText()
		{
			if (_workspaceService.Current is not { } workspace || ActiveFile is not { } file || Selected is not { } section)
			{
				return;
			}

			workspace.SetAiRawText(file, section.Name, RawText);
			_notifier.Success(Loc.Get("ai.rawText"), Loc.Get("ai.rawApplied"));
		}

		[RelayCommand]
		private void ResetRawText()
		{
			RawText = ActiveFile is { } file && Selected is { } section ? file.GetRawText(section.Name) : String.Empty;
		}

		#endregion

		#region Packets and files

		[RelayCommand]
		private async Task AddPacket()
		{
			if (_workspaceService.Current is not { } workspace || ActiveFile is not { } file)
			{
				return;
			}

			Int32 number = file.NextPacketNumber();
			String? name = await _dialogs.PromptAsync(Loc.Format("ai.addTitle", number), Loc.Get("ai.addName"), String.Empty);
			if (String.IsNullOrWhiteSpace(name))
			{
				return;
			}

			name = name.Trim();
			if (name.Contains(']', StringComparison.Ordinal) || name.Contains('[', StringComparison.Ordinal) || file.Find(name) is not null)
			{
				_notifier.Warning(Loc.Get("ai.addTitle2"), Loc.Get("ai.nameInvalid"));
				return;
			}

			workspace.AddAiPacket(file, name);
			Selected = Packets.FirstOrDefault(p => String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
			_notifier.Success(Loc.Get("ai.addTitle2"), Loc.Format("ai.added", name, number));
		}

		private Boolean HasSelectedPacket()
		{
			return Selected is not null;
		}

		[RelayCommand(CanExecute = nameof(HasSelectedPacket))]
		private async Task RemovePacket()
		{
			if (_workspaceService.Current is not { } workspace || ActiveFile is not { } file || Selected is not { } section)
			{
				return;
			}

			Int32? number = section.Number;
			Int32 users = number is null || !ReferenceEquals(file, workspace.Ai) ? 0 : workspace.GetPacketUsers(number.Value).Count();
			String message = users > 0 ? Loc.Format("ai.removeUsed", section.Name, users) : Loc.Format("ai.removeConfirm", section.Name);
			if (await _dialogs.ConfirmAsync(Loc.Get("ai.removeTitle"), message, Loc.Get("delete.button")))
			{
				workspace.RemoveAiPacket(file, section.Name);
			}
		}

		partial void OnSelectedUserChanged(ProtoEntry? value)
		{
		}

		[RelayCommand]
		private void OpenUser(ProtoEntry? entry)
		{
			entry ??= SelectedUser;
			if (entry is not null)
			{
				_shell.ShowProto(entry.Pid);
			}
		}

		[RelayCommand]
		private void OpenCustomFile()
		{
			if (_workspaceService.Current is not { } workspace)
			{
				return;
			}

			String? path = _dialogs.PickOpenFile(Loc.Get("ai.openTitle"), "AI (*.txt)|*.txt|*.*|*.*");
			if (path is null)
			{
				return;
			}

			try
			{
				workspace.OpenCustomAi(path);
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
			{
				_notifier.Error(Loc.Get("ai.openTitle"), ex is InvalidDataException ? Loc.Get("ai.notAiFile") : ex.Message);
				return;
			}

			Attach(workspace);
			_notifier.Info(Loc.Get("ai.openTitle"), Loc.Format("ai.openedCustom", Path.GetFileName(path)));
		}

		[RelayCommand]
		private void CloseCustomFile()
		{
			if (_workspaceService.Current is not { } workspace)
			{
				return;
			}

			workspace.CloseCustomAi();
			Attach(workspace);
		}

		#endregion
	}
}