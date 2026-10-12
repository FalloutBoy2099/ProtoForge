namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Net.Http;
	using System.Reflection;
	using System.Text.Json;
	using System.Threading.Tasks;
	using System.Windows;
	using System.Windows.Controls;
	using System.Windows.Media;
	using CommunityToolkit.Mvvm.ComponentModel;
	using CommunityToolkit.Mvvm.Input;
	using Services;

	public sealed partial class AboutViewModel : ObservableObject
	{
		private readonly IDialogService _dialogs;
		private readonly INotifier _notifier;

		public AboutViewModel(IDialogService dialogs, INotifier notifier)
		{
			_dialogs = dialogs;
			_notifier = notifier;
			Version? version = Assembly.GetExecutingAssembly().GetName().Version;
			VersionText = Loc.Format("about.version", version is null ? "1.0" : $"{version.Major}.{version.Minor}.{version.Build}");
			Tagline = Loc.Get("about.tagline");
			SystemInfo = $".NET {Environment.Version}  |  {System.Runtime.InteropServices.RuntimeInformation.OSDescription}  |  {Loc.LanguageCode}";
			Shortcuts =
			[
				new("Ctrl+O", Loc.Get("shortcut.open")),
				new("Ctrl+S", Loc.Get("shortcut.save")),
				new("Ctrl+Z / Ctrl+Y", Loc.Get("shortcut.undoRedo")),
				new("Ctrl+F / Ctrl+E", Loc.Get("shortcut.search")),
				new("Ctrl+D", Loc.Get("shortcut.duplicate")),
				new("Del", Loc.Get("shortcut.delete")),
				new("Ctrl+Shift+C", Loc.Get("shortcut.copyPid")),
				new("Enter / Esc", Loc.Get("shortcut.enterEsc")),
				new("↑ ↓ PgUp PgDn", Loc.Get("shortcut.navigate")),
				new("F3 / Shift+F3", Loc.Get("shortcut.findNext")),
				new("F6", Loc.Get("shortcut.switchPane")),
				new("Ctrl+Shift+R", Loc.Get("shortcut.revert")),
			];
		}

		public Boolean HasHomepage
		{
			get { return AppInfo.HasHomepage; }
		}

		public Boolean CanCheckForUpdates
		{
			get { return AppInfo.CanCheckForUpdates; }
		}

		/// <summary>Shows the README of the project (embedded in the program) in a dialog.</summary>
		[RelayCommand]
		private async Task ShowReadme()
		{
			Assembly assembly = typeof(AboutViewModel).Assembly;
			using Stream? stream = assembly.GetManifestResourceStream("README.md");
			String text = stream is null ? Loc.Get("about.readmeMissing") : await new StreamReader(stream).ReadToEndAsync();
			TextBox box = new()
			{
				Text = text,
				IsReadOnly = true,
				TextWrapping = TextWrapping.Wrap,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				FontFamily = new FontFamily("Consolas"),
				FontSize = 12,
				Width = 700,
				Height = 420,
				AcceptsReturn = true,
			};
			await _dialogs.ShowAsync(Loc.Get("about.readme"), box, Loc.Get("common.ok"), Loc.Get("common.cancel"));
		}

		[RelayCommand]
		private void OpenHomepage()
		{
			if (AppInfo.HasHomepage)
			{
				Process.Start(new ProcessStartInfo(AppInfo.HomepageUrl) { UseShellExecute = true });
			}
		}

		/// <summary>Asks the release address of the project for the latest version (only when the user clicks the button).</summary>
		[RelayCommand]
		private async Task CheckForUpdates()
		{
			if (!AppInfo.CanCheckForUpdates)
			{
				return;
			}

			try
			{
				using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
				client.DefaultRequestHeaders.UserAgent.ParseAdd("ProtoForge");
				using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync(AppInfo.LatestReleaseApiUrl));
				String? tag = json.RootElement.TryGetProperty("tag_name", out JsonElement t) ? t.GetString()
					: json.RootElement.TryGetProperty("version", out JsonElement v) ? v.GetString() : null;
				if (tag is null || !Version.TryParse(tag.TrimStart('v', 'V'), out Version? latest))
				{
					_notifier.Warning(Loc.Get("about.updates"), Loc.Get("about.updateUnknown"));
					return;
				}

				Version current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0);
				if (latest > current)
				{
					_notifier.Info(Loc.Get("about.updates"), Loc.Format("about.updateAvailable", latest));
					if (AppInfo.HasHomepage)
					{
						OpenHomepage();
					}
				}
				else
				{
					_notifier.Success(Loc.Get("about.updates"), Loc.Get("about.upToDate"));
				}
			}
			catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
			{
				_notifier.Error(Loc.Get("about.updates"), ProtoForge.Core.UserTexts.Describe(ex));
			}
		}

		public String VersionText { get; }

		public String Tagline { get; }

		public String SystemInfo { get; }

		public IReadOnlyList<KeyValuePair<String, String>> Shortcuts { get; }
	}
}