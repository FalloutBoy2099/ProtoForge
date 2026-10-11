namespace ProtoForge.UI.Views
{
	using System.Windows.Controls;
	using ViewModels;

	public partial class ToolsPage : Page
	{
		public ToolsPage(ToolsViewModel viewModel)
		{
			DataContext = viewModel;
			InitializeComponent();
		}
	}
}