namespace ProtoForge.Core.Formats.Pro
{
	using System;

	/// <summary>Object type stored in the high byte of a prototype ID (PID).</summary>
	public enum ProtoObjectType : Byte
	{
		Item = 0,
		Critter = 1,
		Scenery = 2,
		Wall = 3,
		Tile = 4,
		Misc = 5,
	}

	public static class ProtoObjectTypeExtensions
	{
		public static readonly ProtoObjectType[] All =
		[
			ProtoObjectType.Item,
			ProtoObjectType.Critter,
			ProtoObjectType.Scenery,
			ProtoObjectType.Wall,
			ProtoObjectType.Tile,
			ProtoObjectType.Misc,
		];

		/// <summary>Folder name under <c>proto/</c> and <c>art/</c>.</summary>
		public static String GetFolderName(this ProtoObjectType type)
		{
			return type switch
			{
				ProtoObjectType.Item => "items",
				ProtoObjectType.Critter => "critters",
				ProtoObjectType.Scenery => "scenery",
				ProtoObjectType.Wall => "walls",
				ProtoObjectType.Tile => "tiles",
				ProtoObjectType.Misc => "misc",
				_ => throw new ArgumentOutOfRangeException(nameof(type)),
			};
		}

		/// <summary>Virtual path of the prototype directory, e.g. <c>proto/items</c>.</summary>
		public static String GetProtoDirectory(this ProtoObjectType type)
		{
			return "proto/" + type.GetFolderName();
		}

		/// <summary>Virtual path of the LST file listing prototypes, e.g. <c>proto/items/items.lst</c>.</summary>
		public static String GetListPath(this ProtoObjectType type)
		{
			return type.GetProtoDirectory() + "/" + type.GetFolderName() + ".lst";
		}

		/// <summary>Name of the MSG file holding names and descriptions (relative to <c>text/&lt;lang&gt;/game/</c>).</summary>
		public static String GetMessageFileName(this ProtoObjectType type)
		{
			return type switch
			{
				ProtoObjectType.Item => "pro_item.msg",
				ProtoObjectType.Critter => "pro_crit.msg",
				ProtoObjectType.Scenery => "pro_scen.msg",
				ProtoObjectType.Wall => "pro_wall.msg",
				ProtoObjectType.Tile => "pro_tile.msg",
				ProtoObjectType.Misc => "pro_misc.msg",
				_ => throw new ArgumentOutOfRangeException(nameof(type)),
			};
		}

		/// <summary>Virtual path of the art LST for the object type, e.g. <c>art/items/items.lst</c>.</summary>
		public static String GetArtListPath(this ProtoObjectType type)
		{
			return "art/" + type.GetFolderName() + "/" + type.GetFolderName() + ".lst";
		}
	}
}