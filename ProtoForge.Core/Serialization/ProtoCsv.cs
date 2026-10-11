namespace ProtoForge.Core.Serialization
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Linq;
	using System.Text;
	using Domain;
	using Formats.Pro;
	using Reference;

	/// <summary>What a CSV export contains.</summary>
	public sealed class CsvExportOptions
	{
		/// <summary>The field keys to write (the identity columns are always written); null writes every field.</summary>
		public ISet<String>? Columns { get; init; }

		/// <summary>
		/// Readable values: lists are written with their names (needs <see cref="Choices"/>) and flag fields become one
		/// <c>Field.Flag</c> column per flag with the value <c>true</c> or <c>false</c>.
		/// </summary>
		public Boolean Readable { get; init; }

		public ChoiceCatalog? Choices { get; init; }
	}

	/// <summary>
	/// CSV export/import. One table per prototype kind (the columns of a weapon and of a critter are not the same), which makes the
	/// files easy to edit in a spreadsheet and re-import.
	/// </summary>
	public static class ProtoCsv
	{
		private static readonly String[] FixedColumns = ["Pid", "File", "Name", "Description"];

		/// <summary>Exports the entries grouped by kind; the keys are the kinds, the values the CSV text.</summary>
		public static IReadOnlyDictionary<ProtoKind, String> Export(IEnumerable<ProtoEntry> entries, Char delimiter = ';', CsvExportOptions? options = null)
		{
			Dictionary<ProtoKind, String> result = [];
			foreach (IGrouping<ProtoKind, ProtoEntry> group in entries.Where(e => e.File is not null).GroupBy(e => e.File!.Kind))
			{
				result[group.Key] = ExportKind(group.Key, group, delimiter, options);
			}

			return result;
		}

		/// <summary>The keys of the fields found in the schemas of the given kinds (first appearance order), for the column picker.</summary>
		public static IReadOnlyList<String> GetFieldKeys(IEnumerable<ProtoKind> kinds)
		{
			List<String> keys = [];
			foreach (ProtoKind kind in kinds)
			{
				foreach (ProtoField field in ProtoSchemas.For(kind).Fields)
				{
					if (!keys.Contains(field.Key))
					{
						keys.Add(field.Key);
					}
				}
			}

			return keys;
		}

		public static String ExportKind(ProtoKind kind, IEnumerable<ProtoEntry> entries, Char delimiter = ';', CsvExportOptions? options = null)
		{
			ProtoSchema schema = ProtoSchemas.For(kind);
			Boolean readable = options?.Readable == true;
			List<ProtoField> fields = [.. schema.Fields.Where(f => options?.Columns is null || options.Columns.Contains(f.Key))];

			// Header: one column per field, or one per flag for the flag fields of a readable export.
			List<String> header = [.. FixedColumns];
			foreach (ProtoField field in fields)
			{
				if (readable && field.Editor == FieldEditor.Flags && field.FlagSet is not null)
				{
					header.AddRange(FlagSets.Get(field.FlagSet).Bits.Select(b => $"{field.Key}.{b.Name}"));
				}
				else
				{
					header.Add(field.Key);
				}
			}

			StringBuilder builder = new();
			AppendRow(builder, delimiter, header);
			foreach (ProtoEntry entry in entries)
			{
				ProtoFile? file = entry.File;
				if (file is null)
				{
					continue;
				}

				List<String> cells =
				[
					entry.Pid.ToString(),
					EscapeText(entry.FileName),
					EscapeText(entry.Name),
					EscapeText(entry.Description),
				];
				foreach (ProtoField field in fields)
				{
					Int32? value = file.TryGet(field);
					if (readable && field.Editor == FieldEditor.Flags && field.FlagSet is not null)
					{
						foreach (FlagBit bit in FlagSets.Get(field.FlagSet).Bits)
						{
							cells.Add(value is null ? String.Empty : (unchecked((UInt32)value.Value) & bit.Mask) == bit.Mask ? "true" : "false");
						}
					}
					else if (readable && field.Editor == FieldEditor.Choice && field.ChoiceSet is not null && options?.Choices is { } choices && value is not null)
					{
						cells.Add(choices.Describe(field.ChoiceSet, value.Value));
					}
					else
					{
						cells.Add(value?.ToString(CultureInfo.InvariantCulture) ?? String.Empty);
					}
				}

				AppendRow(builder, delimiter, cells);
			}

			return builder.ToString();
		}

		/// <summary>
		/// Builds the import plan of a CSV file. Lines that start with <c>#</c> are ignored (comments or lines the user wants to skip),
		/// lists may be written with their names and flags as <c>Field.Flag</c> columns with <c>true</c>/<c>false</c> (see <see cref="CsvExportOptions"/>).
		/// </summary>
		public static ImportPlan CreatePlan(String csv, ProtoCatalog catalog, Char delimiter = ';', ChoiceCatalog? choices = null)
		{
			ImportPlan plan = new();
			List<List<String>> rows = ParseRows(csv, delimiter);
			if (rows.Count == 0)
			{
				plan.Warnings.Add("The CSV file is empty.");
				return plan;
			}

			List<String> header = rows[0];
			if (header.Count > 0 && header[0].StartsWith('#'))
			{
				header[0] = header[0].TrimStart('#');
			}

			Int32 pidColumn = header.FindIndex(h => String.Equals(h, "Pid", StringComparison.OrdinalIgnoreCase));
			if (pidColumn < 0)
			{
				plan.Warnings.Add("The CSV file has no 'Pid' column.");
				return plan;
			}

			Int32 nameColumn = header.FindIndex(h => String.Equals(h, "Name", StringComparison.OrdinalIgnoreCase));
			Int32 descriptionColumn = header.FindIndex(h => String.Equals(h, "Description", StringComparison.OrdinalIgnoreCase));

			for (Int32 r = 1; r < rows.Count; r++)
			{
				List<String> row = rows[r];
				if (row.Count > 0 && row[0].TrimStart().StartsWith('#'))
				{
					plan.Warnings.Add($"Line {r + 1}: ignored (it starts with #).");
					continue;
				}

				if (row.Count <= pidColumn || !ProtoId.TryParse(row[pidColumn], out ProtoId pid))
				{
					plan.Warnings.Add($"Line {r + 1}: invalid PID.");
					continue;
				}

				ProtoEntry? entry = catalog.Find(pid);
				if (entry?.File is null)
				{
					plan.Warnings.Add($"{pid}: no such prototype; skipped.");
					continue;
				}

				ProtoSchema schema = entry.File.Schema;
				List<(ProtoField Field, Int32 Value)> parsed = [];
				Dictionary<ProtoField, Int32> flagValues = [];
				for (Int32 c = 0; c < header.Count && c < row.Count; c++)
				{
					if (String.IsNullOrWhiteSpace(row[c]))
					{
						continue;
					}

					ProtoField? field = schema.Find(header[c]);
					if (field is null && TryFindFlag(schema, header[c], out ProtoField? flagField, out FlagBit? bit))
					{
						if (!TryParseBoolean(row[c], out Boolean on))
						{
							plan.Warnings.Add($"{pid}: column '{header[c]}' is not true or false.");
							continue;
						}

						Int32 current = flagValues.TryGetValue(flagField!, out Int32 pending) ? pending : entry.File.Get(flagField!);
						UInt32 raw = unchecked((UInt32)current);
						flagValues[flagField!] = unchecked((Int32)(on ? raw | bit!.Mask : raw & ~bit!.Mask));
						continue;
					}

					if (field is null || field.IsReadOnly)
					{
						continue;
					}

					Int32 value;
					if (!ProtoJson.TryParseInt32(row[c], out value))
					{
						ChoiceOption? named = field.ChoiceSet is not null && choices is not null
							? choices.Get(field.ChoiceSet).Cast<ChoiceOption?>().FirstOrDefault(o => String.Equals(o!.Value.Label, row[c].Trim(), StringComparison.OrdinalIgnoreCase))
							: null;
						if (named is null)
						{
							plan.Warnings.Add($"{pid}: column '{header[c]}' is not an integer or a known name.");
							continue;
						}

						value = named.Value.Value;
					}

					parsed.Add((field, value));
				}

				foreach (KeyValuePair<ProtoField, Int32> pair in flagValues)
				{
					parsed.Add((pair.Key, pair.Value));
				}

				foreach ((ProtoField field, Int32 value) in parsed.OrderBy(p => p.Field.IsVirtual))
				{
					plan.AddField(entry, field, value);
				}

				String? name = nameColumn >= 0 && nameColumn < row.Count ? UnescapeText(row[nameColumn]) : null;
				String? description = descriptionColumn >= 0 && descriptionColumn < row.Count ? UnescapeText(row[descriptionColumn]) : null;
				plan.AddText(entry, name, description);
			}

			return plan;
		}

		private static Boolean StartsLikeFormula(String text)
		{
			return text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r';
		}

		/// <summary>
		/// A spreadsheet runs a cell that starts with = + - @ as a formula. Texts come from game files of unknown origin, so they get
		/// a leading apostrophe (removed again when the file is imported).
		/// </summary>
		public static String EscapeText(String text)
		{
			return StartsLikeFormula(text) ? "'" + text : text;
		}

		public static String UnescapeText(String text)
		{
			return text.Length > 1 && text[0] == '\'' && StartsLikeFormula(text[1..]) ? text[1..] : text;
		}

		private static Boolean TryFindFlag(ProtoSchema schema, String column, out ProtoField? field, out FlagBit? bit)
		{
			field = null;
			bit = null;
			Int32 dot = column.LastIndexOf('.');
			if (dot <= 0)
			{
				return false;
			}

			field = schema.Find(column[..dot]);
			if (field is null || field.Editor != FieldEditor.Flags || field.FlagSet is null)
			{
				return false;
			}

			String name = column[(dot + 1)..];
			bit = FlagSets.Get(field.FlagSet).Bits.FirstOrDefault(b => String.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
			return bit is not null;
		}

		private static Boolean TryParseBoolean(String text, out Boolean value)
		{
			switch (text.Trim().ToLowerInvariant())
			{
				case "true" or "1" or "yes" or "x":
					value = true;
					return true;
				case "false" or "0" or "no":
					value = false;
					return true;
				default:
					value = false;
					return false;
			}
		}

		private static void AppendRow(StringBuilder builder, Char delimiter, IEnumerable<String> cells)
		{
			Boolean first = true;
			foreach (String cell in cells)
			{
				if (!first)
				{
					builder.Append(delimiter);
				}

				first = false;
				if (cell.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0)
				{
					builder.Append('"').Append(cell.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
				}
				else
				{
					builder.Append(cell);
				}
			}

			builder.Append("\r\n");
		}

		/// <summary>RFC 4180 style parser (quoted cells, doubled quotes, embedded line breaks).</summary>
		internal static List<List<String>> ParseRows(String text, Char delimiter)
		{
			List<List<String>> rows = [];
			List<String> row = [];
			StringBuilder cell = new();
			Boolean inQuotes = false;
			Boolean hasContent = false;

			for (Int32 i = 0; i < text.Length; i++)
			{
				Char c = text[i];
				if (inQuotes)
				{
					if (c == '"')
					{
						if (i + 1 < text.Length && text[i + 1] == '"')
						{
							cell.Append('"');
							i++;
						}
						else
						{
							inQuotes = false;
						}
					}
					else
					{
						cell.Append(c);
					}

					continue;
				}

				if (c == '"')
				{
					inQuotes = true;
					hasContent = true;
				}
				else if (c == delimiter)
				{
					row.Add(cell.ToString());
					cell.Clear();
					hasContent = true;
				}
				else if (c == '\r' || c == '\n')
				{
					if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
					{
						i++;
					}

					if (hasContent || cell.Length > 0)
					{
						row.Add(cell.ToString());
						rows.Add(row);
					}

					row = [];
					cell.Clear();
					hasContent = false;
				}
				else
				{
					cell.Append(c);
					hasContent = true;
				}
			}

			if (hasContent || cell.Length > 0)
			{
				row.Add(cell.ToString());
				rows.Add(row);
			}

			return rows;
		}
	}
}