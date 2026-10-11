namespace ProtoForge.Core.Formats.Frm
{
	using System;
	using System.IO;

	/// <summary>A decoded Windows bitmap: top-down BGRA pixels, plus the palette indices when the file was 8-bit.</summary>
	public sealed class BmpImage
	{
		public BmpImage(Int32 width, Int32 height, Byte[] bgra, Byte[]? indices, Byte[]? paletteRgb)
		{
			Width = width;
			Height = height;
			Bgra = bgra;
			Indices = indices;
			PaletteRgb = paletteRgb;
		}

		public Int32 Width { get; }

		public Int32 Height { get; }

		/// <summary>Top-down, 4 bytes per pixel (blue, green, red, alpha).</summary>
		public Byte[] Bgra { get; }

		/// <summary>Palette indices, top-down, for 8-bit bitmaps (null otherwise).</summary>
		public Byte[]? Indices { get; }

		/// <summary>The palette of an 8-bit bitmap as 768 bytes (R, G, B), or null.</summary>
		public Byte[]? PaletteRgb { get; }
	}

	/// <summary>Reads and writes the uncompressed Windows bitmaps (BMP) used to exchange FRM images with other tools.</summary>
	public static class BmpFile
	{
		private const Int32 FileHeaderSize = 14;
		private const Int32 InfoHeaderSize = 40;

		public static BmpImage Read(ReadOnlySpan<Byte> data)
		{
			if (data.Length < FileHeaderSize + InfoHeaderSize || data[0] != (Byte)'B' || data[1] != (Byte)'M')
			{
				throw new InvalidDataException("The file is not a Windows bitmap.");
			}

			Int32 pixelOffset = ReadInt32(data, 10);
			Int32 headerSize = ReadInt32(data, 14);
			if (headerSize < InfoHeaderSize)
			{
				throw new InvalidDataException("Only bitmaps with a BITMAPINFOHEADER (or newer) are supported.");
			}

			Int32 width = ReadInt32(data, 18);
			Int32 rawHeight = ReadInt32(data, 22);
			Int32 bitCount = ReadInt16(data, 28);
			Int32 compression = ReadInt32(data, 30);
			Int32 colorsUsed = ReadInt32(data, 46);
			Boolean bottomUp = rawHeight > 0;
			Int32 height = Math.Abs(rawHeight);

			if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
			{
				throw new InvalidDataException("The bitmap size is not valid.");
			}

			if (compression is not (0 or 3))
			{
				throw new InvalidDataException("Compressed bitmaps are not supported.");
			}

			if (bitCount is not (8 or 24 or 32))
			{
				throw new InvalidDataException("Only 8, 24 and 32 bits per pixel bitmaps are supported.");
			}

			Byte[]? paletteRgb = null;
			Byte[]? paletteBgr = null;
			if (bitCount == 8)
			{
				Int32 count = colorsUsed == 0 ? 256 : Math.Min(256, colorsUsed);
				Int32 paletteOffset = FileHeaderSize + headerSize;
				if (paletteOffset + (count * 4) > data.Length)
				{
					throw new InvalidDataException("The bitmap palette is truncated.");
				}

				paletteRgb = new Byte[768];
				paletteBgr = new Byte[768];
				for (Int32 i = 0; i < count; i++)
				{
					Byte b = data[paletteOffset + (i * 4)];
					Byte g = data[paletteOffset + (i * 4) + 1];
					Byte r = data[paletteOffset + (i * 4) + 2];
					paletteRgb[i * 3] = r;
					paletteRgb[(i * 3) + 1] = g;
					paletteRgb[(i * 3) + 2] = b;
					paletteBgr[i * 3] = b;
					paletteBgr[(i * 3) + 1] = g;
					paletteBgr[(i * 3) + 2] = r;
				}
			}

			Int32 stride = ((width * bitCount) + 31) / 32 * 4;
			if (pixelOffset < 0 || pixelOffset + ((Int64)stride * height) > data.Length)
			{
				throw new InvalidDataException("The bitmap pixel data is truncated.");
			}

			Byte[] bgra = new Byte[width * height * 4];
			Byte[]? indices = bitCount == 8 ? new Byte[width * height] : null;
			for (Int32 y = 0; y < height; y++)
			{
				Int32 sourceRow = pixelOffset + ((bottomUp ? height - 1 - y : y) * stride);
				for (Int32 x = 0; x < width; x++)
				{
					Int32 target = ((y * width) + x) * 4;
					switch (bitCount)
					{
						case 8:
							Byte index = data[sourceRow + x];
							indices![(y * width) + x] = index;
							bgra[target] = paletteBgr![index * 3];
							bgra[target + 1] = paletteBgr[(index * 3) + 1];
							bgra[target + 2] = paletteBgr[(index * 3) + 2];
							bgra[target + 3] = 255;
							break;
						case 24:
							bgra[target] = data[sourceRow + (x * 3)];
							bgra[target + 1] = data[sourceRow + (x * 3) + 1];
							bgra[target + 2] = data[sourceRow + (x * 3) + 2];
							bgra[target + 3] = 255;
							break;
						default:
							bgra[target] = data[sourceRow + (x * 4)];
							bgra[target + 1] = data[sourceRow + (x * 4) + 1];
							bgra[target + 2] = data[sourceRow + (x * 4) + 2];
							bgra[target + 3] = data[sourceRow + (x * 4) + 3];
							break;
					}
				}
			}

			return new BmpImage(width, height, bgra, indices, paletteRgb);
		}

		/// <summary>An 8-bit bitmap with the given palette (768 bytes, R, G, B). Index 0 is the transparent colour of Fallout images.</summary>
		public static Byte[] Write8Bit(Int32 width, Int32 height, ReadOnlySpan<Byte> indices, ReadOnlySpan<Byte> paletteRgb)
		{
			Int32 stride = (width + 3) / 4 * 4;
			Int32 pixelOffset = FileHeaderSize + InfoHeaderSize + 1024;
			Byte[] file = new Byte[pixelOffset + (stride * height)];
			WriteHeaders(file, width, height, 8, pixelOffset, stride * height, 256);
			for (Int32 i = 0; i < 256; i++)
			{
				Int32 target = FileHeaderSize + InfoHeaderSize + (i * 4);
				file[target] = paletteRgb[(i * 3) + 2];
				file[target + 1] = paletteRgb[(i * 3) + 1];
				file[target + 2] = paletteRgb[i * 3];
			}

			for (Int32 y = 0; y < height; y++)
			{
				Int32 row = pixelOffset + ((height - 1 - y) * stride);
				indices.Slice(y * width, width).CopyTo(file.AsSpan(row, width));
			}

			return file;
		}

		/// <summary>A 32-bit bitmap (blue, green, red, alpha); transparent pixels keep their alpha of 0.</summary>
		public static Byte[] Write32Bit(Int32 width, Int32 height, ReadOnlySpan<Byte> bgra)
		{
			Int32 stride = width * 4;
			Int32 pixelOffset = FileHeaderSize + InfoHeaderSize;
			Byte[] file = new Byte[pixelOffset + (stride * height)];
			WriteHeaders(file, width, height, 32, pixelOffset, stride * height, 0);
			for (Int32 y = 0; y < height; y++)
			{
				bgra.Slice(y * stride, stride).CopyTo(file.AsSpan(pixelOffset + ((height - 1 - y) * stride), stride));
			}

			return file;
		}

		private static void WriteHeaders(Byte[] file, Int32 width, Int32 height, Int32 bitCount, Int32 pixelOffset, Int32 imageSize, Int32 colors)
		{
			file[0] = (Byte)'B';
			file[1] = (Byte)'M';
			WriteInt32(file, 2, file.Length);
			WriteInt32(file, 10, pixelOffset);
			WriteInt32(file, 14, InfoHeaderSize);
			WriteInt32(file, 18, width);
			WriteInt32(file, 22, height);
			file[26] = 1;
			file[28] = (Byte)bitCount;
			WriteInt32(file, 34, imageSize);
			WriteInt32(file, 38, 2835);
			WriteInt32(file, 42, 2835);
			WriteInt32(file, 46, colors);
			WriteInt32(file, 50, colors);
		}

		private static Int32 ReadInt32(ReadOnlySpan<Byte> data, Int32 offset)
		{
			return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
		}

		private static Int32 ReadInt16(ReadOnlySpan<Byte> data, Int32 offset)
		{
			return System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));
		}

		private static void WriteInt32(Byte[] data, Int32 offset, Int32 value)
		{
			System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);
		}
	}
}