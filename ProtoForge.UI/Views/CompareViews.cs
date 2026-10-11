namespace ProtoForge.UI.Views
{
	using System.Collections.Generic;
	using System.Windows.Controls;
	using Wpf.Ui.Controls;
	using ProtoForge.Core.Domain;
	using Services;
	using ViewModels;

	/// <summary>The side-by-side comparison, hosted both in a navigation page and in separate windows.</summary>
	public partial class CompareView : UserControl
	{
		public CompareView()
		{
			InitializeComponent();
		}
	}

	public sealed class ComparePage : Page
	{
		public ComparePage(CompareViewModel viewModel, ShellNavigator shell)
		{
			ViewModel = viewModel;
			Title = "Compare";
			ScrollViewer.SetCanContentScroll(this, false);
			DataContext = viewModel;
			Content = new Grid
			{
				Margin = new System.Windows.Thickness(12, 0, 12, 8),
				Children = { new CompareView() },
			};
			shell.VanillaCompareRequested += (_, entry) => viewModel.SetVanillaVsCurrent(entry);
			shell.PairCompareRequested += (_, pair) => viewModel.SetPair(pair.A, pair.B);

			// Opening the page with nothing to compare yet: start from what is selected in the library.
			Loaded += (_, _) =>
			{
				IReadOnlyList<ProtoEntry> selection = shell.SelectionProvider?.Invoke() ?? [];
				if (viewModel.Left.Entry is null && viewModel.Right.Entry is null && selection.Count > 0)
				{
					if (selection.Count == 2)
					{
						viewModel.SetPair(selection[0], selection[1]);
					}
					else
					{
						viewModel.SetVanillaVsCurrent(selection[0]);
					}
				}
			};
		}

		public CompareViewModel ViewModel { get; }
	}

	/// <summary>A free-standing compare window, so two comparisons (or a comparison and the editor) can be arranged side by side.</summary>
	public sealed class CompareWindow : FluentWindow
	{
		public CompareWindow(CompareViewModel viewModel)
		{
			ViewModel = viewModel;
			Title = Loc.Get("compare.windowTitle");
			Width = 1100;
			Height = 760;
			WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
			ExtendsContentIntoTitleBar = true;
			WindowBackdropType = WindowBackdropType.Mica;
			DataContext = viewModel;

			Grid root = new();
			root.RowDefinitions.Add(new RowDefinition { Height = System.Windows.GridLength.Auto });
			root.RowDefinitions.Add(new RowDefinition());
			root.Children.Add(new TitleBar { Title = Title });
			Grid host = new() { Margin = new System.Windows.Thickness(16, 0, 16, 16) };
			host.Children.Add(new CompareView());
			Grid.SetRow(host, 1);
			root.Children.Add(host);
			Content = root;
			Owner = System.Windows.Application.Current.MainWindow;
		}

		public CompareViewModel ViewModel { get; }
	}
}