namespace ProtoForge.UI.Views
{
	using System;
	using System.Windows;
	using System.Windows.Controls;
	using System.Windows.Input;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Templates;
	using Controls;
	using Services;
	using ViewModels;

	public partial class LibraryPage : Page
	{
		private readonly LibraryViewModel _viewModel;

		public LibraryPage(LibraryViewModel viewModel)
		{
			_viewModel = viewModel;
			DataContext = viewModel;
			InitializeComponent();
			viewModel.ShowProtoRequested += OnShowProtoRequested;
			viewModel.FocusListRequested += (_, _) => FocusList();
		}

		/// <summary>Gives the focus to the list, selecting the first row when nothing is selected yet.</summary>
		private void FocusList()
		{
			Dispatcher.BeginInvoke(() =>
			{
				if (Grid.SelectedItem is null && Grid.Items.Count > 0)
				{
					Grid.SelectedIndex = 0;
				}

				if (Grid.SelectedItem is not null)
				{
					Grid.ScrollIntoView(Grid.SelectedItem);
					if (Grid.ItemContainerGenerator.ContainerFromItem(Grid.SelectedItem) is DataGridRow row)
					{
						row.Focus();
						return;
					}
				}

				Grid.Focus();
			}, System.Windows.Threading.DispatcherPriority.Loaded);
		}

		/// <summary>
		/// Page-wide keys: F3 / Shift+F3 step through the list, F6 (or Escape from the editor) switches between the list and the
		/// editor, Ctrl+Shift+R reverts the selection.
		/// </summary>
		private void OnPageKeyDown(Object sender, KeyEventArgs e)
		{
			Boolean shift = Keyboard.Modifiers == ModifierKeys.Shift;
			Boolean inEditor = Keyboard.FocusedElement is DependencyObject focused && FindParent<TabControl>(focused) == EditorTabs;

			if (e.Key == Key.F3 && (Keyboard.Modifiers == ModifierKeys.None || shift))
			{
				MoveSelection(shift ? -1 : 1);
				e.Handled = true;
			}
			else if (e.Key == Key.F6)
			{
				if (inEditor)
				{
					FocusList();
				}
				else
				{
					EditorTabs.Focus();
					EditorTabs.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
				}

				e.Handled = true;
			}
			else if (e.Key == Key.Escape && inEditor && Keyboard.FocusedElement is not (System.Windows.Controls.Primitives.TextBoxBase or ComboBox))
			{
				FocusList();
				e.Handled = true;
			}
			else if (e.Key == Key.R && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && _viewModel.Editor is { } editor)
			{
				editor.RevertCommand.Execute(null);
				e.Handled = true;
			}
		}

		private void MoveSelection(Int32 delta)
		{
			Int32 count = Grid.Items.Count;
			if (count == 0)
			{
				return;
			}

			Int32 current = Grid.SelectedItem is null ? (delta > 0 ? -1 : count) : Grid.Items.IndexOf(Grid.SelectedItem);
			Int32 next = ((current + delta) % count + count) % count;
			Grid.SelectedItems.Clear();
			Grid.SelectedIndex = next;
			FocusList();
		}

		/// <summary>Enter adds a line break to a description, up to the two lines the game can show (new sfall versions understand them).</summary>
		private void OnDescriptionKeyDown(Object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter && sender is System.Windows.Controls.TextBox box)
			{
				String remaining = box.Text.Remove(box.SelectionStart, box.SelectionLength);
				if (remaining.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Length >= ProtoTextService.MaxDescriptionLines)
				{
					e.Handled = true;
				}
			}
		}

		/// <summary>Double click on a usage: shows that prototype in the list.</summary>
		private void OnUsageDoubleClick(Object sender, MouseButtonEventArgs e)
		{
			if (sender is System.Windows.Controls.DataGrid { SelectedItem: UsageRow row, DataContext: UsageViewModel usage })
			{
				usage.Show(row);
			}
		}

		private void OnGridSelectionChanged(Object sender, SelectionChangedEventArgs e)
		{
			_viewModel.OnSelectionChanged(Grid.SelectedItems);
		}

		private void OnGridKeyDown(Object sender, KeyEventArgs e)
		{
			if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
			{
				_viewModel.DuplicateCommand.Execute(null);
				e.Handled = true;
			}
			else if (e.Key == Key.Delete)
			{
				_viewModel.DeleteCommand.Execute(null);
				e.Handled = true;
			}
			else if (e.Key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
			{
				_viewModel.CopyPidCommand.Execute(null);
				e.Handled = true;
			}
			else if (e.Key == Key.Enter)
			{
				// Jump into the editor so the whole workflow works from the keyboard.
				EditorTabs.Focus();
				EditorTabs.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
				e.Handled = true;
			}
			else if (e.Key == Key.Escape && !String.IsNullOrEmpty(_viewModel.SearchText))
			{
				_viewModel.ClearFiltersCommand.Execute(null);
				e.Handled = true;
			}
		}

		private void OnGridDoubleClick(Object sender, MouseButtonEventArgs e)
		{
			if (Grid.SelectedItem is not null && e.OriginalSource is DependencyObject source && FindParent<DataGridRow>(source) is not null)
			{
				EditorTabs.Focus();
			}
		}

		private void OnNewClick(Object sender, RoutedEventArgs e)
		{
			ContextMenu menu = new() { PlacementTarget = NewButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
			foreach (TemplateGroup group in _viewModel.TemplateGroups)
			{
				MenuItem groupItem = new() { Header = Loc.Has("category." + group.Category) ? Loc.Get("category." + group.Category) : group.Category };
				foreach (ProtoTemplate template in group.Templates)
				{
					MenuItem item = new()
					{
						Header = Loc.Has("template." + template.Id) ? Loc.Get("template." + template.Id) : template.DisplayName,
						Tag = template,
					};
					item.Click += (_, _) => _viewModel.NewFromTemplateCommand.Execute(template);
					groupItem.Items.Add(item);
				}

				menu.Items.Add(groupItem);
			}

			if (menu.Items.Count == 0)
			{
				return;
			}

			menu.IsOpen = true;
		}

		private void OnShowProtoRequested(Object? sender, ProtoId pid)
		{
			ProtoEntry? entry = _viewModel.Workspace.Current?.Catalog.Find(pid);
			if (entry is null)
			{
				return;
			}

			if (_viewModel.View is { } view && !view.Contains(entry))
			{
				_viewModel.ClearFiltersCommand.Execute(null);
			}

			Dispatcher.BeginInvoke(() =>
			{
				Grid.SelectedItems.Clear();
				Grid.SelectedItem = entry;
				Grid.ScrollIntoView(entry);
				Grid.Focus();
			}, System.Windows.Threading.DispatcherPriority.Loaded);
		}

		private void OnHexByteEdited(Object? sender, HexByteEditedEventArgs e)
		{
			_viewModel.Editor?.Hex.EditByte(e.Offset, e.Value);
		}

		private static T? FindParent<T>(DependencyObject child)
			where T : DependencyObject
		{
			DependencyObject? current = child;
			while (current is not null)
			{
				if (current is T match)
				{
					return match;
				}

				current = System.Windows.Media.VisualTreeHelper.GetParent(current);
			}

			return null;
		}
	}
}