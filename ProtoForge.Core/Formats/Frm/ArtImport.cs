namespace ProtoForge.Core.Formats.Frm
{
	using System;
	using System.IO;
	using System.Text.RegularExpressions;

	/// <summary>Prepares a picture the user brings (a FRM, or a BMP to convert) to be added to the art of the game.</summary>
	public static partial class ArtImport
	{
		/// <summary>The longest base name the art lists of the game accept (without the extension).</summary>
		public const Int32 MaxNameLength = 20;

		/// <summary>True for the file types that can be imported.</summary>
		public static Boolean IsSupported(String path)
		{
			String extension = Path.GetExtension(path);
			return extension.Equals(".frm", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>The FRM bytes of a file: a FRM is checked and kept, a BMP is converted with the palette of the game.</summary>
		public static Byte[] ToFrm(String path, Byte[] data, Palette palette)
		{
			if (Path.GetExtension(path).Equals(".bmp", StringComparison.OrdinalIgnoreCase))
			{
				return FrmWriter.WriteStatic(FrmConverter.ToFrame(BmpFile.Read(data), palette));
			}

			FrmFile.Parse(data);
			return data;
		}

		/// <summary>The name the picture gets in the game: lower case, letters, digits, '_' and '-' only, at most 20 characters, <c>.frm</c>.</summary>
		public static String GetGameName(String path)
		{
			String stem = InvalidCharacters().Replace(Path.GetFileNameWithoutExtension(path).ToLowerInvariant(), "_");
			stem = stem.Length == 0 ? "image" : stem.Length > MaxNameLength ? stem[..MaxNameLength] : stem;
			return stem + ".frm";
		}

		[GeneratedRegex("[^a-z0-9_\\-]", RegexOptions.CultureInvariant)]
		private static partial Regex InvalidCharacters();
	}
}