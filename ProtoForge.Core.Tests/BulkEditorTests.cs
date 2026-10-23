namespace ProtoForge.Core.Tests
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Editing;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Templates;
	using Xunit;

	public class BulkEditorTests
	{
		private static List<ProtoEntry> Weapons(params (Int32 Damage, Int32 Range)[] values)
		{
			List<ProtoEntry> entries = [];
			for (Int32 i = 0; i < values.Length; i++)
			{
				ProtoId id = ProtoId.From(ProtoObjectType.Item, i + 1);
				ProtoFile file = BuiltInTemplates.Default(ProtoKind.Weapon).Create(id);
				file.TrySet(FieldKeys.MaxDamage, values[i].Damage);
				file.TrySet("MaxRangePrimary", values[i].Range);
				entries.Add(new ProtoEntry(id, $"{i + 1:D8}.pro", file, null, "master.dat"));
			}

			return entries;
		}

		[Fact]
		public void Multiply_RoundsToTheNearestWholeNumber()
		{
			List<ProtoEntry> entries = Weapons((10, 1), (15, 1), (7, 1));

			IReadOnlyList<BulkChange> plan = BulkEditor.Plan(entries, ProtoKind.Weapon, FieldKeys.MaxDamage, BulkOperation.Multiply, 1.2);

			Assert.Equal([12, 18, 8], plan.Select(c => c.NewValue));
			Assert.Equal([10, 15, 7], plan.Select(c => c.OldValue));
		}

		[Fact]
		public void Condition_LimitsTheEditToTheMatchingPrototypes()
		{
			List<ProtoEntry> entries = Weapons((10, 5), (10, 25), (10, 40));

			IReadOnlyList<BulkChange> plan = BulkEditor.Plan(entries, ProtoKind.Weapon, FieldKeys.MaxDamage, BulkOperation.Add, 5, new BulkCondition("MaxRangePrimary", BulkComparison.Greater, 20));

			Assert.Equal([entries[1], entries[2]], plan.Select(c => c.Entry));
			Assert.All(plan, c => Assert.Equal(15, c.NewValue));
		}

		[Fact]
		public void Set_SkipsThePrototypesThatAlreadyHaveTheValue()
		{
			List<ProtoEntry> entries = Weapons((10, 1), (20, 1));

			IReadOnlyList<BulkChange> plan = BulkEditor.Plan(entries, ProtoKind.Weapon, FieldKeys.MaxDamage, BulkOperation.Set, 20);

			Assert.Equal(entries[0], Assert.Single(plan).Entry);
		}

		[Fact]
		public void TheResultStaysInsideTheRangeOfTheField()
		{
			ProtoField field = ProtoSchemas.For(ProtoKind.Weapon).Require("BurstRounds");

			Assert.Equal(field.Max, BulkEditor.Compute(field, BulkOperation.Multiply, 1000, 50));
			Assert.Equal(field.Min, BulkEditor.Compute(field, BulkOperation.Add, -1000, 5));
		}

		[Fact]
		public void OtherKindsAreLeftAlone_AndImagesAndTextsCannotBeEdited()
		{
			List<ProtoEntry> entries = Weapons((10, 1));

			Assert.Empty(BulkEditor.Plan(entries, ProtoKind.Armor, "ArmorClass", BulkOperation.Add, 1));
			Assert.Empty(BulkEditor.Plan(entries, ProtoKind.Weapon, FieldKeys.FrmId, BulkOperation.Set, 1));

			IReadOnlyList<ProtoField> fields = BulkEditor.GetEditableFields(ProtoKind.Weapon);
			Assert.Contains(fields, f => f.Key == FieldKeys.MaxDamage);
			Assert.Contains(fields, f => f.Key == "DamageType" && f.Editor == FieldEditor.Choice);
			Assert.Contains(fields, f => f.Key == FieldKeys.Flags && f.Editor == FieldEditor.Flags);
			Assert.DoesNotContain(fields, f => f.Editor is FieldEditor.Art or FieldEditor.Proto or FieldEditor.Message or FieldEditor.Script);
		}

		[Fact]
		public void Flags_AreTurnedOnAndOff_AndTheExclusiveOnesAreSwitched()
		{
			ProtoField flags = ProtoSchemas.For(ProtoKind.Weapon).Require(FieldKeys.Flags);
			const Int32 flat = 0x00000008;
			const Int32 transparentRed = 0x00004000;
			const Int32 transparentWall = 0x00010000;

			Assert.Equal(flat, BulkEditor.Compute(flags, BulkOperation.SetFlag, flat, 0));
			Assert.Equal(0, BulkEditor.Compute(flags, BulkOperation.ClearFlag, flat, flat));
			Assert.Equal(transparentWall, BulkEditor.Compute(flags, BulkOperation.SetFlag, transparentWall, transparentRed));
			Assert.Equal(unchecked((Int32)0x80000008), BulkEditor.Compute(flags, BulkOperation.SetFlag, 0x80000000u, flat));
		}

		[Fact]
		public void Choice_TakesTheValue_AndOnlyTheChangedPrototypesAreListed()
		{
			List<ProtoEntry> entries = Weapons((10, 1), (10, 1));
			entries[1].File!.TrySet("DamageType", 3);

			IReadOnlyList<BulkChange> plan = BulkEditor.Plan(entries, ProtoKind.Weapon, "DamageType", BulkOperation.Set, 3);

			Assert.Equal(entries[0], Assert.Single(plan).Entry);
			Assert.Equal(3, plan[0].NewValue);
		}
	}
}