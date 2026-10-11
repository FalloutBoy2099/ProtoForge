namespace ProtoForge.Core.Vfs
{
	using System;
	using System.Collections.Generic;

	/// <summary>
	/// Virtual game paths are relative, lower-case and use forward slashes (<c>proto/items/items.lst</c>).
	/// Fallout resolves files case-insensitively and accepts both separators, so every lookup goes through <see cref="Normalize"/>.
	/// </summary>
	public static class GamePath
	{
		public static String Normalize(String path)
		{
			if (String.IsNullOrEmpty(path))
			{
				return String.Empty;
			}

			Span<Char> buffer = path.Length <= 512 ? stackalloc Char[path.Length] : new Char[path.Length];
			Int32 length = 0;
			Boolean lastWasSeparator = true;
			foreach (Char c in path)
			{
				Char current = c == '\\' ? '/' : Char.ToLowerInvariant(c);
				if (current == '/')
				{
					if (lastWasSeparator)
					{
						continue;
					}

					lastWasSeparator = true;
				}
				else
				{
					lastWasSeparator = false;
				}

				buffer[length++] = current;
			}

			if (length > 0 && buffer[length - 1] == '/')
			{
				length--;
			}

			// "./foo" => "foo"
			ReadOnlySpan<Char> result = buffer[..length];
			if (result.StartsWith("./"))
			{
				result = result[2..];
			}

			return new String(result);
		}

		private static readonly HashSet<String> DeviceNames = new(StringComparer.Ordinal)
		{
			"con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
			"lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
		};

		/// <summary>True for a Windows device name (<c>CON</c>, <c>NUL</c>, <c>COM1</c>...), with or without an extension.</summary>
		public static Boolean IsDeviceName(String name)
		{
			Int32 dot = name.IndexOf('.', StringComparison.Ordinal);
			return DeviceNames.Contains((dot < 0 ? name : name[..dot]).ToLowerInvariant());
		}

		/// <summary>
		/// True when the path can safely be joined to a folder on disk: no <c>..</c> or <c>.</c> segment, no drive or stream
		/// separator, no wildcard or control character, no Windows device name. Paths found in LST files and DAT archives are untrusted.
		/// </summary>
		public static Boolean IsSafe(String? path)
		{
			if (String.IsNullOrWhiteSpace(path))
			{
				return false;
			}

			foreach (Char c in path)
			{
				if (c < ' ' || c is ':' or '*' or '?' or '"' or '<' or '>' or '|')
				{
					return false;
				}
			}

			foreach (String segment in Normalize(path).Split('/'))
			{
				if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.'))
				{
					return false;
				}

				Int32 dot = segment.IndexOf('.', StringComparison.Ordinal);
				if (DeviceNames.Contains(dot < 0 ? segment : segment[..dot]))
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>The full path of a virtual path below a root folder, or null when it is not safe or would leave the root.</summary>
		public static String? ResolveInside(String root, String virtualPath)
		{
			if (!IsSafe(virtualPath))
			{
				return null;
			}

			String rootFull = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
			String full = System.IO.Path.GetFullPath(System.IO.Path.Combine(rootFull, virtualPath.Replace('/', System.IO.Path.DirectorySeparatorChar)));
			return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full : null;
		}

		public static String Combine(String directory, String name)
		{
			return Normalize(directory + "/" + name);
		}

		public static String GetDirectory(String normalizedPath)
		{
			Int32 index = normalizedPath.LastIndexOf('/');
			return index < 0 ? String.Empty : normalizedPath[..index];
		}

		public static String GetFileName(String normalizedPath)
		{
			Int32 index = normalizedPath.LastIndexOf('/');
			return index < 0 ? normalizedPath : normalizedPath[(index + 1)..];
		}

		public static String GetExtension(String normalizedPath)
		{
			String name = GetFileName(normalizedPath);
			Int32 index = name.LastIndexOf('.');
			return index < 0 ? String.Empty : name[index..];
		}

		public static String ChangeExtension(String normalizedPath, String extension)
		{
			Int32 slash = normalizedPath.LastIndexOf('/');
			Int32 dot = normalizedPath.LastIndexOf('.');
			String withoutExtension = dot > slash ? normalizedPath[..dot] : normalizedPath;
			return withoutExtension + extension;
		}
	}
}