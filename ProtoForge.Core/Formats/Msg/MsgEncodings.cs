namespace ProtoForge.Core.Formats.Msg
{
	using System;
	using System.Collections.Generic;
	using System.Text;

	/// <summary>
	/// MSG files are single-byte text whose code page depends on the game language. Only the resolution of a language to a
	/// code page lives here; the user can always override it in the settings.
	/// </summary>
	public static class MsgEncodings
	{
		/// <summary>Pseudo code page number of the LevCorp variant of DOS 866 (see <see cref="LevCorpEncoding"/>).</summary>
		public const Int32 LevCorp = -866;

		public static readonly IReadOnlyList<KeyValuePair<Int32, String>> Common =
		[
			new(1252, "Western European (Windows-1252)"),
			new(1250, "Central European (Windows-1250)"),
			new(1251, "Cyrillic (Windows-1251)"),
			new(866, "Cyrillic (DOS 866)"),
			new(LevCorp, "Cyrillic (LevCorp, DOS 866 variant)"),
			new(1253, "Greek (Windows-1253)"),
			new(1254, "Turkish (Windows-1254)"),
			new(936, "Chinese Simplified (GBK)"),
			new(950, "Chinese Traditional (Big5)"),
			new(932, "Japanese (Shift-JIS)"),
			new(949, "Korean (EUC-KR)"),
		];

		static MsgEncodings()
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		}

		public static Encoding Get(Int32 codePage)
		{
			return codePage == LevCorp ? new LevCorpEncoding() : Encoding.GetEncoding(codePage);
		}

		public static Int32 GetDefaultCodePage(String language)
		{
			switch ((language ?? String.Empty).Trim().ToLowerInvariant())
			{
				case "polish":
				case "czech":
				case "hungarian":
				case "slovak":
				case "romanian":
					return 1250;
				case "russian":
				case "ukrainian":
				case "bulgarian":
					return 1251;
				case "greek":
					return 1253;
				case "turkish":
					return 1254;
				case "chs":
				case "chinese":
					return 936;
				case "cht":
					return 950;
				case "japanese":
					return 932;
				case "korean":
					return 949;
				default:
					return 1252;
			}
		}

		public static Encoding ForLanguage(String language)
		{
			return Get(GetDefaultCodePage(language));
		}
	}
}