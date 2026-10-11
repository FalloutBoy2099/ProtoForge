namespace ProtoForge.Core.Formats.Pro
{
	using System;
	using System.Globalization;

	/// <summary>
	/// Prototype identifier: the object type lives in the high byte, the 1-based line index of the LST file in the low 24 bits.
	/// </summary>
	public readonly record struct ProtoId(Int32 Value) : IComparable<ProtoId>, IComparable
	{
		public ProtoObjectType Type
		{
			get { return (ProtoObjectType)((Value >> 24) & 0xFF); }
		}

		/// <summary>1-based index into the LST file of <see cref="Type"/>.</summary>
		public Int32 Index
		{
			get { return Value & 0x00FFFFFF; }
		}

		public static ProtoId From(ProtoObjectType type, Int32 index)
		{
			return new ProtoId(((Int32)type << 24) | (index & 0x00FFFFFF));
		}

		public Int32 CompareTo(ProtoId other)
		{
			return Value.CompareTo(other.Value);
		}

		public Int32 CompareTo(Object? obj)
		{
			return obj is ProtoId other ? CompareTo(other) : 1;
		}

		public static Boolean operator <(ProtoId left, ProtoId right)
		{
			return left.Value < right.Value;
		}

		public static Boolean operator >(ProtoId left, ProtoId right)
		{
			return left.Value > right.Value;
		}

		public static Boolean operator <=(ProtoId left, ProtoId right)
		{
			return left.Value <= right.Value;
		}

		public static Boolean operator >=(ProtoId left, ProtoId right)
		{
			return left.Value >= right.Value;
		}

		public override String ToString()
		{
			return "0x" + Value.ToString("X8", CultureInfo.InvariantCulture);
		}

		public static Boolean TryParse(String? text, out ProtoId id)
		{
			id = default;
			if (String.IsNullOrWhiteSpace(text))
			{
				return false;
			}

			text = text.Trim();
			Int32 value;
			if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			{
				if (!Int32.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
				{
					return false;
				}
			}
			else if (!Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
			{
				return false;
			}

			id = new ProtoId(value);
			return true;
		}
	}
}