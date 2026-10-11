namespace ProtoForge.Core.Formats.Dat
{
	using System;
	using System.IO;
	using IO;

	/// <summary>
	/// LZSS decoder used by Fallout 1 DAT archives (4096 bytes ring buffer pre-filled with spaces, 18 bytes max match).
	/// </summary>
	public static class Lzss
	{
		private const Int32 RingSize = 4096;
		private const Int32 MaxMatch = 18;
		private const Int32 Threshold = 2;

		/// <summary>
		/// Decodes a Fallout 1 file made of blocks: a big-endian Int16 length (high bit set = stored block) followed by the block data.
		/// </summary>
		public static Byte[] DecodeBlocks(ReadOnlySpan<Byte> packed, Int32 unpackedSize)
		{
			Byte[] output = new Byte[unpackedSize];
			Int32 written = 0;
			Int32 position = 0;
			while (written < unpackedSize && position + 2 <= packed.Length)
			{
				UInt16 header = BigEndian.ReadUInt16(packed, position);
				position += 2;
				Int32 length = header & 0x7FFF;
				if (length > packed.Length - position)
				{
					throw new InvalidDataException("Truncated LZSS block.");
				}

				ReadOnlySpan<Byte> block = packed.Slice(position, length);
				position += length;
				if ((header & 0x8000) != 0)
				{
					Int32 count = Math.Min(length, unpackedSize - written);
					block[..count].CopyTo(output.AsSpan(written));
					written += count;
				}
				else
				{
					written += DecodeBlock(block, output.AsSpan(written));
				}
			}

			if (written != unpackedSize)
			{
				throw new InvalidDataException($"LZSS data ended after {written} of {unpackedSize} bytes.");
			}

			return output;
		}

		/// <summary>Decodes one compressed block and returns the number of bytes produced (limited by the output size).</summary>
		public static Int32 DecodeBlock(ReadOnlySpan<Byte> input, Span<Byte> output)
		{
			Byte[] ring = new Byte[RingSize + MaxMatch - 1];
			Array.Fill(ring, (Byte)0x20);
			Int32 r = RingSize - MaxMatch;
			Int32 inPos = 0;
			Int32 outPos = 0;
			Int32 flags = 0;

			while (true)
			{
				flags >>= 1;
				if ((flags & 0x100) == 0)
				{
					if (inPos >= input.Length)
					{
						break;
					}

					flags = input[inPos++] | 0xFF00;
				}

				if ((flags & 1) != 0)
				{
					if (inPos >= input.Length || outPos >= output.Length)
					{
						break;
					}

					Byte value = input[inPos++];
					output[outPos++] = value;
					ring[r] = value;
					r = (r + 1) & (RingSize - 1);
				}
				else
				{
					if (inPos + 1 >= input.Length)
					{
						break;
					}

					Int32 low = input[inPos++];
					Int32 high = input[inPos++];
					Int32 offset = low | ((high & 0xF0) << 4);
					Int32 length = (high & 0x0F) + Threshold + 1;
					for (Int32 k = 0; k < length; k++)
					{
						if (outPos >= output.Length)
						{
							return outPos;
						}

						Byte value = ring[(offset + k) & (RingSize - 1)];
						output[outPos++] = value;
						ring[r] = value;
						r = (r + 1) & (RingSize - 1);
					}
				}
			}

			return outPos;
		}
	}
}