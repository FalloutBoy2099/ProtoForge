namespace ProtoForge.UI.Controls
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Windows;
	using System.Windows.Controls;
	using System.Windows.Media;
	using System.Windows.Threading;

	/// <summary>An image that plays a list of frames in a loop (the stand animation of a critter); a single frame is shown as it is.</summary>
	public sealed class AnimatedImage : Image
	{
		public static readonly DependencyProperty FramesProperty = DependencyProperty.Register(
			nameof(Frames), typeof(IReadOnlyList<ImageSource>), typeof(AnimatedImage), new PropertyMetadata(null, OnFramesChanged));

		public static readonly DependencyProperty FramesPerSecondProperty = DependencyProperty.Register(
			nameof(FramesPerSecond), typeof(Int32), typeof(AnimatedImage), new PropertyMetadata(10, OnFramesChanged));

		private readonly DispatcherTimer _timer;
		private Int32 _index;

		public AnimatedImage()
		{
			_timer = new DispatcherTimer(DispatcherPriority.Render);
			_timer.Tick += OnTick;
			Loaded += (_, _) => Restart();
			Unloaded += (_, _) => _timer.Stop();
		}

		public IReadOnlyList<ImageSource>? Frames
		{
			get { return (IReadOnlyList<ImageSource>?)GetValue(FramesProperty); }
			set { SetValue(FramesProperty, value); }
		}

		public Int32 FramesPerSecond
		{
			get { return (Int32)GetValue(FramesPerSecondProperty); }
			set { SetValue(FramesPerSecondProperty, value); }
		}

		private static void OnFramesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			((AnimatedImage)d).Restart();
		}

		private void Restart()
		{
			_timer.Stop();
			_index = 0;
			IReadOnlyList<ImageSource>? frames = Frames;
			Source = frames?.FirstOrDefault();
			if (frames is { Count: > 1 } && IsLoaded)
			{
				_timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(1000.0 / Math.Max(1, FramesPerSecond), 40, 1000));
				_timer.Start();
			}
		}

		private void OnTick(Object? sender, EventArgs e)
		{
			IReadOnlyList<ImageSource>? frames = Frames;
			if (frames is not { Count: > 1 })
			{
				_timer.Stop();
				return;
			}

			_index = (_index + 1) % frames.Count;
			Source = frames[_index];
		}
	}
}