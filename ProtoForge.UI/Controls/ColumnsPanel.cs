namespace ProtoForge.UI.Controls
{
	using System;
	using System.Windows;
	using System.Windows.Controls;

	/// <summary>
	/// Lays its children out in equal columns, row after row. The number of columns follows the width that is available (as many as fit
	/// with <see cref="MinItemWidth"/>, between <see cref="MinColumns"/> and <see cref="MaxColumns"/>) and the columns share all of it.
	/// </summary>
	public sealed class ColumnsPanel : Panel
	{
		public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
			nameof(MinItemWidth),
			typeof(Double),
			typeof(ColumnsPanel),
			new FrameworkPropertyMetadata(170.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

		public static readonly DependencyProperty MinColumnsProperty = DependencyProperty.Register(
			nameof(MinColumns),
			typeof(Int32),
			typeof(ColumnsPanel),
			new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));

		public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
			nameof(MaxColumns),
			typeof(Int32),
			typeof(ColumnsPanel),
			new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

		public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
			nameof(ItemHeight),
			typeof(Double),
			typeof(ColumnsPanel),
			new FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

		public Double MinItemWidth
		{
			get { return (Double)GetValue(MinItemWidthProperty); }
			set { SetValue(MinItemWidthProperty, value); }
		}

		public Int32 MinColumns
		{
			get { return (Int32)GetValue(MinColumnsProperty); }
			set { SetValue(MinColumnsProperty, value); }
		}

		public Int32 MaxColumns
		{
			get { return (Int32)GetValue(MaxColumnsProperty); }
			set { SetValue(MaxColumnsProperty, value); }
		}

		public Double ItemHeight
		{
			get { return (Double)GetValue(ItemHeightProperty); }
			set { SetValue(ItemHeightProperty, value); }
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			Double width = Double.IsInfinity(availableSize.Width) ? MinItemWidth * Math.Max(1, MinColumns) : availableSize.Width;
			Int32 columns = GetColumns(width);
			Double cell = width / columns;
			foreach (UIElement child in InternalChildren)
			{
				child.Measure(new Size(cell, ItemHeight));
			}

			Int32 rows = (Int32)Math.Ceiling(InternalChildren.Count / (Double)columns);
			return new Size(width, rows * ItemHeight);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			Int32 columns = GetColumns(finalSize.Width);
			Double cell = finalSize.Width / columns;
			for (Int32 i = 0; i < InternalChildren.Count; i++)
			{
				InternalChildren[i].Arrange(new Rect((i % columns) * cell, (i / columns) * ItemHeight, cell, ItemHeight));
			}

			return finalSize;
		}

		private Int32 GetColumns(Double width)
		{
			Int32 fitting = MinItemWidth > 0 ? (Int32)Math.Floor(width / MinItemWidth) : MaxColumns;
			return Math.Max(1, Math.Min(Math.Max(MinColumns, MaxColumns), Math.Max(MinColumns, fitting)));
		}
	}
}