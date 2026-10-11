namespace ProtoForge.UI.Services
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.IO;
	using System.Reflection;
	using System.Linq;
	using System.Text;
	using System.Text.Json;
	using System.Text.RegularExpressions;
	using System.Windows.Markup;

	/// <summary>
	/// Minimal localisation: flat JSON dictionaries embedded in the assembly. English is the fallback; a missing key
	/// falls back to a readable version of the key itself, so a forgotten translation never shows an empty label.
	/// </summary>
	public static partial class Loc
	{
		private static Dictionary<String, String> _current = [];
		private static Dictionary<String, String> _fallback = [];

		public static String LanguageCode { get; private set; } = "en";

		public static IReadOnlyList<KeyValuePair<String, String>> AvailableLanguages { get; } =
		[
			new("en", "English"),
			new("de", "Deutsch"),
			new("es", "Español"),
			new("fr", "Français"),
			new("it", "Italiano"),
			new("pt", "Português"),
			new("ru", "Русский"),
			new("sv", "Svenska"),
			new("pl", "Polski"),
			new("cs", "Čeština"),
			new("zh", "简体中文"),
		];

		/// <summary>Loads the dictionaries. <paramref name="languageCode"/> empty means "follow the operating system".</summary>
		public static void Initialize(String languageCode)
		{
			String code = String.IsNullOrWhiteSpace(languageCode)
				? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
				: languageCode;
			if (!AvailableLanguages.Any(l => l.Key == code))
			{
				code = "en";
			}

			LanguageCode = code;
			_fallback = Load("en");
			_current = code == "en" ? _fallback : Load(code);
			CultureInfo culture = new(code);
			CultureInfo.CurrentUICulture = culture;
			CultureInfo.DefaultThreadCurrentUICulture = culture;
		}

		public static String Get(String key)
		{
			if (_current.TryGetValue(key, out String? value) || _fallback.TryGetValue(key, out value))
			{
				return value;
			}

			return Humanize(key[(key.LastIndexOf('.') + 1)..]);
		}

		public static String Format(String key, params Object[] arguments)
		{
			return String.Format(CultureInfo.CurrentCulture, Get(key), arguments);
		}

		public static Boolean Has(String key)
		{
			return _current.ContainsKey(key) || _fallback.ContainsKey(key);
		}

		public static String Field(String key)
		{
			return Has("field." + key) ? Get("field." + key) : Humanize(key);
		}

		public static String Group(String key)
		{
			return Has("group." + key) ? Get("group." + key) : Humanize(key);
		}

		public static String Flag(String key)
		{
			return Has("flag." + key) ? Get("flag." + key) : Humanize(key);
		}

		/// <summary><c>MaxRangePrimary</c> becomes <c>Max range primary</c>; acronyms (<c>EMP</c>) are kept.</summary>
		public static String Humanize(String identifier)
		{
			String[] words = WordBoundary().Split(identifier.Replace('_', ' '));
			StringBuilder builder = new(identifier.Length + 4);
			foreach (String raw in words)
			{
				String word = raw.Trim();
				if (word.Length == 0)
				{
					continue;
				}

				Boolean acronym = word.Length > 1 && word.All(Char.IsUpper);
				if (builder.Length > 0)
				{
					builder.Append(' ');
				}

				builder.Append(acronym || builder.Length == 0 ? word : Char.ToLowerInvariant(word[0]) + word[1..]);
			}

			return builder.Length == 0 ? identifier : Char.ToUpperInvariant(builder[0]) + builder.ToString(1, builder.Length - 1);
		}

		[GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|\s+")]
		private static partial Regex WordBoundary();

		private static Dictionary<String, String> Load(String code)
		{
			Assembly assembly = typeof(Loc).Assembly;
			String? resource = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith($"Strings.{code}.json", StringComparison.OrdinalIgnoreCase));
			if (resource is null)
			{
				return [];
			}

			using Stream stream = assembly.GetManifestResourceStream(resource)!;
			return JsonSerializer.Deserialize<Dictionary<String, String>>(stream) ?? [];
		}
	}

	/// <summary><c>{loc:Loc menu.open}</c> in XAML.</summary>
	[MarkupExtensionReturnType(typeof(String))]
	public sealed class LocExtension : MarkupExtension
	{
		public LocExtension()
		{
			Key = String.Empty;
		}

		public LocExtension(String key)
		{
			Key = key;
		}

		[ConstructorArgument("key")]
		public String Key { get; set; }

		public override Object ProvideValue(IServiceProvider serviceProvider)
		{
			return Loc.Get(Key);
		}
	}
}