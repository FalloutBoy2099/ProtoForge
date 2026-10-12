namespace ProtoForge.Core.Editing
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Domain;

	/// <summary>The new texts of one prototype after a find and replace.</summary>
	public sealed record TextReplacement(ProtoEntry Entry, String Name, String Description, Int32 Occurrences);

	/// <summary>Find and replace in the names and descriptions of prototypes.</summary>
	public static class TextReplacer
	{
		/// <summary>The prototypes whose texts change (nothing is modified here), with the number of occurrences replaced in each.</summary>
		public static IReadOnlyList<TextReplacement> Plan(
			IEnumerable<ProtoEntry> entries,
			ProtoTextService texts,
			String find,
			String replacement,
			Boolean inNames,
			Boolean inDescriptions,
			Boolean matchCase)
		{
			if (find.Length == 0)
			{
				return [];
			}

			StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
			List<TextReplacement> result = [];
			foreach (ProtoEntry entry in entries)
			{
				if (!texts.CanEditTexts(entry))
				{
					continue;
				}

				Int32 occurrences = 0;
				String name = inNames ? Replace(entry.Name, find, replacement, comparison, ref occurrences) : entry.Name;
				String description = inDescriptions ? Replace(entry.Description, find, replacement, comparison, ref occurrences) : entry.Description;
				if (occurrences > 0 && (name != entry.Name || description != entry.Description))
				{
					result.Add(new TextReplacement(entry, name, description, occurrences));
				}
			}

			return result;
		}

		private static String Replace(String text, String find, String replacement, StringComparison comparison, ref Int32 occurrences)
		{
			Int32 index = text.IndexOf(find, comparison);
			if (index < 0)
			{
				return text;
			}

			System.Text.StringBuilder builder = new();
			Int32 last = 0;
			while (index >= 0)
			{
				builder.Append(text, last, index - last).Append(replacement);
				last = index + find.Length;
				occurrences++;
				index = text.IndexOf(find, last, comparison);
			}

			return builder.Append(text, last, text.Length - last).ToString();
		}
	}
}