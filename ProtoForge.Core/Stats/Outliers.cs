namespace ProtoForge.Core.Stats
{
	using System;
	using System.Collections.Generic;
	using System.Linq;

	/// <summary>Finds the values that stand far from the others (to point out weapons or armors that are out of balance).</summary>
	public static class Outliers
	{
		/// <summary>Not enough values to say that one is unusual.</summary>
		public const Int32 MinimumCount = 8;

		/// <summary>For each value, whether it is more than <paramref name="deviations"/> standard deviations away from the mean.</summary>
		public static Boolean[] Find(IReadOnlyList<Double?> values, Double deviations = 2.0)
		{
			Boolean[] result = new Boolean[values.Count];
			List<Double> known = [.. values.Where(v => v is not null).Select(v => v!.Value)];
			if (known.Count < MinimumCount)
			{
				return result;
			}

			Double mean = known.Average();
			Double spread = Math.Sqrt(known.Sum(v => (v - mean) * (v - mean)) / known.Count);
			if (spread <= 0)
			{
				return result;
			}

			for (Int32 i = 0; i < values.Count; i++)
			{
				result[i] = values[i] is { } value && Math.Abs(value - mean) > deviations * spread;
			}

			return result;
		}
	}
}