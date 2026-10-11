namespace ProtoForge.Core.Formats.Ai
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Text;

	/// <summary>
	/// Minimal tolerant INI reader (fallout2.cfg, ddraw.ini, AI.txt, ...): case-insensitive, <c>;</c> and <c>#</c> comments,
	/// duplicate sections merged. Sections keep their declaration order.
	/// </summary>
	public sealed class IniFile
	{
		private readonly Dictionary<String, Dictionary<String, String>> _sections = new(StringComparer.OrdinalIgnoreCase);
		private readonly List<String> _sectionOrder = [];

		public IReadOnlyList<String> SectionNames
		{
			get { return _sectionOrder; }
		}

		public static IniFile Parse(Byte[] bytes)
		{
			return Parse(Encoding.Latin1.GetString(bytes));
		}

		public static IniFile Parse(String text)
		{
			IniFile ini = new();
			Dictionary<String, String>? current = null;
			foreach (String rawLine in text.Split('\n'))
			{
				String line = rawLine.Trim();
				if (line.Length == 0 || line[0] == ';' || line[0] == '#')
				{
					continue;
				}

				if (line[0] == '[')
				{
					Int32 close = line.IndexOf(']', StringComparison.Ordinal);
					if (close > 0)
					{
						current = ini.GetOrCreateSection(line[1..close].Trim());
					}

					continue;
				}

				Int32 equals = line.IndexOf('=', StringComparison.Ordinal);
				if (equals <= 0 || current is null)
				{
					continue;
				}

				String key = line[..equals].Trim();
				String value = line[(equals + 1)..];
				Int32 comment = value.IndexOf(';', StringComparison.Ordinal);
				if (comment >= 0)
				{
					value = value[..comment];
				}

				current[key] = value.Trim();
			}

			return ini;
		}

		public Boolean HasSection(String section)
		{
			return _sections.ContainsKey(section);
		}

		public IReadOnlyDictionary<String, String> GetSection(String section)
		{
			return _sections.TryGetValue(section, out Dictionary<String, String>? values)
				? values
				: new Dictionary<String, String>();
		}

		public String? GetString(String section, String key)
		{
			return _sections.TryGetValue(section, out Dictionary<String, String>? values) && values.TryGetValue(key, out String? value)
				? value
				: null;
		}

		public String GetString(String section, String key, String fallback)
		{
			return GetString(section, key) ?? fallback;
		}

		public Int32? GetInt32(String section, String key)
		{
			String? value = GetString(section, key);
			return value is not null && Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out Int32 number)
				? number
				: null;
		}

		private Dictionary<String, String> GetOrCreateSection(String name)
		{
			if (!_sections.TryGetValue(name, out Dictionary<String, String>? section))
			{
				section = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
				_sections[name] = section;
				_sectionOrder.Add(name);
			}

			return section;
		}
	}
}