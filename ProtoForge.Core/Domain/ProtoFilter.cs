namespace ProtoForge.Core.Domain
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using Formats.Pro;

	/// <summary>
	/// Search and filter criteria for the prototype list. The text query supports plain words (matched against name, description,
	/// file name and PID) and prefixed terms: <c>pid:1234</c>, <c>file:0012</c>, <c>kind:weapon</c>, <c>flag:Flat</c>,
	/// <c>text:rifle</c>, <c>source:rpu.dat</c>, <c>art:missing</c>, <c>script:acbrahmn</c> (a part of the script name, its number in scripts.lst, or <c>none</c>).
	/// </summary>
	public sealed class ProtoFilter
	{
		private readonly List<Func<ProtoEntry, Boolean>> _predicates = [];

		public ProtoFilter(String? query, IReadOnlyList<String>? scriptNames = null)
		{
			Query = query ?? String.Empty;
			foreach (String token in Tokenize(Query))
			{
				_predicates.Add(CreatePredicate(token, scriptNames ?? []));
			}
		}

		public String Query { get; }

		/// <summary>Only these object types (null/empty = all).</summary>
		public ISet<ProtoObjectType>? Types { get; init; }

		/// <summary>Only these kinds (null/empty = all).</summary>
		public ISet<ProtoKind>? Kinds { get; init; }

		/// <summary>Flags that must all be set in <c>Flags</c>.</summary>
		public Int32 RequiredFlags { get; init; }

		/// <summary>Flags that must all be clear in <c>Flags</c>.</summary>
		public Int32 ForbiddenFlags { get; init; }

		public Int32 RequiredExtFlags { get; init; }

		public Int32 ForbiddenExtFlags { get; init; }

		public Boolean ModifiedOnly { get; init; }

		/// <summary>true = only protos with valid art, false = only protos with missing art, null = any.</summary>
		public Boolean? HasArt { get; init; }

		public Func<ProtoEntry, Boolean?>? ArtChecker { get; init; }

		public Func<ProtoEntry, Boolean>? ErrorChecker { get; init; }

		public Boolean ErrorsOnly { get; init; }

		public String? SourceName { get; init; }

		public Boolean IsEmpty
		{
			get
			{
				return _predicates.Count == 0 && (Types is null || Types.Count == 0) && (Kinds is null || Kinds.Count == 0)
					&& RequiredFlags == 0 && ForbiddenFlags == 0 && RequiredExtFlags == 0 && ForbiddenExtFlags == 0
					&& !ModifiedOnly && HasArt is null && !ErrorsOnly && SourceName is null;
			}
		}

		public Boolean Matches(ProtoEntry entry)
		{
			if (Types is { Count: > 0 } && !Types.Contains(entry.Type))
			{
				return false;
			}

			if (Kinds is { Count: > 0 } && (entry.Kind is null || !Kinds.Contains(entry.Kind.Value)))
			{
				return false;
			}

			if (ModifiedOnly && !entry.IsModified)
			{
				return false;
			}

			if (SourceName is not null && !String.Equals(entry.SourceName, SourceName, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			if (RequiredFlags != 0 || ForbiddenFlags != 0 || RequiredExtFlags != 0 || ForbiddenExtFlags != 0)
			{
				ProtoFile? file = entry.File;
				if (file is null)
				{
					return false;
				}

				Int32 flags = file.Flags;
				Int32 ext = file.FlagsExt;
				if ((flags & RequiredFlags) != RequiredFlags || (flags & ForbiddenFlags) != 0
					|| (ext & RequiredExtFlags) != RequiredExtFlags || (ext & ForbiddenExtFlags) != 0)
				{
					return false;
				}
			}

			if (HasArt is not null && ArtChecker is not null)
			{
				Boolean? art = ArtChecker(entry);
				if (art is not null && art != HasArt)
				{
					return false;
				}
			}

			if (ErrorsOnly && ErrorChecker is not null && !ErrorChecker(entry))
			{
				return false;
			}

			foreach (Func<ProtoEntry, Boolean> predicate in _predicates)
			{
				if (!predicate(entry))
				{
					return false;
				}
			}

			return true;
		}

		private static IEnumerable<String> Tokenize(String query)
		{
			List<String> tokens = [];
			Int32 i = 0;
			while (i < query.Length)
			{
				while (i < query.Length && Char.IsWhiteSpace(query[i]))
				{
					i++;
				}

				if (i >= query.Length)
				{
					break;
				}

				Int32 start = i;
				Boolean quoted = false;
				while (i < query.Length && (quoted || !Char.IsWhiteSpace(query[i])))
				{
					if (query[i] == '"')
					{
						quoted = !quoted;
					}

					i++;
				}

				tokens.Add(query[start..i].Replace("\"", String.Empty, StringComparison.Ordinal));
			}

			return tokens;
		}

		private static Func<ProtoEntry, Boolean> CreatePredicate(String token, IReadOnlyList<String> scriptNames)
		{
			Int32 colon = token.IndexOf(':', StringComparison.Ordinal);
			if (colon > 0)
			{
				String key = token[..colon].ToLowerInvariant();
				String value = token[(colon + 1)..];
				switch (key)
				{
					case "pid":
						return e => MatchesPid(e, value);
					case "file":
						return e => e.FileName.Contains(value, StringComparison.OrdinalIgnoreCase);
					case "name":
						return e => e.Name.Contains(value, StringComparison.OrdinalIgnoreCase);
					case "text":
					case "msg":
						return e => e.Name.Contains(value, StringComparison.OrdinalIgnoreCase)
							|| e.Description.Contains(value, StringComparison.OrdinalIgnoreCase);
					case "kind":
					case "type":
						return e => MatchesKind(e, value);
					case "source":
						return e => e.SourceName.Contains(value, StringComparison.OrdinalIgnoreCase);
					case "flag":
						return e => MatchesFlag(e, value);
					case "script":
						return e => MatchesScript(e, value, scriptNames);
					default:
						break;
				}
			}

			return e => e.Name.Contains(token, StringComparison.OrdinalIgnoreCase)
				|| e.Description.Contains(token, StringComparison.OrdinalIgnoreCase)
				|| e.FileName.Contains(token, StringComparison.OrdinalIgnoreCase)
				|| MatchesPid(e, token);
		}

		private static Boolean MatchesScript(ProtoEntry entry, String value, IReadOnlyList<String> scriptNames)
		{
			Int32 index = entry.ScriptIndex;
			if (value.Length == 0)
			{
				return index >= 0;
			}

			if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
			{
				return entry.File is not null && index < 0;
			}

			if (index < 0)
			{
				return false;
			}

			if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out Int32 line))
			{
				return index == line;
			}

			return index < scriptNames.Count && scriptNames[index].Contains(value, StringComparison.OrdinalIgnoreCase);
		}

		private static Boolean MatchesPid(ProtoEntry entry, String value)
		{
			if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
				&& Int32.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out Int32 hex))
			{
				return entry.Pid.Value == hex || entry.Pid.Index == hex;
			}

			if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out Int32 number))
			{
				return entry.Pid.Index == number || entry.Pid.Value == number;
			}

			return entry.Pid.ToString().Contains(value, StringComparison.OrdinalIgnoreCase);
		}

		private static Boolean MatchesKind(ProtoEntry entry, String value)
		{
			if (entry.Kind is null)
			{
				return false;
			}

			return entry.Kind.Value.ToString().Contains(value, StringComparison.OrdinalIgnoreCase)
				|| entry.Type.ToString().Contains(value, StringComparison.OrdinalIgnoreCase);
		}

		private static Boolean MatchesFlag(ProtoEntry entry, String name)
		{
			ProtoFile? file = entry.File;
			if (file is null)
			{
				return false;
			}

			foreach (FlagSet set in FlagSets.All)
			{
				foreach (FlagBit bit in set.Bits)
				{
					if (!String.Equals(bit.Name, name, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					Int32 value = set.Id == FlagSets.ObjectFlags ? file.Flags : set.Id == FlagSets.CritterFlags
						? file.TryGet(FieldKeys.CritterFlags) ?? 0
						: file.FlagsExt;
					if ((unchecked((UInt32)value) & bit.Mask) == bit.Mask)
					{
						return true;
					}
				}
			}

			return false;
		}
	}
}