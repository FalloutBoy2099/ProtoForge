namespace ProtoForge.Core.Formats.Msg
{
	using System;
	using System.Text;

	/// <summary>
	/// The "LevCorp" text encoding of some Russian translations: DOS code page 866 where the letters at 0xE0-0xEF are stored at 0xB0-0xBF
	/// (the pseudo-graphics positions), so that the game fonts of that translation can draw them.
	/// </summary>
	public sealed class LevCorpEncoding : Encoding
	{
		private const Int32 DosCodePage = 866;
		private const Byte StoredFirst = 0xB0;
		private const Byte StoredLast = 0xBF;
		private const Byte TextFirst = 0xE0;
		private const Byte TextLast = 0xEF;
		private const Byte Shift = 0x30;

		private readonly Encoding _dos;

		public LevCorpEncoding()
		{
			_dos = GetEncoding(DosCodePage, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
		}

		public override String EncodingName
		{
			get { return "Cyrillic (LevCorp)"; }
		}

		public override Int32 GetByteCount(Char[] chars, Int32 index, Int32 count)
		{
			return _dos.GetByteCount(chars, index, count);
		}

		public override Int32 GetBytes(Char[] chars, Int32 charIndex, Int32 charCount, Byte[] bytes, Int32 byteIndex)
		{
			Int32 written = _dos.GetBytes(chars, charIndex, charCount, bytes, byteIndex);
			for (Int32 i = byteIndex; i < byteIndex + written; i++)
			{
				if (bytes[i] is >= TextFirst and <= TextLast)
				{
					bytes[i] = (Byte)(bytes[i] - Shift);
				}
			}

			return written;
		}

		public override Int32 GetCharCount(Byte[] bytes, Int32 index, Int32 count)
		{
			return _dos.GetCharCount(bytes, index, count);
		}

		public override Int32 GetChars(Byte[] bytes, Int32 byteIndex, Int32 byteCount, Char[] chars, Int32 charIndex)
		{
			Byte[] converted = new Byte[byteCount];
			for (Int32 i = 0; i < byteCount; i++)
			{
				Byte value = bytes[byteIndex + i];
				converted[i] = value is >= StoredFirst and <= StoredLast ? (Byte)(value + Shift) : value;
			}

			return _dos.GetChars(converted, 0, byteCount, chars, charIndex);
		}

		public override Int32 GetMaxByteCount(Int32 charCount)
		{
			return _dos.GetMaxByteCount(charCount);
		}

		public override Int32 GetMaxCharCount(Int32 byteCount)
		{
			return _dos.GetMaxCharCount(byteCount);
		}
	}
}