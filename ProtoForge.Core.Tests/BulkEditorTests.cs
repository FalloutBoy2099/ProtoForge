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
		public void OtherKindsAreLeftAlone_AndOnlyPlainNumbersCanBeEdited()
		{
			List<ProtoEntry> entries = Weapons((10, 1));

			Assert.Empty(BulkEditor.Plan(entries, ProtoKind.Armor, "ArmorClass", BulkOperation.Add, 1));
			Assert.Empty(BulkEditor.Plan(entries, ProtoKind.Weapon, FieldKeys.Flags, BulkOperation.Set, 1));

			IReadOnlyList<ProtoField> fields = BulkEditor.GetEditableFields(ProtoKind.Weapon);
			Assert.Contains(fields, f => f.Key == FieldKeys.MaxDamage);
			Assert.DoesNotContain(fields, f => f.Key == FieldKeys.Flags);
			Assert.DoesNotContain(fields, f => f.Editor != FieldEditor.Number);
		}
	}
}