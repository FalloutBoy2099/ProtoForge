namespace ProtoForge.Core.Tests
{
	using System;
	using System.IO;
	using System.Linq;
	using ProtoForge.Core.Editing;
	using ProtoForge.Infrastructure.Rules;
	using Xunit;

	public class BulkRuleStoreTests : IDisposable
	{
		private readonly String _folder = Path.Combine(Path.GetTempPath(), "protoforge-rules-" + Guid.NewGuid().ToString("N"));

		public void Dispose()
		{
			if (Directory.Exists(_folder))
			{
				Directory.Delete(_folder, recursive: true);
			}
		}

		[Fact]
		public void SavedRules_AreKept_ReplacedByName_AndDeleted()
		{
			BulkRuleStore store = new(Path.Combine(_folder, "rules.json"));
			Assert.Empty(store.Load());

			store.Save(new BulkRule("Stronger pistols", "Weapon", "MaxDamage", BulkOperation.Multiply, 1.2, "MaxRangePrimary", BulkComparison.Greater, 20));
			store.Save(new BulkRule("Flat", "Armor", "Flags", BulkOperation.SetFlag, 8));
			store.Save(new BulkRule("stronger pistols", "Weapon", "MaxDamage", BulkOperation.Multiply, 1.5));

			BulkRule[] rules = [.. store.Load()];
			Assert.Equal(["Flat", "stronger pistols"], rules.Select(r => r.Name));
			Assert.Equal(1.5, rules[1].Operand);
			Assert.Null(rules[1].ConditionField);
			Assert.Equal(BulkOperation.SetFlag, rules[0].Operation);
			Assert.True(store.Exists("FLAT"));

			store.Delete("flat");
			Assert.Equal(["stronger pistols"], store.Load().Select(r => r.Name));
		}

		[Fact]
		public void AFileThatCannotBeRead_GivesNoRule_AndIsLeftAlone()
		{
			Directory.CreateDirectory(_folder);
			String path = Path.Combine(_folder, "rules.json");
			File.WriteAllText(path, "{ not json");

			Assert.Empty(new BulkRuleStore(path).Load());
			Assert.Equal("{ not json", File.ReadAllText(path));
		}
	}
}