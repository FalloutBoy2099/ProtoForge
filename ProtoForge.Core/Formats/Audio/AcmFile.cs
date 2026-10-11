namespace ProtoForge.Core.Formats.Audio
{
	using System;
	using System.Buffers.Binary;
	using System.IO;
	using System.Text;

	/// <summary>
	/// A Fallout 1/2 Interplay ACM sound file, decoded to 16-bit PCM. This is a managed port of the ACM decoder of libacm
	/// (Marko Kreen, Jan Gjerlufsen) in the form used by FFmpeg's "interplayacm" decoder, and it is bit-exact with it.
	/// <para>
	/// Stream layout: a 14-byte header (signature 0x01032897, total value count, channels, sample rate, and a packed
	/// <c>levels | (subblocks &lt;&lt; 4)</c> word) followed by an LSB-first bit stream. The stream is a sequence of blocks of
	/// <c>subblocks * 2^levels</c> values. Each block carries an amplitude table and one coded column per <c>2^levels</c>
	/// columns; the columns are then run through the subband ("juggle") reconstruction and scaled down by <c>levels</c> bits.
	/// </para>
	/// </summary>
	public sealed class AcmFile
	{
		private const Int32 HeaderSize = 14;
		private const UInt32 Signature = 0x01032897;
		private const Int32 MaxChannels = 16;
		private const Int32 MaxBlockValues = 1 << 22;
		private const Int32 AmplitudeBufferSize = 0x10000;
		private const Int32 MidOffset = 0x8000;
		private const Int32 PcmHeaderSize = 44;
		private const Int64 MaxPcmBytes = 1L << 27;

		private AcmFile(Int32 channels, Int32 sampleRate, Int32 sampleCount, Byte[] pcm16)
		{
			Channels = channels;
			SampleRate = sampleRate;
			SampleCount = sampleCount;
			Pcm16 = pcm16;
		}

		/// <summary>Number of interleaved channels (Fallout sound effects are mono, music may be stereo).</summary>
		public Int32 Channels { get; }

		/// <summary>Sampling rate in Hz.</summary>
		public Int32 SampleRate { get; }

		/// <summary>Number of samples per channel.</summary>
		public Int32 SampleCount { get; }

		/// <summary>Decoded audio: signed 16-bit little-endian samples, interleaved by channel.</summary>
		public Byte[] Pcm16 { get; }

		/// <summary>Length of the audio in seconds.</summary>
		public Double Duration
		{
			get { return SampleRate <= 0 ? 0 : (Double)SampleCount / SampleRate; }
		}

		/// <summary>Decodes a complete ACM file. Throws <see cref="InvalidDataException"/> for malformed or truncated data.</summary>
		public static AcmFile Read(ReadOnlySpan<Byte> data)
		{
			if (data.Length < HeaderSize)
			{
				throw new InvalidDataException("The ACM file is too short.");
			}

			if (BinaryPrimitives.ReadUInt32LittleEndian(data) != Signature)
			{
				throw new InvalidDataException("The file is not an Interplay ACM file (bad signature).");
			}

			UInt32 totalValues = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));
			Int32 channels = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8));
			Int32 sampleRate = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10));
			Int32 packed = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12));
			Int32 level = packed & 0x0F;
			Int32 rows = packed >> 4;

			if (channels < 1 || channels > MaxChannels)
			{
				throw new InvalidDataException("The ACM file has an invalid channel count.");
			}

			if (rows < 1)
			{
				throw new InvalidDataException("The ACM file has an invalid sub-block count.");
			}

			Int32 columns = 1 << level;
			Int64 blockValues = (Int64)rows * columns;
			if (blockValues > MaxBlockValues)
			{
				throw new InvalidDataException("The ACM block size is too large.");
			}

			Int32 samplesPerBlock = (Int32)blockValues / channels;
			if (samplesPerBlock < 1)
			{
				throw new InvalidDataException("The ACM block is smaller than one sample frame.");
			}

			// Every block needs at least 20 bits of header and 5 bits per column, which bounds the amount of audio the data can hold.
			Int64 minBlockBits = 20 + (5L * columns);
			Int64 maxBlocks = (((Int64)(data.Length - HeaderSize) * 8) / minBlockBits) + 1;
			Int64 maxSamples = maxBlocks * samplesPerBlock;

			// A zero count means "decode until the data ends".
			Int64 sampleCount = totalValues / channels;
			Boolean unbounded = totalValues == 0;
			if (unbounded)
			{
				sampleCount = maxSamples;
			}
			else if (sampleCount > maxSamples)
			{
				throw new InvalidDataException("The ACM file is truncated.");
			}

			Int64 pcmLength = sampleCount * channels * 2;
			if (pcmLength > MaxPcmBytes)
			{
				throw new InvalidDataException("The ACM file is too large.");
			}

			Byte[] pcm = new Byte[pcmLength];
			BlockDecoder decoder = new(level, rows, channels);
			BitReader reader = new(data.Slice(HeaderSize));
			Int64 written = 0;

			while (written < sampleCount)
			{
				// The encoder drops the trailing zero bits of the last block, which decode as silence, so the final block may start
				// with less than a full set of column headers left. Any other block that runs out of data means a truncated file.
				Boolean isLast = !unbounded && written + samplesPerBlock >= sampleCount;
				if (!isLast && reader.BitsLeft < 5L * columns)
				{
					if (unbounded)
					{
						break;
					}

					throw new InvalidDataException("The ACM file is truncated.");
				}

				decoder.DecodeBlock(ref reader);
				Int32 count = (Int32)Math.Min(samplesPerBlock, sampleCount - written);
				decoder.WriteSamples(pcm.AsSpan((Int32)(written * channels * 2)), count * channels);
				written += count;
			}

			if (written != sampleCount)
			{
				pcm = pcm.AsSpan(0, (Int32)(written * channels * 2)).ToArray();
			}

			return new AcmFile(channels, sampleRate, (Int32)written, pcm);
		}

		/// <summary>Builds a complete RIFF/WAVE file (PCM, 16-bit) with the decoded audio.</summary>
		public Byte[] ToWav()
		{
			Int32 blockAlign = Channels * 2;
			Byte[] wav = new Byte[PcmHeaderSize + Pcm16.Length];
			Span<Byte> span = wav;

			Encoding.ASCII.GetBytes("RIFF", span);
			BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), 36 + Pcm16.Length);
			Encoding.ASCII.GetBytes("WAVE", span.Slice(8));
			Encoding.ASCII.GetBytes("fmt ", span.Slice(12));
			BinaryPrimitives.WriteInt32LittleEndian(span.Slice(16), 16);
			BinaryPrimitives.WriteInt16LittleEndian(span.Slice(20), 1);
			BinaryPrimitives.WriteInt16LittleEndian(span.Slice(22), (Int16)Channels);
			BinaryPrimitives.WriteInt32LittleEndian(span.Slice(24), SampleRate);
			BinaryPrimitives.WriteInt32LittleEndian(span.Slice(28), SampleRate * blockAlign);
			BinaryPrimitives.WriteInt16LittleEndian(span.Slice(32), (Int16)blockAlign);
			BinaryPrimitives.WriteInt16LittleEndian(span.Slice(34), 16);
			Encoding.ASCII.GetBytes("data", span.Slice(36));
			BinaryPrimitives.WriteInt32LittleEndian(span.Slice(40), Pcm16.Length);
			Pcm16.CopyTo(span.Slice(PcmHeaderSize));
			return wav;
		}

		/// <summary>LSB-first bit reader. Reading past the end yields zero bits; <see cref="BitsLeft"/> then becomes negative.</summary>
		private ref struct BitReader
		{
			private readonly ReadOnlySpan<Byte> _data;
			private Int32 _position;
			private UInt64 _cache;
			private Int32 _cacheBits;

			public BitReader(ReadOnlySpan<Byte> data)
			{
				_data = data;
				_position = 0;
				_cache = 0;
				_cacheBits = 0;
			}

			public readonly Int64 BitsLeft
			{
				get { return ((Int64)_data.Length * 8) - (((Int64)_position * 8) - _cacheBits); }
			}

			public Int32 Read(Int32 count)
			{
				if (_cacheBits < count)
				{
					Refill();
				}

				Int32 value = (Int32)(_cache & ((1UL << count) - 1));
				_cache >>= count;
				_cacheBits -= count;
				return value;
			}

			public Int32 ReadBit()
			{
				if (_cacheBits < 1)
				{
					Refill();
				}

				Int32 value = (Int32)(_cache & 1);
				_cache >>= 1;
				_cacheBits--;
				return value;
			}

			private void Refill()
			{
				if (_position + 4 <= _data.Length)
				{
					_cache |= (UInt64)BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_position)) << _cacheBits;
					_position += 4;
					_cacheBits += 32;
					return;
				}

				while (_cacheBits <= 56)
				{
					UInt64 value = _position < _data.Length ? _data[_position] : 0UL;
					_cache |= value << _cacheBits;
					_position++;
					_cacheBits += 8;
				}
			}
		}

		/// <summary>The stateful part of the decoder: amplitude table, coefficient block and the reconstruction filter memory.</summary>
		private sealed class BlockDecoder
		{
			private static readonly Int32[] Map1Bit = [-1, +1];
			private static readonly Int32[] Map2BitNear = [-2, -1, +1, +2];
			private static readonly Int32[] Map2BitFar = [-3, -2, +2, +3];
			private static readonly Int32[] Map3Bit = [-4, -3, -2, -1, +1, +2, +3, +4];

			private readonly Int32 _level;
			private readonly Int32 _rows;
			private readonly Int32 _columns;
			private readonly Int32 _channels;
			private readonly Int32[] _block;
			private readonly Int32[] _wrap;
			private readonly Int32[] _amplitude = new Int32[AmplitudeBufferSize];

			public BlockDecoder(Int32 level, Int32 rows, Int32 channels)
			{
				_level = level;
				_rows = rows;
				_columns = 1 << level;
				_channels = channels;
				_block = new Int32[rows * _columns];
				_wrap = new Int32[Math.Max(0, (2 * _columns) - 2)];
			}

			public void DecodeBlock(ref BitReader reader)
			{
				Int32 power = reader.Read(4);
				Int32 step = reader.Read(16);
				Int32 count = 1 << power;

				// The amplitude table is intentionally not cleared between blocks: stale entries behave as in the reference decoder.
				Int32 value = 0;
				for (Int32 i = 0; i < count; i++)
				{
					_amplitude[MidOffset + i] = value;
					value = unchecked(value + step);
				}

				value = unchecked(-step);
				for (Int32 i = 1; i <= count; i++)
				{
					_amplitude[MidOffset - i] = value;
					value = unchecked(value - step);
				}

				for (Int32 column = 0; column < _columns; column++)
				{
					Int32 kind = reader.Read(5);
					Fill(ref reader, kind, column);
				}

				Juggle();
			}

			/// <summary>Writes the first <paramref name="valueCount"/> values of the block as 16-bit samples.</summary>
			public void WriteSamples(Span<Byte> destination, Int32 valueCount)
			{
				for (Int32 i = 0; i < valueCount; i++)
				{
					BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(i * 2), unchecked((Int16)(_block[i] >> _level)));
				}
			}

			private void Set(Int32 row, Int32 column, Int32 index)
			{
				_block[(row << _level) + column] = _amplitude[MidOffset + index];
			}

			private void Fill(ref BitReader reader, Int32 kind, Int32 column)
			{
				switch (kind)
				{
					case 0:
						FillZero(column);
						break;
					case >= 3 and <= 16:
						FillLinear(ref reader, kind, column);
						break;
					case 17:
						FillK13(ref reader, column);
						break;
					case 18:
						FillK12(ref reader, column);
						break;
					case 19:
						FillT15(ref reader, column);
						break;
					case 20:
						FillK24(ref reader, column);
						break;
					case 21:
						FillK23(ref reader, column);
						break;
					case 22:
						FillT27(ref reader, column);
						break;
					case 23:
						FillK35(ref reader, column);
						break;
					case 24:
						FillK34(ref reader, column);
						break;
					case 26:
						FillK45(ref reader, column);
						break;
					case 27:
						FillK44(ref reader, column);
						break;
					case 29:
						FillT37(ref reader, column);
						break;
					default:
						throw new InvalidDataException("The ACM file contains an invalid column type.");
				}
			}

			private void FillZero(Int32 column)
			{
				for (Int32 i = 0; i < _rows; i++)
				{
					Set(i, column, 0);
				}
			}

			private void FillLinear(ref BitReader reader, Int32 bits, Int32 column)
			{
				Int32 middle = 1 << (bits - 1);
				for (Int32 i = 0; i < _rows; i++)
				{
					Set(i, column, reader.Read(bits) - middle);
				}
			}

			private void FillK13(ref BitReader reader, Int32 column)
			{
				for (Int32 i = 0; i < _rows; i++)
				{
					if (reader.ReadBit() == 0)
					{
						Set(i++, column, 0);
						if (i >= _rows)
						{
							break;
						}

						Set(i, column, 0);
						continue;
					}

					if (reader.ReadBit() == 0)
					{
						Set(i, column, 0);
						continue;
					}

					Set(i, column, Map1Bit[reader.ReadBit()]);
				}
			}

			private void FillK12(ref BitReader reader, Int32 column)
			{
				for (Int32 i = 0; i < _rows; i++)
				{
					if (reader.ReadBit() == 0)
					{
						Set(i, column, 0);
						continue;
					}

					Set(i, column, Map1Bit[reader.ReadBit()]);
				}
			}

			private void FillK24(ref BitReader reader, Int32 column)
			{
				for (Int32 i = 0; i < _rows; i++)
				{
					if (reader.ReadBit() == 0)
					{
						Set(i++, column, 0);
						if (i >= _rows)
						{
							break;
						}

						Set(i, column, 0);
						continue;
					}

					if (reader.ReadBit() == 0)
					{
						Set(i, column, 0);
						continue;
					}

					Set(i, column, Map2BitNear[reader.Read(2)]);
				}
			}

			private void FillK23(ref BitReader reader, Int32 column)
			{
				for (Int32 i = 0; i < _rows; i++)
				{
					if (reader.ReadBit() == 0)
					{
						Set(i, column, 0);
						continue;
					}

					Set(i, column, Map2BitNear[reader.Read(2)]);
				}
			}

			private void FillK35(ref BitReader reader, Int32 column)
			{
				for (Int32 i = 0; i < _rows; i++)
				{
					if (reader.ReadBit() == 0)
					{
						Set(i++, column, 0);
						if (i >= _rows)
						{
							break;
						}

						Set(i, column, 0);
						continue;
					}

					if (reader.ReadBit() == 0)
					{
						Set(i, column, 0);
						continue;
					}

					if (reader.ReadBit() == 0)
					{
						Set(i, column, Map1Bit[reader.ReadBit()]);
						continue;
					}

					Set(i, column, Map2BitFar[reader.Read(2)]);
				}
			}

			private void FillK34(ref BitReader reader, Int32 column)
			{
				for (Int32 i = 0; i < _rows; i++)
				{
					if (reader.ReadBit() == 0)
					{
						Set(i, column, 0);
						continue;
					}

					if (reader.ReadBit() == 0)
					{
						Set(i, column, Map1Bit[reader.ReadBit()]);
						continue;
					}

					Set(i, column, Map2BitFar[reader.Read(2)]);
				}
			}

			private void FillK45(ref BitReader reader, Int32 column)
			{
				for (Int32 i = 0; i < _rows; i++)
				{
					if (reader.ReadBit() == 0)
					{
						Set(i++, column, 0);
						if (i >= _rows)
						{
							break;
						}

						Set(i, column, 0);
						continue;
					}

					if (reader.ReadBit() == 0)
					{
						Set(i, column, 0);
						continue;
					}

					Set(i, column, Map3Bit[reader.Read(3)]);
				}
			}

			private void FillK44(ref BitReader reader, Int32 column)
			{
				for (Int32 i = 0; i < _rows; i++)
				{
					if (reader.ReadBit() == 0)
					{
						Set(i, column, 0);
						continue;
					}

					Set(i, column, Map3Bit[reader.Read(3)]);
				}
			}

			/// <summary>Three ternary digits (-1..1) packed into 5 bits.</summary>
			private void FillT15(ref BitReader reader, Int32 column)
			{
				for (Int32 i = 0; i < _rows; i++)
				{
					Int32 code = reader.Read(5);
					if (code > 26)
					{
						throw new InvalidDataException("The ACM file contains an invalid ternary code.");
					}

					Int32 n1 = (code % 3) - 1;
					Int32 n2 = (code / 3 % 3) - 1;
					Int32 n3 = (code / 9) - 1;

					Set(i++, column, n1);
					if (i >= _rows)
					{
						break;
					}

					Set(i++, column, n2);
					if (i >= _rows)
					{
						break;
					}

					Set(i, column, n3);
				}
			}

			/// <summary>Three base-5 digits (-2..2) packed into 7 bits.</summary>
			private void FillT27(ref BitReader reader, Int32 column)
			{
				for (Int32 i = 0; i < _rows; i++)
				{
					Int32 code = reader.Read(7);
					if (code > 124)
					{
						throw new InvalidDataException("The ACM file contains an invalid quinary code.");
					}

					Int32 n1 = (code % 5) - 2;
					Int32 n2 = (code / 5 % 5) - 2;
					Int32 n3 = (code / 25) - 2;

					Set(i++, column, n1);
					if (i >= _rows)
					{
						break;
					}

					Set(i++, column, n2);
					if (i >= _rows)
					{
						break;
					}

					Set(i, column, n3);
				}
			}

			/// <summary>Two base-11 digits (-5..5) packed into 7 bits.</summary>
			private void FillT37(ref BitReader reader, Int32 column)
			{
				for (Int32 i = 0; i < _rows; i++)
				{
					Int32 code = reader.Read(7);
					if (code > 120)
					{
						throw new InvalidDataException("The ACM file contains an invalid base-11 code.");
					}

					Int32 n1 = (code % 11) - 5;
					Int32 n2 = (code / 11) - 5;

					Set(i++, column, n1);
					if (i >= _rows)
					{
						break;
					}

					Set(i, column, n2);
				}
			}

			/// <summary>Subband reconstruction of the whole block, in groups of rows that fit the filter memory.</summary>
			private void Juggle()
			{
				// Nothing to reconstruct when the sub-blocks are a single value wide.
				if (_level == 0)
				{
					return;
				}

				Int32 stepCount = _level > 9 ? 1 : (2048 >> _level) - 2;
				Int32 todo = _rows;
				Int32 blockOffset = 0;

				while (true)
				{
					Int32 wrapOffset = 0;
					Int32 subCount = Math.Min(stepCount, todo);
					Int32 subLength = _columns / 2;
					subCount *= 2;

					JuggleStage(wrapOffset, blockOffset, subLength, subCount);
					wrapOffset += subLength * 2;

					Int32 position = blockOffset;
					for (Int32 i = 0; i < subCount; i++)
					{
						_block[position]++;
						position += subLength;
					}

					while (subLength > 1)
					{
						subLength /= 2;
						subCount *= 2;
						JuggleStage(wrapOffset, blockOffset, subLength, subCount);
						wrapOffset += subLength * 2;
					}

					if (todo <= stepCount)
					{
						break;
					}

					todo -= stepCount;
					blockOffset += stepCount << _level;
				}
			}

			private void JuggleStage(Int32 wrapOffset, Int32 blockOffset, Int32 subLength, Int32 subCount)
			{
				Int32[] block = _block;
				Int32[] wrap = _wrap;
				Int32 pairs = subCount / 2;

				unchecked
				{
					for (Int32 i = 0; i < subLength; i++)
					{
						Int32 p = blockOffset + i;
						Int32 r0 = wrap[wrapOffset + (i * 2)];
						Int32 r1 = wrap[wrapOffset + (i * 2) + 1];
						for (Int32 j = 0; j < pairs; j++)
						{
							Int32 r2 = block[p];
							block[p] = (r1 * 2) + (r0 + r2);
							p += subLength;
							Int32 r3 = block[p];
							block[p] = (r2 * 2) - (r1 + r3);
							p += subLength;
							r0 = r2;
							r1 = r3;
						}

						wrap[wrapOffset + (i * 2)] = r0;
						wrap[wrapOffset + (i * 2) + 1] = r1;
					}
				}
			}
		}
	}
}