namespace ProtoForge.Core.Formats.Pro
{
	using System;
	using System.Collections.Generic;

	/// <summary>Fluent helper that lays fields out sequentially, so offsets are never typed by hand.</summary>
	internal sealed class SchemaBuilder
	{
		private readonly ProtoKind _kind;
		private readonly List<ProtoField> _fields = [];
		private String _group = "General";
		private Int32 _offset;

		public SchemaBuilder(ProtoKind kind)
		{
			_kind = kind;
		}

		public Int32 Offset
		{
			get { return _offset; }
		}

		public SchemaBuilder Group(String group)
		{
			_group = group;
			return this;
		}

		public SchemaBuilder At(Int32 offset)
		{
			_offset = offset;
			return this;
		}

		public SchemaBuilder Skip(Int32 count)
		{
			_offset += count;
			return this;
		}

		public SchemaBuilder Number(String key, Int32 min = Int32.MinValue, Int32 max = Int32.MaxValue, Int32 defaultValue = 0, Boolean advanced = false, Boolean readOnly = false)
		{
			return Add(new ProtoField(key, _group, _offset, 4, FieldEditor.Number)
			{
				Min = min,
				Max = max,
				DefaultValue = defaultValue,
				IsAdvanced = advanced,
				IsReadOnly = readOnly,
			});
		}

		public SchemaBuilder Character(String key, Int32 defaultValue = 0)
		{
			return Add(new ProtoField(key, _group, _offset, 1, FieldEditor.Character)
			{
				Min = 0,
				Max = 255,
				DefaultValue = defaultValue,
			});
		}

		public SchemaBuilder Choice(String key, String set, Int32 defaultValue = 0, Boolean readOnly = false)
		{
			return Add(new ProtoField(key, _group, _offset, 4, FieldEditor.Choice)
			{
				ChoiceSet = set,
				DefaultValue = defaultValue,
				IsReadOnly = readOnly,
			});
		}

		/// <summary>A one-byte value picked from a list (the sound letters).</summary>
		public SchemaBuilder ByteChoice(String key, String set, Int32 defaultValue = 0)
		{
			return Add(new ProtoField(key, _group, _offset, 1, FieldEditor.Choice)
			{
				ChoiceSet = set,
				DefaultValue = defaultValue,
				Min = 0,
				Max = 255,
			});
		}

		public SchemaBuilder Flags(String key, String set, Int32 defaultValue = 0)
		{
			return Add(new ProtoField(key, _group, _offset, 4, FieldEditor.Flags)
			{
				FlagSet = set,
				DefaultValue = defaultValue,
			});
		}

		public SchemaBuilder Art(String key, ArtType type, Int32 defaultValue = -1)
		{
			return Add(new ProtoField(key, _group, _offset, 4, FieldEditor.Art)
			{
				ArtType = type,
				DefaultValue = defaultValue,
			});
		}

		public SchemaBuilder Proto(String key, ProtoObjectType type, ProtoKind? kind = null, Int32 defaultValue = -1)
		{
			return Add(new ProtoField(key, _group, _offset, 4, FieldEditor.Proto)
			{
				ReferenceType = type,
				ReferenceKind = kind,
				DefaultValue = defaultValue,
			});
		}

		public SchemaBuilder Message(String key)
		{
			return Add(new ProtoField(key, _group, _offset, 4, FieldEditor.Message));
		}

		public SchemaBuilder Script(String key)
		{
			return Add(new ProtoField(key, _group, _offset, 4, FieldEditor.Script) { DefaultValue = -1 });
		}

		public SchemaBuilder Light(String key)
		{
			return Add(new ProtoField(key, _group, _offset, 4, FieldEditor.LightIntensity)
			{
				Min = 0,
				Max = 65536,
			});
		}

		public SchemaBuilder Pid(String key)
		{
			return Add(new ProtoField(key, _group, _offset, 4, FieldEditor.Number)
			{
				IsReadOnly = true,
				IsAdvanced = true,
			});
		}

		/// <summary>Adds a bit-field view over the field that was just declared (does not advance the offset).</summary>
		public SchemaBuilder SubField(String key, ProtoField host, UInt32 mask, Int32 shift, String choiceSet)
		{
			_fields.Add(new ProtoField(key, _group, host.Offset, host.Size, FieldEditor.Choice)
			{
				ChoiceSet = choiceSet,
				Mask = mask,
				Shift = shift,
			});
			return this;
		}

		public ProtoField Find(String key)
		{
			return _fields.Find(f => String.Equals(f.Key, key, StringComparison.Ordinal))
				?? throw new InvalidOperationException($"Field '{key}' is not declared yet.");
		}

		public ProtoSchema Build()
		{
			return new ProtoSchema(_kind, _fields);
		}

		private SchemaBuilder Add(ProtoField field)
		{
			_fields.Add(field);
			_offset += field.Size;
			return this;
		}
	}
}