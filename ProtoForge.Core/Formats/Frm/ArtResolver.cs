namespace ProtoForge.Core.Formats.Frm
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Lst;
	using Pro;
	using Vfs;

	/// <summary>
	/// Turns frame IDs into art file paths using the <c>art/&lt;type&gt;/&lt;type&gt;.lst</c> lists of the game.
	/// </summary>
	public sealed class ArtResolver
	{
		private static readonly String[] PortraitSuffixes = ["nf1", "gf1", "bf1", "nf2", "gf2", "bf2", "nn", "gn", "bn", "np", "gp", "bp"];

		private readonly IGameFileSystem _files;
		private readonly Dictionary<ArtType, LstFile?> _lists = [];

		public ArtResolver(IGameFileSystem files)
		{
			_files = files;
		}

		public static String GetListPath(ArtType type)
		{
			String folder = FrameId.GetFolderName(type);
			return $"art/{folder}/{folder}.lst";
		}

		public LstFile? GetList(ArtType type)
		{
			lock (_lists)
			{
				if (!_lists.TryGetValue(type, out LstFile? list))
				{
					Byte[]? bytes = _files.ReadAllBytes(GetListPath(type));
					list = bytes is null ? null : LstFile.Parse(bytes);
					_lists[type] = list;
				}

				return list;
			}
		}

		/// <summary>Entry names (without arguments) for pickers.</summary>
		public IReadOnlyList<String> GetNames(ArtType type)
		{
			LstFile? list = GetList(type);
			return list is null ? [] : [.. Enumerable.Range(0, list.Count).Select(list.GetName)];
		}

		public void Invalidate()
		{
			lock (_lists)
			{
				_lists.Clear();
			}
		}

		/// <summary>Virtual path of the FRM file for a frame ID, or null when the ID cannot be resolved.</summary>
		public String? ResolvePath(FrameId fid)
		{
			if (fid.IsNone)
			{
				return null;
			}

			ArtType type = fid.Type;
			if (type == ArtType.Heads)
			{
				return ResolvePortraitPath(fid);
			}

			LstFile? list = GetList(type);
			String? name = list?.TryGetName(fid.Index);
			if (String.IsNullOrEmpty(name))
			{
				return null;
			}

			String folder = FrameId.GetFolderName(type);
			if (type == ArtType.Critter)
			{
				String standing = $"art/{folder}/{name}{(Char)('a' + fid.Weapon)}{(Char)('a' + Math.Min(25, fid.Animation))}.frm";
				if (_files.Exists(standing))
				{
					return standing;
				}

				// Fall back to the plain "standing, unarmed" animation, the most common preview.
				return $"art/{folder}/{name}aa.frm";
			}

			if (!name.Contains('.', StringComparison.Ordinal))
			{
				name += ".frm";
			}

			return $"art/{folder}/{name}";
		}

		public Boolean Exists(FrameId fid)
		{
			String? path = ResolvePath(fid);
			return path is not null && _files.Exists(path);
		}

		/// <summary>Best available portrait of a head FID (neutral fidget first).</summary>
		public String? ResolvePortraitPath(FrameId fid)
		{
			String? name = GetList(ArtType.Heads)?.TryGetName(fid.Index);
			if (String.IsNullOrEmpty(name))
			{
				return null;
			}

			IReadOnlyList<String> files = _files.Enumerate("art/heads");
			foreach (String suffix in PortraitSuffixes)
			{
				String candidate = $"art/heads/{name}{suffix}.frm";
				if (files.Contains(candidate, StringComparer.Ordinal))
				{
					return candidate;
				}
			}

			String prefix = $"art/heads/{name}";
			return files.FirstOrDefault(f => f.StartsWith(prefix, StringComparison.Ordinal));
		}
	}
}