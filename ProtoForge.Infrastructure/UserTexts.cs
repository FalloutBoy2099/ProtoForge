namespace ProtoForge.Infrastructure
{
	using System;
	using System.Globalization;

	/// <summary>
	/// The messages the infrastructure shows to the user (warnings, progress). The English template is the fallback; the UI plugs in a
	/// translator so that the same message appears in the language of the interface.
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
	}
}