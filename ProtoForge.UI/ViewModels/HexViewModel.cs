namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Globalization;
	using System.IO;
	using System.Linq;
	using System.Threading.Tasks;
	using System.Windows;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Infrastructure.Settings;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	/// <summary>A byte range of the file that belongs to a known field.</summary>
	public sealed record HexRegion(Int32 Offset, Int32 Length, ProtoField Field, Int32 ColorIndex);

	/// <summary>The raw bytes of the selected prototype, with the known fields overlaid and the bytes changed since loading highlighted.</summary>
	public sealed partial class HexViewModel : ObservableObject, IDisposable
	{
		private readonly GameWorkspace _workspace;
		private readonly SettingsService _settings;
		private readonly IDialogService _dialogs;
		private readonly INotifier _notifier;
		private readonly Dictionary<String, FileSystemWatcher> _watchers = [];
		private readonly Dictionary<String, ProtoEntry> _watched = [];
		private readonly HashSet<String> _asking = [];

		[ObservableProperty]
		private Byte[] _data = [];

		[ObservableProperty]
		private IReadOnlyList<HexRegion> _regions = [];

		[ObservableProperty]
		private IReadOnlyList<Int32> _changedOffsets = [];

		[ObservableProperty]
		private Int32 _selectedOffset = -1;

		[ObservableProperty]
		private String _selectionInfo = String.Empty;

		private ProtoEntry? _entry;

		public HexViewModel(GameWorkspace workspace, SettingsService settings, IDialogService dialogs, INotifier notifier)
		{
			_workspace = workspace;
			_settings = settings;
			_dialogs = dialogs;
			_notifier = notifier;
		}

		/// <summary>
		/// Writes the prototype to a temporary file and opens it in the hex editor of the settings. When the editor saves the file,
		/// the changes are offered as an undoable import: the game files are never touched.
		/// </summary>
		[RelayCommand]
		private void OpenInExternalEditor()
		{
			if (_entry?.File is not { } file)
			{
				return;
			}

			String editor = _settings.Current.ExternalHexEditor;
			if (editor.Length == 0 || !File.Exists(editor))
			{
				String? picked = _dialogs.PickOpenFile(Loc.Get("hex.pickEditor"), "*.exe|*.exe|*.*|*.*");
				if (picked is null)
				{
					return;
				}

				editor = picked;
				_settings.Current.ExternalHexEditor = picked;
				_settings.Save();
			}

			String folder = Path.Combine(Path.GetTempPath(), "ProtoForge", "hex");
			Directory.CreateDirectory(folder);
			String path = Path.Combine(folder, $"{_entry.Pid.Value:X8}-{_entry.FileName}");
			File.WriteAllBytes(path, file.ToArray());

			try
			{
				Process.Start(new ProcessStartInfo(editor, "\"" + path + "\"") { UseShellExecute = true });
			}
			catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
			{
				_notifier.Error(Loc.Get("hex.external"), ex.Message);
				return;
			}

			Watch(path, _entry);
			_notifier.Info(Loc.Get("hex.external"), Loc.Get("hex.externalOpened"));
		}

		private void Watch(String path, ProtoEntry entry)
		{
			_watched[path] = entry;
			if (_watchers.ContainsKey(path))
			{
				return;
			}

			FileSystemWatcher watcher = new(Path.GetDirectoryName(path)!, Path.GetFileName(path)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size };
			watcher.Changed += (_, e) => Application.Current?.Dispatcher.InvokeAsync(async () => await OnExternalChangeAsync(e.FullPath));
			watcher.EnableRaisingEvents = true;
			_watchers[path] = watcher;
		}

		private async Task OnExternalChangeAsync(String path)
		{
			if (!_watched.TryGetValue(path, out ProtoEntry? entry) || !_asking.Add(path))
			{
				return;
			}

			try
			{
				await Task.Delay(500);
				Byte[] bytes;
				try
				{
					bytes = await File.ReadAllBytesAsync(path);
				}
				catch (IOException)
				{
					return;
				}

				if (entry.File is null || bytes.AsSpan().SequenceEqual(entry.File.Span))
				{
					return;
				}

				ProtoFile? parsed = ProtoFile.TryParse(bytes, entry.Type, out String? error);
				if (parsed is null)
				{
					_notifier.Warning(Loc.Get("hex.external"), Loc.Format("hex.externalInvalid", error ?? String.Empty));
					return;
				}

				if (await _dialogs.ConfirmAsync(Loc.Get("hex.external"), Loc.Format("hex.externalChanged", entry.FileName), Loc.Get("hex.externalImport")))
				{
					_workspace.ReplaceFile(entry, parsed, Loc.Get("hex.external"));
				}
			}
			finally
			{
				_asking.Remove(path);
			}
		}

		public void Dispose()
		{
			foreach (KeyValuePair<String, FileSystemWatcher> pair in _watchers)
			{
				pair.Value.Dispose();
				try
				{
					File.Delete(pair.Key);
				}
				catch (IOException)
				{
					// The temporary copy is only a convenience: leaving it behind is harmless.
				}
			}

			_watchers.Clear();
			_watched.Clear();
		}

		public Boolean HasData
		{
			get { return Data.Length > 0; }
		}

		public void Load(ProtoEntry? entry)
		{
			_entry = entry;
			ProtoFile? file = entry?.File;
			if (file is null)
			{
				Data = [];
				Regions = [];
				ChangedOffsets = [];
				SelectionInfo = String.Empty;
				OnPropertyChanged(nameof(HasData));
				return;
			}

			Data = file.ToArray();
			List<String> groups = [.. file.Schema.Groups];
			Regions =
			[
				.. file.Schema.Fields
					.Where(f => !f.IsVirtual && file.HasField(f))
					.Select(f => new HexRegion(f.Offset, f.Size, f, groups.IndexOf(f.Group))),
			];
			ChangedOffsets = ProtoDiff.GetDifferentOffsets(entry!.Baseline, file);
			OnPropertyChanged(nameof(HasData));
			UpdateSelectionInfo();
		}

		public void Refresh()
		{
			Load(_entry);
		}

		/// <summary>Writes one byte (an undoable replacement of the whole file content).</summary>
		public void EditByte(Int32 offset, Byte value)
		{
			ProtoFile? file = _entry?.File;
			if (_entry is null || file is null || offset < 0 || offset >= file.Length || file.ReadByte(offset) == value)
			{
				return;
			}

			ProtoFile copy = file.Clone();
			copy.WriteByte(offset, value);
			_workspace.ReplaceFile(_entry, copy, Loc.Format("hex.edit", offset.ToString("X2", CultureInfo.InvariantCulture)));
		}

		partial void OnSelectedOffsetChanged(Int32 value)
		{
			UpdateSelectionInfo();
		}

		private void UpdateSelectionInfo()
		{
			if (SelectedOffset < 0 || SelectedOffset >= Data.Length)
			{
				SelectionInfo = String.Empty;
				return;
			}

			HexRegion? region = Regions.FirstOrDefault(r => SelectedOffset >= r.Offset && SelectedOffset < r.Offset + r.Length);
			String position = $"0x{SelectedOffset:X2} = 0x{Data[SelectedOffset]:X2} ({Data[SelectedOffset]})";
			if (region is null)
			{
				SelectionInfo = position + "  -  " + Loc.Get("hex.unknownByte");
				return;
			}

			Int32? value = _entry?.File?.TryGet(region.Field);
			SelectionInfo = $"{position}  -  {Loc.Field(region.Field.Key)} @0x{region.Offset:X2}+{region.Length} = {value}";
		}
	}
}