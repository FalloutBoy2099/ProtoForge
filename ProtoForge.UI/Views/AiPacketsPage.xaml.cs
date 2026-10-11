namespace ProtoForge.UI.Views
{
	using System;
	using System.Windows.Controls;
	using System.Windows.Input;
	using ViewModels;

	public partial class AiPacketsPage : Page
	{
		private readonly AiPacketsViewModel _viewModel;

		public AiPacketsPage(AiPacketsViewModel viewModel)
		{
			_viewModel = viewModel;
			DataContext = viewModel;
			InitializeComponent();
		}

		private void OnUserDoubleClick(Object sender, MouseButtonEventArgs e)
		{
			_viewModel.OpenUserCommand.Execute(_viewModel.SelectedUser);
		}
	}
}