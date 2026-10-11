namespace ProtoForge.Core.Tests
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using ProtoForge.Core.Formats.Frm;
	using Xunit;

	public class FrmToolsTests
	{
		private static Palette CreatePalette()
		{
			// A gradient palette: every entry has distinct 6-bit components.
			Byte[] file = new Byte[768];
			for (Int32 i = 0; i < 256; i++)
			{
				file[i * 3] = (Byte)(i / 4);
				file[(i * 3) + 1] = (Byte)((255 - i) / 4);
				file[(i * 3) + 2] = (Byte)((i * 7 % 256) / 4);
			}

			return new Palette(file);
		}

		[Fact]
		public void Frm_WriteThenParse_IsIdentical()
		{
			FrmFrame frame = new(3, 2, -1, 4, [0, 5, 6, 7, 0, 9]);

			FrmFile parsed = FrmFile.Parse(FrmWriter.WriteStatic(frame, 10));

			Assert.Equal(10, parsed.FramesPerSecond);
			Assert.Single(parsed.Directions);
			FrmFrame result = parsed.Directions[0][0];
			Assert.Equal(3, result.Width);
			Assert.Equal(2, result.Height);
			Assert.Equal(-1, result.OffsetX);
			Assert.Equal(4, result.OffsetY);
			Assert.Equal(frame.Pixels, result.Pixels);
		}

		[Fact]
		public void Frm_SixDirections_KeepTheirFrames()
		{
			FrmFrame a = new(1, 1, 0, 0, [1]);
			FrmFrame b = new(1, 1, 0, 0, [2]);
			IReadOnlyList<FrmFrame>[] directions = [[a, b], [b, a], [a, a], [b, b], [a, b], [b, a]];
			FrmFile original = new(8, 1, directions);

			FrmFile parsed = FrmFile.Parse(FrmWriter.Write(original));

			Assert.Equal(6, parsed.Directions.Count);
			Assert.Equal(2, parsed.FrameCount);
			Assert.Equal(2, parsed.Directions[1][0].Pixels[0]);
			Assert.Equal(1, parsed.Directions[5][1].Pixels[0]);
		}

		[Fact]
		public void Bmp8_WriteThenRead_KeepsIndicesAndPalette()
		{
			Palette palette = CreatePalette();
			Byte[] indices = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

			BmpImage image = BmpFile.Read(BmpFile.Write8Bit(4, 3, indices, palette.ToRgb8()));

			Assert.Equal(4, image.Width);
			Assert.Equal(3, image.Height);
			Assert.Equal(indices, image.Indices);
			Assert.Equal(palette.ToRgb8(), image.PaletteRgb);
		}

		[Fact]
		public void Bmp32_WriteThenRead_KeepsTheAlpha()
		{
			Byte[] bgra = [1, 2, 3, 255, 4, 5, 6, 0, 7, 8, 9, 255, 10, 11, 12, 128, 0, 0, 0, 0, 255, 255, 255, 255];

			BmpImage image = BmpFile.Read(BmpFile.Write32Bit(3, 2, bgra));

			Assert.Equal(bgra, image.Bgra);
		}

		[Fact]
		public void FrameToBmp8_AndBack_IsLossless()
		{
			Palette palette = CreatePalette();
			FrmFrame frame = new(5, 4, 0, 0, [.. Enumerable.Range(0, 20).Select(i => (Byte)((i * 11 % 255) + 1))]);

			FrmFrame back = FrmConverter.ToFrame(BmpFile.Read(FrmConverter.ToBmp8(frame, palette)), palette);

			Assert.Equal(frame.Pixels, back.Pixels);
		}

		[Fact]
		public void FrameToBmp32_AndBack_KeepsColorsAndTransparency()
		{
			Palette palette = CreatePalette();
			FrmFrame frame = new(3, 1, 0, 0, [0, 100, 200]);

			FrmFrame back = FrmConverter.ToFrame(BmpFile.Read(FrmConverter.ToBmp32(frame, palette)), palette);

			Assert.Equal(frame.Pixels, back.Pixels);
		}

		[Fact]
		public void MagentaKey_BecomesTransparent_AndOtherColorsMapToThePalette()
		{
			Palette palette = CreatePalette();
			(Byte r, Byte g, Byte b) = palette.GetColor(50);
			Byte[] bgra = [255, 0, 255, 255, b, g, r, 255];
			Byte[] bmp = BmpFile.Write32Bit(2, 1, bgra);
			// A 24-bit bitmap has no alpha: build one by hand from the 32-bit image.
			BmpImage image = BmpFile.Read(bmp);

			FrmFrame frame = FrmConverter.ToFrame(image, palette);

			Assert.Equal(0, frame.Pixels[0]);
			Assert.Equal(50, frame.Pixels[1]);
		}

		[Fact]
		public void Bmp_RejectsGarbage()
		{
			Assert.Throws<InvalidDataException>(() => BmpFile.Read(new Byte[100]));
			Byte[] valid = BmpFile.Write32Bit(2, 2, new Byte[16]);
			Assert.Throws<InvalidDataException>(() => BmpFile.Read(valid.AsSpan(0, valid.Length - 4)));
		}
	}
}