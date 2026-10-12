namespace ProtoForge.Core.Tests
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.IO;
	using System.Text;
	using ProtoForge.Core.Editing;
	using ProtoForge.Core.Formats.Ai;
	using ProtoForge.Core.Formats.Dat;
	using ProtoForge.Core.Formats.Frm;
	using ProtoForge.Core.Formats.Msg;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Templates;
	using ProtoForge.Infrastructure.Layout;
	using ProtoForge.Infrastructure.Templates;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Serialization;
	using ProtoForge.Core.Vfs;
	using ProtoForge.Infrastructure.Layers;
	using Xunit;

	/// <summary>One test per defect found by the code audit, so that none of them comes back.</summary>
	public class AuditRegressionTests
	{
		private sealed class NoOpAction : IUndoableAction
		{
			public String Description
			{
				get { return "noop"; }
			}

			public void Do()
			{
			}

			public void Undo()
			{
			}
		}

		[Fact]
		public void Undo_ThenAnotherEdit_IsNotTakenForTheSavedState()
		{
			UndoManager undo = new();
			undo.Execute(new NoOpAction());
			undo.Execute(new NoOpAction());
			undo.MarkSaved();
			Assert.False(undo.HasUnsavedChanges);

			undo.Undo();
			undo.Execute(new NoOpAction());

			Assert.True(undo.HasUnsavedChanges);
		}

		[Theory]
		[InlineData("proto/items/00000001.pro", true)]
		[InlineData("proto/items/../../x.pro", false)]
		[InlineData("..\\x.pro", false)]
		[InlineData("proto/items/c:evil.pro", false)]
		[InlineData("proto/items/nul.pro", false)]
		[InlineData("proto/items/a?.pro", false)]
		[InlineData("proto/items/trailing.", false)]
		[InlineData("", false)]
		public void GamePath_IsSafe(String path, Boolean expected)
		{
			Assert.Equal(expected, GamePath.IsSafe(path));
		}

		[Fact]
		public void ResolveInside_NeverLeavesTheRoot()
		{
			String root = Path.Combine(Path.GetTempPath(), "pf-root");

			Assert.NotNull(GamePath.ResolveInside(root, "proto/items/1.pro"));
			Assert.Null(GamePath.ResolveInside(root, "proto/../../1.pro"));
		}

		[Fact]
		public void ArtResolver_ReturnsNullForFrameIdsWithAnUnknownType()
		{
			using LayeredFileSystem files = new([]);
			ArtResolver art = new(files);
			FrameId garbage = new(unchecked((Int32)0xCCCCCCCC));

			Assert.Null(art.ResolvePath(garbage));
			Assert.False(art.Exists(garbage));
			Assert.Null(art.GetList((ProtoForge.Core.Formats.Pro.ArtType)13));
		}

		[Fact]
		public void Dat2_WithAnImpossibleFileCount_IsRejected()
		{
			String path = Path.Combine(Path.GetTempPath(), "pf-bad-" + Guid.NewGuid().ToString("N") + ".dat");
			Byte[] bytes = new Byte[12];
			BitConverter.GetBytes(0x7FFFFFF0).CopyTo(bytes, 0);
			BitConverter.GetBytes(4).CopyTo(bytes, 4);
			BitConverter.GetBytes(12).CopyTo(bytes, 8);
			File.WriteAllBytes(path, bytes);
			try
			{
				Assert.Throws<InvalidDataException>(() => DatArchive.Open(path));
			}
			finally
			{
				File.Delete(path);
			}
		}

		[Fact]
		public void Msg_NewEntryAfterAFileWithoutFinalLineBreak_StaysOnItsOwnLine()
		{
			Encoding encoding = Encoding.Latin1;
			MsgFile file = MsgFile.Parse(encoding.GetBytes("{100}{}{first}"), encoding);

			file.SetText(101, "second");
			MsgFile reloaded = MsgFile.Parse(file.ToBytes(), encoding);

			Assert.Equal("first", reloaded.Find(100)?.Text);
			Assert.Equal("second", reloaded.Find(101)?.Text);
		}

		[Fact]
		public void Msg_RemoveDeletesEveryCopyOfADuplicatedId()
		{
			Encoding encoding = Encoding.Latin1;
			MsgFile file = MsgFile.Parse(encoding.GetBytes("{5}{}{a}\r\n{5}{}{b}\r\n"), encoding);

			file.Remove(5);

			Assert.Null(MsgFile.Parse(file.ToBytes(), encoding).Find(5));
		}

		[Fact]
		public void Msg_ManyOpeningBracesWithoutClosingOnes_DoNotHang()
		{
			Encoding encoding = Encoding.Latin1;
			String text = String.Concat(System.Linq.Enumerable.Repeat("{1}{2\r\n", 40000));
			System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

			MsgFile.Parse(encoding.GetBytes(text), encoding);

			Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Took {watch.Elapsed}");
		}

		[Theory]
		[InlineData("=1+1", "'=1+1")]
		[InlineData("-5", "'-5")]
		[InlineData("Hello", "Hello")]
		public void Csv_TextCellsThatLookLikeFormulasAreEscaped_AndRestored(String text, String escaped)
		{
			Assert.Equal(escaped, ProtoCsv.EscapeText(text));
			Assert.Equal(text, ProtoCsv.UnescapeText(escaped));
		}

		[Fact]
		public void AiFile_KeepsHeaderComments_AndReadsValuesWithoutTheirComment()
		{
			AiFile file = AiFile.Parse("[Rat]  ; the rat\r\naggression=5 ; calm\r\npacket_num=1\r\n");

			Assert.Equal("5", file.GetValue("Rat", "aggression"));
			file.SetValue("Rat", "min_hp", "3");
			Assert.StartsWith("[Rat]  ; the rat", file.ToText(), StringComparison.Ordinal);
		}

		[Fact]
		public void AiFile_RemovingAKey_RemovesItsDuplicatesToo()
		{
			AiFile file = AiFile.Parse("[A]\r\nx=1\r\nx=2\r\npacket_num=1\r\n");

			file.SetValue("A", "x", null);

			Assert.Null(file.GetValue("A", "x"));
		}

		[Fact]
		public void AiFile_UndoOfAnAddedDuplicateName_RemovesTheNewPacket()
		{
			AiFile file = AiFile.Parse("[A]\r\npacket_num=1\r\n");
			UndoManager undo = new();

			undo.Execute(new AiAddPacketAction(file, "A", AiFile.CreateDefaultPacketLines(2)));
			undo.Undo();

			Assert.Single(file.Sections);
			Assert.Equal("1", file.GetValue("A", "packet_num"));
		}

		[Fact]
		public void Frm_WithANegativeDirectionOffset_IsAnInvalidFile()
		{
			Byte[] data = FrmWriter.WriteStatic(new FrmFrame(1, 1, 0, 0, [1]));
			System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x22), -5);

			Assert.Throws<InvalidDataException>(() => FrmFile.Parse(data));
		}

		[Fact]
		public void Bmp32_WithZeroAlphaEverywhere_IsReadAsOpaque()
		{
			Byte[] bmp = BmpFile.Write32Bit(1, 1, [10, 20, 30, 0]);

			Assert.Equal(255, BmpFile.Read(bmp).Bgra[3]);
		}

		[Fact]
		public void UserTemplates_OverwriteAtomically_AndKeepReservedNamesUsable()
		{
			String folder = Path.Combine(Path.GetTempPath(), "pf-tpl-" + Guid.NewGuid().ToString("N"));
			try
			{
				UserTemplateStore store = new(folder);
				ProtoFile file = ProtoFactory.CreateDefault(ProtoKind.MiscItem, ProtoId.From(ProtoObjectType.Item, 1));

				store.Save("con", file);
				store.Save("con", file);

				Assert.True(store.Exists("con"));
				Assert.Single(store.Load());
				Assert.Empty(Directory.GetFiles(folder, "*.protoforge-tmp"));
				store.Delete("con");
				Assert.False(store.Exists("con"));
			}
			finally
			{
				if (Directory.Exists(folder))
				{
					Directory.Delete(folder, true);
				}
			}
		}

		[Fact]
		public void ModsOrder_CommentsOnlyStartAtALineOrAfterASpace()
		{
			String root = Path.Combine(Path.GetTempPath(), "pf-mods-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path.Combine(root, "mods"));
			Directory.CreateDirectory(Path.Combine(root, "mods", "mod#1"));
			Directory.CreateDirectory(Path.Combine(root, "mods", "plain"));
			try
			{
				File.WriteAllText(Path.Combine(root, "mods", "mods_order.txt"), "# comment\r\nmod#1\r\nplain ; trailing\r\n");

				DiscoveryResult result = GameLayoutDiscovery.FromGameFolder(root);

				Assert.Contains(result.Layout.Layers, l => l.Path.EndsWith("mod#1", StringComparison.Ordinal));
				Assert.Contains(result.Layout.Layers, l => l.Path.EndsWith("plain", StringComparison.Ordinal));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Fact]
		public void TextService_UndoOfAFirstEdit_DoesNotCreateAnEmptyEntry()
		{
			MessageStore store = new(_ => null, Encoding.Latin1);
			ProtoTextService texts = new(store);
			ProtoFile file = ProtoFactory.CreateDefault(ProtoKind.MiscItem, ProtoId.From(ProtoObjectType.Item, 1));
			ProtoEntry entry = new(ProtoId.From(ProtoObjectType.Item, 1), "x.pro", file, null, "test");
			MsgFile messages = texts.GetFile(ProtoObjectType.Item);

			texts.Write(entry, String.Empty, String.Empty);

			Assert.Null(messages.Find(file.MessageId));
		}

		[Fact]
		public void Description_LineBreaksAreStoredAsSfallEscapes_AndLimitedToTwoLines()
		{
			Assert.Equal("one\\ntwo", ProtoTextService.ToStored("one\r\ntwo"));
			Assert.Equal("one\\ntwo three", ProtoTextService.ToStored("one\ntwo\nthree"));
			Assert.Equal("one\ntwo", ProtoTextService.ToDisplay("one\\ntwo"));
			Assert.Equal("plain", ProtoTextService.ToStored("plain"));
		}

		[Fact]
		public void TextService_DescriptionWithALineBreak_RoundTripsThroughTheMessageFile()
		{
			MessageStore store = new(_ => null, Encoding.Latin1);
			ProtoTextService texts = new(store);
			ProtoFile file = ProtoFactory.CreateDefault(ProtoKind.MiscItem, ProtoId.From(ProtoObjectType.Item, 1));
			ProtoEntry entry = new(ProtoId.From(ProtoObjectType.Item, 1), "x.pro", file, null, "test");

			texts.Write(entry, "Item", "first\nsecond");

			Assert.Equal("first\\nsecond", texts.GetFile(ProtoObjectType.Item).GetText(file.MessageId + 1));
			Assert.Equal("first\nsecond", texts.Read(entry).Description);
		}

		[Fact]
		public void TextService_DescriptionWithCrLf_NeverWritesACarriageReturnToTheFile()
		{
			Encoding encoding = Encoding.Latin1;
			MessageStore store = new(_ => MsgFile.Parse(encoding.GetBytes("{100}{}{Name}\r\n{101}{}{Desc}\r\n"), encoding), encoding);
			ProtoTextService texts = new(store);
			ProtoFile file = ProtoFactory.CreateDefault(ProtoKind.MiscItem, ProtoId.From(ProtoObjectType.Item, 1));
			file.MessageId = 100;
			ProtoEntry entry = new(ProtoId.From(ProtoObjectType.Item, 1), "x.pro", file, null, "test");

			texts.Write(entry, "Name", "first\r\nsecond");
			String written = encoding.GetString(texts.GetFile(ProtoObjectType.Item).ToBytes());

			Assert.Equal("{100}{}{Name}\r\n{101}{}{first\\nsecond}\r\n", written);
		}

		[Fact]
		public void Msg_CarriageReturnsNeverReachTheFile()
		{
			Encoding encoding = Encoding.Latin1;
			MsgFile file = MsgFile.Parse(encoding.GetBytes("{1}{}{a}\r\n"), encoding);

			file.SetText(2, "one\r\ntwo\rthree");

			String written = encoding.GetString(file.ToBytes());
			Assert.DoesNotContain("one\r", written, StringComparison.Ordinal);
			Assert.DoesNotContain("two\r", written, StringComparison.Ordinal);
		}

		[Fact]
		public void LayeredFileSystem_ListsTheLanguageFoldersOfTheLayers()
		{
			String root = Path.Combine(Path.GetTempPath(), "pf-lang-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path.Combine(root, "text", "french", "game"));
			Directory.CreateDirectory(Path.Combine(root, "text", "German", "game"));
			try
			{
				GameLayout layout = new();
				layout.Layers.Add(new LayerDefinition { Path = root, Kind = LayerKind.Directory, Name = "data" });
				using LayeredFileSystem files = LayeredFileSystem.Open(layout, []);

				Assert.Equal(["french", "German"], files.EnumerateDirectories("text"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Theory]
		[InlineData(936, "中文测试：铁砧与锤子")]
		[InlineData(950, "中文測試：鐵砧與錘子")]
		[InlineData(949, "한국어 시험: 모루와 망치")]
		[InlineData(932, "日本語のテスト：金床とハンマー")]
		public void Msg_DoubleByteFiles_RoundTripExactly_AndKeepTheOtherEntriesWhenOneIsEdited(Int32 codePage, String sample)
		{
			System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
			Encoding encoding = Encoding.GetEncoding(codePage);
			Byte[] original = encoding.GetBytes("{100}{}{" + sample + "}\r\n{101}{}{second " + sample + "}\r\n{200}{}{plain}\r\n");

			MsgFile file = MsgFile.Parse(original, encoding);

			Assert.Equal(original, file.ToBytes());
			file.SetText(200, "changed");
			MsgFile reloaded = MsgFile.Parse(file.ToBytes(), encoding);
			Assert.Equal(sample, reloaded.Find(100)?.Text);
			Assert.Equal("second " + sample, reloaded.Find(101)?.Text);
			Assert.Equal("changed", reloaded.Find(200)?.Text);
		}

		[Fact]
		public void TextReplacer_ReplacesInNamesAndDescriptions_AndCountsTheOccurrences()
		{
			MessageStore store = new(_ => null, Encoding.Latin1);
			ProtoTextService texts = new(store);
			ProtoFile file = ProtoFactory.CreateDefault(ProtoKind.MiscItem, ProtoId.From(ProtoObjectType.Item, 1));
			file.MessageId = 100;
			ProtoEntry entry = new(ProtoId.From(ProtoObjectType.Item, 1), "x.pro", file, null, "test");
			texts.Write(entry, "Big Gun", "A big gun, big!");

			IReadOnlyList<TextReplacement> any = TextReplacer.Plan([entry], texts, "big", "huge", true, true, false);
			IReadOnlyList<TextReplacement> exact = TextReplacer.Plan([entry], texts, "big", "huge", true, true, true);
			IReadOnlyList<TextReplacement> namesOnly = TextReplacer.Plan([entry], texts, "Big", "Small", true, false, true);

			Assert.Equal("huge Gun", any.Single().Name);
			Assert.Equal("A huge gun, huge!", any.Single().Description);
			Assert.Equal(3, any.Single().Occurrences);
			Assert.Equal(2, exact.Single().Occurrences);
			Assert.Equal("Small Gun", namesOnly.Single().Name);
			Assert.Equal("A big gun, big!", namesOnly.Single().Description);
			Assert.Empty(TextReplacer.Plan([entry], texts, "absent", "x", true, true, false));
		}

		[Fact]
		public void UndoManager_ListsTheRedoActionsAndJumpsSeveralSteps()
		{
			UndoManager undo = new() { MergeWindow = TimeSpan.Zero };
			for (Int32 i = 0; i < 4; i++)
			{
				undo.Execute(new NoOpAction());
			}

			undo.UndoMany(3);

			Assert.Single(undo.UndoHistory);
			Assert.Equal(3, undo.RedoHistory.Count);
			undo.RedoMany(2);
			Assert.Equal(3, undo.UndoHistory.Count);
			Assert.Single(undo.RedoHistory);
		}
	}
}