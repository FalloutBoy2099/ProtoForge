namespace ProtoForge.UI.Views
{
	using System.Windows.Controls;
	using ViewModels;

	public partial class SettingsPage : Page
	{
		public SettingsPage(SettingsViewModel viewModel)
		{
			DataContext = viewModel;
			InitializeComponent();
		}
	}
}