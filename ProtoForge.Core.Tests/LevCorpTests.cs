namespace ProtoForge.Core.Tests
{
	using System;
	using System.Text;
	using ProtoForge.Core.Formats.Msg;
	using Xunit;

	public class LevCorpTests
	{
		[Fact]
		public void Letters_AreStoredInThePseudoGraphicsPositions()
		{
			Encoding levCorp = MsgEncodings.Get(MsgEncodings.LevCorp);

			// "р" (0xE0 in cp866) is stored as 0xB0, "я" (0xEF) as 0xBF; "а" (0x A0) is unchanged.
			Byte[] bytes = levCorp.GetBytes("ряа");

			Assert.Equal([0xB0, 0xBF, 0xA0], bytes);
		}

		[Fact]
		public void Text_RoundTrips()
		{
			Encoding levCorp = MsgEncodings.Get(MsgEncodings.LevCorp);
			String text = "{100}{}{Привет, мир! Проверка кодировки}";

			Assert.Equal(text, levCorp.GetString(levCorp.GetBytes(text)));
		}

		[Fact]
		public void MsgFile_ReadsAndWritesLevCorp()
		{
			Encoding levCorp = MsgEncodings.Get(MsgEncodings.LevCorp);
			Byte[] file = levCorp.GetBytes("{100}{}{Пистолет}\r\n");

			MsgFile msg = MsgFile.Parse(file, levCorp);

			Assert.Equal("Пистолет", msg.Find(100)?.Text);
			Assert.Equal(file, msg.ToBytes());
		}
	}
}