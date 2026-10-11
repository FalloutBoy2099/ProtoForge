namespace ProtoForge.Core.Tests
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Lst;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Reference;
	using ProtoForge.Core.Serialization;
	using ProtoForge.Core.Templates;
	using Xunit;

	public class CsvOptionsTests
	{
		private static (ProtoCatalog Catalog, ProtoEntry Entry) CreateCatalog()
		{
			ProtoId pid = ProtoId.From(ProtoObjectType.Item, 1);
			ProtoFile file = ProtoFactory.CreateDefault(ProtoKind.Weapon, pid);
			file.TrySet("MinDamage", 4);
			file.TrySet("DamageType", 2);
			file.TrySet(FieldKeys.Flags, 0x00000008 | 0x00004000);
			ProtoEntry entry = new(pid, "00000001.pro", file, null, "test");
			entry.SetTexts("Gun", "A gun.", asBaseline: true);
			ProtoCatalog catalog = new();
			catalog.Load(ProtoObjectType.Item, new LstFile(["00000001.pro"]), [entry]);
			return (catalog, entry);
		}

		[Fact]
		public void Export_WithColumns_WritesOnlyTheChosenFields()
		{
			(_, ProtoEntry entry) = CreateCatalog();
			CsvExportOptions options = new() { Columns = new HashSet<String> { "MinDamage", "Weight" } };

			String csv = ProtoCsv.ExportKind(ProtoKind.Weapon, [entry], ';', options);

			Assert.Equal("Pid;File;Name;Description;Weight;MinDamage", csv.Split("\r\n")[0]);
		}

		[Fact]
		public void ReadableExport_UsesNamesAndFlagColumns()
		{
			(_, ProtoEntry entry) = CreateCatalog();
			CsvExportOptions options = new() { Readable = true, Choices = new ChoiceCatalog(), Columns = new HashSet<String> { "DamageType", FieldKeys.Flags } };

			String[] lines = ProtoCsv.ExportKind(ProtoKind.Weapon, [entry], ';', options).Split("\r\n");
			String[] header = lines[0].Split(';');
			String[] row = lines[1].Split(';');

			Assert.Equal("Fire", row[Array.IndexOf(header, "DamageType")]);
			Assert.Equal("true", row[Array.IndexOf(header, "Flags.Flat")]);
			Assert.Equal("true", row[Array.IndexOf(header, "Flags.TransRed")]);
			Assert.Equal("false", row[Array.IndexOf(header, "Flags.Mouse3d")]);
		}

		[Fact]
		public void ReadableCsv_CanBeImportedAgain()
		{
			(ProtoCatalog catalog, ProtoEntry entry) = CreateCatalog();
			ChoiceCatalog choices = new();
			String csv = "Pid;Name;DamageType;Flags.Flat;Flags.TransRed;Flags.Mouse3d\r\n"
				+ $"{entry.Pid};Gun;Laser;false;false;true\r\n";

			ImportPlan plan = ProtoCsv.CreatePlan(csv, catalog, ';', choices);
			catalog.Find(entry.Pid)!.File!.TrySet("DamageType", 0);
			plan.ToAction(new ProtoTextService(new MessageStore(_ => null, System.Text.Encoding.Latin1)), "import").Do();

			ProtoFile file = entry.File!;
			Assert.Equal(1, file.Get("DamageType"));
			Assert.Equal(0x1, file.Get(FieldKeys.Flags) & 0x1);
			Assert.Equal(0, file.Get(FieldKeys.Flags) & (0x00000008 | 0x00004000));
		}

		[Fact]
		public void Import_IgnoresLinesStartingWithHash()
		{
			(ProtoCatalog catalog, ProtoEntry entry) = CreateCatalog();
			String csv = "Pid;MinDamage\r\n" + $"#{entry.Pid};99\r\n";

			ImportPlan plan = ProtoCsv.CreatePlan(csv, catalog);

			Assert.True(plan.IsEmpty);
			Assert.Contains(plan.Warnings, w => w.Contains("ignored", StringComparison.Ordinal));
		}
	}
}