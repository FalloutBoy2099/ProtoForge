namespace ProtoForge.UI.Views
{
	using System.Windows.Controls;
	using ViewModels;

	public partial class AboutPage : Page
	{
		public AboutPage(AboutViewModel viewModel)
		{
			DataContext = viewModel;
			InitializeComponent();
		}
	}
}