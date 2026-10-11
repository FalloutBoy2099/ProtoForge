namespace ProtoForge.Core.Tests
{
	using System;
	using System.IO;

	/// <summary>Locates optional real game data. Tests that need it are skipped when it is not installed.</summary>
	internal static class TestEnvironment
	{
		public static String? Fallout2Folder
		{
			get
			{
				String? configured = Environment.GetEnvironmentVariable("PROTOFORGE_TEST_FALLOUT2");
				if (!String.IsNullOrEmpty(configured) && Directory.Exists(configured))
				{
					return configured;
				}

				const String defaultPath = @"C:\Fallout2\F2_RPU_2.4.34";
				return Directory.Exists(defaultPath) ? defaultPath : null;
			}
		}

		public static String? Fallout1Folder
		{
			get
			{
				String? configured = Environment.GetEnvironmentVariable("PROTOFORGE_TEST_FALLOUT1");
				if (!String.IsNullOrEmpty(configured) && Directory.Exists(configured))
				{
					return configured;
				}

				const String defaultPath = @"D:\Torrent\Down\Fallout Setup\Fallout1-extract";
				return Directory.Exists(defaultPath) ? defaultPath : null;
			}
		}

		public static String? Fallout1MasterDat
		{
			get
			{
				String? configured = Environment.GetEnvironmentVariable("PROTOFORGE_TEST_FALLOUT1_DAT");
				if (!String.IsNullOrEmpty(configured) && File.Exists(configured))
				{
					return configured;
				}

				String? folder = Fallout1Folder;
				String? fromFolder = folder is null ? null : Path.Combine(folder, "MASTER.DAT");
				if (fromFolder is not null && File.Exists(fromFolder))
				{
					return fromFolder;
				}

				const String defaultPath = @"D:/FalloutX/Source/fallout_1/_/master.dat";
				return File.Exists(defaultPath) ? defaultPath : null;
			}
		}

		public static String FixturePath(String name)
		{
			return Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
		}

		public static Byte[] ReadFixture(String name)
		{
			return File.ReadAllBytes(FixturePath(name));
		}
	}
}