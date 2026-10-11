namespace ProtoForge.Core.Formats.Frm
{
	using System;
	using System.Collections.Generic;
	using IO;

	/// <summary>Writes a FRM file (the exact inverse of <see cref="FrmFile.Parse"/>).</summary>
	public static class FrmWriter
	{
		private const Int32 HeaderSize = 0x3E;
		private const Int32 Version = 4;

		public static Byte[] Write(FrmFile file)
		{
			if (file.Directions.Count is not (1 or 6))
			{
				throw new ArgumentException("A FRM has one or six directions.", nameof(file));
			}

			Int32 directionCount = file.Directions.Count;
			Int32 framesPerDirection = file.FrameCount;

			// Size of the frame area and the offset of each direction (relative to the end of the header).
			Int32[] offsets = new Int32[6];
			Int32 total = 0;
			for (Int32 d = 0; d < directionCount; d++)
			{
				offsets[d] = total;
				IReadOnlyList<FrmFrame> frames = file.Directions[d];
				if (frames.Count != framesPerDirection)
				{
					throw new ArgumentException("Every direction must have the same number of frames.", nameof(file));
				}

				foreach (FrmFrame frame in frames)
				{
					total += 12 + frame.Pixels.Length;
				}
			}

			Byte[] data = new Byte[HeaderSize + total];
			BigEndian.WriteInt32(data, 0x00, Version);
			BigEndian.WriteInt16(data, 0x04, (Int16)file.FramesPerSecond);
			BigEndian.WriteInt16(data, 0x06, (Int16)file.ActionFrame);
			BigEndian.WriteInt16(data, 0x08, (Int16)framesPerDirection);
			for (Int32 d = 0; d < 6; d++)
			{
				BigEndian.WriteInt32(data, 0x22 + (d * 4), directionCount == 1 ? 0 : offsets[d]);
			}

			BigEndian.WriteInt32(data, 0x3A, total);

			Int32 position = HeaderSize;
			for (Int32 d = 0; d < directionCount; d++)
			{
				foreach (FrmFrame frame in file.Directions[d])
				{
					BigEndian.WriteInt16(data, position, (Int16)frame.Width);
					BigEndian.WriteInt16(data, position + 2, (Int16)frame.Height);
					BigEndian.WriteInt32(data, position + 4, frame.Pixels.Length);
					BigEndian.WriteInt16(data, position + 8, (Int16)frame.OffsetX);
					BigEndian.WriteInt16(data, position + 10, (Int16)frame.OffsetY);
					Buffer.BlockCopy(frame.Pixels, 0, data, position + 12, frame.Pixels.Length);
					position += 12 + frame.Pixels.Length;
				}
			}

			return data;
		}

		/// <summary>A single static image (one direction, one frame), like the inventory and map art.</summary>
		public static Byte[] WriteStatic(FrmFrame frame, Int32 framesPerSecond = 1)
		{
			return Write(new FrmFile(framesPerSecond, 0, [[frame]]));
		}
	}
}