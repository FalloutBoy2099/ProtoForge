namespace ProtoForge.Core.Tests.Formats
{
	using System;
	using System.IO;
	using System.Text;
	using Xunit;
	using ProtoForge.Core.Formats.Dat;

	public class Dat1Tests
	{
		[Fact]
		public void Lzss_DecodesLiteralsAndBackReferences()
		{
			// flags 0b00000111: three literals, then a match (offset 4078 = start of the ring buffer data, length 6).
			Byte[] block = [0x07, (Byte)'a', (Byte)'b', (Byte)'c', 0xEE, 0xF3];
			Byte[] output = new Byte[9];

			Int32 written = Lzss.DecodeBlock(block, output);

			Assert.Equal(9, written);
			Assert.Equal("abcabcabc", Encoding.ASCII.GetString(output));
		}

		[Fact]
		public void Lzss_DecodeBlocks_HandlesStoredAndCompressedBlocks()
		{
			Byte[] packed =
			[
				0x80, 0x03, (Byte)'x', (Byte)'y', (Byte)'z', // stored block of 3 bytes
				0x00, 0x06, 0x07, (Byte)'a', (Byte)'b', (Byte)'c', 0xEE, 0xF3, // compressed block => "abcabcabc"
			];

			Byte[] result = Lzss.DecodeBlocks(packed, 12);

			Assert.Equal("xyzabcabcabc", Encoding.ASCII.GetString(result));
		}

		[Fact]
		public void Archive_ReadsFallout1Layout()
		{
			String path = Path.Combine(Path.GetTempPath(), "protoforge-test-" + Guid.NewGuid().ToString("N") + ".dat");
			try
			{
				File.WriteAllBytes(path, BuildDat1());
				using DatArchive archive = DatArchive.Open(path);

				Assert.Equal(DatFormat.Fallout1, archive.Format);
				Assert.Equal(1, archive.Count);
				Assert.Equal("abcabcabc", Encoding.ASCII.GetString(archive.Read("TEST.TXT")!));
				Assert.Null(archive.Read("missing.txt"));
			}
			finally
			{
				File.Delete(path);
			}
		}

		private static Byte[] BuildDat1()
		{
			using MemoryStream stream = new();
			void Int32Be(Int32 value)
			{
				stream.WriteByte((Byte)(value >> 24));
				stream.WriteByte((Byte)(value >> 16));
				stream.WriteByte((Byte)(value >> 8));
				stream.WriteByte((Byte)value);
			}

			Int32Be(1);
			Int32Be(0x0A);
			Int32Be(0);
			Int32Be(0);
			stream.WriteByte(1);
			stream.WriteByte((Byte)'.');
			Int32Be(1);
			Int32Be(0x0A);
			Int32Be(0x10);
			Int32Be(0);
			stream.WriteByte(8);
			stream.Write(Encoding.ASCII.GetBytes("test.txt"));
			Int32Be(0x40);
			Int32 offset = (Int32)stream.Position + 12;
			Int32Be(offset);
			Int32Be(9);
			Int32Be(8);
			stream.Write([0x00, 0x06, 0x07, (Byte)'a', (Byte)'b', (Byte)'c', 0xEE, 0xF3]);
			return stream.ToArray();
		}
	}
}