namespace ProtoForge.Core.Formats.Pro
{
	using System;
	using IO;

	/// <summary>
	/// The raw content of a <c>.pro</c> file, edited in place through the field schema of its <see cref="ProtoKind"/>.
	/// The bytes are never reinterpreted or resized behind the user's back: unknown or trailing bytes survive a load/save round trip.
	/// </summary>
	public sealed class ProtoFile
	{
		private readonly Byte[] _data;

		public ProtoFile(ProtoKind kind, Byte[] data)
		{
			Kind = kind;
			_data = data;
		}

		public ProtoKind Kind { get; }

		public ProtoSchema Schema
		{
			get { return ProtoSchemas.For(Kind); }
		}

		public ProtoObjectType ObjectType
		{
			get { return Kind.GetObjectType(); }
		}

		public Int32 Length
		{
			get { return _data.Length; }
		}

		public ReadOnlySpan<Byte> Span
		{
			get { return _data; }
		}

		/// <summary>True when the file has exactly the size the engine expects for this kind.</summary>
		public Boolean HasExpectedLength
		{
			get { return _data.Length == Kind.GetExpectedSize() || _data.Length == Kind.GetAlternativeSize(); }
		}

		/// <summary>Fallout 1 critter layout (no damage type field).</summary>
		public Boolean IsFallout1Critter
		{
			get { return Kind == ProtoKind.Critter && _data.Length == 412; }
		}

		/// <summary>Parses a file whose object type is already known from its location (<c>proto/&lt;type&gt;/</c>).</summary>
		public static ProtoFile? TryParse(ReadOnlySpan<Byte> bytes, ProtoObjectType expectedType, out String? error)
		{
			Int32 subType = bytes.Length >= 0x24 ? BigEndian.ReadInt32(bytes, 0x20) : 0;
			ProtoKind? kind = ProtoKindExtensions.Resolve(expectedType, subType);
			if (kind is null)
			{
				error = $"Unknown {expectedType} sub type {subType}.";
				return null;
			}

			if (bytes.Length < 12)
			{
				error = "The file is too short to be a prototype.";
				return null;
			}

			error = null;
			return new ProtoFile(kind.Value, bytes.ToArray());
		}

		public ProtoFile Clone()
		{
			return new ProtoFile(Kind, (Byte[])_data.Clone());
		}

		public Byte[] ToArray()
		{
			return (Byte[])_data.Clone();
		}

		public Boolean HasField(ProtoField field)
		{
			return field.End <= _data.Length;
		}

		public Int32? TryGet(ProtoField field)
		{
			if (!HasField(field))
			{
				return null;
			}

			Int32 raw = field.Size == 1 ? _data[field.Offset] : BigEndian.ReadInt32(_data, field.Offset);
			if (field.IsVirtual)
			{
				raw = unchecked((Int32)(((UInt32)raw & field.Mask) >> field.Shift));
			}

			return raw;
		}

		public Int32 Get(ProtoField field)
		{
			return TryGet(field) ?? field.DefaultValue;
		}

		public Int32? TryGet(String key)
		{
			ProtoField? field = Schema.Find(key);
			return field is null ? null : TryGet(field);
		}

		public Int32 Get(String key, Int32 fallback = 0)
		{
			return TryGet(key) ?? fallback;
		}

		public Boolean TrySet(ProtoField field, Int32 value)
		{
			if (!HasField(field))
			{
				return false;
			}

			if (field.IsVirtual)
			{
				UInt32 raw = field.Size == 1 ? _data[field.Offset] : unchecked((UInt32)BigEndian.ReadInt32(_data, field.Offset));
				raw = (raw & ~field.Mask) | ((unchecked((UInt32)value) << field.Shift) & field.Mask);
				value = unchecked((Int32)raw);
			}

			if (field.Size == 1)
			{
				_data[field.Offset] = unchecked((Byte)value);
			}
			else
			{
				BigEndian.WriteInt32(_data, field.Offset, value);
			}

			return true;
		}

		public Boolean TrySet(String key, Int32 value)
		{
			ProtoField? field = Schema.Find(key);
			return field is not null && TrySet(field, value);
		}

		public Int32 ReadInt32(Int32 offset)
		{
			return BigEndian.ReadInt32(_data, offset);
		}

		public void WriteByte(Int32 offset, Byte value)
		{
			_data[offset] = value;
		}

		public Byte ReadByte(Int32 offset)
		{
			return _data[offset];
		}

		/// <summary>The PID stored inside the file (it should match the position of the file in the LST).</summary>
		public Int32 StoredProtoId
		{
			get { return ReadInt32(0); }
			set { BigEndian.WriteInt32(_data, 0, value); }
		}

		public Int32 MessageId
		{
			get { return Get(FieldKeys.MessageId); }
			set { TrySet(FieldKeys.MessageId, value); }
		}

		public Int32 FrmId
		{
			get { return Get(FieldKeys.FrmId, -1); }
		}

		public Int32 Flags
		{
			get { return Get(FieldKeys.Flags); }
		}

		public Int32 FlagsExt
		{
			get { return Get(FieldKeys.FlagsExt); }
		}

		/// <summary>True when both files have identical bytes.</summary>
		public Boolean ContentEquals(ProtoFile other)
		{
			return Kind == other.Kind && _data.AsSpan().SequenceEqual(other._data);
		}
	}
}