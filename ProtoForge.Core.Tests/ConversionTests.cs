namespace ProtoForge.Core.Tests
{
	using System;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Templates;
	using Xunit;

	public class ConversionTests
	{
		[Fact]
		public void WeaponToMisc_KeepsTheCommonDataAndDropsTheWeaponData()
		{
			ProtoId pid = ProtoId.From(ProtoObjectType.Item, 12);
			ProtoFile weapon = ProtoFactory.CreateDefault(ProtoKind.Weapon, pid);
			weapon.TrySet(FieldKeys.Weight, 5);
			weapon.TrySet(FieldKeys.Cost, 700);
			weapon.TrySet(FieldKeys.FrmId, 0x00000022);
			weapon.TrySet(FieldKeys.MessageId, 1200);
			weapon.TrySet("MinDamage", 9);
			weapon.TrySet(FieldKeys.FlagsExt, 0xA000 | 0x400 | 0x16);

			ProtoFile misc = ProtoConversion.Convert(weapon, ProtoKind.MiscItem);

			Assert.Equal(ProtoKind.MiscItem, misc.Kind);
			Assert.Equal(ProtoKind.MiscItem.GetExpectedSize(), misc.Length);
			Assert.Equal(pid.Value, misc.StoredProtoId);
			Assert.Equal(5, misc.Get(FieldKeys.SubType));
			Assert.Equal(5, misc.Get(FieldKeys.Weight));
			Assert.Equal(700, misc.Get(FieldKeys.Cost));
			Assert.Equal(0x22, misc.Get(FieldKeys.FrmId));
			Assert.Equal(1200, misc.Get(FieldKeys.MessageId));
			Assert.Equal(0xA000, misc.Get(FieldKeys.FlagsExt));
			Assert.Null(misc.TryGet("MinDamage"));
		}

		[Fact]
		public void DoorToStairs_ChangesTheSizeAndTheSubType()
		{
			ProtoFile door = ProtoFactory.CreateDefault(ProtoKind.SceneryDoor, ProtoId.From(ProtoObjectType.Scenery, 3));

			ProtoFile ladder = ProtoConversion.Convert(door, ProtoKind.SceneryLadderBottom);

			Assert.Equal(ProtoKind.SceneryLadderBottom.GetExpectedSize(), ladder.Length);
			Assert.Equal(3, ladder.Get(FieldKeys.SubType));
		}

		[Fact]
		public void OtherFamilies_AreNotConvertible()
		{
			Assert.False(ProtoConversion.CanConvert(ProtoKind.Weapon, ProtoKind.Critter));
			Assert.False(ProtoConversion.CanConvert(ProtoKind.Critter, ProtoKind.Critter));
			Assert.Empty(ProtoConversion.GetTargets(ProtoKind.Wall));
			Assert.Equal(7, ProtoConversion.GetTargets(ProtoKind.Armor).Count);
		}
	}
}