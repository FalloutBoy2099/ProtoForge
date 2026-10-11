namespace ProtoForge.UI.Services
{
	using System;
	using System.Collections.Concurrent;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;
	using System.Windows.Media;
	using System.Windows.Media.Imaging;
	using ProtoForge.Core.Formats.Frm;
	using ProtoForge.Infrastructure.Workspace;

	/// <summary>All the frames of one direction of a FRM, each on the same canvas, plus the speed of the animation.</summary>
	public sealed record ArtAnimation(IReadOnlyList<BitmapSource> Frames, Int32 FramesPerSecond, Int32 DirectionCount);

	/// <summary>
	/// Decodes FRM art into frozen WPF bitmaps (item sprites, inventory icons, critter stand animations, head portraits).
	/// Results are cached by file path and invalidated when a new workspace is opened.
	/// </summary>
	public sealed class ArtImageService
	{
		private readonly ConcurrentDictionary<String, BitmapSource?> _cache = new(StringComparer.Ordinal);
		private readonly ConcurrentDictionary<String, ArtAnimation?> _animations = new(StringComparer.Ordinal);
		private GameWorkspace? _workspace;

		public void Attach(GameWorkspace? workspace)
		{
			if (_workspace is not null)
			{
				_workspace.ArtChanged -= OnArtChanged;
			}

			_workspace = workspace;
			_cache.Clear();
			_animations.Clear();
			if (workspace is not null)
			{
				workspace.ArtChanged += OnArtChanged;
			}
		}

		private void OnArtChanged(Object? sender, EventArgs e)
		{
			_cache.Clear();
			_animations.Clear();
		}

		/// <summary>All the frames of a direction (the default direction of a critter is south-west), for animated previews.</summary>
		public Task<ArtAnimation?> GetAnimationAsync(FrameId fid, Int32 direction = -1)
		{
			GameWorkspace? workspace = _workspace;
			String? path = workspace is null || fid.IsNone ? null : workspace.Art.ResolvePath(fid);
			if (workspace is null || path is null)
			{
				return Task.FromResult<ArtAnimation?>(null);
			}

			String key = $"{path}|{direction}";
			if (_animations.TryGetValue(key, out ArtAnimation? cached))
			{
				return Task.FromResult(cached);
			}

			return Task.Run(() =>
			{
				ArtAnimation? animation = DecodeAnimation(workspace, path, direction);
				_animations[key] = animation;
				return animation;
			});
		}

		private static ArtAnimation? DecodeAnimation(GameWorkspace workspace, String path, Int32 direction)
		{
			try
			{
				Byte[]? bytes = workspace.Files.ReadAllBytes(path);
				if (bytes is null)
				{
					return null;
				}

				FrmFile frm = FrmFile.Parse(bytes);
				if (frm.Directions.Count == 0 || frm.Directions[0].Count == 0)
				{
					return null;
				}

				Int32 index = direction >= 0 && direction < frm.Directions.Count ? direction : frm.Directions.Count > 3 ? 3 : 0;
				IReadOnlyList<FrmFrame> frames = frm.Directions[index];
				Int32 width = frames.Max(f => f.Width);
				Int32 height = frames.Max(f => f.Height);
				if (width == 0 || height == 0)
				{
					return null;
				}

				List<BitmapSource> bitmaps = [];
				foreach (FrmFrame frame in frames)
				{
					// Every frame is drawn on the same canvas (bottom centre) so that the animation does not jump around.
					Byte[] canvas = new Byte[width * height * 4];
					Byte[] pixels = workspace.Palette.ToBgra(frame);
					Int32 left = (width - frame.Width) / 2;
					Int32 top = height - frame.Height;
					for (Int32 y = 0; y < frame.Height; y++)
					{
						Buffer.BlockCopy(pixels, y * frame.Width * 4, canvas, (((top + y) * width) + left) * 4, frame.Width * 4);
					}

					BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, canvas, width * 4);
					bitmap.Freeze();
					bitmaps.Add(bitmap);
				}

				return new ArtAnimation(bitmaps, frm.FramesPerSecond > 0 ? frm.FramesPerSecond : 10, frm.Directions.Count);
			}
			catch (Exception ex) when (ex is System.IO.InvalidDataException or ArgumentException or System.IO.IOException)
			{
				return null;
			}
		}

		/// <summary>Returns the image for a frame ID, decoding it on a worker thread when it is not cached.</summary>
		public Task<BitmapSource?> GetAsync(FrameId fid, Int32 direction = -1)
		{
			GameWorkspace? workspace = _workspace;
			if (workspace is null || fid.IsNone)
			{
				return Task.FromResult<BitmapSource?>(null);
			}

			String? path = workspace.Art.ResolvePath(fid);
			if (path is null)
			{
				return Task.FromResult<BitmapSource?>(null);
			}

			String key = $"{path}|{direction}";
			if (_cache.TryGetValue(key, out BitmapSource? cached))
			{
				return Task.FromResult(cached);
			}

			return Task.Run(() =>
			{
				BitmapSource? bitmap = Decode(workspace, path, direction);
				_cache[key] = bitmap;
				return bitmap;
			});
		}

		public BitmapSource? GetCached(FrameId fid, Int32 direction = -1)
		{
			String? path = _workspace?.Art.ResolvePath(fid);
			return path is not null && _cache.TryGetValue($"{path}|{direction}", out BitmapSource? bitmap) ? bitmap : null;
		}

		private static BitmapSource? Decode(GameWorkspace workspace, String path, Int32 direction)
		{
			try
			{
				Byte[]? bytes = workspace.Files.ReadAllBytes(path);
				if (bytes is null)
				{
					return null;
				}

				FrmFile frm = FrmFile.Parse(bytes);
				if (frm.Directions.Count == 0 || frm.Directions[0].Count == 0)
				{
					return null;
				}

				// Critters face south-west by default (direction 3), like the original previews.
				Int32 index = direction >= 0 && direction < frm.Directions.Count ? direction : frm.Directions.Count > 3 ? 3 : 0;
				FrmFrame frame = frm.Directions[index][0];
				if (frame.Width == 0 || frame.Height == 0)
				{
					return null;
				}

				Byte[] pixels = workspace.Palette.ToBgra(frame);
				BitmapSource bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, pixels, frame.Width * 4);
				bitmap.Freeze();
				return bitmap;
			}
			catch (Exception ex) when (ex is System.IO.InvalidDataException or ArgumentException or System.IO.IOException)
			{
				return null;
			}
		}
	}
}