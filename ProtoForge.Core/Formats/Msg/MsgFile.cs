namespace ProtoForge.Core.Formats.Msg
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Linq;
	using System.Text;

	/// <summary>One <c>{id}{audio}{text}</c> message.</summary>
	public sealed class MsgEntry
	{
		internal MsgEntry(Int32 id, String audio, String text, String raw, String trailing)
		{
			Id = id;
			Audio = audio;
			Text = text;
			Raw = raw;
			Trailing = trailing;
		}

		public Int32 Id { get; }

		public String Audio { get; internal set; }

		public String Text { get; internal set; }

		/// <summary>Original source of the entry (without the line end); null once the entry has been modified or created.</summary>
		internal String? Raw { get; set; }

		/// <summary>Everything after the closing brace up to and including the line end.</summary>
		internal String Trailing { get; set; }

		public Boolean IsModified
		{
			get { return Raw is null; }
		}
	}

	/// <summary>
	/// A Fallout MSG file. Comments, blank lines and untouched entries are written back byte for byte, so editing one string
	/// only changes that string in the saved file.
	/// </summary>
	public sealed class MsgFile
	{
		private sealed class Item
		{
			public Item(String raw, MsgEntry? entry)
			{
				RawText = raw;
				Entry = entry;
			}

			public String RawText { get; }

			public MsgEntry? Entry { get; }
		}

		private readonly Dictionary<Int32, (Boolean Existed, String Text, String Audio)> _originals = [];
		private readonly List<Item> _items = [];
		private readonly Dictionary<Int32, MsgEntry> _byId = [];
		private String _lineEnding = "\r\n";

		public MsgFile(Encoding encoding)
		{
			Encoding = encoding;
		}

		public Encoding Encoding { get; }

		/// <summary>True when at least one message differs from the state at load time (add + remove of the same message leaves the file clean).</summary>
		public Boolean IsDirty
		{
			get
			{
				foreach (KeyValuePair<Int32, (Boolean Existed, String Text, String Audio)> original in _originals)
				{
					MsgEntry? current = Find(original.Key);
					if (current is null != !original.Value.Existed)
					{
						return true;
					}

					if (current is not null && (current.Text != original.Value.Text || current.Audio != original.Value.Audio))
					{
						return true;
					}
				}

				return false;
			}
		}

		public event EventHandler? Changed;

		public IEnumerable<MsgEntry> Entries
		{
			get { return _items.Where(i => i.Entry is not null).Select(i => i.Entry!); }
		}

		public Int32 Count
		{
			get { return _byId.Count; }
		}

		public static MsgFile Parse(Byte[] bytes, Encoding encoding)
		{
			MsgFile file = new(encoding);
			file.ParseText(encoding.GetString(bytes));
			return file;
		}

		public MsgEntry? Find(Int32 id)
		{
			return _byId.TryGetValue(id, out MsgEntry? entry) ? entry : null;
		}

		public String? GetText(Int32 id)
		{
			return Find(id)?.Text;
		}

		/// <summary>Creates or updates a message, keeping ascending id order for new entries.</summary>
		public MsgEntry SetText(Int32 id, String text, String? audio = null)
		{
			text = Sanitize(text);
			Remember(id);
			if (_byId.TryGetValue(id, out MsgEntry? existing))
			{
				if (existing.Text == text && (audio is null || existing.Audio == audio))
				{
					return existing;
				}

				existing.Text = text;
				if (audio is not null)
				{
					existing.Audio = audio;
				}

				existing.Raw = null;
				MarkDirty();
				return existing;
			}

			MsgEntry entry = new(id, audio ?? String.Empty, text, String.Empty, _lineEnding) { Raw = null };
			Int32 insertAt = _items.Count;
			for (Int32 i = _items.Count - 1; i >= 0; i--)
			{
				MsgEntry? candidate = _items[i].Entry;
				if (candidate is not null && candidate.Id < id)
				{
					insertAt = i + 1;
					break;
				}

				if (candidate is not null && candidate.Id > id)
				{
					insertAt = i;
				}
			}

			_items.Insert(insertAt, new Item(String.Empty, entry));
			_byId[id] = entry;
			MarkDirty();
			return entry;
		}

		public Boolean Remove(Int32 id)
		{
			Remember(id);
			if (!_byId.Remove(id, out MsgEntry? entry))
			{
				return false;
			}

			_items.RemoveAll(i => ReferenceEquals(i.Entry, entry));
			MarkDirty();
			return true;
		}

		public void AcceptChanges()
		{
			_originals.Clear();
		}

		public Byte[] ToBytes()
		{
			return Encoding.GetBytes(ToText());
		}

		public String ToText()
		{
			StringBuilder builder = new();
			foreach (Item item in _items)
			{
				MsgEntry? entry = item.Entry;
				if (entry is null)
				{
					builder.Append(item.RawText);
				}
				else if (entry.Raw is not null)
				{
					builder.Append(entry.Raw).Append(entry.Trailing);
				}
				else
				{
					builder.Append('{').Append(entry.Id.ToString(CultureInfo.InvariantCulture)).Append("}{")
						.Append(entry.Audio).Append("}{").Append(entry.Text).Append('}').Append(entry.Trailing);
				}
			}

			return builder.ToString();
		}

		private static String Sanitize(String text)
		{
			return text.Replace('}', ')').Replace("{", "(", StringComparison.Ordinal);
		}

		private void MarkDirty()
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}

		private void Remember(Int32 id)
		{
			if (_originals.ContainsKey(id))
			{
				return;
			}

			MsgEntry? current = Find(id);
			_originals[id] = (current is not null, current?.Text ?? String.Empty, current?.Audio ?? String.Empty);
		}

		private void ParseText(String text)
		{
			if (text.Contains("\r\n", StringComparison.Ordinal))
			{
				_lineEnding = "\r\n";
			}
			else if (text.Contains('\n', StringComparison.Ordinal))
			{
				_lineEnding = "\n";
			}

			Int32 i = 0;
			while (i < text.Length)
			{
				Int32 lineStart = i;
				Int32 lineEnd = FindLineEnd(text, i);

				// skip blank space to check what the line starts with
				Int32 probe = i;
				while (probe < lineEnd && (text[probe] == ' ' || text[probe] == '\t'))
				{
					probe++;
				}

				if (probe < lineEnd && text[probe] == '{')
				{
					Int32 entryEnd = TryParseEntry(text, probe, out MsgEntry? entry, lineStart);
					if (entry is not null)
					{
						Int32 trailingEnd = FindLineEnd(text, entryEnd);
						entry.Trailing = text[entryEnd..trailingEnd];
						_items.Add(new Item(String.Empty, entry));
						_byId[entry.Id] = entry;
						i = trailingEnd;
						continue;
					}
				}

				_items.Add(new Item(text[lineStart..lineEnd], null));
				i = lineEnd;
			}
		}

		/// <summary>Index just after the line terminator (or the end of the text).</summary>
		private static Int32 FindLineEnd(String text, Int32 from)
		{
			Int32 newline = text.IndexOf('\n', from);
			return newline < 0 ? text.Length : newline + 1;
		}

		private static Int32 TryParseEntry(String text, Int32 start, out MsgEntry? entry, Int32 rawStart)
		{
			entry = null;
			Int32 position = start;
			if (!TryReadBraced(text, ref position, out String idText)
				|| !TryReadBraced(text, ref position, out String audio)
				|| !TryReadBraced(text, ref position, out String value))
			{
				return start;
			}

			if (!Int32.TryParse(idText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out Int32 id))
			{
				return start;
			}

			entry = new MsgEntry(id, audio, value, text[rawStart..position], String.Empty);
			return position;
		}

		private static Boolean TryReadBraced(String text, ref Int32 position, out String content)
		{
			content = String.Empty;
			Int32 p = position;
			while (p < text.Length && (text[p] == ' ' || text[p] == '\t'))
			{
				p++;
			}

			if (p >= text.Length || text[p] != '{')
			{
				return false;
			}

			Int32 close = text.IndexOf('}', p + 1);
			if (close < 0)
			{
				return false;
			}

			content = text.Substring(p + 1, close - p - 1);
			position = close + 1;
			return true;
		}
	}
}