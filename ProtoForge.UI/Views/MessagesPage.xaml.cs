namespace ProtoForge.UI.Views
{
	using System.Windows.Controls;
	using ViewModels;

	public partial class MessagesPage : Page
	{
		public MessagesPage(MessagesViewModel viewModel)
		{
			DataContext = viewModel;
			InitializeComponent();
		}
	}
}