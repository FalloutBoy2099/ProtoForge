namespace ProtoForge.UI.Views
{
	using System;
	using System.ComponentModel;
	using System.Windows;
	using System.Windows.Controls;
	using Services;
	using ViewModels;

	public partial class MessagesPage : Page
	{
		private readonly MessagesViewModel _viewModel;

		public MessagesPage(MessagesViewModel viewModel)
		{
			_viewModel = viewModel;
			DataContext = viewModel;
			InitializeComponent();
			viewModel.PropertyChanged += OnViewModelPropertyChanged;
			ApplyPairing();
		}

		private void OnViewModelPropertyChanged(Object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(MessagesViewModel.IsPaired))
			{
				ApplyPairing();
			}
		}

		/// <summary>The prototype texts show a name and a description per row; the other files a single text.</summary>
		private void ApplyPairing()
		{
			DescriptionColumn.Visibility = _viewModel.IsPaired ? Visibility.Visible : Visibility.Collapsed;
			TextColumn.Header = _viewModel.IsPaired ? Loc.Get("field.name") : Loc.Get("messages.text");
		}
	}
}