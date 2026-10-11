namespace ProtoForge.Core.Formats.Ai
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;

	/// <summary>One <c>[Name]</c> block of an AI file with its raw lines (everything after the header, comments and blank lines included).</summary>
	public sealed class AiSection
	{
		public AiSection(String name, IEnumerable<String> lines)
		{
			Name = name;
			Lines = [.. lines];
		}

		public String Name { get; }

		public List<String> Lines { get; }

		/// <summary>The value of a key (the last one when it is repeated), or null.</summary>
		public String? GetValue(String key)
		{
			String? result = null;
			foreach (String line in Lines)
			{
				if (TryParseLine(line, out String lineKey, out String value) && String.Equals(lineKey, key, StringComparison.OrdinalIgnoreCase))
				{
					result = value;
				}
			}

			return result;
		}

		/// <summary>The key/value lines in file order (comments and blank lines are skipped).</summary>
		public IReadOnlyList<KeyValuePair<String, String>> GetProperties()
		{
			List<KeyValuePair<String, String>> properties = [];
			foreach (String line in Lines)
			{
				if (TryParseLine(line, out String key, out String value))
				{
					properties.Add(new KeyValuePair<String, String>(key, value));
				}
			}

			return properties;
		}

		internal static Boolean TryParseLine(String line, out String key, out String value)
		{
			key = String.Empty;
			value = String.Empty;
			String trimmed = line.Trim();
			if (trimmed.Length == 0 || trimmed[0] is ';' or '#' or '[')
			{
				return false;
			}

			Int32 equals = trimmed.IndexOf('=', StringComparison.Ordinal);
			if (equals <= 0)
			{
				return false;
			}

			key = trimmed[..equals].Trim();
			value = trimmed[(equals + 1)..].Trim();
			return true;
		}
	}

	/// <summary>
	/// An editable <c>AI.txt</c> (combat AI packets). The file is kept as lines so that everything the user did not touch
	/// (order, comments, line endings, unknown keys) is written back exactly as it was read.
	/// </summary>
	public sealed class AiFile
	{
		private readonly List<String> _preamble = [];
		private readonly List<AiSection> _sections = [];
		private String _newLine = "\r\n";
		private Boolean _endsWithNewLine = true;
		private String? _cachedText;
		private String _acceptedText = String.Empty;

		public event EventHandler? Changed;

		public IReadOnlyList<AiSection> Sections
		{
			get { return _sections; }
		}

		/// <summary>True when the content differs from what was loaded or last saved.</summary>
		public Boolean IsDirty
		{
			get { return !String.Equals(ToText(), _acceptedText, StringComparison.Ordinal); }
		}

		public static AiFile Parse(Byte[] bytes)
		{
			return Parse(Encoding.Latin1.GetString(bytes));
		}

		public static AiFile Parse(String text)
		{
			AiFile file = new();
			file._newLine = text.Contains("\r\n", StringComparison.Ordinal) || !text.Contains('\n', StringComparison.Ordinal) ? "\r\n" : "\n";
			file._endsWithNewLine = text.Length == 0 || text.EndsWith('\n');

			String[] lines = text.Split('\n');
			Int32 count = file._endsWithNewLine && lines.Length > 0 ? lines.Length - 1 : lines.Length;
			AiSection? current = null;
			for (Int32 i = 0; i < count; i++)
			{
				String line = lines[i].TrimEnd('\r');
				String trimmed = line.TrimStart();
				if (trimmed.StartsWith('[') && trimmed.Contains(']', StringComparison.Ordinal))
				{
					Int32 close = trimmed.IndexOf(']', StringComparison.Ordinal);
					current = new AiSection(trimmed[1..close].Trim(), []);
					file._sections.Add(current);
				}
				else if (current is null)
				{
					file._preamble.Add(line);
				}
				else
				{
					current.Lines.Add(line);
				}
			}

			file._acceptedText = file.ToText();
			return file;
		}

		public static AiFile CreateEmpty()
		{
			return Parse(String.Empty);
		}

		/// <summary>The lines of a new packet with sensible default values (the template of the original Proto Manager).</summary>
		public static IReadOnlyList<String> CreateDefaultPacketLines(Int32 packetNumber)
		{
			System.Reflection.Assembly assembly = typeof(AiFile).Assembly;
			String resource = assembly.GetManifestResourceNames().First(n => n.EndsWith("defaultAI.txt", StringComparison.OrdinalIgnoreCase));
			using System.IO.Stream stream = assembly.GetManifestResourceStream(resource)!;
			using System.IO.StreamReader reader = new(stream, Encoding.Latin1);
			List<String> lines = [];
			String? line;
			while ((line = reader.ReadLine()) is not null)
			{
				lines.Add(line.StartsWith("packet_num=", StringComparison.OrdinalIgnoreCase) ? "packet_num=" + packetNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) : line);
			}

			lines.Add(String.Empty);
			return lines;
		}

		public AiSection? Find(String name)
		{
			return _sections.FirstOrDefault(s => String.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
		}

		public String? GetValue(String section, String key)
		{
			return Find(section)?.GetValue(key);
		}

		/// <summary>The number of the packet (its <c>packet_num</c> value), or null.</summary>
		public Int32? GetPacketNumber(AiSection section)
		{
			return Int32.TryParse(section.GetValue("packet_num"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out Int32 number) ? number : null;
		}

		public Int32 NextPacketNumber()
		{
			Int32 max = 0;
			foreach (AiSection section in _sections)
			{
				if (GetPacketNumber(section) is { } number && number > max)
				{
					max = number;
				}
			}

			return max + 1;
		}

		/// <summary>Writes a value (replacing the line of the key or adding one at the end of the section). Null removes the key. Returns false when nothing changed.</summary>
		public Boolean SetValue(String section, String key, String? value)
		{
			AiSection? target = Find(section);
			if (target is null)
			{
				return false;
			}

			Int32 index = -1;
			for (Int32 i = 0; i < target.Lines.Count; i++)
			{
				if (AiSection.TryParseLine(target.Lines[i], out String lineKey, out _) && String.Equals(lineKey, key, StringComparison.OrdinalIgnoreCase))
				{
					index = i;
				}
			}

			if (value is null)
			{
				if (index < 0)
				{
					return false;
				}

				target.Lines.RemoveAt(index);
			}
			else if (index >= 0)
			{
				AiSection.TryParseLine(target.Lines[index], out String existingKey, out String existingValue);
				if (existingValue == value)
				{
					return false;
				}

				target.Lines[index] = existingKey + "=" + value;
			}
			else
			{
				Int32 insertAt = target.Lines.Count;
				while (insertAt > 0 && target.Lines[insertAt - 1].Trim().Length == 0)
				{
					insertAt--;
				}

				target.Lines.Insert(insertAt, key + "=" + value);
			}

			Touch();
			return true;
		}

		public AiSection AddSection(String name, IEnumerable<String> lines)
		{
			AiSection section = new(name, lines);
			if (_sections.Count > 0)
			{
				AiSection last = _sections[^1];
				if (last.Lines.Count == 0 || last.Lines[^1].Trim().Length != 0)
				{
					last.Lines.Add(String.Empty);
				}
			}

			_sections.Add(section);
			Touch();
			return section;
		}

		/// <summary>The number of lines of the last section (used to undo the separator line that adding a section appends to it).</summary>
		public Int32 LastSectionLineCount
		{
			get { return _sections.Count == 0 ? 0 : _sections[^1].Lines.Count; }
		}

		/// <summary>Drops the lines of the last section after the given count.</summary>
		public void TruncateLastSection(Int32 lineCount)
		{
			if (_sections.Count > 0 && _sections[^1].Lines.Count > lineCount)
			{
				_sections[^1].Lines.RemoveRange(lineCount, _sections[^1].Lines.Count - lineCount);
				Touch();
			}
		}

		public void InsertSection(Int32 index, AiSection section)
		{
			_sections.Insert(Math.Clamp(index, 0, _sections.Count), section);
			Touch();
		}

		/// <summary>Removes a section and returns it with its former position (for undo), or null when it does not exist.</summary>
		public (AiSection Section, Int32 Index)? RemoveSection(String name)
		{
			Int32 index = _sections.FindIndex(s => String.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
			if (index < 0)
			{
				return null;
			}

			AiSection removed = _sections[index];
			_sections.RemoveAt(index);
			Touch();
			return (removed, index);
		}

		/// <summary>The text of a section without its header, as shown in the raw text editor.</summary>
		public String GetRawText(String section)
		{
			AiSection? target = Find(section);
			if (target is null)
			{
				return String.Empty;
			}

			Int32 last = target.Lines.Count;
			while (last > 0 && target.Lines[last - 1].Trim().Length == 0)
			{
				last--;
			}

			return String.Join(Environment.NewLine, target.Lines.Take(last));
		}

		/// <summary>Replaces the lines of a section (the trailing blank line that separates sections is kept).</summary>
		public Boolean SetRawText(String section, String text)
		{
			AiSection? target = Find(section);
			if (target is null || String.Equals(text, GetRawText(section), StringComparison.Ordinal))
			{
				return false;
			}

			Boolean separated = target.Lines.Count > 0 && target.Lines[^1].Trim().Length == 0;
			target.Lines.Clear();
			target.Lines.AddRange(text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.TrimEnd('\r')));
			if (separated || _sections.IndexOf(target) < _sections.Count - 1)
			{
				while (target.Lines.Count > 0 && target.Lines[^1].Trim().Length == 0)
				{
					target.Lines.RemoveAt(target.Lines.Count - 1);
				}

				target.Lines.Add(String.Empty);
			}

			Touch();
			return true;
		}

		public String ToText()
		{
			if (_cachedText is not null)
			{
				return _cachedText;
			}

			StringBuilder builder = new();
			List<String> lines = [.. _preamble];
			foreach (AiSection section in _sections)
			{
				lines.Add("[" + section.Name + "]");
				lines.AddRange(section.Lines);
			}

			for (Int32 i = 0; i < lines.Count; i++)
			{
				builder.Append(lines[i]);
				if (i < lines.Count - 1 || _endsWithNewLine)
				{
					builder.Append(_newLine);
				}
			}

			_cachedText = builder.ToString();
			return _cachedText;
		}

		public Byte[] ToBytes()
		{
			return Encoding.Latin1.GetBytes(ToText());
		}

		/// <summary>The current content becomes the reference state (after a successful save).</summary>
		public void AcceptChanges()
		{
			_acceptedText = ToText();
		}

		public AiPacketCatalog ToCatalog()
		{
			return AiPacketCatalog.Parse(ToBytes());
		}

		private void Touch()
		{
			_cachedText = null;
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}
}