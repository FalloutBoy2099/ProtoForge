namespace ProtoForge.Core.Reference
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.IO;
	using System.Reflection;
	using System.Text.RegularExpressions;

	/// <summary>The team numbers of the original game (from the <c>TEAMS.H</c> script header) used to label the critter team field.</summary>
	public static partial class TeamCatalog
	{
		public static IReadOnlyList<ChoiceOption> LoadDefault()
		{
			Assembly assembly = typeof(TeamCatalog).Assembly;
			String? resource = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith("teams.h", StringComparison.OrdinalIgnoreCase));
			if (resource is null)
			{
				return [new ChoiceOption(0, "Player")];
			}

			using Stream stream = assembly.GetManifestResourceStream(resource)!;
			using StreamReader reader = new(stream);
			return Parse(reader.ReadToEnd());
		}

		public static IReadOnlyList<ChoiceOption> Parse(String header)
		{
			SortedDictionary<Int32, String> byNumber = [];
			foreach (Match match in DefinePattern().Matches(header))
			{
				Int32 number = Int32.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
				String name = Humanize(match.Groups["name"].Value);
				byNumber.TryAdd(number, name);
			}

			List<ChoiceOption> options = [];
			foreach (KeyValuePair<Int32, String> pair in byNumber)
			{
				options.Add(new ChoiceOption(pair.Key, pair.Value));
			}

			return options;
		}

		private static String Humanize(String constant)
		{
			String[] parts = constant.Split('_', StringSplitOptions.RemoveEmptyEntries);
			for (Int32 i = 0; i < parts.Length; i++)
			{
				parts[i] = parts[i].Length > 1 ? parts[i][0] + parts[i][1..].ToLowerInvariant() : parts[i];
			}

			return String.Join(' ', parts);
		}

		[GeneratedRegex(@"#define\s+TEAM_(?<name>\w+)\s+\((?<number>\d+)\)", RegexOptions.CultureInvariant)]
		private static partial Regex DefinePattern();
	}
}