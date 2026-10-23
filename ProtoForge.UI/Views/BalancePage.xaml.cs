namespace ProtoForge.UI.Views
{
	using System;
	using System.Windows;
	using System.Windows.Controls;
	using System.Windows.Input;
	using ViewModels;

	public partial class BalancePage : Page
	{
		private readonly BalanceViewModel _viewModel;

		public BalancePage(BalanceViewModel viewModel)
		{
			_viewModel = viewModel;
			DataContext = viewModel;
			InitializeComponent();

			// What was edited in the other pages shows up when the page is opened again.
			// The columns of the table of weapons against armors are named after the armors that were picked.
			viewModel.PropertyChanged += (_, e) =>
			{
				if (e.PropertyName == nameof(BalanceViewModel.MatrixHeaders))
				{
					ApplyMatrixHeaders();
				}
			};
			IsVisibleChanged += (_, e) =>
			{
				if (e.NewValue is true)
				{
					viewModel.Refresh();
				}
			};
		}

		private void ApplyMatrixHeaders()
		{
			DataGridColumn[] columns = [MatrixColumn0, MatrixColumn1, MatrixColumn2, MatrixColumn3, MatrixColumn4, MatrixColumn5];
			for (Int32 i = 0; i < columns.Length; i++)
			{
				String header = i < _viewModel.MatrixHeaders.Count ? _viewModel.MatrixHeaders[i] : String.Empty;
				columns[i].Header = header;
				columns[i].Visibility = header.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
			}
		}

		private void OnMatrixDoubleClick(Object sender, MouseButtonEventArgs e)
		{
			if (sender is DataGrid { SelectedItem: MatrixRow row })
			{
				_viewModel.Show(row.Entry);
			}
		}

		private void OnRefreshClick(Object sender, RoutedEventArgs e)
		{
			_viewModel.Refresh();
		}

		private void OnRowDoubleClick(Object sender, MouseButtonEventArgs e)
		{
			if (sender is DataGrid { SelectedItem: BalanceRow row })
			{
				_viewModel.Show(row);
			}
		}
	}
}