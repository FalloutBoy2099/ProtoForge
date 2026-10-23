namespace ProtoForge.UI.Views
{
	using System;
	using System.Windows.Controls;
	using System.Windows.Input;
	using ViewModels;

	public partial class MapsPage : Page
	{
		private readonly MapsViewModel _viewModel;

		public MapsPage(MapsViewModel viewModel)
		{
			_viewModel = viewModel;
			DataContext = viewModel;
			InitializeComponent();

			// The maps are read the first time the page is shown; the unused prototypes follow the edits made elsewhere.
			IsVisibleChanged += async (_, e) =>
			{
				if (e.NewValue is true)
				{
					await viewModel.LoadAsync();
				}
			};
		}

		private void OnObjectDoubleClick(Object sender, MouseButtonEventArgs e)
		{
			if (sender is DataGrid { SelectedItem: MapObjectRow row })
			{
				_viewModel.Show(row.Entry);
			}
		}

		private void OnUnusedDoubleClick(Object sender, MouseButtonEventArgs e)
		{
			if (sender is DataGrid { SelectedItem: UnusedRow row })
			{
				_viewModel.Show(row.Entry);
			}
		}
	}
}