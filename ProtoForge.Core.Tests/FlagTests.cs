namespace ProtoForge.Core.Tests
{
	using System;
	using ProtoForge.Core.Formats.Pro;
	using Xunit;

	public class FlagTests
	{
		[Fact]
		public void TransparencyModes_AreAlternatives()
		{
			FlagSet set = FlagSets.Get(FlagSets.ObjectFlags);

			UInt32 conflicts = set.GetConflicts(0x00010000);

			Assert.Equal(FlagSets.TransparencyModes & ~0x00010000u, conflicts);
			Assert.Equal(0u, set.GetConflicts(0x00000008));
		}

		[Fact]
		public void BigGunAndEnergy_AreAlternatives()
		{
			FlagSet set = FlagSets.Get(FlagSets.WeaponExtFlags);

			Assert.Equal(0x400u, set.GetConflicts(0x100));
			Assert.Equal(0x100u, set.GetConflicts(0x400));
			Assert.Equal(0u, set.GetConflicts(0x200));
		}

		[Theory]
		[InlineData("Mouse3d")]
		[InlineData("WalkThru")]
		public void OtherObjectFlags_HaveNoConflict(String name)
		{
			FlagSet set = FlagSets.Get(FlagSets.ObjectFlags);
			UInt32 mask = 0;
			foreach (FlagBit bit in set.Bits)
			{
				if (bit.Name == name)
				{
					mask = bit.Mask;
				}
			}

			Assert.Equal(0u, set.GetConflicts(mask));
		}
	}
}