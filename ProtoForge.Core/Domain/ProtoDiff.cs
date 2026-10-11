namespace ProtoForge.Core.Domain
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Formats.Pro;

	/// <summary>The comparison of one field between two prototypes.</summary>
	public sealed record FieldDiff(String Key, String Group, ProtoField? FieldA, ProtoField? FieldB, Int32? ValueA, Int32? ValueB)
	{
		public Boolean IsDifferent
		{
			get { return ValueA != ValueB; }
		}

		public ProtoField? Field
		{
			get { return FieldA ?? FieldB; }
		}
	}

	/// <summary>Field by field comparison of two prototype files (vanilla vs mod, two items, before/after an edit).</summary>
	public static class ProtoDiff
	{
		/// <summary>
		/// Compares two files. Fields are matched by key, so two different kinds (a pistol and a rifle) can be compared on their common fields
		/// while kind specific fields appear as present on one side only.
		/// </summary>
		public static IReadOnlyList<FieldDiff> Compare(ProtoFile? a, ProtoFile? b)
		{
			List<FieldDiff> result = [];
			ProtoSchema? schemaA = a?.Schema;
			ProtoSchema? schemaB = b?.Schema;
			HashSet<String> seen = new(StringComparer.Ordinal);

			if (schemaA is not null)
			{
				foreach (ProtoField fieldA in schemaA.Fields)
				{
					ProtoField? fieldB = schemaB?.Find(fieldA.Key);
					seen.Add(fieldA.Key);
					result.Add(new FieldDiff(fieldA.Key, fieldA.Group, fieldA, fieldB, a!.TryGet(fieldA), fieldB is null ? null : b!.TryGet(fieldB)));
				}
			}

			if (schemaB is not null)
			{
				foreach (ProtoField fieldB in schemaB.Fields.Where(f => !seen.Contains(f.Key)))
				{
					result.Add(new FieldDiff(fieldB.Key, fieldB.Group, null, fieldB, null, b!.TryGet(fieldB)));
				}
			}

			return result;
		}

		public static Int32 CountDifferences(ProtoFile? a, ProtoFile? b)
		{
			return Compare(a, b).Count(d => d.IsDifferent && (d.Field is null || !d.Field.IsVirtual));
		}

		/// <summary>Byte offsets that differ between two raw files (used by the hex view).</summary>
		public static IReadOnlyList<Int32> GetDifferentOffsets(ProtoFile? a, ProtoFile? b)
		{
			if (a is null || b is null)
			{
				return [];
			}

			List<Int32> offsets = [];
			ReadOnlySpan<Byte> left = a.Span;
			ReadOnlySpan<Byte> right = b.Span;
			Int32 length = Math.Max(left.Length, right.Length);
			for (Int32 i = 0; i < length; i++)
			{
				Boolean hasLeft = i < left.Length;
				Boolean hasRight = i < right.Length;
				if (hasLeft != hasRight || (hasLeft && left[i] != right[i]))
				{
					offsets.Add(i);
				}
			}

			return offsets;
		}
	}
}