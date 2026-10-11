namespace ProtoForge.Core.Serialization
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.IO;
	using System.Linq;
	using System.Text;
	using System.Text.Json;
	using Domain;
	using Formats.Pro;

	/// <summary>Structured JSON export/import of prototypes (one object per prototype, fields keyed by their schema key).</summary>
	public static class ProtoJson
	{
		public const String FormatName = "ProtoForge.Protos";
		public const Int32 FormatVersion = 1;

		public static String Export(IEnumerable<ProtoEntry> entries, String producer)
		{
			using MemoryStream stream = new();
			using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
			{
				writer.WriteStartObject();
				writer.WriteString("format", FormatName);
				writer.WriteNumber("version", FormatVersion);
				writer.WriteString("producer", producer);
				writer.WriteStartArray("protos");
				foreach (ProtoEntry entry in entries.Where(e => e.File is not null))
				{
					WriteEntry(writer, entry);
				}

				writer.WriteEndArray();
				writer.WriteEndObject();
			}

			return Encoding.UTF8.GetString(stream.ToArray());
		}

		/// <summary>Reads a JSON document and computes what it would change in the catalog.</summary>
		public static ImportPlan CreatePlan(String json, ProtoCatalog catalog)
		{
			ImportPlan plan = new();
			using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("protos", out JsonElement protos)
				|| protos.ValueKind != JsonValueKind.Array)
			{
				throw new InvalidDataException("The file is not a ProtoForge JSON export (missing 'protos' array).");
			}

			foreach (JsonElement item in protos.EnumerateArray())
			{
				if (!item.TryGetProperty("pid", out JsonElement pidElement) || !TryReadInt32(pidElement, out Int32 pidValue))
				{
					plan.Warnings.Add("An entry without a valid 'pid' was skipped.");
					continue;
				}

				ProtoEntry? entry = catalog.Find(pidValue);
				if (entry?.File is null)
				{
					plan.Warnings.Add($"{new ProtoId(pidValue)}: no such prototype in the current game; skipped.");
					continue;
				}

				if (item.TryGetProperty("kind", out JsonElement kindElement)
					&& kindElement.ValueKind == JsonValueKind.String
					&& !String.Equals(kindElement.GetString(), entry.File.Kind.ToString(), StringComparison.OrdinalIgnoreCase))
				{
					plan.Warnings.Add($"{entry.Pid}: kind mismatch ({kindElement.GetString()} vs {entry.File.Kind}); skipped.");
					continue;
				}

				if (item.TryGetProperty("fields", out JsonElement fields) && fields.ValueKind == JsonValueKind.Object)
				{
					ReadFields(plan, entry, fields);
				}

				String? name = item.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String
					? nameElement.GetString()
					: null;
				String? description = item.TryGetProperty("description", out JsonElement descElement) && descElement.ValueKind == JsonValueKind.String
					? descElement.GetString()
					: null;
				plan.AddText(entry, name, description);
			}

			return plan;
		}

		/// <summary>Applies non-virtual fields first so that bit sub-fields (attack modes) refine the stored value.</summary>
		internal static void ReadFields(ImportPlan plan, ProtoEntry entry, JsonElement fields)
		{
			ProtoSchema schema = entry.File!.Schema;
			List<(ProtoField Field, Int32 Value)> parsed = [];
			foreach (JsonProperty property in fields.EnumerateObject())
			{
				ProtoField? field = schema.Find(property.Name);
				if (field is null)
				{
					plan.Warnings.Add($"{entry.Pid}: unknown field '{property.Name}' ignored.");
					continue;
				}

				if (field.IsReadOnly)
				{
					continue;
				}

				if (!TryReadInt32(property.Value, out Int32 value))
				{
					plan.Warnings.Add($"{entry.Pid}: field '{property.Name}' has a value that is not an integer.");
					continue;
				}

				parsed.Add((field, value));
			}

			foreach ((ProtoField field, Int32 value) in parsed.OrderBy(p => p.Field.IsVirtual))
			{
				plan.AddField(entry, field, value);
			}
		}

		internal static Boolean TryReadInt32(JsonElement element, out Int32 value)
		{
			value = 0;
			switch (element.ValueKind)
			{
				case JsonValueKind.Number:
					if (element.TryGetInt32(out value))
					{
						return true;
					}

					if (element.TryGetUInt32(out UInt32 unsigned))
					{
						value = unchecked((Int32)unsigned);
						return true;
					}

					return false;
				case JsonValueKind.String:
					return TryParseInt32(element.GetString(), out value);
				default:
					return false;
			}
		}

		/// <summary>Parses decimal or <c>0x</c> hexadecimal text, accepting unsigned 32-bit values (flags).</summary>
		public static Boolean TryParseInt32(String? text, out Int32 value)
		{
			value = 0;
			if (String.IsNullOrWhiteSpace(text))
			{
				return false;
			}

			text = text.Trim();
			if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			{
				if (UInt32.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out UInt32 hex))
				{
					value = unchecked((Int32)hex);
					return true;
				}

				return false;
			}

			if (Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
			{
				return true;
			}

			if (UInt32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out UInt32 unsigned))
			{
				value = unchecked((Int32)unsigned);
				return true;
			}

			return false;
		}

		private static void WriteEntry(Utf8JsonWriter writer, ProtoEntry entry)
		{
			ProtoFile file = entry.File!;
			writer.WriteStartObject();
			writer.WriteString("pid", entry.Pid.ToString());
			writer.WriteString("file", entry.FileName);
			writer.WriteString("type", entry.Type.ToString());
			writer.WriteString("kind", file.Kind.ToString());
			writer.WriteString("name", entry.Name);
			writer.WriteString("description", entry.Description);

			writer.WriteStartObject("fields");
			foreach (ProtoField field in file.Schema.Fields)
			{
				Int32? value = file.TryGet(field);
				if (value is not null)
				{
					writer.WriteNumber(field.Key, value.Value);
				}
			}

			writer.WriteEndObject();

			// Informational only: lets people read the flags without decoding bits. Ignored on import.
			writer.WriteStartObject("flagNames");
			foreach (ProtoField field in file.Schema.Fields.Where(f => f.Editor == FieldEditor.Flags && f.FlagSet is not null))
			{
				Int32? value = file.TryGet(field);
				if (value is null)
				{
					continue;
				}

				writer.WriteStartArray(field.Key);
				foreach (FlagBit bit in FlagSets.Get(field.FlagSet!).GetActive(value.Value))
				{
					writer.WriteStringValue(bit.Name);
				}

				writer.WriteEndArray();
			}

			writer.WriteEndObject();
			writer.WriteEndObject();
		}
	}
}