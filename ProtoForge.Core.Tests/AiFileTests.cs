namespace ProtoForge.Core.Tests
{
	using System;
	using System.Linq;
	using System.Text;
	using ProtoForge.Core.Editing;
	using ProtoForge.Core.Formats.Ai;
	using Xunit;

	public class AiFileTests
	{
		private const String Sample = "[Rat]\r\naggression=10\r\npacket_num=7\r\n\r\n[Boss]\r\naggression=50\r\nchem_primary_desire=81,-1\r\npacket_num=8\r\nmystery=abc\r\n";

		[Fact]
		public void Parse_And_Write_RoundTripsExactly()
		{
			AiFile file = AiFile.Parse(Encoding.Latin1.GetBytes(Sample));

			Assert.Equal(Sample, file.ToText());
			Assert.Equal(["Rat", "Boss"], file.Sections.Select(s => s.Name));
			Assert.False(file.IsDirty);
		}

		[Fact]
		public void RoundTrip_KeepsLineFeedFilesAndMissingFinalNewline()
		{
			String text = "[A]\npacket_num=1\n[B]\npacket_num=2";

			Assert.Equal(text, AiFile.Parse(text).ToText());
		}

		[Fact]
		public void SetValue_ReplacesTheLineInPlace_AndAddsMissingKeysBeforeTheBlankLine()
		{
			AiFile file = AiFile.Parse(Sample);

			Assert.True(file.SetValue("Rat", "aggression", "99"));
			Assert.True(file.SetValue("Rat", "disposition", "coward"));

			Assert.StartsWith("[Rat]\r\naggression=99\r\npacket_num=7\r\ndisposition=coward\r\n\r\n[Boss]", file.ToText(), StringComparison.Ordinal);
			Assert.True(file.IsDirty);
			Assert.False(file.SetValue("Rat", "aggression", "99"));
		}

		[Fact]
		public void SetValue_WithNull_RemovesTheKey()
		{
			AiFile file = AiFile.Parse(Sample);

			Assert.True(file.SetValue("Boss", "mystery", null));

			Assert.Null(file.GetValue("Boss", "mystery"));
			Assert.DoesNotContain("mystery", file.ToText(), StringComparison.Ordinal);
		}

		[Fact]
		public void NextPacketNumber_IsOneAfterTheHighest()
		{
			Assert.Equal(9, AiFile.Parse(Sample).NextPacketNumber());
		}

		[Fact]
		public void Actions_AreUndoable()
		{
			AiFile file = AiFile.Parse(Sample);
			UndoManager undo = new();

			undo.Execute(new AiValueAction(file, "Rat", "aggression", "99"));
			undo.Execute(new AiAddPacketAction(file, "New", AiFile.CreateDefaultPacketLines(9)));
			undo.Execute(new AiRemovePacketAction(file, file.Find("Rat")!));
			undo.Execute(new AiRawTextAction(file, "Boss", "packet_num=8\r\nfoo=bar"));

			Assert.Equal(["Boss", "New"], file.Sections.Select(s => s.Name));
			Assert.Equal("bar", file.GetValue("Boss", "foo"));
			Assert.Equal("9", file.GetValue("New", "packet_num"));

			while (undo.CanUndo)
			{
				undo.Undo();
			}

			Assert.Equal(Sample, file.ToText());
			Assert.False(file.IsDirty);
		}

		[Fact]
		public void DrugList_ParsesAndFormats()
		{
			Assert.Equal([81, 40], AiParameters.ParseDrugs("81, 40,-1"));
			Assert.Empty(AiParameters.ParseDrugs("-1"));
			Assert.Equal("-1", AiParameters.FormatDrugs([]));
			Assert.Equal("1,2,3", AiParameters.FormatDrugs([1, 2, 3, 4]));
		}

		[Fact]
		public void DefaultPacket_HasTheRequestedNumber()
		{
			String[] lines = [.. AiFile.CreateDefaultPacketLines(42)];

			Assert.Contains("packet_num=42", lines);
		}
	}
}