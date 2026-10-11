namespace ProtoForge.UI.Services
{
	using System;
	using System.Threading;
	using System.Threading.Tasks;
	using System.Windows;
	using System.Windows.Controls;
	using Microsoft.Win32;
	using Wpf.Ui;
	using Wpf.Ui.Abstractions;
	using Wpf.Ui.Appearance;
	using Wpf.Ui.Controls;
	using Wpf.Ui.Extensions;

	/// <summary>Resolves navigation pages from the DI container, so pages can receive their view model by constructor injection.</summary>
	public sealed class DependencyInjectionPageProvider : INavigationViewPageProvider
	{
		public Object? GetPage(Type pageType)
		{
			return App.Services.GetService(pageType);
		}
	}

	/// <summary>Non-blocking messages (snackbars).</summary>
	public interface INotifier
	{
		void Info(String title, String message);

		void Success(String title, String message);

		void Warning(String title, String message);

		void Error(String title, String message);
	}

	public sealed class Notifier : INotifier
	{
		private readonly ISnackbarService _snackbar;

		public Notifier(ISnackbarService snackbar)
		{
			_snackbar = snackbar;
		}

		public void Info(String title, String message)
		{
			Show(title, message, ControlAppearance.Info, SymbolRegular.Info24, 4);
		}

		public void Success(String title, String message)
		{
			Show(title, message, ControlAppearance.Success, SymbolRegular.CheckmarkCircle24, 3);
		}

		public void Warning(String title, String message)
		{
			Show(title, message, ControlAppearance.Caution, SymbolRegular.Warning24, 6);
		}

		public void Error(String title, String message)
		{
			Show(title, message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24, 10);
		}

		private void Show(String title, String message, ControlAppearance appearance, SymbolRegular icon, Int32 seconds)
		{
			Application.Current.Dispatcher.Invoke(() =>
				_snackbar.Show(title, message, appearance, new SymbolIcon(icon), TimeSpan.FromSeconds(seconds)));
		}
	}

	public enum DialogChoice
	{
		Primary,
		Secondary,
		Cancel,
	}

	/// <summary>File pickers and modal dialogs.</summary>
	public interface IDialogService
	{
		String? PickFolder(String title, String? initialFolder = null);

		String? PickOpenFile(String title, String filter, String? initialFolder = null);

		String? PickSaveFile(String title, String filter, String defaultName);

		Task<DialogChoice> AskAsync(String title, String message, String primary, String? secondary, String close);

		Task<Boolean> ConfirmAsync(String title, String message, String primary);

		Task<Boolean> ShowAsync(String title, Object content, String primary, String close);

		Task<String?> PromptAsync(String title, String label, String initial);
	}

	public sealed class DialogService : IDialogService
	{
		private readonly IContentDialogService _dialogs;

		public DialogService(IContentDialogService dialogs)
		{
			_dialogs = dialogs;
		}

		public String? PickFolder(String title, String? initialFolder = null)
		{
			OpenFolderDialog dialog = new() { Title = title };
			if (!String.IsNullOrEmpty(initialFolder) && System.IO.Directory.Exists(initialFolder))
			{
				dialog.InitialDirectory = initialFolder;
			}

			return dialog.ShowDialog() == true ? dialog.FolderName : null;
		}

		public String? PickOpenFile(String title, String filter, String? initialFolder = null)
		{
			OpenFileDialog dialog = new() { Title = title, Filter = filter, CheckFileExists = true };
			if (!String.IsNullOrEmpty(initialFolder) && System.IO.Directory.Exists(initialFolder))
			{
				dialog.InitialDirectory = initialFolder;
			}

			return dialog.ShowDialog() == true ? dialog.FileName : null;
		}

		public String? PickSaveFile(String title, String filter, String defaultName)
		{
			SaveFileDialog dialog = new() { Title = title, Filter = filter, FileName = defaultName, OverwritePrompt = true };
			return dialog.ShowDialog() == true ? dialog.FileName : null;
		}

		public async Task<DialogChoice> AskAsync(String title, String message, String primary, String? secondary, String close)
		{
			ContentDialogResult result = await _dialogs.ShowSimpleDialogAsync(
				new SimpleContentDialogCreateOptions
				{
					Title = title,
					Content = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 },
					PrimaryButtonText = primary,
					SecondaryButtonText = secondary ?? String.Empty,
					CloseButtonText = close,
				},
				CancellationToken.None);

			return result switch
			{
				ContentDialogResult.Primary => DialogChoice.Primary,
				ContentDialogResult.Secondary => DialogChoice.Secondary,
				_ => DialogChoice.Cancel,
			};
		}

		public async Task<Boolean> ConfirmAsync(String title, String message, String primary)
		{
			return await AskAsync(title, message, primary, null, Loc.Get("common.cancel")) == DialogChoice.Primary;
		}

		public async Task<Boolean> ShowAsync(String title, Object content, String primary, String close)
		{
			ContentDialogResult result = await _dialogs.ShowSimpleDialogAsync(
				new SimpleContentDialogCreateOptions
				{
					Title = title,
					Content = content,
					PrimaryButtonText = primary,
					CloseButtonText = close,
				},
				CancellationToken.None);
			return result == ContentDialogResult.Primary;
		}

		public async Task<String?> PromptAsync(String title, String label, String initial)
		{
			Wpf.Ui.Controls.TextBox box = new() { Text = initial, MinWidth = 360, PlaceholderText = label };
			StackPanel panel = new();
			panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
			panel.Children.Add(box);
			Boolean accepted = await ShowAsync(title, panel, Loc.Get("common.ok"), Loc.Get("common.cancel"));
			return accepted ? box.Text.Trim() : null;
		}
	}

	/// <summary>Light/dark theme switching.</summary>
	public static class ThemeHelper
	{
		/// <summary>Raised with the theme name ("Dark", "Light" or "System") each time a theme is applied.</summary>
		public static event EventHandler<String>? Applied;

		public static void Apply(String theme)
		{
			ApplicationTheme target = theme switch
			{
				"Light" => ApplicationTheme.Light,
				"System" => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark || ApplicationThemeManager.GetSystemTheme() == SystemTheme.Glow
					? ApplicationTheme.Dark
					: ApplicationTheme.Light,
				_ => ApplicationTheme.Dark,
			};
			ApplicationThemeManager.Apply(target, WindowBackdropType.Mica, updateAccent: true);
			Applied?.Invoke(null, theme);
		}
	}
}