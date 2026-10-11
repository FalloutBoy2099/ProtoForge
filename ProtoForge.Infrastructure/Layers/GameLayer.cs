namespace ProtoForge.Infrastructure.Layers
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using ProtoForge.Core.Formats.Dat;
	using ProtoForge.Core.Vfs;

	/// <summary>One source of files: a physical folder or a DAT archive.</summary>
	public abstract class GameLayer : IDisposable
	{
		protected GameLayer(LayerInfo info)
		{
			Info = info;
		}

		public LayerInfo Info { get; }

		public abstract Boolean TryGetSize(String path, out Int64 size);

		public abstract Byte[]? Read(String path);

		/// <summary>True when the file exists on disk with the read-only attribute (always false inside a DAT archive).</summary>
		public virtual Boolean IsReadOnly(String path)
		{
			return false;
		}

		/// <summary>Virtual paths (normalised) of the files directly inside a directory.</summary>
		public abstract IReadOnlyList<String> Enumerate(String directory);

		public virtual void Dispose()
		{
		}
	}

	/// <summary>A folder on disk (the <c>data</c> folder, a loose mod, the output folder, or a DAT that was extracted).</summary>
	public sealed class DirectoryLayer : GameLayer
	{
		private readonly String _root;

		public DirectoryLayer(LayerInfo info, String root)
			: base(info)
		{
			_root = root;
		}

		public String Root
		{
			get { return _root; }
		}

		public override Boolean TryGetSize(String path, out Int64 size)
		{
			String? physical = ToPhysical(path);
			FileInfo? file = physical is null ? null : new FileInfo(physical);
			if (file is { Exists: true })
			{
				size = file.Length;
				return true;
			}

			size = 0;
			return false;
		}

		public override Boolean IsReadOnly(String path)
		{
			try
			{
				String? physical = ToPhysical(path);
				return physical is not null && new FileInfo(physical) is { Exists: true, IsReadOnly: true };
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return false;
			}
		}

		public override Byte[]? Read(String path)
		{
			String? physical = ToPhysical(path);
			if (physical is null)
			{
				return null;
			}

			try
			{
				using FileStream stream = new(physical, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
				Byte[] data = new Byte[stream.Length];
				stream.ReadExactly(data);
				return data;
			}
			catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
			{
				return null;
			}
		}

		public override IReadOnlyList<String> Enumerate(String directory)
		{
			String? physical = ToPhysical(directory);
			if (physical is null || !Directory.Exists(physical))
			{
				return [];
			}

			return [.. Directory.EnumerateFiles(physical)
				.Where(f => !f.EndsWith(".protoforge-tmp", StringComparison.OrdinalIgnoreCase))
				.Select(f => GamePath.Combine(directory, Path.GetFileName(f)))];
		}

		/// <summary>The path on disk, or null when the virtual path is not safe (an LST line or DAT entry must not reach outside the folder).</summary>
		private String? ToPhysical(String virtualPath)
		{
			return virtualPath.Length == 0 ? _root : GamePath.ResolveInside(_root, virtualPath);
		}
	}

	/// <summary>A DAT archive; the directory tree is indexed lazily the first time a directory is enumerated.</summary>
	public sealed class DatLayer : GameLayer
	{
		private readonly DatArchive _archive;
		private Dictionary<String, List<String>>? _directories;

		public DatLayer(LayerInfo info, DatArchive archive)
			: base(info)
		{
			_archive = archive;
		}

		public DatArchive Archive
		{
			get { return _archive; }
		}

		public override Boolean TryGetSize(String path, out Int64 size)
		{
			DatEntry? entry = _archive.Find(path);
			size = entry?.UnpackedSize ?? 0;
			return entry is not null;
		}

		public override Byte[]? Read(String path)
		{
			try
			{
				return _archive.Read(path);
			}
			catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
			{
				// A damaged entry is reported as unreadable by the caller instead of failing the whole load.
				return null;
			}
		}

		public override IReadOnlyList<String> Enumerate(String directory)
		{
			Dictionary<String, List<String>> index = _directories ??= BuildDirectoryIndex();
			return index.TryGetValue(GamePath.Normalize(directory), out List<String>? files) ? files : [];
		}

		public override void Dispose()
		{
			_archive.Dispose();
		}

		private Dictionary<String, List<String>> BuildDirectoryIndex()
		{
			Dictionary<String, List<String>> index = new(StringComparer.Ordinal);
			foreach (DatEntry entry in _archive.Entries)
			{
				String directory = GamePath.GetDirectory(entry.Path);
				if (!index.TryGetValue(directory, out List<String>? list))
				{
					list = [];
					index[directory] = list;
				}

				list.Add(entry.Path);
			}

			return index;
		}
	}
}