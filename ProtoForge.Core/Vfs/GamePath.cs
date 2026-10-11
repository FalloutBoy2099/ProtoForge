namespace ProtoForge.Core.Vfs
{
	using System;

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