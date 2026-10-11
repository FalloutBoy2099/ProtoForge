namespace ProtoForge.Core.Formats.Pro
{
	using System;

	/// <summary>Kind of editor used to present a prototype field.</summary>
	public enum FieldEditor
	{
		Number,
		Choice,
		Flags,
		Art,
		Proto,
		Message,
		Script,
		LightIntensity,
		Character,
	}

	/// <summary>Object type encoded in bits 24..27 of a frame ID (FID).</summary>
	public enum ArtType
	{
		Item = 0,
		Critter = 1,
		Scenery = 2,
		Wall = 3,
		Tile = 4,
		Misc = 5,
		Interface = 6,
		Inventory = 7,
		Heads = 8,
		Background = 9,
		Skilldex = 10,
		Dialog = 11,
	}

	/// <summary>
	/// Describes one editable value inside a prototype file: where it lives, how it is stored and how it should be edited.
	/// Schemas are pure data, so the editor panels, validation, diff, hex overlay and import/export are all driven by them.
	/// </summary>
	public sealed class ProtoField
	{
		public ProtoField(String key, String group, Int32 offset, Int32 size, FieldEditor editor)
		{
			Key = key;
			Group = group;
			Offset = offset;
			Size = size;
			Editor = editor;
		}

		/// <summary>Stable identifier used for JSON/CSV, localisation and bindings.</summary>
		public String Key { get; }

		/// <summary>Logical section of the editor (General, Appearance, Weapon, ...).</summary>
		public String Group { get; }

		public Int32 Offset { get; }

		/// <summary>Storage size in bytes: 4 (big-endian Int32) or 1.</summary>
		public Int32 Size { get; }

		public FieldEditor Editor { get; }

		public Int32 Min { get; init; } = Int32.MinValue;

		public Int32 Max { get; init; } = Int32.MaxValue;

		public Int32 DefaultValue { get; init; }

		/// <summary>Identifier of the <see cref="Reference.ChoiceCatalog"/> list for <see cref="FieldEditor.Choice"/>.</summary>
		public String? ChoiceSet { get; init; }

		/// <summary>Identifier of the <see cref="FlagSets"/> list for <see cref="FieldEditor.Flags"/>.</summary>
		public String? FlagSet { get; init; }

		public ArtType? ArtType { get; init; }

		/// <summary>For <see cref="FieldEditor.Proto"/>: object type of the referenced prototype.</summary>
		public ProtoObjectType? ReferenceType { get; init; }

		/// <summary>For <see cref="FieldEditor.Proto"/>: optional restriction to one kind (e.g. ammo only).</summary>
		public ProtoKind? ReferenceKind { get; init; }

		/// <summary>Bit mask applied on the stored value (sub-fields such as the weapon attack modes).</summary>
		public UInt32 Mask { get; init; } = UInt32.MaxValue;

		public Int32 Shift { get; init; }

		/// <summary>Not shown in the standard editor; displayed in the advanced mode only.</summary>
		public Boolean IsAdvanced { get; init; }

		public Boolean IsReadOnly { get; init; }

		/// <summary>A sub-field sharing storage with another one (bit mask over a stored value).</summary>
		public Boolean IsVirtual
		{
			get { return Mask != UInt32.MaxValue; }
		}

		public Int32 End
		{
			get { return Offset + Size; }
		}

		public override String ToString()
		{
			return Key + "@0x" + Offset.ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
		}
	}
}