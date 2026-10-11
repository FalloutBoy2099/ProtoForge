namespace ProtoForge.UI.Controls
{
	using System;
	using System.Windows;
	using System.Windows.Controls;
	using System.Windows.Controls.Primitives;

	/// <summary>
	/// The track of a scroll bar whose thumb never gets shorter than <see cref="MinThumbLength"/>. The standard track lets the thumb shrink
	/// to a few pixels when a list has thousands of rows, which makes it nearly impossible to grab.
	/// </summary>
	public sealed class MinThumbTrack : Track
	{
		public static readonly DependencyProperty MinThumbLengthProperty = DependencyProperty.Register(
			nameof(MinThumbLength),
			typeof(Double),
			typeof(MinThumbTrack),
			new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsArrange));

		/// <summary>Value units covered by one pixel of thumb travel (needed to turn a drag distance into a value).</summary>
		private Double _density;
		private Rect _thumbRect;

		public Double MinThumbLength
		{
			get { return (Double)GetValue(MinThumbLengthProperty); }
			set { SetValue(MinThumbLengthProperty, value); }
		}

		protected override Size ArrangeOverride(Size arrangeSize)
		{
			Double viewport = ViewportSize;
			if (Double.IsNaN(viewport))
			{
				return base.ArrangeOverride(arrangeSize);
			}

			Boolean vertical = Orientation == Orientation.Vertical;
			Double trackLength = vertical ? arrangeSize.Height : arrangeSize.Width;
			Double crossLength = vertical ? arrangeSize.Width : arrangeSize.Height;
			Double range = Math.Max(0.0, Maximum - Minimum);
			Double offset = Math.Min(range, Math.Max(0.0, Value - Minimum));
			Double extent = range + viewport;
			Double thumbLength = extent > 0 ? trackLength * viewport / extent : trackLength;
			thumbLength = Math.Min(trackLength, Math.Max(MinThumbLength, thumbLength));
			Double free = Math.Max(0.0, trackLength - thumbLength);
			Double travelled = range > 0 ? free * offset / range : 0.0;
			_density = free > 0 ? range / free : 0.0;

			// The end of the track where the minimum is: the top of a reversed vertical bar, the left of a horizontal one that is not reversed.
			Boolean minimumAtStart = vertical ? IsDirectionReversed : !IsDirectionReversed;
			Double thumbStart = minimumAtStart ? travelled : trackLength - travelled - thumbLength;
			Double decreaseStart = minimumAtStart ? 0.0 : thumbStart + thumbLength;
			Double decreaseLength = minimumAtStart ? thumbStart : trackLength - decreaseStart;
			Double increaseStart = minimumAtStart ? thumbStart + thumbLength : 0.0;
			Double increaseLength = minimumAtStart ? trackLength - increaseStart : thumbStart;

			Arrange(DecreaseRepeatButton, vertical, decreaseStart, decreaseLength, crossLength);
			Arrange(IncreaseRepeatButton, vertical, increaseStart, increaseLength, crossLength);
			_thumbRect = vertical
				? new Rect(0, thumbStart, crossLength, thumbLength)
				: new Rect(thumbStart, 0, thumbLength, crossLength);
			Thumb?.Arrange(_thumbRect);
			return arrangeSize;
		}

		public override Double ValueFromDistance(Double horizontal, Double vertical)
		{
			Double scale = IsDirectionReversed ? -1 : 1;
			return Orientation == Orientation.Horizontal ? scale * horizontal * _density : -1 * scale * vertical * _density;
		}

		public override Double ValueFromPoint(Point pt)
		{
			Point center = new(_thumbRect.X + _thumbRect.Width / 2, _thumbRect.Y + _thumbRect.Height / 2);
			Double value = Value + ValueFromDistance(pt.X - center.X, pt.Y - center.Y);
			return Math.Min(Maximum, Math.Max(Minimum, value));
		}

		private static void Arrange(RepeatButton? button, Boolean vertical, Double start, Double length, Double crossLength)
		{
			button?.Arrange(vertical ? new Rect(0, start, crossLength, Math.Max(0.0, length)) : new Rect(start, 0, Math.Max(0.0, length), crossLength));
		}
	}
}