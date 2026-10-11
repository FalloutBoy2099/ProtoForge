namespace ProtoForge.Core.Formats.Pro
{
	using System;

	/// <summary>Item sub types stored at offset 0x20 of an item prototype.</summary>
	public enum ItemSubType
	{
		Armor = 0,
		Container = 1,
		Drug = 2,
		Weapon = 3,
		Ammo = 4,
		Misc = 5,
		Key = 6,
	}

	/// <summary>Scenery sub types stored at offset 0x20 of a scenery prototype.</summary>
	public enum SceneryType
	{
		Door = 0,
		Stairs = 1,
		Elevator = 2,
		LadderBottom = 3,
		LadderTop = 4,
		Generic = 5,
	}

	/// <summary>
	/// Concrete prototype layout. A kind selects the field schema and the expected binary size.
	/// </summary>
	public enum ProtoKind
	{
		Armor,
		Container,
		Drug,
		Weapon,
		Ammo,
		MiscItem,
		Key,
		Critter,
		SceneryDoor,
		SceneryStairs,
		SceneryElevator,
		SceneryLadderBottom,
		SceneryLadderTop,
		SceneryGeneric,
		Wall,
		Tile,
		Misc,
	}

	public static class ProtoKindExtensions
	{
		public static readonly ProtoKind[] All = Enum.GetValues<ProtoKind>();

		public static ProtoObjectType GetObjectType(this ProtoKind kind)
		{
			switch (kind)
			{
				case ProtoKind.Armor:
				case ProtoKind.Container:
				case ProtoKind.Drug:
				case ProtoKind.Weapon:
				case ProtoKind.Ammo:
				case ProtoKind.MiscItem:
				case ProtoKind.Key:
					return ProtoObjectType.Item;
				case ProtoKind.Critter:
					return ProtoObjectType.Critter;
				case ProtoKind.SceneryDoor:
				case ProtoKind.SceneryStairs:
				case ProtoKind.SceneryElevator:
				case ProtoKind.SceneryLadderBottom:
				case ProtoKind.SceneryLadderTop:
				case ProtoKind.SceneryGeneric:
					return ProtoObjectType.Scenery;
				case ProtoKind.Wall:
					return ProtoObjectType.Wall;
				case ProtoKind.Tile:
					return ProtoObjectType.Tile;
				case ProtoKind.Misc:
					return ProtoObjectType.Misc;
				default:
					throw new ArgumentOutOfRangeException(nameof(kind));
			}
		}

		/// <summary>Size in bytes of a well-formed file of this kind.</summary>
		public static Int32 GetExpectedSize(this ProtoKind kind)
		{
			return kind switch
			{
				ProtoKind.Armor => 129,
				ProtoKind.Container => 65,
				ProtoKind.Drug => 125,
				ProtoKind.Weapon => 122,
				ProtoKind.Ammo => 81,
				ProtoKind.MiscItem => 69,
				ProtoKind.Key => 61,
				ProtoKind.Critter => 416,
				ProtoKind.SceneryDoor => 49,
				ProtoKind.SceneryStairs => 49,
				ProtoKind.SceneryElevator => 49,
				ProtoKind.SceneryLadderBottom => 45,
				ProtoKind.SceneryLadderTop => 45,
				ProtoKind.SceneryGeneric => 45,
				ProtoKind.Wall => 36,
				ProtoKind.Tile => 28,
				ProtoKind.Misc => 28,
				_ => throw new ArgumentOutOfRangeException(nameof(kind)),
			};
		}

		/// <summary>Alternative accepted size (Fallout 1 critters have no damage type field).</summary>
		public static Int32? GetAlternativeSize(this ProtoKind kind)
		{
			return kind == ProtoKind.Critter ? 412 : null;
		}

		public static Boolean IsItem(this ProtoKind kind)
		{
			return kind.GetObjectType() == ProtoObjectType.Item;
		}

		public static Boolean IsScenery(this ProtoKind kind)
		{
			return kind.GetObjectType() == ProtoObjectType.Scenery;
		}

		/// <summary>Resolves the kind from the object type and the sub type stored at 0x20 (ignored for single-layout types).</summary>
		public static ProtoKind? Resolve(ProtoObjectType type, Int32 subType)
		{
			switch (type)
			{
				case ProtoObjectType.Item:
					return subType switch
					{
						0 => ProtoKind.Armor,
						1 => ProtoKind.Container,
						2 => ProtoKind.Drug,
						3 => ProtoKind.Weapon,
						4 => ProtoKind.Ammo,
						5 => ProtoKind.MiscItem,
						6 => ProtoKind.Key,
						_ => null,
					};
				case ProtoObjectType.Critter:
					return ProtoKind.Critter;
				case ProtoObjectType.Scenery:
					return subType switch
					{
						0 => ProtoKind.SceneryDoor,
						1 => ProtoKind.SceneryStairs,
						2 => ProtoKind.SceneryElevator,
						3 => ProtoKind.SceneryLadderBottom,
						4 => ProtoKind.SceneryLadderTop,
						5 => ProtoKind.SceneryGeneric,
						_ => null,
					};
				case ProtoObjectType.Wall:
					return ProtoKind.Wall;
				case ProtoObjectType.Tile:
					return ProtoKind.Tile;
				case ProtoObjectType.Misc:
					return ProtoKind.Misc;
				default:
					return null;
			}
		}

		/// <summary>Value written at offset 0x20 for kinds that carry a sub type (items, scenery).</summary>
		public static Int32? GetSubTypeValue(this ProtoKind kind)
		{
			return kind switch
			{
				ProtoKind.Armor => 0,
				ProtoKind.Container => 1,
				ProtoKind.Drug => 2,
				ProtoKind.Weapon => 3,
				ProtoKind.Ammo => 4,
				ProtoKind.MiscItem => 5,
				ProtoKind.Key => 6,
				ProtoKind.SceneryDoor => 0,
				ProtoKind.SceneryStairs => 1,
				ProtoKind.SceneryElevator => 2,
				ProtoKind.SceneryLadderBottom => 3,
				ProtoKind.SceneryLadderTop => 4,
				ProtoKind.SceneryGeneric => 5,
				_ => null,
			};
		}
	}
}