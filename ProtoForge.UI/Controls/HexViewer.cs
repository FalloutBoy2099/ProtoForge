namespace ProtoForge.UI.Controls
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Linq;
	using System.Windows;
	using System.Windows.Input;
	using System.Windows.Media;
	using ViewModels;

	/// <summary>
	/// A compact hex dump (offset, 16 bytes per row, ASCII) with the known fields tinted, the bytes changed since loading outlined
	/// and keyboard editing: type two hexadecimal digits to overwrite the selected byte.
	/// </summary>
	public sealed class HexViewer : FrameworkElement
	{
		public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
			nameof(Data), typeof(Byte[]), typeof(HexViewer), new FrameworkPropertyMetadata(Array.Empty<Byte>(), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

		public static readonly DependencyProperty RegionsProperty = DependencyProperty.Register(
			nameof(Regions), typeof(IReadOnlyList<HexRegion>), typeof(HexViewer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

		public static readonly DependencyProperty ChangedOffsetsProperty = DependencyProperty.Register(
			nameof(ChangedOffsets), typeof(IReadOnlyList<Int32>), typeof(HexViewer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

		public static readonly DependencyProperty SelectedOffsetProperty = DependencyProperty.Register(
			nameof(SelectedOffset), typeof(Int32), typeof(HexViewer),
			new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

		private const Int32 BytesPerRow = 16;
		private const Double RowHeight = 22;
		private const Double OffsetWidth = 64;
		private const Double CellWidth = 27;
		private const Double GroupGap = 10;
		private const Double AsciiGap = 24;
		private const Double AsciiCellWidth = 9;
		private const Double FontSize = 13.5;

		private static readonly Typeface Face = new("Consolas");

		private static readonly Color[] RegionColors =
		[
			Color.FromRgb(0x4C, 0x9B, 0xE8),
			Color.FromRgb(0x6C, 0xCB, 0x5F),
			Color.FromRgb(0xE8, 0x8F, 0x4C),
			Color.FromRgb(0xB1, 0x6C, 0xE8),
			Color.FromRgb(0xE8, 0xC8, 0x4C),
			Color.FromRgb(0x4C, 0xD9, 0xD0),
			Color.FromRgb(0xE8, 0x6C, 0x9B),
			Color.FromRgb(0x9B, 0xA7, 0xB8),
		];

		private Int32 _pendingNibble = -1;

		public HexViewer()
		{
			Focusable = true;
			FocusVisualStyle = null;
			ClipToBounds = true;
		}

		public Byte[] Data
		{
			get { return (Byte[])GetValue(DataProperty); }
			set { SetValue(DataProperty, value); }
		}

		public IReadOnlyList<HexRegion>? Regions
		{
			get { return (IReadOnlyList<HexRegion>?)GetValue(RegionsProperty); }
			set { SetValue(RegionsProperty, value); }
		}

		public IReadOnlyList<Int32>? ChangedOffsets
		{
			get { return (IReadOnlyList<Int32>?)GetValue(ChangedOffsetsProperty); }
			set { SetValue(ChangedOffsetsProperty, value); }
		}

		public Int32 SelectedOffset
		{
			get { return (Int32)GetValue(SelectedOffsetProperty); }
			set { SetValue(SelectedOffsetProperty, value); }
		}

		/// <summary>Raised when the user types a new value for a byte.</summary>
		public event EventHandler<HexByteEditedEventArgs>? ByteEdited;

		protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
		{
			return new HexViewerAutomationPeer(this);
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			Int32 rows = Math.Max(1, (Data.Length + BytesPerRow - 1) / BytesPerRow);
			Double width = OffsetWidth + (BytesPerRow * CellWidth) + GroupGap + AsciiGap + (BytesPerRow * AsciiCellWidth) + 8;
			return new Size(width, RowHeight * (rows + 1) + 6);
		}

		protected override void OnRender(DrawingContext dc)
		{
			Byte[] data = Data;
			Double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
			Brush text = FindBrush("TextFillColorPrimaryBrush", Brushes.White);
			Brush muted = FindBrush("TextFillColorTertiaryBrush", Brushes.Gray);
			Brush accent = FindBrush("SystemAccentColorPrimaryBrush", Brushes.DodgerBlue);
			Brush selection = FindBrush("SystemAccentColorSecondaryBrush", accent);
			dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

			// Column header
			for (Int32 column = 0; column < BytesPerRow; column++)
			{
				DrawText(dc, column.ToString("X2", CultureInfo.InvariantCulture), CellX(column), 0, muted, dpi, bold: false);
			}

			HashSet<Int32> changed = ChangedOffsets is null ? [] : [.. ChangedOffsets];
			IReadOnlyList<HexRegion> regions = Regions ?? [];
			Brush[] tints = [.. RegionColors.Select(c => Frozen(new SolidColorBrush(Color.FromArgb(0x55, c.R, c.G, c.B))))];

			for (Int32 i = 0; i < data.Length; i++)
			{
				Int32 row = i / BytesPerRow;
				Int32 column = i % BytesPerRow;
				Double y = (row + 1) * RowHeight;
				if (column == 0)
				{
					DrawText(dc, (row * BytesPerRow).ToString("X4", CultureInfo.InvariantCulture), 4, y, muted, dpi, bold: false);
				}

				Rect cell = new(CellX(column) - 2, y + 1, CellWidth - 1, RowHeight - 2);
				HexRegion? region = regions.FirstOrDefault(r => i >= r.Offset && i < r.Offset + r.Length);
				if (region is not null)
				{
					dc.DrawRoundedRectangle(tints[Math.Max(0, region.ColorIndex) % tints.Length], null, cell, 3, 3);
				}

				if (changed.Contains(i))
				{
					dc.DrawRoundedRectangle(null, new Pen(FindBrush("SystemFillColorCautionBrush", Brushes.Orange), 1.5), cell, 3, 3);
				}

				if (i == SelectedOffset)
				{
					dc.DrawRoundedRectangle(selection, new Pen(accent, 1.5), cell, 3, 3);
				}

				String hex = _pendingNibble >= 0 && i == SelectedOffset
					? _pendingNibble.ToString("X", CultureInfo.InvariantCulture) + "_"
					: data[i].ToString("X2", CultureInfo.InvariantCulture);
				DrawText(dc, hex, CellX(column), y + 1, text, dpi, bold: changed.Contains(i));

				Char c = data[i] is >= 32 and < 127 ? (Char)data[i] : '·';
				Double ax = AsciiX(column);
				if (i == SelectedOffset)
				{
					dc.DrawRectangle(selection, null, new Rect(ax - 1, y + 1, AsciiCellWidth, RowHeight - 2));
				}

				DrawText(dc, c.ToString(), ax, y + 1, c == '·' ? muted : text, dpi, bold: false);
			}
		}

		protected override void OnMouseDown(MouseButtonEventArgs e)
		{
			base.OnMouseDown(e);
			Keyboard.Focus(this);
			e.Handled = true;
			Point p = e.GetPosition(this);
			Int32 row = (Int32)(p.Y / RowHeight) - 1;
			Int32 column = -1;
			if (p.X >= CellX(0) - 2 && p.X < CellX(BytesPerRow))
			{
				column = (Int32)Math.Floor(((p.X - CellX(0) + 2) - (p.X >= CellX(8) - 2 ? GroupGap : 0)) / CellWidth);
			}
			else if (p.X >= AsciiX(0) && p.X < AsciiX(BytesPerRow))
			{
				column = (Int32)((p.X - AsciiX(0)) / AsciiCellWidth);
			}

			Int32 offset = row * BytesPerRow + column;
			if (row >= 0 && column is >= 0 and < BytesPerRow && offset < Data.Length)
			{
				_pendingNibble = -1;
				SelectedOffset = offset;
			}
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			Int32 offset = SelectedOffset;
			Int32 length = Data.Length;
			if (length == 0)
			{
				return;
			}

			Int32 target = e.Key switch
			{
				Key.Left => offset - 1,
				Key.Right => offset + 1,
				Key.Up => offset - BytesPerRow,
				Key.Down => offset + BytesPerRow,
				Key.Home => 0,
				Key.End => length - 1,
				_ => Int32.MinValue,
			};

			if (target != Int32.MinValue)
			{
				_pendingNibble = -1;
				SelectedOffset = Math.Clamp(target, 0, length - 1);
				e.Handled = true;
				return;
			}

			if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
			{
				// Ctrl+F, Ctrl+E, Ctrl+D... are commands of the window, not hexadecimal digits.
				return;
			}

			Int32 nibble = ToNibble(e.Key);
			if (nibble < 0 || offset < 0 || offset >= length)
			{
				if (e.Key == Key.Escape)
				{
					_pendingNibble = -1;
					InvalidateVisual();
				}

				return;
			}

			e.Handled = true;
			if (_pendingNibble < 0)
			{
				_pendingNibble = nibble;
				InvalidateVisual();
				return;
			}

			Byte value = (Byte)((_pendingNibble << 4) | nibble);
			_pendingNibble = -1;
			ByteEdited?.Invoke(this, new HexByteEditedEventArgs(offset, value));
			if (offset + 1 < length)
			{
				SelectedOffset = offset + 1;
			}

			InvalidateVisual();
		}

		private static Int32 ToNibble(Key key)
		{
			if (key is >= Key.D0 and <= Key.D9)
			{
				return key - Key.D0;
			}

			if (key is >= Key.NumPad0 and <= Key.NumPad9)
			{
				return key - Key.NumPad0;
			}

			return key is >= Key.A and <= Key.F ? 10 + (key - Key.A) : -1;
		}

		private static Double CellX(Int32 column)
		{
			return OffsetWidth + (column * CellWidth) + (column >= 8 ? GroupGap : 0);
		}

		private static Double AsciiX(Int32 column)
		{
			return OffsetWidth + (BytesPerRow * CellWidth) + GroupGap + AsciiGap + (column * AsciiCellWidth);
		}

		private static void DrawText(DrawingContext dc, String text, Double x, Double y, Brush brush, Double dpi, Boolean bold)
		{
			FormattedText formatted = new(
				text,
				CultureInfo.InvariantCulture,
				FlowDirection.LeftToRight,
				bold ? new Typeface(Face.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal) : Face,
				FontSize,
				brush,
				dpi);
			dc.DrawText(formatted, new Point(x, y + 1));
		}

		private static Brush FindBrush(String key, Brush fallback)
		{
			return Application.Current?.TryFindResource(key) as Brush ?? fallback;
		}

		private static Brush Frozen(Brush brush)
		{
			brush.Freeze();
			return brush;
		}
	}

	/// <summary>Exposes the hex dump to assistive technologies and UI automation.</summary>
	internal sealed class HexViewerAutomationPeer : System.Windows.Automation.Peers.FrameworkElementAutomationPeer
	{
		public HexViewerAutomationPeer(HexViewer owner)
			: base(owner)
		{
		}

		protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore()
		{
			return System.Windows.Automation.Peers.AutomationControlType.Custom;
		}

		protected override String GetClassNameCore()
		{
			return nameof(HexViewer);
		}

		protected override String GetNameCore()
		{
			return ProtoForge.UI.Services.Loc.Get("tab.hex");
		}
	}

	public sealed class HexByteEditedEventArgs : EventArgs
	{
		public HexByteEditedEventArgs(Int32 offset, Byte value)
		{
			Offset = offset;
			Value = value;
		}

		public Int32 Offset { get; }

		public Byte Value { get; }
	}
}