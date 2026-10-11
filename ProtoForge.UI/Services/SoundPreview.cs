namespace ProtoForge.UI.Services
{
	using System;
	using System.IO;
	using System.Media;

	/// <summary>Plays a short sound from memory (the weapon sound preview). A new sound replaces the one still playing.</summary>
	public static class SoundPreview
	{
		private static SoundPlayer? _player;
		private static MemoryStream? _stream;

		public static void Play(Byte[] wav)
		{
			_player?.Stop();
			_player?.Dispose();
			_stream?.Dispose();
			_stream = new MemoryStream(wav);
			_player = new SoundPlayer(_stream);
			_player.Play();
		}
	}
}