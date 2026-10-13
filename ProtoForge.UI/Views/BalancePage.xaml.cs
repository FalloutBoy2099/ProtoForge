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
			IsVisibleChanged += (_, e) =>
			{
				if (e.NewValue is true)
				{
					viewModel.Refresh();
				}
			};
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