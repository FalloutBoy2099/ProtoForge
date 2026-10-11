namespace ProtoForge.UI.Views
{
	using System;
	using System.Windows.Controls;
	using System.Windows.Input;
	using ViewModels;

	public partial class ValidationPage : Page
	{
		private readonly ValidationViewModel _viewModel;

		public ValidationPage(ValidationViewModel viewModel)
		{
			_viewModel = viewModel;
			DataContext = viewModel;
			InitializeComponent();
		}

		private void OnRowDoubleClick(Object sender, MouseButtonEventArgs e)
		{
			if (_viewModel.SelectedRow is not null)
			{
				_viewModel.OpenIssueCommand.Execute(_viewModel.SelectedRow);
			}
		}
	}
}