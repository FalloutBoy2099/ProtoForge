namespace ProtoForge.Core.Formats.Pro
{
	using System;
	using System.Collections.Generic;
	using System.Linq;

	/// <summary>One named bit of a bit-field.</summary>
	public sealed record FlagBit(UInt32 Mask, String Name)
	{
		public Int32 SignedMask
		{
			get { return unchecked((Int32)Mask); }
		}
	}

	/// <summary>A named set of flag bits (object flags, extended flags, critter flags, ...).</summary>
	public sealed class FlagSet
	{
		public FlagSet(String id, IReadOnlyList<FlagBit> bits, UInt32 extraKnownMask = 0, IReadOnlyList<UInt32>? exclusiveGroups = null)
		{
			Id = id;
			Bits = bits;
			KnownMask = bits.Aggregate(extraKnownMask, (acc, b) => acc | b.Mask);
			ExclusiveGroups = exclusiveGroups ?? [];
		}

		public String Id { get; }

		public IReadOnlyList<FlagBit> Bits { get; }

		/// <summary>Union of all documented bits.</summary>
		public UInt32 KnownMask { get; }

		/// <summary>Groups of bits of which at most one may be set (the transparency modes, big gun / energy weapon).</summary>
		public IReadOnlyList<UInt32> ExclusiveGroups { get; }

		/// <summary>The other bits that must be cleared when <paramref name="mask"/> is turned on.</summary>
		public UInt32 GetConflicts(UInt32 mask)
		{
			UInt32 conflicts = 0;
			foreach (UInt32 group in ExclusiveGroups)
			{
				if ((group & mask) != 0)
				{
					conflicts |= group & ~mask;
				}
			}

			return conflicts;
		}

		public IEnumerable<FlagBit> GetActive(Int32 value)
		{
			UInt32 raw = unchecked((UInt32)value);
			return Bits.Where(b => (raw & b.Mask) == b.Mask);
		}
	}

	/// <summary>Registry of the flag bit-fields found in prototypes (names follow the original Fallout mapper).</summary>
	public static class FlagSets
	{
		public const String ObjectFlags = "ObjectFlags";
		public const String ExtFlags = "ExtFlags";
		public const String WeaponExtFlags = "WeaponExtFlags";
		public const String CritterFlags = "CritterFlags";
		public const String ContainerFlags = "ContainerFlags";
		public const String DoorFlags = "DoorFlags";

		/// <summary>The six transparency bits are alternatives: the engine only uses one of them (TransNone means "opaque").</summary>
		public const UInt32 TransparencyModes = 0x000FC000;

		/// <summary>A weapon is either a big gun or an energy weapon.</summary>
		public const UInt32 BigGunOrEnergy = 0x00000100 | 0x00000400;

		private static readonly Dictionary<String, FlagSet> Sets = Build();

		public static FlagSet Get(String id)
		{
			return Sets[id];
		}

		public static Boolean TryGet(String id, out FlagSet set)
		{
			return Sets.TryGetValue(id, out set!);
		}

		public static IEnumerable<FlagSet> All
		{
			get { return Sets.Values; }
		}

		private static Dictionary<String, FlagSet> Build()
		{
			List<FlagBit> extItem =
			[
				new FlagBit(0x00000800, "Use"),
				new FlagBit(0x00001000, "UseOn"),
				new FlagBit(0x00002000, "Look"),
				new FlagBit(0x00004000, "Talk"),
				new FlagBit(0x00008000, "PickUp"),
				new FlagBit(0x08000000, "HiddenItem"),
			];

			List<FlagBit> extWeapon = [.. extItem];
			extWeapon.Insert(0, new FlagBit(0x00000100, "BigGun"));
			extWeapon.Insert(1, new FlagBit(0x00000200, "TwoHand"));
			extWeapon.Insert(2, new FlagBit(0x00000400, "Energy"));

			List<FlagSet> sets =
			[
				new FlagSet(ObjectFlags,
				[
					new FlagBit(0x00000001, "Mouse3d"),
					new FlagBit(0x00000004, "WalkThru"),
					new FlagBit(0x00000008, "Flat"),
					new FlagBit(0x00000010, "NoBlock"),
					new FlagBit(0x00000020, "Lighting"),
					new FlagBit(0x00000400, "Temp"),
					new FlagBit(0x00000800, "MultiHex"),
					new FlagBit(0x00001000, "NoHighlight"),
					new FlagBit(0x00002000, "Used"),
					new FlagBit(0x00004000, "TransRed"),
					new FlagBit(0x00008000, "TransNone"),
					new FlagBit(0x00010000, "TransWall"),
					new FlagBit(0x00020000, "TransGlass"),
					new FlagBit(0x00040000, "TransSteam"),
					new FlagBit(0x00080000, "TransEnergy"),
					new FlagBit(0x01000000, "LeftHand"),
					new FlagBit(0x02000000, "RightHand"),
					new FlagBit(0x04000000, "Worn"),
					new FlagBit(0x10000000, "WallTransEnd"),
					new FlagBit(0x20000000, "LightThru"),
					new FlagBit(0x40000000, "Seen"),
					new FlagBit(0x80000000, "ShootThru"),
				],
				exclusiveGroups: [TransparencyModes]),
				new FlagSet(ExtFlags, extItem),
				// The low byte holds the primary and secondary attack modes (edited as separate fields), not flags.
				new FlagSet(WeaponExtFlags, extWeapon, 0xFFu, [BigGunOrEnergy]),
				new FlagSet(CritterFlags,
				[
					new FlagBit(0x00000002, "Barter"),
					new FlagBit(0x00000020, "NoSteal"),
					new FlagBit(0x00000040, "NoDrop"),
					new FlagBit(0x00000080, "NoLimbs"),
					new FlagBit(0x00000100, "Ages"),
					new FlagBit(0x00000200, "NoHeal"),
					new FlagBit(0x00000400, "Invulnerable"),
					new FlagBit(0x00000800, "NoFlatten"),
					new FlagBit(0x00001000, "SpecialDeath"),
					new FlagBit(0x00002000, "RangedHandToHand"),
					new FlagBit(0x00004000, "NoKnockBack"),
				]),
				new FlagSet(ContainerFlags,
				[
					new FlagBit(0x00000001, "CannotPickUp"),
					new FlagBit(0x00000008, "MagicHandsGround"),
				]),
				new FlagSet(DoorFlags,
				[
					new FlagBit(0x00000001, "WalkThruOpen"),
				]),
			];

			return sets.ToDictionary(s => s.Id, StringComparer.Ordinal);
		}
	}
}