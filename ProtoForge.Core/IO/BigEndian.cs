namespace ProtoForge.Core.IO
{
	using System;
	using System.Buffers.Binary;

	/// <summary>
	/// Big-endian primitive access. Fallout 1/2 prototype, FRM and DAT1 data are stored in big-endian byte order.
	/// </summary>
	public static class BigEndian
	{
		public static Int32 ReadInt32(ReadOnlySpan<Byte> buffer, Int32 offset)
		{
			return BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(offset, 4));
		}

		public static Int16 ReadInt16(ReadOnlySpan<Byte> buffer, Int32 offset)
		{
			return BinaryPrimitives.ReadInt16BigEndian(buffer.Slice(offset, 2));
		}

		public static UInt16 ReadUInt16(ReadOnlySpan<Byte> buffer, Int32 offset)
		{
			return BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset, 2));
		}

		public static void WriteInt32(Span<Byte> buffer, Int32 offset, Int32 value)
		{
			BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(offset, 4), value);
		}

		public static void WriteInt16(Span<Byte> buffer, Int32 offset, Int16 value)
		{
			BinaryPrimitives.WriteInt16BigEndian(buffer.Slice(offset, 2), value);
		}
	}
}