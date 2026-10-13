namespace ProtoForge.UI.Controls
{
	using System;
	using System.Linq;
	using System.Windows;
	using ViewModels.Fields;

	/// <summary>
	/// Lets an image file (FRM or BMP) be dropped on an art field: the file is added to the game and assigned
	/// (<see cref="ArtFieldViewModel.Import"/>). Set <c>controls:ArtDrop.IsEnabled="True"</c> on the template of the field.
	/// </summary>
	public static class ArtDrop
	{
		public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
			"IsEnabled",
			typeof(Boolean),
			typeof(ArtDrop),
			new PropertyMetadata(false, OnIsEnabledChanged));

		public static Boolean GetIsEnabled(DependencyObject element)
		{
			return (Boolean)element.GetValue(IsEnabledProperty);
		}

		public static void SetIsEnabled(DependencyObject element, Boolean value)
		{
			element.SetValue(IsEnabledProperty, value);
		}

		private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is not UIElement element)
			{
				return;
			}

			element.DragOver -= OnDragOver;
			element.Drop -= OnDrop;
			if (element is FrameworkElement framework)
			{
				framework.Loaded -= OnLoaded;
			}

			element.AllowDrop = e.NewValue is true;
			if (e.NewValue is true)
			{
				element.DragOver += OnDragOver;
				element.Drop += OnDrop;
				if (element is FrameworkElement loading)
				{
					loading.Loaded += OnLoaded;
				}
			}
		}

		/// <summary>
		/// A drop is only raised on an element that allows it, and the elements inside the field (image, list, label) are the ones under the
		/// pointer: they all allow it, and the events bubble up to the field.
		/// </summary>
		private static void OnLoaded(Object sender, RoutedEventArgs e)
		{
			AllowDropEverywhere((DependencyObject)sender);
		}

		private static void AllowDropEverywhere(DependencyObject parent)
		{
			for (Int32 i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
			{
				DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
				if (child is UIElement element)
				{
					element.AllowDrop = true;
				}

				AllowDropEverywhere(child);
			}
		}

		private static String? GetImagePath(DragEventArgs e)
		{
			return e.Data.GetData(DataFormats.FileDrop) is String[] files
				? files.FirstOrDefault(f => f.EndsWith(".frm", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
				: null;
		}

		private static void OnDragOver(Object sender, DragEventArgs e)
		{
			Boolean ok = ((FrameworkElement)sender).DataContext is ArtFieldViewModel { CanImport: true } && GetImagePath(e) is not null;
			e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		}

		private static void OnDrop(Object sender, DragEventArgs e)
		{
			if (((FrameworkElement)sender).DataContext is ArtFieldViewModel field && GetImagePath(e) is { } path)
			{
				field.Import(path);
			}

			e.Handled = true;
		}
	}
}