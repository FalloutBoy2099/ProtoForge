namespace ProtoForge.Core.Vfs
{
	using System;
	using System.Collections.Generic;

	/// <summary>One source of game files: a folder, a DAT archive or a "patches" directory.</summary>
	public sealed record LayerInfo(Int32 Index, String Name, String? Path, Boolean IsDat, Boolean IsOutput);

	/// <summary>Where a virtual file was found.</summary>
	public sealed record GameFileLocation(String VirtualPath, LayerInfo Layer, Int64 Size);

	/// <summary>
	/// Read access to the game data seen as one virtual tree: the highest priority layer providing a path wins,
	/// exactly like the Fallout engine resolves <c>data/</c>, patch DATs and master.dat.
	/// </summary>
	public interface IGameFileSystem
	{
		/// <summary>Layers ordered from the highest to the lowest priority.</summary>
		IReadOnlyList<LayerInfo> Layers { get; }

		Boolean Exists(String path);

		/// <summary>Highest priority location of the file, or null.</summary>
		GameFileLocation? Locate(String path);

		/// <summary>All locations of the file (overrides first). Used to compare a modded file with the vanilla one.</summary>
		IReadOnlyList<GameFileLocation> LocateAll(String path);

		Byte[]? ReadAllBytes(String path);

		/// <summary>Reads the file as provided by one given layer.</summary>
		Byte[]? ReadFromLayer(String path, LayerInfo layer);

		/// <summary>Virtual paths of the files directly inside <paramref name="directory"/>, across all layers.</summary>
		IReadOnlyList<String> Enumerate(String directory);
	}
}