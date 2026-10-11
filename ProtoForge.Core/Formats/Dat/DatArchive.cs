namespace ProtoForge.Core.Formats.Dat
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.IO.Compression;
	using Microsoft.Win32.SafeHandles;
	using IO;
	using Vfs;

	public enum DatFormat
	{
		Fallout1,
		Fallout2,
	}

	/// <summary>One file stored inside a DAT archive. <see cref="Path"/> is a normalised virtual path.</summary>
	public sealed record DatEntry(String Path, Int64 Offset, Int32 PackedSize, Int32 UnpackedSize, Boolean IsCompressed);

	/// <summary>
	/// Read-only access to a Fallout 1 (LZSS) or Fallout 2 (zlib) DAT archive. Only the directory tree is read when the archive
	/// is opened; file contents are read on demand with positional reads, so one instance can be shared between threads.
	/// </summary>
	public sealed class DatArchive : IDisposable
	{
		private readonly SafeFileHandle _handle;
		private readonly Dictionary<String, DatEntry> _entries;

		private DatArchive(String filePath, DatFormat format, SafeFileHandle handle, Dictionary<String, DatEntry> entries)
		{
			FilePath = filePath;
			Format = format;
			_handle = handle;
			_entries = entries;
		}

		public String FilePath { get; }

		public DatFormat Format { get; }

		public IReadOnlyCollection<DatEntry> Entries
		{
			get { return _entries.Values; }
		}

		public Int32 Count
		{
			get { return _entries.Count; }
		}

		public static DatArchive Open(String filePath)
		{
			SafeFileHandle handle = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOptions.RandomAccess);
			try
			{
				Int64 length = RandomAccess.GetLength(handle);
				if (length < 16)
				{
					throw new InvalidDataException("The file is too small to be a DAT archive.");
				}

				Byte[] tail = new Byte[8];
				RandomAccess.Read(handle, tail, length - 8);
				Int32 treeSize = BitConverter.ToInt32(tail, 0);
				Int32 dataSize = BitConverter.ToInt32(tail, 4);

				if (dataSize == length && treeSize > 0 && treeSize < length)
				{
					return new DatArchive(filePath, DatFormat.Fallout2, handle, ReadDat2(handle, length, treeSize));
				}

				return new DatArchive(filePath, DatFormat.Fallout1, handle, ReadDat1(handle));
			}
			catch
			{
				handle.Dispose();
				throw;
			}
		}

		public Boolean Contains(String path)
		{
			return _entries.ContainsKey(GamePath.Normalize(path));
		}

		public DatEntry? Find(String path)
		{
			return _entries.TryGetValue(GamePath.Normalize(path), out DatEntry? entry) ? entry : null;
		}

		/// <summary>Reads and decompresses one file.</summary>
		public Byte[] Read(DatEntry entry)
		{
			Byte[] packed = new Byte[entry.PackedSize];
			Int32 total = 0;
			while (total < packed.Length)
			{
				Int32 read = RandomAccess.Read(_handle, packed.AsSpan(total), entry.Offset + total);
				if (read <= 0)
				{
					throw new EndOfStreamException($"Unexpected end of '{FilePath}' while reading '{entry.Path}'.");
				}

				total += read;
			}

			if (!entry.IsCompressed)
			{
				return packed;
			}

			if (Format == DatFormat.Fallout1)
			{
				return Lzss.DecodeBlocks(packed, entry.UnpackedSize);
			}

			Byte[] result = new Byte[entry.UnpackedSize];
			using MemoryStream source = new(packed, writable: false);
			using ZLibStream zlib = new(source, CompressionMode.Decompress);
			zlib.ReadExactly(result);
			return result;
		}

		public Byte[]? Read(String path)
		{
			DatEntry? entry = Find(path);
			return entry is null ? null : Read(entry);
		}

		public void Dispose()
		{
			_handle.Dispose();
		}

		private static Dictionary<String, DatEntry> ReadDat2(SafeFileHandle handle, Int64 length, Int32 treeSize)
		{
			Byte[] tree = new Byte[treeSize];
			Int64 treeStart = length - 8 - treeSize;
			Int32 read = 0;
			while (read < tree.Length)
			{
				Int32 count = RandomAccess.Read(handle, tree.AsSpan(read), treeStart + read);
				if (count <= 0)
				{
					throw new EndOfStreamException("The DAT directory tree is truncated.");
				}

				read += count;
			}

			Int32 fileCount = BitConverter.ToInt32(tree, 0);
			Dictionary<String, DatEntry> entries = new(fileCount, StringComparer.Ordinal);
			Int32 position = 4;
			for (Int32 i = 0; i < fileCount; i++)
			{
				Int32 nameLength = BitConverter.ToInt32(tree, position);
				position += 4;
				String name = System.Text.Encoding.Latin1.GetString(tree, position, nameLength);
				position += nameLength;
				Boolean compressed = tree[position] != 0;
				Int32 unpacked = BitConverter.ToInt32(tree, position + 1);
				Int32 packed = BitConverter.ToInt32(tree, position + 5);
				Int32 offset = BitConverter.ToInt32(tree, position + 9);
				position += 13;

				String path = GamePath.Normalize(name);
				entries[path] = new DatEntry(path, offset, compressed ? packed : unpacked, unpacked, compressed);
			}

			return entries;
		}

		private static Dictionary<String, DatEntry> ReadDat1(SafeFileHandle handle)
		{
			SequentialReader reader = new(handle);

			Int32 directoryCount = reader.ReadInt32();
			if (directoryCount <= 0 || directoryCount > 100000)
			{
				throw new InvalidDataException("Not a valid Fallout DAT archive.");
			}

			reader.ReadInt32();
			reader.ReadInt32();
			reader.ReadInt32();

			String[] directories = new String[directoryCount];
			for (Int32 i = 0; i < directoryCount; i++)
			{
				directories[i] = reader.ReadString();
			}

			Dictionary<String, DatEntry> entries = new(StringComparer.Ordinal);
			for (Int32 d = 0; d < directoryCount; d++)
			{
				Int32 fileCount = reader.ReadInt32();
				reader.ReadInt32();
				reader.ReadInt32();
				reader.ReadInt32();
				for (Int32 f = 0; f < fileCount; f++)
				{
					String name = reader.ReadString();
					Int32 attributes = reader.ReadInt32();
					Int32 offset = reader.ReadInt32();
					Int32 unpacked = reader.ReadInt32();
					Int32 packed = reader.ReadInt32();

					String full = directories[d] == "." ? name : directories[d] + "/" + name;
					String path = GamePath.Normalize(full);
					Boolean compressed = attributes == 0x40;
					entries[path] = new DatEntry(path, offset, compressed ? packed : unpacked, unpacked, compressed);
				}
			}

			return entries;
		}

		/// <summary>Buffered forward reader built on positional reads, so it never disturbs (or closes) the shared file handle.</summary>
		private sealed class SequentialReader
		{
			private readonly SafeFileHandle _handle;
			private readonly Byte[] _buffer = new Byte[65536];
			private Int64 _bufferStart;
			private Int32 _bufferLength;
			private Int64 _position;

			public SequentialReader(SafeFileHandle handle)
			{
				_handle = handle;
			}

			public Byte ReadByte()
			{
				Span<Byte> one = stackalloc Byte[1];
				Fill(one);
				return one[0];
			}

			public Int32 ReadInt32()
			{
				Span<Byte> four = stackalloc Byte[4];
				Fill(four);
				return BigEndian.ReadInt32(four, 0);
			}

			public String ReadString()
			{
				Int32 length = ReadByte();
				Byte[] bytes = new Byte[length];
				Fill(bytes);
				return System.Text.Encoding.Latin1.GetString(bytes);
			}

			private void Fill(Span<Byte> target)
			{
				Int32 copied = 0;
				while (copied < target.Length)
				{
					Int64 bufferEnd = _bufferStart + _bufferLength;
					if (_position < _bufferStart || _position >= bufferEnd)
					{
						_bufferStart = _position;
						_bufferLength = RandomAccess.Read(_handle, _buffer, _position);
						if (_bufferLength <= 0)
						{
							throw new EndOfStreamException("Unexpected end of the DAT archive.");
						}

						bufferEnd = _bufferStart + _bufferLength;
					}

					Int32 available = (Int32)(bufferEnd - _position);
					Int32 count = Math.Min(available, target.Length - copied);
					_buffer.AsSpan((Int32)(_position - _bufferStart), count).CopyTo(target[copied..]);
					copied += count;
					_position += count;
				}
			}
		}
	}
}