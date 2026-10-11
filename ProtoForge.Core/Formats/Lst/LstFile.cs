namespace ProtoForge.Core.Formats.Lst
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;

	/// <summary>
	/// A Fallout LST file: one entry per line. Prototype lists contain file names; art lists may carry a trailing
	/// <c>,N</c> argument and script lists a <c>; comment</c>. The raw lines are kept so that saving never loses information.
	/// </summary>
	public sealed class LstFile
	{
		private readonly List<String> _lines;

		public LstFile()
		{
			_lines = [];
		}

		public LstFile(IEnumerable<String> lines)
		{
			_lines = [.. lines];
			while (_lines.Count > 0 && String.IsNullOrWhiteSpace(_lines[^1]))
			{
				_lines.RemoveAt(_lines.Count - 1);
			}
		}

		public Int32 Count
		{
			get { return _lines.Count; }
		}

		public IReadOnlyList<String> Lines
		{
			get { return _lines; }
		}

		public static LstFile Parse(Byte[] bytes)
		{
			String text = Encoding.Latin1.GetString(bytes);
			return new LstFile(text.Split('\n').Select(l => l.TrimEnd('\r')));
		}

		/// <summary>Entry name without arguments or comments (<c>hmwarr,1</c> becomes <c>hmwarr</c>).</summary>
		public String GetName(Int32 index)
		{
			String line = _lines[index];
			Int32 cut = line.IndexOfAny([',', ';', '#']);
			return (cut >= 0 ? line[..cut] : line).Trim();
		}

		public String? TryGetName(Int32 index)
		{
			return index >= 0 && index < _lines.Count ? GetName(index) : null;
		}

		public Int32 IndexOf(String name)
		{
			for (Int32 i = 0; i < _lines.Count; i++)
			{
				if (String.Equals(GetName(i), name, StringComparison.OrdinalIgnoreCase))
				{
					return i;
				}
			}

			return -1;
		}

		public Int32 Add(String line)
		{
			_lines.Add(line);
			return _lines.Count - 1;
		}

		public void RemoveLast()
		{
			_lines.RemoveAt(_lines.Count - 1);
		}

		public void SetLine(Int32 index, String line)
		{
			_lines[index] = line;
		}

		public LstFile Clone()
		{
			return new LstFile(_lines);
		}

		public Byte[] ToBytes()
		{
			StringBuilder builder = new();
			foreach (String line in _lines)
			{
				builder.Append(line).Append("\r\n");
			}

			return Encoding.Latin1.GetBytes(builder.ToString());
		}
	}
}