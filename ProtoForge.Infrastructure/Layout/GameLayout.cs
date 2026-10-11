namespace ProtoForge.Infrastructure.Layout
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;

	public enum LayerKind
	{
		Directory,
		DatArchive,
	}

	/// <summary>Where a layer comes from; informational, shown in the sources list.</summary>
	public enum LayerRole
	{
		Patches,
		Mod,
		ExtraPatch,
		PatchArchive,
		Engine,
		Base,
		Other,
	}

	/// <summary>One entry of the ordered list of data sources (a folder or a DAT archive).</summary>
	public sealed class LayerDefinition
	{
		public String Path { get; set; } = String.Empty;

		public LayerKind Kind { get; set; }

		public String Name { get; set; } = String.Empty;

		public LayerRole Role { get; set; }

		public Boolean IsEnabled { get; set; } = true;

		/// <summary>Set while building the effective list: edits are saved in this layer.</summary>
		public Boolean IsOutput { get; set; }

		public LayerDefinition Clone()
		{
			return (LayerDefinition)MemberwiseClone();
		}
	}

	/// <summary>
	/// The complete description of what ProtoForge loads: the game folder, the language, the ordered sources
	/// (highest priority first) and the folder where modified files are written.
	/// </summary>
	public sealed class GameLayout
	{
		public String? GameFolder { get; set; }

		public String Language { get; set; } = "english";

		/// <summary>Folder receiving the modified files. Never a DAT archive.</summary>
		public String? OutputFolder { get; set; }

		/// <summary>Sources ordered from the highest to the lowest priority.</summary>
		public List<LayerDefinition> Layers { get; set; } = [];

		/// <summary>Display name of the layout (recent projects list).</summary>
		public String DisplayName
		{
			get
			{
				String? path = OutputFolder ?? GameFolder;
				return String.IsNullOrEmpty(path) ? "(empty)" : new DirectoryInfo(path).Name;
			}
		}

		public GameLayout Clone()
		{
			return new GameLayout
			{
				GameFolder = GameFolder,
				Language = Language,
				OutputFolder = OutputFolder,
				Layers = [.. Layers.Select(l => l.Clone())],
			};
		}

		/// <summary>The enabled layers in lookup order, with the output folder on top when it is not already one of them.</summary>
		public IEnumerable<LayerDefinition> GetEffectiveLayers()
		{
			List<LayerDefinition> result = [];
			String? output = OutputFolder is null ? null : Normalize(OutputFolder);
			Boolean outputPresent = false;

			foreach (LayerDefinition layer in Layers.Where(l => l.IsEnabled))
			{
				LayerDefinition copy = layer.Clone();
				copy.IsOutput = output is not null && copy.Kind == LayerKind.Directory
					&& String.Equals(Normalize(copy.Path), output, StringComparison.OrdinalIgnoreCase);
				outputPresent |= copy.IsOutput;
				result.Add(copy);
			}

			if (output is not null && !outputPresent)
			{
				result.Insert(0, new LayerDefinition
				{
					Path = OutputFolder!,
					Kind = LayerKind.Directory,
					Name = "Output (" + new DirectoryInfo(OutputFolder!).Name + ")",
					Role = LayerRole.Patches,
					IsOutput = true,
				});
			}

			return result;
		}

		private static String Normalize(String path)
		{
			return System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
		}
	}
}