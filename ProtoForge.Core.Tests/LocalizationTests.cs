namespace ProtoForge.Core.Tests
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text.Json;
	using System.Text.RegularExpressions;
	using Xunit;

	/// <summary>Guards the UI translations: both languages define the same keys and every key used in code or XAML exists.</summary>
	public partial class LocalizationTests
	{
		private static readonly String[] DynamicPrefixes = ["category.", "issue.", "kind.", "layer.role.", "template.", "theme.", "tip.", "type.", "choice.", "ai.group.", "ai.param.", "field."];

		private static String FindUiFolder()
		{
			String? current = AppContext.BaseDirectory;
			while (current is not null && !File.Exists(Path.Combine(current, "ProtoForge.slnx")))
			{
				current = Path.GetDirectoryName(current);
			}

			return Path.Combine(current ?? throw new DirectoryNotFoundException("Solution root not found."), "ProtoForge.UI");
		}

		private static Dictionary<String, String> Load(String language)
		{
			String path = Path.Combine(FindUiFolder(), "Resources", "Strings", $"Strings.{language}.json");
			return JsonSerializer.Deserialize<Dictionary<String, String>>(File.ReadAllText(path))!;
		}

		public static TheoryData<String> Languages =>
		[
			"cs", "de", "es", "fr", "it", "pl", "pt", "ru", "sv", "zh",
		];

		[Theory]
		[MemberData(nameof(Languages))]
		public void EveryLanguage_DefinesTheSameKeysAsEnglish(String language)
		{
			Dictionary<String, String> en = Load("en");
			Dictionary<String, String> translation = Load(language);

			// Translations of the built-in lists (choice.*) are optional: English comes from the core defaults.
			List<String> missing = [.. en.Keys.Except(translation.Keys).Order()];
			List<String> extra = [.. translation.Keys.Except(en.Keys).Where(k => !k.StartsWith("choice.", StringComparison.Ordinal)).Order()];
			Assert.True(missing.Count == 0, $"{language} misses: " + String.Join(", ", missing));
			Assert.True(extra.Count == 0, $"{language} has unknown keys: " + String.Join(", ", extra));

			foreach (KeyValuePair<String, String> pair in en)
			{
				Assert.False(String.IsNullOrWhiteSpace(translation[pair.Key]), $"{language}: {pair.Key} is empty");
				Assert.Equal(Placeholders(pair.Value), Placeholders(translation[pair.Key]));
			}
		}

		private static String Placeholders(String text)
		{
			return String.Join(",", PlaceholderPattern().Matches(text).Select(m => m.Value).Order());
		}

		[GeneratedRegex("\\{\\d+\\}")]
		private static partial Regex PlaceholderPattern();

		[Fact]
		public void EveryKeyUsedByTheUi_IsDefined()
		{
			HashSet<String> en = [.. Load("en").Keys];
			HashSet<String> used = [];
			String root = Path.GetDirectoryName(FindUiFolder())!;
			foreach (String file in new[] { "ProtoForge.UI", "ProtoForge.Infrastructure", "ProtoForge.Core" }
				.SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, project), "*.*", SearchOption.AllDirectories))
				.Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".xaml", StringComparison.Ordinal))
					&& !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
					&& !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
					&& !f.EndsWith("Loc.cs", StringComparison.Ordinal)
					&& !f.EndsWith("UserTexts.cs", StringComparison.Ordinal)))
			{
				String text = File.ReadAllText(file);
				foreach (Match match in CodeKey().Matches(text))
				{
					used.Add(match.Groups[1].Value);
				}

				foreach (Match match in XamlKey().Matches(text))
				{
					used.Add(match.Groups[1].Value);
				}
			}

			List<String> missing = [.. used.Where(k => !en.Contains(k) && !DynamicPrefixes.Any(p => k == p || k.StartsWith(p, StringComparison.Ordinal) && k.EndsWith('.'))).Order()];
			Assert.True(missing.Count == 0, "Undefined keys: " + String.Join(", ", missing));
		}

		[GeneratedRegex("(?:Loc\\.(?:Get|Format|Has)|UserTexts\\.Format)\\(\"([^\"]+)\"")]
		private static partial Regex CodeKey();

		[GeneratedRegex("\\{loc:Loc ([A-Za-z0-9_.]+)\\}")]
		private static partial Regex XamlKey();
	}
}