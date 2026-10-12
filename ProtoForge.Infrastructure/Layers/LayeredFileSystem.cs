namespace ProtoForge.Infrastructure.Layers
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading;
	using ProtoForge.Core.Formats.Dat;
	using ProtoForge.Core.Vfs;
	using Layout;

	/// <summary>
	/// The virtual game tree: layers are searched from the highest to the lowest priority, like the Fallout engine does
	/// with <c>data/</c>, patch archives and <c>master.dat</c>.
	/// </summary>
	public sealed class LayeredFileSystem : IGameFileSystem, IDisposable
	{
		private readonly List<GameLayer> _layers;
		private readonly Dictionary<String, IReadOnlyList<String>> _directoryCache = new(StringComparer.Ordinal);
		private readonly Dictionary<String, Byte[]> _overlay = new(StringComparer.Ordinal);
		private Int32 _generation;

		/// <summary>The pseudo layer of the files created in this session and not saved yet (it wins over every real layer).</summary>
		public static readonly LayerInfo PendingLayer = new(-1, "Pending", null, false, false);

		public LayeredFileSystem(IEnumerable<GameLayer> layers)
		{
			_layers = [.. layers];
			for (Int32 i = 0; i < _layers.Count; i++)
			{
				// Re-index so that LayerInfo.Index always equals the position in this file system.
				LayerInfo info = _layers[i].Info;
				if (info.Index != i)
				{
					throw new ArgumentException("Layer indices must match their position.", nameof(layers));
				}
			}

			Layers = [.. _layers.Select(l => l.Info)];
		}

		public IReadOnlyList<LayerInfo> Layers { get; }

		/// <summary>True when one of the layers is a Fallout 1 (DAT1) archive: the game is Fallout 1.</summary>
		public Boolean HasFallout1Archive
		{
			get { return _layers.OfType<DatLayer>().Any(l => l.Archive.Format == DatFormat.Fallout1); }
		}

		/// <summary>Opens every enabled layer of a layout. Layers that cannot be opened are skipped and reported in <paramref name="warnings"/>.</summary>
		public static LayeredFileSystem Open(GameLayout layout, ICollection<String> warnings)
		{
			List<GameLayer> layers = [];
			foreach (LayerDefinition definition in layout.GetEffectiveLayers())
			{
				LayerInfo info = new(layers.Count, definition.Name, definition.Path, definition.Kind == LayerKind.DatArchive, definition.IsOutput);
				try
				{
					if (definition.Kind == LayerKind.DatArchive)
					{
						layers.Add(new DatLayer(info, DatArchive.Open(definition.Path)));
					}
					else if (System.IO.Directory.Exists(definition.Path))
					{
						layers.Add(new DirectoryLayer(info, definition.Path));
					}
					else if (!definition.IsOutput)
					{
						warnings.Add(UserTexts.Format("warn.folderNotFound", "Folder not found: {0}", definition.Path));
					}
					else
					{
						// The output folder may not exist yet: it will be created on the first save.
						layers.Add(new DirectoryLayer(info, definition.Path));
					}
				}
				catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException)
				{
					warnings.Add(UserTexts.Format("warn.cannotOpen", "Cannot open '{0}': {1}", definition.Path, ex.Message));
				}
			}

			return new LayeredFileSystem(layers);
		}

		/// <summary>Files created in this session (new art, art lists) that are visible to every reader but not written yet.</summary>
		public IReadOnlyDictionary<String, Byte[]> Pending
		{
			get { lock (_overlay) { return new Dictionary<String, Byte[]>(_overlay, StringComparer.Ordinal); } }
		}

		public Boolean HasPending
		{
			get { lock (_overlay) { return _overlay.Count > 0; } }
		}

		public void SetPending(String path, Byte[] data)
		{
			lock (_overlay)
			{
				_overlay[GamePath.Normalize(path)] = data;
			}

			Invalidate();
		}

		public void RemovePending(String path)
		{
			lock (_overlay)
			{
				_overlay.Remove(GamePath.Normalize(path));
			}

			Invalidate();
		}

		public void ClearPending()
		{
			lock (_overlay)
			{
				_overlay.Clear();
			}

			Invalidate();
		}

		private Boolean TryGetPending(String normalized, out Byte[]? data)
		{
			lock (_overlay)
			{
				return _overlay.TryGetValue(normalized, out data);
			}
		}

		public Boolean Exists(String path)
		{
			String normalized = GamePath.Normalize(path);
			return TryGetPending(normalized, out _) || _layers.Any(l => l.TryGetSize(normalized, out _));
		}

		public GameFileLocation? Locate(String path)
		{
			String normalized = GamePath.Normalize(path);
			if (TryGetPending(normalized, out Byte[]? pending))
			{
				return new GameFileLocation(normalized, PendingLayer, pending!.Length);
			}

			foreach (GameLayer layer in _layers)
			{
				if (layer.TryGetSize(normalized, out Int64 size))
				{
					return new GameFileLocation(normalized, layer.Info, size);
				}
			}

			return null;
		}

		public IReadOnlyList<GameFileLocation> LocateAll(String path)
		{
			String normalized = GamePath.Normalize(path);
			List<GameFileLocation> result = [];
			if (TryGetPending(normalized, out Byte[]? pending))
			{
				result.Add(new GameFileLocation(normalized, PendingLayer, pending!.Length));
			}

			foreach (GameLayer layer in _layers)
			{
				if (layer.TryGetSize(normalized, out Int64 size))
				{
					result.Add(new GameFileLocation(normalized, layer.Info, size));
				}
			}

			return result;
		}

		public Byte[]? ReadAllBytes(String path)
		{
			String normalized = GamePath.Normalize(path);
			if (TryGetPending(normalized, out Byte[]? pending))
			{
				return pending;
			}

			foreach (GameLayer layer in _layers)
			{
				Byte[]? data = layer.Read(normalized);
				if (data is not null)
				{
					return data;
				}
			}

			return null;
		}

		public Byte[]? ReadFromLayer(String path, LayerInfo layer)
		{
			if (layer.Index < 0)
			{
				return TryGetPending(GamePath.Normalize(path), out Byte[]? pending) ? pending : null;
			}

			return _layers[layer.Index].Read(GamePath.Normalize(path));
		}

		public Boolean IsReadOnly(String path, LayerInfo layer)
		{
			return layer.Index >= 0 && _layers[layer.Index].IsReadOnly(GamePath.Normalize(path));
		}

		public IReadOnlyList<String> Enumerate(String directory)
		{
			String normalized = GamePath.Normalize(directory);
			lock (_directoryCache)
			{
				if (_directoryCache.TryGetValue(normalized, out IReadOnlyList<String>? cached))
				{
					return cached;
				}
			}

			Int32 generation = Volatile.Read(ref _generation);
			HashSet<String> files = new(StringComparer.Ordinal);
			foreach (GameLayer layer in _layers)
			{
				foreach (String file in layer.Enumerate(normalized))
				{
					files.Add(file);
				}
			}

			lock (_overlay)
			{
				foreach (String pending in _overlay.Keys.Where(k => String.Equals(GamePath.GetDirectory(k), normalized, StringComparison.Ordinal)))
				{
					files.Add(pending);
				}
			}

			List<String> sorted = [.. files.Order(StringComparer.Ordinal)];
			lock (_directoryCache)
			{
				// A listing built while files were added or removed is already out of date: do not keep it.
				if (generation == Volatile.Read(ref _generation))
				{
					_directoryCache[normalized] = sorted;
				}
			}

			return sorted;
		}

		/// <summary>The names of the folders directly inside a directory, over all the layers (without duplicates, ignoring case).</summary>
		public IReadOnlyList<String> EnumerateDirectories(String directory)
		{
			String normalized = GamePath.Normalize(directory);
			HashSet<String> names = new(StringComparer.OrdinalIgnoreCase);
			foreach (GameLayer layer in _layers)
			{
				foreach (String name in layer.EnumerateDirectories(normalized))
				{
					names.Add(name);
				}
			}

			return [.. names.Order(StringComparer.OrdinalIgnoreCase)];
		}

		/// <summary>Forgets cached directory listings (call after files were written).</summary>
		public void Invalidate()
		{
			Interlocked.Increment(ref _generation);
			lock (_directoryCache)
			{
				_directoryCache.Clear();
			}
		}

		public void Dispose()
		{
			foreach (GameLayer layer in _layers)
			{
				layer.Dispose();
			}

			_layers.Clear();
		}
	}
}