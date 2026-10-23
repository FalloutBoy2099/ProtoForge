namespace ProtoForge.Core.Tests
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Ai;
	using ProtoForge.Core.Formats.Frm;
	using ProtoForge.Core.Formats.Lst;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Reference;
	using ProtoForge.Core.Templates;
	using ProtoForge.Core.Validation;
	using ProtoForge.Core.Vfs;
	using ProtoForge.Infrastructure.Layers;
	using Xunit;

	/// <summary>The checks of <see cref="ConsistencyRule"/> and the script file check of <see cref="ScriptRule"/>.</summary>
	public class ConsistencyRuleTests : IDisposable
	{
		private readonly String _scripts = Path.Combine(Path.GetTempPath(), "protoforge-scripts-" + Guid.NewGuid().ToString("N"));

		public void Dispose()
		{
			if (Directory.Exists(_scripts))
			{
				Directory.Delete(_scripts, recursive: true);
			}
		}

		private static ProtoEntry Add(List<ProtoEntry> entries, ProtoKind kind, Int32 index)
		{
			ProtoId id = ProtoId.From(ProtoObjectType.Item, index);
			ProtoEntry entry = new(id, ProtoDuplicator.FormatFileName(index), BuiltInTemplates.Default(kind).Create(id), null, "master.dat");
			entries.Add(entry);
			return entry;
		}

		private ValidationContext Context(List<ProtoEntry> entries, IGameFileSystem files, IReadOnlyList<String>? scriptNames = null)
		{
			MessageStore messages = new(_ => null, Encoding.Latin1);
			ProtoCatalog catalog = new();
			LstFile list = new();
			foreach (ProtoEntry entry in entries)
			{
				list.Add(entry.FileName);
			}

			catalog.Load(ProtoObjectType.Item, list, entries);
			return new ValidationContext(catalog, files, messages, new ProtoTextService(messages), new ArtResolver(files), new ChoiceCatalog(), new AiPacketCatalog([]), scriptNames?.Count ?? 0, scriptNames);
		}

		private static IReadOnlyList<String> Run(ValidationContext context, params ProtoEntry[] entries)
		{
			List<ValidationIssue> issues = [];
			foreach (IValidationRule rule in new IValidationRule[] { new ConsistencyRule(), new ScriptRule() })
			{
				foreach (ProtoEntry entry in entries)
				{
					rule.Validate(context, entry, new IssueSink(issues, rule.Id));
				}

				rule.ValidateAll(context, new IssueSink(issues, rule.Id));
			}

			return [.. issues.Select(i => i.Code)];
		}

		[Fact]
		public void Weapon_BurstLargerThanMagazine_AndAmmoUseWithoutDefaultAmmo_AreReported()
		{
			List<ProtoEntry> entries = [];
			ProtoEntry weapon = Add(entries, ProtoKind.Weapon, 1);
			weapon.File!.TrySet("BurstRounds", 10);
			weapon.File.TrySet("MaxAmmo", 5);
			weapon.File.TrySet(FieldKeys.Caliber, 3);
			weapon.File.TrySet(FieldKeys.AmmoPid, -1);
			using LayeredFileSystem files = new([]);

			IReadOnlyList<String> codes = Run(Context(entries, files), weapon);

			Assert.Contains("BurstExceedsMagazine", codes);
			Assert.Contains("WeaponWithoutAmmo", codes);
		}

		[Fact]
		public void Weapon_WithAConsistentMagazine_RaisesNothing()
		{
			List<ProtoEntry> entries = [];
			ProtoEntry weapon = Add(entries, ProtoKind.Weapon, 1);
			weapon.File!.TrySet("BurstRounds", 5);
			weapon.File.TrySet("MaxAmmo", 12);
			weapon.File.TrySet(FieldKeys.MaxDamage, 10);
			weapon.File.TrySet("ApCostPrimary", 4);
			weapon.File.TrySet("MaxRangePrimary", 20);
			weapon.File.TrySet(FieldKeys.Caliber, 0);
			using LayeredFileSystem files = new([]);

			Assert.Empty(Run(Context(entries, files), weapon));
		}

		[Fact]
		public void ZeroApCost_AndZeroRange_AreReportedForADamagingWeapon()
		{
			List<ProtoEntry> entries = [];
			ProtoEntry weapon = Add(entries, ProtoKind.Weapon, 1);
			weapon.File!.TrySet(FieldKeys.MaxDamage, 10);
			weapon.File.TrySet("ApCostPrimary", 0);
			weapon.File.TrySet("MaxRangePrimary", 0);
			using LayeredFileSystem files = new([]);

			IReadOnlyList<String> codes = Run(Context(entries, files), weapon);

			Assert.Contains("ZeroApCost", codes);
			Assert.Contains("ZeroRange", codes);
		}

		[Fact]
		public void PowerCellThatDoesNotExist_IsReported()
		{
			List<ProtoEntry> entries = [];
			ProtoEntry misc = Add(entries, ProtoKind.MiscItem, 1);
			misc.File!.TrySet("PowerPid", ProtoId.From(ProtoObjectType.Item, 99).Value);
			using LayeredFileSystem files = new([]);

			Assert.Contains("ReferenceMissing", Run(Context(entries, files), misc));

			misc.File.TrySet("PowerPid", -1);
			Assert.DoesNotContain("ReferenceMissing", Run(Context(entries, files), misc));
		}

		[Fact]
		public void ItemWithoutInventoryImage_IsReported()
		{
			List<ProtoEntry> entries = [];
			ProtoEntry item = Add(entries, ProtoKind.MiscItem, 1);
			item.File!.TrySet(FieldKeys.InventoryFrmId, -1);
			using LayeredFileSystem files = new([]);

			Assert.Contains("NoInventoryArt", Run(Context(entries, files), item));
		}

		[Fact]
		public void AmmoNoWeaponUses_IsReported()
		{
			List<ProtoEntry> entries = [];
			ProtoEntry weapon = Add(entries, ProtoKind.Weapon, 1);
			weapon.File!.TrySet(FieldKeys.Caliber, 1);
			ProtoEntry ammo = Add(entries, ProtoKind.Ammo, 2);
			ammo.File!.TrySet(FieldKeys.Caliber, 7);
			ProtoEntry matching = Add(entries, ProtoKind.Ammo, 3);
			matching.File!.TrySet(FieldKeys.Caliber, 1);
			using LayeredFileSystem files = new([]);

			List<ValidationIssue> issues = [];
			new ConsistencyRule().ValidateAll(Context(entries, files), new IssueSink(issues, "consistency"));

			ValidationIssue issue = Assert.Single(issues, i => i.Code == "AmmoWithoutWeapon");
			Assert.Equal(ammo.Pid, issue.Pid);
		}

		[Fact]
		public void Script_ListedButWithoutFile_IsReported_UntilTheFileExists()
		{
			List<ProtoEntry> entries = [];
			ProtoEntry item = Add(entries, ProtoKind.MiscItem, 1);
			item.File!.TrySet(FieldKeys.ScriptId, 0x03000000);
			Directory.CreateDirectory(Path.Combine(_scripts, "scripts"));
			using LayeredFileSystem files = new([new DirectoryLayer(new LayerInfo(0, "data", _scripts, false, false), _scripts)]);
			IReadOnlyList<String> names = ["there.int"];

			Assert.Contains("ScriptFileMissing", Run(Context(entries, files, names), item));

			File.WriteAllText(Path.Combine(_scripts, "scripts", "there.int"), "x");
			Assert.DoesNotContain("ScriptFileMissing", Run(Context(entries, files, names), item));
		}
	}
}