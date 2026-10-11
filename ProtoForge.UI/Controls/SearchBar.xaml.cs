namespace ProtoForge.UI.Controls
{
	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.ComponentModel;
	using System.Linq;
	using System.Windows;
	using System.Windows.Controls;
	using System.Windows.Data;
	using System.Windows.Input;
	using ProtoForge.Core.Formats.Pro;
	using Services;

	/// <summary>One line of the suggestion popup: a prefix to type, a value for a prefix, or an earlier search.</summary>
	public sealed class SearchSuggestion
	{
		public SearchSuggestion(String section, String label, String description, String newText)
		{
			Section = section;
			Label = label;
			Description = description;
			NewText = newText;
		}

		public String Section { get; }

		public String Label { get; }

		public String Description { get; }

		/// <summary>The complete text of the box once the suggestion is accepted.</summary>
		public String NewText { get; }

		public Boolean IsFirst { get; set; }
	}

	/// <summary>
	/// The search box of the title bar: a compact field with a clear button and a popup that completes the search prefixes
	/// (<c>kind:</c>, <c>flag:</c>...), their values and the earlier searches.
	/// </summary>
	public partial class SearchBar : UserControl
	{
		public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
			nameof(Text),
			typeof(String),
			typeof(SearchBar),
			new FrameworkPropertyMetadata(String.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

		public static readonly DependencyProperty HistoryProperty = DependencyProperty.Register(
			nameof(History),
			typeof(IList<String>),
			typeof(SearchBar),
			new PropertyMetadata(null));

		private static readonly (String Key, String Resource)[] Prefixes =
		[
			("pid:", "search.key.pid"),
			("kind:", "search.key.kind"),
			("name:", "search.key.name"),
			("text:", "search.key.text"),
			("file:", "search.key.file"),
			("flag:", "search.key.flag"),
			("source:", "search.key.source"),
		];

		private const Int32 MaxValues = 10;
		private const Int32 MaxRecent = 6;

		private readonly ObservableCollection<SearchSuggestion> _suggestions = [];
		private (String Name, String Label)[]? _flags;

		public SearchBar()
		{
			InitializeComponent();
			ListCollectionView view = new(_suggestions);
			view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SearchSuggestion.Section)));
			SuggestionList.ItemsSource = view;
		}

		public String Text
		{
			get { return (String)GetValue(TextProperty); }
			set { SetValue(TextProperty, value); }
		}

		/// <summary>The earlier searches, newest first (the owner stores them).</summary>
		public IList<String>? History
		{
			get { return (IList<String>?)GetValue(HistoryProperty); }
			set { SetValue(HistoryProperty, value); }
		}

		/// <summary>Raised when the user validates a search (Enter or Down towards the results).</summary>
		public event EventHandler<String>? Committed;

		public event EventHandler? HistoryCleared;

		/// <summary>Puts the cursor in the box with the whole text selected.</summary>
		public void FocusInput()
		{
			Input.Focus();
			Input.SelectAll();
		}

		protected override void OnPreviewKeyDown(KeyEventArgs e)
		{
			base.OnPreviewKeyDown(e);
			switch (e.Key)
			{
				case Key.Tab when SuggestionsPopup.IsOpen && _suggestions.Count > 0 && Keyboard.Modifiers == ModifierKeys.None:
					Accept(_suggestions[0]);
					e.Handled = true;
					break;
				case Key.Escape when SuggestionsPopup.IsOpen:
					SuggestionsPopup.IsOpen = false;
					e.Handled = true;
					break;
				case Key.Enter or Key.Down:
					SuggestionsPopup.IsOpen = false;
					if (Text.Trim().Length > 0)
					{
						Committed?.Invoke(this, Text.Trim());
					}

					break;
				default:
					break;
			}
		}

		private void OnInputGotFocus(Object sender, KeyboardFocusChangedEventArgs e)
		{
			Refresh();
		}

		private void OnInputLostFocus(Object sender, KeyboardFocusChangedEventArgs e)
		{
			SuggestionsPopup.IsOpen = false;
		}

		private void OnInputTextChanged(Object sender, TextChangedEventArgs e)
		{
			if (Input.IsKeyboardFocusWithin)
			{
				Refresh();
			}
		}

		private void OnClearClick(Object sender, RoutedEventArgs e)
		{
			Text = String.Empty;
			Input.Focus();
		}

		private void OnSuggestionClick(Object sender, RoutedEventArgs e)
		{
			if (((FrameworkElement)sender).DataContext is SearchSuggestion suggestion)
			{
				Accept(suggestion);
			}
		}

		private void OnClearHistoryClick(Object sender, RoutedEventArgs e)
		{
			HistoryCleared?.Invoke(this, EventArgs.Empty);
			Refresh();
		}

		private void Accept(SearchSuggestion suggestion)
		{
			Text = suggestion.NewText;
			Input.Focus();
			Input.CaretIndex = Input.Text.Length;
		}

		/// <summary>Rebuilds the list for the word being typed and shows or hides the popup.</summary>
		private void Refresh()
		{
			List<SearchSuggestion> found = [];
			String text = Input.Text;
			Int32 start = text.LastIndexOf(' ') + 1;
			String head = text[..start];
			String token = text[start..];
			Int32 colon = token.IndexOf(':', StringComparison.Ordinal);
			String syntax = Loc.Get("search.syntax");

			if (colon > 0)
			{
				String key = token[..colon].ToLowerInvariant();
				String value = token[(colon + 1)..];
				foreach ((String name, String label) in GetValues(key).Where(n => n.Name.Contains(value, StringComparison.OrdinalIgnoreCase) && !String.Equals(n.Name, value, StringComparison.OrdinalIgnoreCase)).Take(MaxValues))
				{
					found.Add(new SearchSuggestion(Loc.Get("search.values"), name, label, head + key + ":" + name + " "));
				}
			}
			else
			{
				foreach ((String key, String resource) in Prefixes.Where(p => p.Key.StartsWith(token, StringComparison.OrdinalIgnoreCase) && p.Key.Length > token.Length))
				{
					found.Add(new SearchSuggestion(syntax, key, Loc.Get(resource), head + key));
				}
			}

			AddRecent(text, found);
			if (found.Count > 0)
			{
				found[0].IsFirst = true;
			}

			_suggestions.Clear();
			foreach (SearchSuggestion suggestion in found)
			{
				_suggestions.Add(suggestion);
			}

			String recent = Loc.Get("search.recent");
			ClearHistoryButton.Visibility = found.Any(s => s.Section == recent) ? Visibility.Visible : Visibility.Collapsed;
			SuggestionsPopup.IsOpen = _suggestions.Count > 0 && Input.IsKeyboardFocusWithin;
		}

		private void AddRecent(String text, List<SearchSuggestion> found)
		{
			if (History is not { Count: > 0 } history)
			{
				return;
			}

			String recent = Loc.Get("search.recent");
			foreach (String entry in history
				.Where(h => !String.Equals(h, text, StringComparison.OrdinalIgnoreCase) && (text.Length == 0 || h.Contains(text, StringComparison.OrdinalIgnoreCase)))
				.Take(MaxRecent))
			{
				found.Add(new SearchSuggestion(recent, entry, String.Empty, entry));
			}
		}

		private IEnumerable<(String Name, String Label)> GetValues(String key)
		{
			switch (key)
			{
				case "kind" or "type":
					return Enum.GetNames<ProtoKind>().Select(n => (n, Loc.Get("kind." + n)));
				case "flag":
					_flags ??= [.. FlagSets.All.SelectMany(set => set.Bits).Select(bit => bit.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Select(n => (n, Loc.Flag(n)))];
					return _flags;
				default:
					return [];
			}
		}
	}
}