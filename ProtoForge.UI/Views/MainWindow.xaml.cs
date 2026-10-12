namespace ProtoForge.UI.Views
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Windows;
	using Wpf.Ui;
	using Wpf.Ui.Abstractions;
	using Wpf.Ui.Controls;
	using ProtoForge.Infrastructure.Settings;
	using Services;
	using ViewModels;

	public partial class MainWindow : FluentWindow
	{
		private readonly MainWindowViewModel _viewModel;
		private readonly WorkspaceService _workspace;
		private readonly SettingsService _settings;
		private Boolean _closeConfirmed;
		private Boolean? _relaunchWithProject;

		public MainWindow(
			MainWindowViewModel viewModel,
			WorkspaceService workspace,
			SettingsService settings,
			INavigationViewPageProvider pageProvider,
			IContentDialogService dialogService,
			ISnackbarService snackbarService,
			ShellNavigator shell)
		{
			_viewModel = viewModel;
			_workspace = workspace;
			_settings = settings;
			DataContext = viewModel;
			InitializeComponent();

			RootNavigation.SetPageProviderService(pageProvider);
			dialogService.SetDialogHost(RootContentDialog);
			snackbarService.SetSnackbarPresenter(SnackbarPresenter);
			shell.NavigateHandler = page => RootNavigation.Navigate(page);
			shell.ProtoRequested += (_, _) => RootNavigation.Navigate(typeof(LibraryPage));
			shell.VanillaCompareRequested += (_, _) => RootNavigation.Navigate(typeof(ComparePage));
			shell.PairCompareRequested += (_, _) => RootNavigation.Navigate(typeof(ComparePage));
			shell.MessageRequested += (_, _) => RootNavigation.Navigate(typeof(MessagesPage));
			SearchBox.PreviewKeyDown += (_, e) => OnSearchKeyDown(e, shell);
			SearchBox.Committed += (_, text) => viewModel.AddSearchHistory(text);
			SearchBox.HistoryCleared += (_, _) => viewModel.ClearSearchHistory();
			SearchBox.SaveToggled += (_, text) => viewModel.ToggleSavedSearch(text);
			SearchBox.ValueProvider = viewModel.GetSearchValues;
			viewModel.FocusSearchRequested += (_, _) => SearchBox.FocusInput();

			Width = settings.Current.WindowWidth;
			Height = settings.Current.WindowHeight;
			if (settings.Current.WindowMaximized)
			{
				WindowState = WindowState.Maximized;
			}

			Loaded += (_, _) => RootNavigation.Navigate(typeof(LibraryPage));
			Closing += OnClosing;
			Closed += OnClosed;
			Drop += OnDrop;
			DragOver += (_, e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
		}

		/// <summary>Down or Enter moves into the results; Escape clears the search, then leaves the box.</summary>
		private void OnSearchKeyDown(System.Windows.Input.KeyEventArgs e, ShellNavigator shell)
		{
			if (e.Key is System.Windows.Input.Key.Down or System.Windows.Input.Key.Enter)
			{
				RootNavigation.Navigate(typeof(LibraryPage));
				shell.FocusList();
				e.Handled = true;
			}
			else if (e.Key == System.Windows.Input.Key.Escape)
			{
				if (_viewModel.SearchText.Length > 0)
				{
					_viewModel.SearchText = String.Empty;
				}
				else
				{
					shell.FocusList();
				}

				e.Handled = true;
			}
		}

		private const Int32 HistoryLength = 25;

		/// <summary>Lists the latest edits: choosing one goes back (or forward) to the state just after it.</summary>
		private void OnHistoryButtonClick(Object sender, RoutedEventArgs e)
		{
			if (_workspace.Current?.Undo is not { } undo)
			{
				return;
			}

			System.Windows.Controls.ContextMenu menu = CreateMenu(HistoryButton);
			List<ProtoForge.Core.Editing.IUndoableAction> done = [.. undo.UndoHistory];
			done.Reverse();
			List<ProtoForge.Core.Editing.IUndoableAction> undone = [.. undo.RedoHistory];

			// What can be redone comes first (furthest in the future on top), then the current state, then what was done (latest first).
			foreach ((ProtoForge.Core.Editing.IUndoableAction action, Int32 index) in undone.Take(HistoryLength).Select((a, i) => (a, i)).Reverse())
			{
				System.Windows.Controls.MenuItem item = CreateMenuItem(action.Description, false);
				item.Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorTertiaryBrush");
				item.Click += (_, _) => undo.RedoMany(index + 1);
				menu.Items.Add(item);
			}

			System.Windows.Controls.MenuItem current = CreateMenuItem(Loc.Get("history.current"), true);
			current.IsEnabled = false;
			menu.Items.Add(current);
			foreach ((ProtoForge.Core.Editing.IUndoableAction action, Int32 index) in done.Take(HistoryLength).Select((a, i) => (a, i)))
			{
				if (index == 0)
				{
					continue;
				}

				System.Windows.Controls.MenuItem item = CreateMenuItem(action.Description, false);
				item.Click += (_, _) => undo.UndoMany(index);
				menu.Items.Add(item);
			}

			if (done.Count > 0)
			{
				// The first entry of the list is the state that follows the latest action: its description is the one that is current.
				current.Header = $"{Loc.Get("history.current")}: {done[0].Description}";
				System.Windows.Controls.MenuItem start = CreateMenuItem(Loc.Get("history.start"), false);
				start.Click += (_, _) => undo.UndoMany(done.Count);
				menu.Items.Add(new System.Windows.Controls.Separator());
				menu.Items.Add(start);
			}

			menu.IsOpen = true;
		}

		private void OnOpenButtonClick(Object sender, RoutedEventArgs e)
		{
			if (OpenButton.ContextMenu is { } menu)
			{
				menu.PlacementTarget = OpenButton;
				menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
				menu.IsOpen = true;
			}
		}

		private void OnThemeButtonClick(Object sender, RoutedEventArgs e)
		{
			System.Windows.Controls.ContextMenu menu = CreateMenu(ThemeButton);
			foreach (String theme in new[] { "System", "Light", "Dark" })
			{
				System.Windows.Controls.MenuItem item = CreateMenuItem(Loc.Get("theme." + theme), theme == _settings.Current.Theme);
				item.Click += (_, _) =>
				{
					_settings.Current.Theme = theme;
					_settings.Save();
					ThemeHelper.Apply(theme);
				};
				menu.Items.Add(item);
			}

			menu.IsOpen = true;
		}

		private void OnLanguageButtonClick(Object sender, RoutedEventArgs e)
		{
			System.Windows.Controls.ContextMenu menu = CreateMenu(LanguageButton);
			foreach (KeyValuePair<String, String> language in Loc.AvailableLanguages)
			{
				System.Windows.Controls.MenuItem item = CreateMenuItem(language.Value, language.Key == Loc.LanguageCode);
				item.Click += (_, _) => ChangeLanguage(language.Key);
				menu.Items.Add(item);
			}

			menu.IsOpen = true;
		}

		private static System.Windows.Controls.ContextMenu CreateMenu(UIElement target)
		{
			// A menu inherits the font of its target: the glyph buttons use an icon font, which must not reach the texts of the menu.
			return new System.Windows.Controls.ContextMenu
			{
				PlacementTarget = target,
				Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
				FontFamily = Window.GetWindow(target)?.FontFamily ?? System.Windows.SystemFonts.MessageFontFamily,
				FontSize = 12,
			};
		}

		private static System.Windows.Controls.MenuItem CreateMenuItem(String header, Boolean isCurrent)
		{
			return new System.Windows.Controls.MenuItem
			{
				Header = header,
				Icon = isCurrent ? new SymbolIcon { Symbol = SymbolRegular.Checkmark24 } : null,
			};
		}

		/// <summary>The texts are resolved once at start-up, so a new language needs a restart: reopen the same project afterwards.</summary>
		private void ChangeLanguage(String code)
		{
			if (code == Loc.LanguageCode)
			{
				return;
			}

			_settings.Current.UiLanguage = code;
			_settings.Save();
			_relaunchWithProject = _workspace.HasWorkspace;
			Close();
		}

		private void OnClosed(Object? sender, EventArgs e)
		{
			if (_relaunchWithProject is not { } reopen)
			{
				return;
			}

			String? exe = Environment.ProcessPath;
			if (exe is not null)
			{
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, reopen ? "--last" : String.Empty) { UseShellExecute = false });
			}
		}

		private async void OnClosing(Object? sender, System.ComponentModel.CancelEventArgs e)
		{
			// A value being typed is committed when its box loses the focus: do it before looking at what is unsaved.
			System.Windows.Input.Keyboard.ClearFocus();
			SaveWindowState();
			if (_closeConfirmed || !_workspace.HasWorkspace)
			{
				return;
			}

			e.Cancel = true;
			if (await _workspace.ConfirmCloseAsync())
			{
				// Closing again from inside the Closing event is refused when the confirmation completed synchronously.
				_closeConfirmed = true;
				_ = Dispatcher.BeginInvoke(Close);
			}
			else
			{
				// The user kept the window open: a pending language restart is cancelled.
				_relaunchWithProject = null;
			}
		}

		private async void OnDrop(Object sender, DragEventArgs e)
		{
			if (!_workspace.IsBusy && e.Data.GetData(DataFormats.FileDrop) is String[] { Length: > 0 } paths)
			{
				await _viewModel.OpenPathAsync(paths[0]);
			}
		}

		private void SaveWindowState()
		{
			AppSettings s = _settings.Current;
			s.WindowMaximized = WindowState == WindowState.Maximized;
			if (WindowState == WindowState.Normal)
			{
				s.WindowWidth = Width;
				s.WindowHeight = Height;
			}

			_settings.Save();
		}
	}
}