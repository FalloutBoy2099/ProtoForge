namespace ProtoForge.Core.Tests
{
	using System;
	using System.Buffers.Binary;
	using System.IO;
	using System.Security.Cryptography;
	using System.Text;
	using Xunit;
	using ProtoForge.Core.Formats.Audio;

	/// <summary>
	/// ACM decoder tests. The fixtures are real Fallout 2 sound effects (sound/sfx/WA01XXX2.ACM, mono, and WHX1WXX1.ACM, stereo); the
	/// expected PCM is the output of <c>ffmpeg -i x.acm -f s16le -acodec pcm_s16le</c>, stored as a SHA-256 hash.
	/// </summary>
	public class AcmTests
	{
		private static Byte[] ReadFixture(String name)
		{
			return File.ReadAllBytes(TestEnvironment.FixturePath(Path.Combine("Audio", name)));
		}

		private static String ReadHash(String name)
		{
			return File.ReadAllText(TestEnvironment.FixturePath(Path.Combine("Audio", name))).Trim();
		}

		private static String Sha256(Byte[] data)
		{
			return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
		}

		private static Int16[] Samples(Byte[] pcm, Int32 start, Int32 count)
		{
			Int16[] result = new Int16[count];
			for (Int32 i = 0; i < count; i++)
			{
				result[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan((start + i) * 2));
			}

			return result;
		}

		[Fact]
		public void Read_MonoSample_ParsesHeader()
		{
			AcmFile acm = AcmFile.Read(ReadFixture("sample.acm"));

			Assert.Equal(1, acm.Channels);
			Assert.Equal(22050, acm.SampleRate);
			Assert.Equal(18085, acm.SampleCount);
			Assert.Equal(18085 * 2, acm.Pcm16.Length);
		}

		[Fact]
		public void Read_StereoSample_ParsesHeader()
		{
			AcmFile acm = AcmFile.Read(ReadFixture("sample_stereo.acm"));

			Assert.Equal(2, acm.Channels);
			Assert.Equal(22050, acm.SampleRate);
			Assert.Equal(8176, acm.SampleCount);
			Assert.Equal(8176 * 2 * 2, acm.Pcm16.Length);
		}

		[Fact]
		public void Read_MonoSample_IsBitExactWithFfmpeg()
		{
			AcmFile acm = AcmFile.Read(ReadFixture("sample.acm"));

			Assert.Equal(ReadHash("sample.pcm.sha256"), Sha256(acm.Pcm16));
			Assert.Equal(new Int16[] { -25, -41, -32, -6, -46, -62, -2, 6, 11, 10, 10, 18, 2, 5, 23, -11 }, Samples(acm.Pcm16, 1000, 16));
		}

		[Fact]
		public void Read_StereoSample_IsBitExactWithFfmpeg()
		{
			AcmFile acm = AcmFile.Read(ReadFixture("sample_stereo.acm"));

			Assert.Equal(ReadHash("sample_stereo.pcm.sha256"), Sha256(acm.Pcm16));
			Assert.Equal(
				new Int16[] { -1560, -1062, -936, -1616, -2731, -3242, -2650, -1605, -2229, -2686, -2161, -2673, -1877, -3467, -4419, -3372 },
				Samples(acm.Pcm16, 1000, 16));
		}

		[Fact]
		public void ToWav_ProducesPcmRiffFile()
		{
			AcmFile acm = AcmFile.Read(ReadFixture("sample_stereo.acm"));

			Byte[] wav = acm.ToWav();

			Assert.Equal(44 + acm.Pcm16.Length, wav.Length);
			Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
			Assert.Equal(wav.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4)));
			Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(wav, 8, 8));
			Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(16)));
			Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20)));
			Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)));
			Assert.Equal(22050, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
			Assert.Equal(22050 * 4, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(28)));
			Assert.Equal(4, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(32)));
			Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34)));
			Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
			Assert.Equal(acm.Pcm16.Length, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
			Assert.Equal(acm.Pcm16, wav.AsSpan(44).ToArray());
		}

		[Fact]
		public void Read_RejectsBadSignatureAndShortData()
		{
			Byte[] data = ReadFixture("sample.acm");
			data[0] ^= 0xFF;

			Assert.Throws<InvalidDataException>(() => AcmFile.Read(data));
			Assert.Throws<InvalidDataException>(() => AcmFile.Read(new Byte[5]));
		}

		[Fact]
		public void Read_RejectsTruncatedData()
		{
			Byte[] data = ReadFixture("sample.acm");

			Assert.Throws<InvalidDataException>(() => AcmFile.Read(data.AsSpan(0, data.Length / 2)));
			Assert.Throws<InvalidDataException>(() => AcmFile.Read(data.AsSpan(0, 20)));
		}

		[Fact]
		public void Read_CorruptData_OnlyThrowsInvalidDataException()
		{
			Byte[] original = ReadFixture("sample.acm");
			Random random = new(1234);

			for (Int32 i = 0; i < 300; i++)
			{
				Byte[] data = (Byte[])original.Clone();
				Int32 flips = 1 + random.Next(4);
				for (Int32 j = 0; j < flips; j++)
				{
					data[random.Next(data.Length)] ^= (Byte)(1 << random.Next(8));
				}

				try
				{
					AcmFile.Read(data);
				}
				catch (InvalidDataException)
				{
				}
			}
		}

		[SkippableFact]
		public void Read_RealGameSoundEffect_DecodesWhenGameDataIsInstalled()
		{
			String? folder = TestEnvironment.Fallout2Folder;
			Skip.If(folder is null, "Fallout 2 is not installed.");
			String master = Path.Combine(folder!, "master.dat");
			Skip.IfNot(File.Exists(master), "master.dat was not found.");

			using ProtoForge.Core.Formats.Dat.DatArchive archive = ProtoForge.Core.Formats.Dat.DatArchive.Open(master);
			Byte[]? data = archive.Read("sound/sfx/waa1xxx1.acm");
			Skip.If(data is null, "The sound effect was not found.");

			AcmFile acm = AcmFile.Read(data!);

			Assert.True(acm.SampleCount > 0);
			Assert.Equal(acm.SampleCount * acm.Channels * 2, acm.Pcm16.Length);
		}
	}
}