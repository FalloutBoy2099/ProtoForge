namespace ProtoForge.UI.Controls
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Windows;
	using System.Windows.Controls;
	using System.Windows.Input;
	using ProtoForge.Core.Domain;

	/// <summary>A search-as-you-type box that picks one prototype (name, PID or file name).</summary>
	public partial class EntryPicker : UserControl
	{
		public static readonly DependencyProperty EntriesProperty = DependencyProperty.Register(
			nameof(Entries), typeof(IReadOnlyList<ProtoEntry>), typeof(EntryPicker), new PropertyMetadata(null));

		public static readonly DependencyProperty SelectedEntryProperty = DependencyProperty.Register(
			nameof(SelectedEntry), typeof(ProtoEntry), typeof(EntryPicker),
			new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedEntryChanged));

		private Boolean _internalChange;

		public EntryPicker()
		{
			InitializeComponent();
		}

		public IReadOnlyList<ProtoEntry>? Entries
		{
			get { return (IReadOnlyList<ProtoEntry>?)GetValue(EntriesProperty); }
			set { SetValue(EntriesProperty, value); }
		}

		public ProtoEntry? SelectedEntry
		{
			get { return (ProtoEntry?)GetValue(SelectedEntryProperty); }
			set { SetValue(SelectedEntryProperty, value); }
		}

		private static void OnSelectedEntryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			EntryPicker picker = (EntryPicker)d;
			picker._internalChange = true;
			picker.Box.Text = (e.NewValue as ProtoEntry)?.DisplayName ?? String.Empty;
			picker._internalChange = false;
		}

		private void OnGotFocus(Object sender, KeyboardFocusChangedEventArgs e)
		{
			Box.SelectAll();
		}

		private void OnTextChanged(Object sender, TextChangedEventArgs e)
		{
			if (_internalChange || Entries is null || !Box.IsKeyboardFocusWithin)
			{
				return;
			}

			String text = Box.Text.Trim();
			if (text.Length == 0)
			{
				SuggestionsPopup.IsOpen = false;
				return;
			}

			ProtoFilter filter = new(text);
			List<ProtoEntry> matches = [.. Entries.Where(filter.Matches).Take(40)];
			Suggestions.ItemsSource = matches;
			SuggestionsPopup.IsOpen = matches.Count > 0;
			if (matches.Count > 0)
			{
				Suggestions.SelectedIndex = 0;
			}
		}

		private void OnKeyDown(Object sender, KeyEventArgs e)
		{
			if (!SuggestionsPopup.IsOpen)
			{
				return;
			}

			switch (e.Key)
			{
				case Key.Down:
					Suggestions.SelectedIndex = Math.Min(Suggestions.Items.Count - 1, Suggestions.SelectedIndex + 1);
					Suggestions.ScrollIntoView(Suggestions.SelectedItem);
					e.Handled = true;
					break;
				case Key.Up:
					Suggestions.SelectedIndex = Math.Max(0, Suggestions.SelectedIndex - 1);
					Suggestions.ScrollIntoView(Suggestions.SelectedItem);
					e.Handled = true;
					break;
				case Key.Enter:
					Commit(Suggestions.SelectedItem as ProtoEntry);
					e.Handled = true;
					break;
				case Key.Escape:
					SuggestionsPopup.IsOpen = false;
					e.Handled = true;
					break;
				default:
					break;
			}
		}

		private void OnSuggestionClicked(Object sender, MouseButtonEventArgs e)
		{
			if (e.OriginalSource is DependencyObject source)
			{
				ListBoxItem? item = ItemsControl.ContainerFromElement(Suggestions, source) as ListBoxItem;
				Commit(item?.DataContext as ProtoEntry);
			}
		}

		private void Commit(ProtoEntry? entry)
		{
			if (entry is null)
			{
				return;
			}

			SuggestionsPopup.IsOpen = false;
			SelectedEntry = entry;
		}
	}
}