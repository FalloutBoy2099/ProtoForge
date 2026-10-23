namespace ProtoForge.Core.Tests.Domain
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using Xunit;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Editing;
	using ProtoForge.Core.Formats.Lst;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Serialization;
	using ProtoForge.Core.Templates;

	public class EditingTests
	{
		private sealed class Fixture
		{
			public Fixture()
			{
				Messages = new MessageStore(_ => null, Encoding.Latin1);
				Texts = new ProtoTextService(Messages);
				Catalog = new ProtoCatalog();
				List<ProtoEntry> entries = [];
				LstFile list = new();
				for (Int32 i = 1; i <= 3; i++)
				{
					ProtoFile file = BuiltInTemplates.Default(ProtoKind.Weapon).Create(ProtoId.From(ProtoObjectType.Item, i));
					file.TrySet(FieldKeys.MinDamage, i);
					file.TrySet(FieldKeys.MaxDamage, i + 10);
					String fileName = ProtoDuplicator.FormatFileName(i);
					list.Add(fileName);
					entries.Add(new ProtoEntry(ProtoId.From(ProtoObjectType.Item, i), fileName, file, null, "master.dat"));
					Messages.GetOrCreate("pro_item.msg").SetText(i * 100, "Weapon " + i);
				}

				Catalog.Load(ProtoObjectType.Item, list, entries);
				Messages.AcceptChanges();
				Texts.LoadAll(entries);
			}

			public MessageStore Messages { get; }

			public ProtoTextService Texts { get; }

			public ProtoCatalog Catalog { get; }

			public UndoManager Undo { get; } = new();

			public ProtoEntry this[Int32 index]
			{
				get { return Catalog.Find(ProtoId.From(ProtoObjectType.Item, index))!; }
			}
		}

		[Fact]
		public void FieldEdit_IsUndoableAndTracksModification()
		{
			Fixture f = new();
			ProtoField field = f[1].File!.Schema.Require(FieldKeys.MinDamage);

			f.Undo.Execute(new FieldEditAction([f[1]], field, 42, "edit"));

			Assert.Equal(42, f[1].File!.Get(FieldKeys.MinDamage));
			Assert.True(f[1].IsModified);
			Assert.True(f.Undo.CanUndo);

			f.Undo.Undo();
			Assert.Equal(1, f[1].File!.Get(FieldKeys.MinDamage));
			Assert.False(f[1].IsModified);

			f.Undo.Redo();
			Assert.Equal(42, f[1].File!.Get(FieldKeys.MinDamage));
		}

		[Fact]
		public void MassEdit_ChangesEveryEntryInOneUndoStep()
		{
			Fixture f = new();
			ProtoField field = f[1].File!.Schema.Require(FieldKeys.MaxDamage);
			ProtoEntry[] all = [f[1], f[2], f[3]];

			f.Undo.Execute(new FieldEditAction(all, field, 99, "mass"));

			Assert.All(all, e => Assert.Equal(99, e.File!.Get(FieldKeys.MaxDamage)));
			f.Undo.Undo();
			Assert.Equal([11, 12, 13], all.Select(e => e.File!.Get(FieldKeys.MaxDamage)));
			Assert.False(f.Undo.CanUndo);
		}

		[Fact]
		public void ConsecutiveEditsOfTheSameField_AreMerged()
		{
			Fixture f = new();
			ProtoField field = f[1].File!.Schema.Require(FieldKeys.MinDamage);

			f.Undo.Execute(new FieldEditAction([f[1]], field, 5, "edit"));
			f.Undo.Execute(new FieldEditAction([f[1]], field, 6, "edit"));
			f.Undo.Execute(new FieldEditAction([f[1]], field, 7, "edit"));

			Assert.Single(f.Undo.UndoHistory);
			f.Undo.Undo();
			Assert.Equal(1, f[1].File!.Get(FieldKeys.MinDamage));
		}

		[Fact]
		public void Duplicate_AppendsAtTheEndOfTheListWithANewPidAndMessageId()
		{
			Fixture f = new();
			(ProtoFile file, String fileName, String name, String description) = ProtoDuplicator.Duplicate(f[2], f.Catalog.NextIndex(ProtoObjectType.Item), " (copy)");
			AddProtoAction action = new(f.Catalog, f.Texts, ProtoObjectType.Item, file, fileName, "Output", name, description, "duplicate");

			f.Undo.Execute(action);

			ProtoEntry copy = action.Entry!;
			Assert.Equal(4, copy.Index);
			Assert.Equal("00000004.pro", copy.FileName);
			Assert.Equal(ProtoId.From(ProtoObjectType.Item, 4).Value, copy.File!.StoredProtoId);
			Assert.Equal(400, copy.File.MessageId);
			Assert.Equal("Weapon 2 (copy)", copy.Name);
			Assert.Equal("Weapon 2 (copy)", f.Messages.GetOrCreate("pro_item.msg").GetText(400));
			Assert.True(copy.IsNew);
			Assert.Equal(4, f.Catalog.GetList(ProtoObjectType.Item).Count);
			Assert.True(f.Catalog.IsListModified(ProtoObjectType.Item));
			Assert.Equal(f[2].File!.Get(FieldKeys.MinDamage), copy.File.Get(FieldKeys.MinDamage));

			f.Undo.Undo();
			Assert.Equal(3, f.Catalog.GetEntries(ProtoObjectType.Item).Count);
			Assert.False(f.Catalog.IsListModified(ProtoObjectType.Item));
			Assert.Null(f.Messages.GetOrCreate("pro_item.msg").Find(400));

			f.Undo.Redo();
			Assert.Same(copy, f.Catalog.Find(ProtoId.From(ProtoObjectType.Item, 4)));
		}

		[Fact]
		public void TextEdit_UpdatesTheMessageFileAndIsUndoable()
		{
			Fixture f = new();

			f.Undo.Execute(new TextEditAction(f.Texts, f[1], "Renamed", "A description"));

			Assert.Equal("Renamed", f[1].Name);
			Assert.True(f[1].IsModified);
			Assert.True(f.Messages.GetOrCreate("pro_item.msg").IsDirty);
			Assert.Equal("A description", f.Messages.GetOrCreate("pro_item.msg").GetText(101));

			f.Undo.Undo();
			Assert.Equal("Weapon 1", f[1].Name);
		}

		[Fact]
		public void Json_ExportThenImport_ProducesNoChanges_AndAppliesEditedValues()
		{
			Fixture f = new();
			String json = ProtoJson.Export(f.Catalog.All, "test");

			ImportPlan unchanged = ProtoJson.CreatePlan(json, f.Catalog);
			Assert.True(unchanged.IsEmpty, String.Join("; ", unchanged.Warnings));

			String edited = json.Replace("\"MinDamage\": 1,", "\"MinDamage\": 77,", StringComparison.Ordinal);
			ImportPlan plan = ProtoJson.CreatePlan(edited, f.Catalog);

			FieldChange change = Assert.Single(plan.FieldChanges);
			Assert.Equal(77, change.NewValue);
			f.Undo.Execute(plan.ToAction(f.Texts, "import"));
			Assert.Equal(77, f[1].File!.Get(FieldKeys.MinDamage));
		}

		[Fact]
		public void Csv_ExportThenImport_ProducesNoChanges_AndAppliesEditedCells()
		{
			Fixture f = new();
			IReadOnlyDictionary<ProtoKind, String> csv = ProtoCsv.Export(f.Catalog.All);
			String weapons = csv[ProtoKind.Weapon];

			Assert.True(ProtoCsv.CreatePlan(weapons, f.Catalog).IsEmpty);

			String[] lines = weapons.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
			String[] header = lines[0].Split(';');
			Int32 maxColumn = Array.IndexOf(header, FieldKeys.MaxDamage);
			String[] cells = lines[2].Split(';');
			cells[maxColumn] = "500";
			String modified = String.Join("\r\n", [lines[0], lines[1], String.Join(';', cells), lines[3]]);

			ImportPlan plan = ProtoCsv.CreatePlan(modified, f.Catalog);

			FieldChange change = Assert.Single(plan.FieldChanges);
			Assert.Equal(f[2].Pid, change.Entry.Pid);
			Assert.Equal(500, change.NewValue);
		}

		[Fact]
		public void Csv_EscapesDelimitersQuotesAndLineBreaksInTexts()
		{
			Fixture f = new();
			f.Undo.Execute(new TextEditAction(f.Texts, f[1], "A; \"quoted\"\nname", "d"));

			String csv = ProtoCsv.ExportKind(ProtoKind.Weapon, [f[1]]);
			ProtoCatalog catalog = f.Catalog;
			ImportPlan plan = ProtoCsv.CreatePlan(csv, catalog);

			Assert.True(plan.IsEmpty);
			Assert.Contains("\"A; \"\"quoted\"\"\nname\"", csv, StringComparison.Ordinal);
		}

		[Fact]
		public void Filter_SupportsPlainTextAndPrefixedTerms()
		{
			Fixture f = new();
			List<ProtoEntry> all = [.. f.Catalog.All];

			Assert.Equal(3, all.Count(new ProtoFilter(String.Empty).Matches));
			Assert.Single(all, new ProtoFilter("Weapon 2").Matches);
			Assert.Single(all, new ProtoFilter("pid:3").Matches);
			Assert.Equal(3, all.Count(new ProtoFilter("kind:weapon").Matches));
			Assert.DoesNotContain(all, new ProtoFilter("kind:armor").Matches);
			Assert.Equal(3, all.Count(new ProtoFilter("flag:Flat").Matches));
			Assert.Single(all, new ProtoFilter("file:00000002").Matches);
			Assert.Single(all, new ProtoFilter("\"Weapon 3\"").Matches);
		}

		[Fact]
		public void Filter_ScriptTerm_MatchesTheNameTheNumberOrNone()
		{
			Fixture f = new();
			List<ProtoEntry> all = [.. f.Catalog.All];
			IReadOnlyList<String> scripts = ["first.int", "ACBrahmn.int"];
			f[1].File!.TrySet(FieldKeys.ScriptId, 0x03000001);
			f[2].File!.TrySet(FieldKeys.ScriptId, 0x03000000);
			f[3].File!.TrySet(FieldKeys.ScriptId, -1);

			// The numbers of scripts.lst count from 0: 1 is the second line.
			Assert.Equal(1, f[1].ScriptIndex);
			Assert.Equal(-1, f[3].ScriptIndex);
			Assert.Equal([f[1]], all.Where(new ProtoFilter("script:brahm", scripts).Matches));
			Assert.Equal([f[2]], all.Where(new ProtoFilter("script:0", scripts).Matches));
			Assert.Equal([f[3]], all.Where(new ProtoFilter("script:none", scripts).Matches));
			Assert.Equal(2, all.Count(new ProtoFilter("script:", scripts).Matches));
		}

		[Fact]
		public void Diff_ReportsOnlyTheChangedFields()
		{
			Fixture f = new();
			ProtoFile a = f[1].File!;
			ProtoFile b = a.Clone();
			b.TrySet(FieldKeys.MaxDamage, 100);

			IReadOnlyList<FieldDiff> diffs = ProtoDiff.Compare(a, b);

			FieldDiff changed = Assert.Single(diffs, d => d.IsDifferent);
			Assert.Equal(FieldKeys.MaxDamage, changed.Key);
			Assert.Equal(11, changed.ValueA);
			Assert.Equal(100, changed.ValueB);
			Assert.Equal(0x44, Assert.Single(ProtoDiff.GetDifferentOffsets(a, b)));
		}
	}
}