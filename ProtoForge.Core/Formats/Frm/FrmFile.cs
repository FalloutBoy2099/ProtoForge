namespace ProtoForge.Core.Formats.Frm
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using IO;

	/// <summary>One bitmap of a FRM animation, as palette indices (0 = transparent).</summary>
	public sealed class FrmFrame
	{
		public FrmFrame(Int32 width, Int32 height, Int32 offsetX, Int32 offsetY, Byte[] pixels)
		{
			Width = width;
			Height = height;
			OffsetX = offsetX;
			OffsetY = offsetY;
			Pixels = pixels;
		}

		public Int32 Width { get; }

		public Int32 Height { get; }

		public Int32 OffsetX { get; }

		public Int32 OffsetY { get; }

		public Byte[] Pixels { get; }
	}

	/// <summary>A Fallout FRM image/animation (up to 6 directions of N frames, 8-bit paletted).</summary>
	public sealed class FrmFile
	{
		private const Int32 HeaderSize = 0x3E;

		public FrmFile(Int32 framesPerSecond, Int32 actionFrame, IReadOnlyList<IReadOnlyList<FrmFrame>> directions)
		{
			FramesPerSecond = framesPerSecond;
			ActionFrame = actionFrame;
			Directions = directions;
		}

		public Int32 FramesPerSecond { get; }

		public Int32 ActionFrame { get; }

		/// <summary>Frames of each direction. Static images have a single direction with a single frame.</summary>
		public IReadOnlyList<IReadOnlyList<FrmFrame>> Directions { get; }

		public Int32 FrameCount
		{
			get { return Directions.Count == 0 ? 0 : Directions[0].Count; }
		}

		public static FrmFile Parse(ReadOnlySpan<Byte> data)
		{
			if (data.Length < HeaderSize)
			{
				throw new InvalidDataException("The FRM file is too short.");
			}

			Int32 fps = BigEndian.ReadInt16(data, 0x04);
			Int32 action = BigEndian.ReadInt16(data, 0x06);
			Int32 framesPerDirection = BigEndian.ReadInt16(data, 0x08);
			Int32[] offsets = new Int32[6];
			for (Int32 i = 0; i < 6; i++)
			{
				offsets[i] = BigEndian.ReadInt32(data, 0x22 + (i * 4));
			}

			Int32 directionCount = 1;
			for (Int32 i = 1; i < 6; i++)
			{
				if (offsets[i] != 0)
				{
					directionCount = 6;
					break;
				}
			}

			List<IReadOnlyList<FrmFrame>> directions = new(directionCount);
			for (Int32 d = 0; d < directionCount; d++)
			{
				Int32 position = HeaderSize + offsets[d];
				List<FrmFrame> frames = new(framesPerDirection);
				for (Int32 f = 0; f < framesPerDirection; f++)
				{
					if (position + 12 > data.Length)
					{
						throw new InvalidDataException("Truncated FRM frame header.");
					}

					Int32 width = BigEndian.ReadInt16(data, position);
					Int32 height = BigEndian.ReadInt16(data, position + 2);
					Int32 offsetX = BigEndian.ReadInt16(data, position + 8);
					Int32 offsetY = BigEndian.ReadInt16(data, position + 10);
					position += 12;
					Int32 size = width * height;
					if (width < 0 || height < 0 || position + size > data.Length)
					{
						throw new InvalidDataException("Truncated FRM frame data.");
					}

					frames.Add(new FrmFrame(width, height, offsetX, offsetY, data.Slice(position, size).ToArray()));
					position += size;
				}

				directions.Add(frames);
			}

			return new FrmFile(fps, action, directions);
		}
	}

	/// <summary>The 256 colour Fallout palette (<c>color.pal</c>), converted to 8-bit RGB.</summary>
	public sealed class Palette
	{
		private static Palette? _default;
		private readonly Byte[] _rgb;

		public Palette(ReadOnlySpan<Byte> palFile)
		{
			if (palFile.Length < 768)
			{
				throw new InvalidDataException("A palette needs at least 768 bytes.");
			}

			_rgb = new Byte[768];
			for (Int32 i = 0; i < 768; i++)
			{
				// Fallout stores 6-bit components.
				Int32 value = Math.Min(63, (Int32)palFile[i]);
				_rgb[i] = (Byte)((value << 2) | (value >> 4));
			}
		}

		/// <summary>The palette of the original game (embedded), for work that does not need an opened game.</summary>
		public static Palette Default
		{
			get
			{
				if (_default is null)
				{
					System.Reflection.Assembly assembly = typeof(Palette).Assembly;
					using Stream stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().First(n => n.EndsWith("color.pal", StringComparison.OrdinalIgnoreCase)))!;
					Byte[] bytes = new Byte[768];
					stream.ReadExactly(bytes);
					_default = new Palette(bytes);
				}

				return _default;
			}
		}

		/// <summary>The colour of a palette entry as 8-bit red, green, blue.</summary>
		public (Byte R, Byte G, Byte B) GetColor(Int32 index)
		{
			return (_rgb[index * 3], _rgb[(index * 3) + 1], _rgb[(index * 3) + 2]);
		}

		/// <summary>The 256 entries as 768 bytes (R, G, B), 8-bit.</summary>
		public Byte[] ToRgb8()
		{
			return (Byte[])_rgb.Clone();
		}

		/// <summary>The palette entry that looks the most like a colour. Entry 0 is the transparent colour and is never chosen.</summary>
		public Int32 FindNearest(Int32 red, Int32 green, Int32 blue)
		{
			Int32 best = 1;
			Int32 bestDistance = Int32.MaxValue;
			for (Int32 i = 1; i < 256; i++)
			{
				Int32 dr = _rgb[i * 3] - red;
				Int32 dg = _rgb[(i * 3) + 1] - green;
				Int32 db = _rgb[(i * 3) + 2] - blue;
				// Weighted like the human eye (green counts the most).
				Int32 distance = (dr * dr * 30) + (dg * dg * 59) + (db * db * 11);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = i;
					if (distance == 0)
					{
						break;
					}
				}
			}

			return best;
		}

		/// <summary>Converts a frame into BGRA32 pixels (index 0 becomes fully transparent).</summary>
		public Byte[] ToBgra(FrmFrame frame)
		{
			Byte[] bgra = new Byte[frame.Width * frame.Height * 4];
			for (Int32 i = 0; i < frame.Pixels.Length; i++)
			{
				Int32 index = frame.Pixels[i];
				if (index == 0)
				{
					continue;
				}

				Int32 target = i * 4;
				bgra[target] = _rgb[(index * 3) + 2];
				bgra[target + 1] = _rgb[(index * 3) + 1];
				bgra[target + 2] = _rgb[index * 3];
				bgra[target + 3] = 255;
			}

			return bgra;
		}
	}
}