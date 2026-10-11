namespace ProtoForge.Core.Formats.Frm
{
	using System;

	/// <summary>Conversions between FRM frames and bitmaps (the old bmp2frm tool and the "save as BMP" menu of f2wedit).</summary>
	public static class FrmConverter
	{
		/// <summary>The colour that stands for "transparent" in 24-bit bitmaps (they have no alpha channel).</summary>
		public static readonly (Byte R, Byte G, Byte B) TransparentKey = (255, 0, 255);

		/// <summary>
		/// Turns a bitmap into a frame of the game palette. An 8-bit bitmap that already uses the game palette keeps its indices;
		/// otherwise every pixel gets the nearest game colour. Pixels with an alpha below 128, or of the magenta key colour, become transparent (index 0).
		/// </summary>
		public static FrmFrame ToFrame(BmpImage image, Palette palette)
		{
			Byte[] pixels = new Byte[image.Width * image.Height];
			Boolean usesGamePalette = image.Indices is not null && image.PaletteRgb is not null && PaletteMatches(image.PaletteRgb, palette);
			Byte[] cache = new Byte[1 << 15];
			Boolean[] cached = new Boolean[1 << 15];
			for (Int32 i = 0; i < pixels.Length; i++)
			{
				if (usesGamePalette)
				{
					pixels[i] = image.Indices![i];
					continue;
				}

				Byte b = image.Bgra[i * 4];
				Byte g = image.Bgra[(i * 4) + 1];
				Byte r = image.Bgra[(i * 4) + 2];
				Byte a = image.Bgra[(i * 4) + 3];
				if (a < 128 || (r == TransparentKey.R && g == TransparentKey.G && b == TransparentKey.B))
				{
					pixels[i] = 0;
					continue;
				}

				// A 5-bit per channel cache makes large images fast (the palette only has 6 bits per channel anyway).
				Int32 key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
				if (!cached[key])
				{
					cache[key] = (Byte)palette.FindNearest(r, g, b);
					cached[key] = true;
				}

				pixels[i] = cache[key];
			}

			return new FrmFrame(image.Width, image.Height, 0, 0, pixels);
		}

		/// <summary>An 8-bit bitmap with the game palette; index 0 (transparent) is drawn with the palette colour 0.</summary>
		public static Byte[] ToBmp8(FrmFrame frame, Palette palette)
		{
			return BmpFile.Write8Bit(frame.Width, frame.Height, frame.Pixels, palette.ToRgb8());
		}

		/// <summary>A 32-bit bitmap where transparent pixels have an alpha of 0.</summary>
		public static Byte[] ToBmp32(FrmFrame frame, Palette palette)
		{
			return BmpFile.Write32Bit(frame.Width, frame.Height, palette.ToBgra(frame));
		}

		private static Boolean PaletteMatches(Byte[] bmpPalette, Palette palette)
		{
			Byte[] game = palette.ToRgb8();
			// Entry 0 is the transparent colour: bitmaps written by other tools may draw it in any colour.
			for (Int32 i = 3; i < 768; i++)
			{
				if (bmpPalette[i] != game[i])
				{
					return false;
				}
			}

			return true;
		}
	}
}