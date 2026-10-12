namespace ProtoForge.Core
{
	using System;
	using System.Globalization;
	using System.IO;

	/// <summary>
	/// The messages the program shows to the user and builds below the UI layer (warnings, progress, history entries, errors). The English
	/// template is the fallback; the UI plugs in a translator so that the same message appears in the language of the interface.
	/// </summary>
	public static class UserTexts
	{
		/// <summary>Returns the translated template of a key, or null when there is none.</summary>
		public static Func<String, String?>? Translator { get; set; }

		public static String Format(String key, String englishTemplate, params Object[] arguments)
		{
			String template = Translator?.Invoke(key) ?? englishTemplate;
			return String.Format(CultureInfo.CurrentCulture, template, arguments);
		}

		/// <summary>The text to show for an exception: a translated sentence for the usual failures, followed by the technical detail.</summary>
		public static String Describe(Exception exception)
		{
			return exception switch
			{
				InvalidDataException => Format("error.invalidData", "Invalid or damaged data: {0}", exception.Message),
				UnauthorizedAccessException => Format("error.access", "Access denied: {0}", exception.Message),
				IOException => Format("error.io", "File error: {0}", exception.Message),
				_ => exception.Message,
			};
		}
	}
}