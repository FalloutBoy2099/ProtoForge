namespace ProtoForge.Core.Formats.Frm
{
	using System;
	using Pro;

	/// <summary>
	/// Frame ID (FID) as stored in prototypes: <c>rotation(3) | type(4) | animation(8) | weapon(4) | index(12)</c>.
	/// </summary>
	public readonly record struct FrameId(Int32 Value)
	{
		public static readonly FrameId None = new(-1);

		public Boolean IsNone
		{
			get { return Value == -1; }
		}

		public ArtType Type
		{
			get { return (ArtType)((Value >> 24) & 0xF); }
		}

		/// <summary>Index into the art LST file of the type.</summary>
		public Int32 Index
		{
			get { return Value & 0xFFF; }
		}

		public Int32 Weapon
		{
			get { return (Value >> 12) & 0xF; }
		}

		public Int32 Animation
		{
			get { return (Value >> 16) & 0xFF; }
		}

		public Int32 Rotation
		{
			get { return (Value >> 28) & 0x7; }
		}

		public static FrameId Create(ArtType type, Int32 index, Int32 animation = 0, Int32 weapon = 0, Int32 rotation = 0)
		{
			Int32 value = ((rotation & 0x7) << 28) | (((Int32)type & 0xF) << 24) | ((animation & 0xFF) << 16)
				| ((weapon & 0xF) << 12) | (index & 0xFFF);
			return new FrameId(value);
		}

		public static String GetFolderName(ArtType type)
		{
			return type switch
			{
				ArtType.Item => "items",
				ArtType.Critter => "critters",
				ArtType.Scenery => "scenery",
				ArtType.Wall => "walls",
				ArtType.Tile => "tiles",
				ArtType.Misc => "misc",
				ArtType.Interface => "intrface",
				ArtType.Inventory => "inven",
				ArtType.Heads => "heads",
				ArtType.Background => "backgrnd",
				ArtType.Skilldex => "skilldex",
				ArtType.Dialog => "dialog",
				_ => throw new ArgumentOutOfRangeException(nameof(type)),
			};
		}

		public override String ToString()
		{
			return "0x" + Value.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
		}
	}
}