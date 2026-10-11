namespace ProtoForge.UI.Views
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Windows;
	using System.Windows.Controls;
	using CommunityToolkit.Mvvm.ComponentModel;
	using Services;

	/// <summary>One column of the CSV export that can be switched on or off.</summary>
	public sealed partial class CsvColumnViewModel : ObservableObject
	{
		[ObservableProperty]
		private Boolean _isSelected = true;

		public CsvColumnViewModel(String key)
		{
			Key = key;
		}

		public String Key { get; }

		public String Label
		{
			get { return Loc.Field(Key); }
		}
	}

	/// <summary>The choices of the CSV export dialog: which columns to write and whether to write readable values.</summary>
	public sealed partial class CsvOptionsViewModel : ObservableObject
	{
		[ObservableProperty]
		private Boolean _readable;

		public CsvOptionsViewModel(IEnumerable<String> keys, String summary)
		{
			Columns = [.. keys.Select(k => new CsvColumnViewModel(k))];
			Summary = summary;
		}

		public String Summary { get; }

		public IReadOnlyList<CsvColumnViewModel> Columns { get; }

		public ISet<String>? GetSelectedColumns()
		{
			return Columns.All(c => c.IsSelected) ? null : new HashSet<String>(Columns.Where(c => c.IsSelected).Select(c => c.Key), StringComparer.Ordinal);
		}
	}

	public partial class CsvOptionsView : UserControl
	{
		private readonly CsvOptionsViewModel _viewModel;

		public CsvOptionsView(CsvOptionsViewModel viewModel)
		{
			_viewModel = viewModel;
			DataContext = viewModel;
			InitializeComponent();
		}

		private void OnSelectAll(Object sender, RoutedEventArgs e)
		{
			foreach (CsvColumnViewModel column in _viewModel.Columns)
			{
				column.IsSelected = true;
			}
		}

		private void OnSelectNone(Object sender, RoutedEventArgs e)
		{
			foreach (CsvColumnViewModel column in _viewModel.Columns)
			{
				column.IsSelected = false;
			}
		}
	}
}