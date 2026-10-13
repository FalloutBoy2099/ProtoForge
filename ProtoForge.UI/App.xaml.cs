namespace ProtoForge.UI
{
	using System;
	using System.IO;
	using System.Threading.Tasks;
	using System.Windows;
	using System.Windows.Threading;
	using Microsoft.Extensions.DependencyInjection;
	using Wpf.Ui;
	using Wpf.Ui.Abstractions;
	using ProtoForge.Infrastructure.Settings;
	using ProtoForge.Infrastructure.Templates;
	using Services;
	using ViewModels;
	using Views;

	public partial class App : Application
	{
		private ServiceProvider? _services;

		public static IServiceProvider Services
		{
			get { return ((App)Current)._services!; }
		}

		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);
			DispatcherUnhandledException += OnUnhandledException;

			SettingsService settings = new();
			Loc.Initialize(settings.Current.UiLanguage);
			ProtoForge.Core.UserTexts.Translator = key => Loc.Has(key) ? Loc.Get(key) : null;

			ServiceCollection services = new();
			ConfigureServices(services, settings);
			_services = services.BuildServiceProvider();

			ThemeHelper.Apply(settings.Current.Theme);

			MainWindow window = _services.GetRequiredService<MainWindow>();
			window.Show();

			// Command line: ProtoForge.exe <game folder | mod folder | .dat> [--output <folder>]
			if (e.Args.Length > 0 && e.Args[0] == "--last")
			{
				// Restart after a language change: reopen the most recent project.
				if (settings.Current.RecentProjects.Count > 0)
				{
					_ = ReportAsync(_services.GetRequiredService<WorkspaceService>().OpenAsync(settings.Current.RecentProjects[0].Layout.Clone()));
				}
			}
			else if (e.Args.Length > 0)
			{
				String? output = null;
				Int32 outputIndex = Array.FindIndex(e.Args, a => String.Equals(a, "--output", StringComparison.OrdinalIgnoreCase));
				if (outputIndex >= 0 && outputIndex + 1 < e.Args.Length)
				{
					output = e.Args[outputIndex + 1];
				}

				_ = OpenFromCommandLineAsync(e.Args[0], output);
			}
		}

		/// <summary>Awaits a task that nobody else awaits: a failure is shown instead of vanishing.</summary>
		private async Task ReportAsync(Task task)
		{
			try
			{
				await task;
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
			{
				_services?.GetService<INotifier>()?.Error(Loc.Get("open.failed"), ProtoForge.Core.UserTexts.Describe(ex));
			}
		}

		private async Task OpenFromCommandLineAsync(String path, String? output)
		{
			await _services!.GetRequiredService<MainWindowViewModel>().OpenPathAsync(path, output);
		}

		private static void ConfigureServices(IServiceCollection services, SettingsService settings)
		{
			services.AddSingleton(settings);
			services.AddSingleton<UserTemplateStore>();

			services.AddSingleton<INavigationViewPageProvider, DependencyInjectionPageProvider>();
			services.AddSingleton<IContentDialogService, ContentDialogService>();
			services.AddSingleton<ISnackbarService, SnackbarService>();

			services.AddSingleton<INotifier, Notifier>();
			services.AddSingleton<IDialogService, DialogService>();
			services.AddSingleton<ArtImageService>();
			services.AddSingleton<ExchangeService>();
			services.AddSingleton<WorkspaceService>();
			services.AddSingleton<ShellNavigator>();

			services.AddSingleton<MainWindow>();
			services.AddSingleton<MainWindowViewModel>();

			// Pages: one instance each, kept alive by the navigation view.
			services.AddSingleton<LibraryPage>();
			services.AddSingleton<LibraryViewModel>();
			services.AddSingleton<ComparePage>();
			services.AddTransient<CompareViewModel>();
			services.AddSingleton<ValidationPage>();
			services.AddSingleton<ValidationViewModel>();
			services.AddSingleton<MessagesPage>();
			services.AddSingleton<MessagesViewModel>();
			services.AddSingleton<BalancePage>();
			services.AddSingleton<BalanceViewModel>();
			services.AddSingleton<ToolsPage>();
			services.AddSingleton<ToolsViewModel>();
			services.AddSingleton<AiPacketsPage>();
			services.AddSingleton<AiPacketsViewModel>();
			services.AddSingleton<SettingsPage>();
			services.AddSingleton<SettingsViewModel>();
			services.AddSingleton<AboutPage>();
			services.AddSingleton<AboutViewModel>();

			services.AddTransient<CompareWindow>();
		}

		private void OnUnhandledException(Object sender, DispatcherUnhandledExceptionEventArgs e)
		{
			e.Handled = true;
			try
			{
				String log = Path.Combine(SettingsService.GetDataFolder(), "crash.log");
				File.AppendAllText(log, $"[{DateTime.Now:O}] {e.Exception}\r\n\r\n");
			}
			catch (IOException)
			{
				// Nothing more can be done if the log cannot be written.
			}

			INotifier? notifier = _services?.GetService<INotifier>();
			if (notifier is not null)
			{
				notifier.Error(Loc.Get("error.unexpected"), ProtoForge.Core.UserTexts.Describe(e.Exception));
			}
			else
			{
				MessageBox.Show(e.Exception.Message, "ProtoForge", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
	}
}