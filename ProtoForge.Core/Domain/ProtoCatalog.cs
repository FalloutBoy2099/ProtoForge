namespace ProtoForge.Core.Domain
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Formats.Lst;
	using Formats.Pro;

	public enum CatalogChangeKind
	{
		Reset,
		Added,
		Removed,
	}

	public sealed class CatalogChangedEventArgs : EventArgs
	{
		public CatalogChangedEventArgs(CatalogChangeKind kind, ProtoEntry? entry)
		{
			Kind = kind;
			Entry = entry;
		}

		public CatalogChangeKind Kind { get; }

		public ProtoEntry? Entry { get; }
	}

	/// <summary>
	/// Every loaded prototype, grouped by object type, together with the in-memory LST file that defines their PIDs.
	/// A PID is the 1-based line number in the LST, so entries can only be appended or removed from the end without
	/// renumbering existing prototypes (which would break every map and script).
	/// </summary>
	public sealed class ProtoCatalog
	{
		private readonly Dictionary<ProtoObjectType, List<ProtoEntry>> _byType = [];
		private readonly Dictionary<ProtoObjectType, LstFile> _lists = [];
		private readonly Dictionary<ProtoObjectType, LstFile> _originalLists = [];
		private readonly Dictionary<Int32, ProtoEntry> _byPid = [];

		public event EventHandler<CatalogChangedEventArgs>? Changed;

		public Int32 Count
		{
			get { return _byPid.Count; }
		}

		public IEnumerable<ProtoEntry> All
		{
			get { return ProtoObjectTypeExtensions.All.SelectMany(GetEntries); }
		}

		public IEnumerable<ProtoEntry> Modified
		{
			get { return All.Where(e => e.IsModified); }
		}

		public IReadOnlyList<ProtoEntry> GetEntries(ProtoObjectType type)
		{
			return _byType.TryGetValue(type, out List<ProtoEntry>? list) ? list : [];
		}

		public Boolean IsLoaded(ProtoObjectType type)
		{
			return _byType.ContainsKey(type);
		}

		public ProtoEntry? Find(ProtoId pid)
		{
			return _byPid.TryGetValue(pid.Value, out ProtoEntry? entry) ? entry : null;
		}

		public ProtoEntry? Find(Int32 pid)
		{
			return Find(new ProtoId(pid));
		}

		public LstFile GetList(ProtoObjectType type)
		{
			if (!_lists.TryGetValue(type, out LstFile? list))
			{
				list = new LstFile();
				_lists[type] = list;
				_originalLists[type] = list.Clone();
			}

			return list;
		}

		public Boolean IsListModified(ProtoObjectType type)
		{
			if (!_lists.TryGetValue(type, out LstFile? list) || !_originalLists.TryGetValue(type, out LstFile? original))
			{
				return false;
			}

			return !list.Lines.SequenceEqual(original.Lines, StringComparer.Ordinal);
		}

		public IEnumerable<ProtoObjectType> ModifiedLists
		{
			get { return ProtoObjectTypeExtensions.All.Where(IsListModified); }
		}

		/// <summary>The index a newly appended prototype of this type would get.</summary>
		public Int32 NextIndex(ProtoObjectType type)
		{
			return GetEntries(type).Count + 1;
		}

		/// <summary>Replaces the content of one object type (initial load).</summary>
		public void Load(ProtoObjectType type, LstFile list, IEnumerable<ProtoEntry> entries)
		{
			if (_byType.TryGetValue(type, out List<ProtoEntry>? previous))
			{
				foreach (ProtoEntry entry in previous)
				{
					_byPid.Remove(entry.Pid.Value);
				}
			}

			List<ProtoEntry> loaded = [.. entries];
			_byType[type] = loaded;
			_lists[type] = list;
			_originalLists[type] = list.Clone();
			foreach (ProtoEntry entry in loaded)
			{
				_byPid[entry.Pid.Value] = entry;
			}

			Changed?.Invoke(this, new CatalogChangedEventArgs(CatalogChangeKind.Reset, null));
		}

		/// <summary>Appends a prototype at the end of its LST. The caller provides a file whose PID already matches the new index.</summary>
		public ProtoEntry Append(ProtoObjectType type, ProtoFile file, String fileName, String sourceName)
		{
			Int32 index = NextIndex(type);
			ProtoEntry entry = new(ProtoId.From(type, index), fileName, file, null, sourceName) { IsNew = true };

			if (!_byType.TryGetValue(type, out List<ProtoEntry>? list))
			{
				list = [];
				_byType[type] = list;
			}

			list.Add(entry);
			GetList(type).Add(fileName);
			_byPid[entry.Pid.Value] = entry;
			Changed?.Invoke(this, new CatalogChangedEventArgs(CatalogChangeKind.Added, entry));
			return entry;
		}

		/// <summary>Re-attaches an entry previously removed with <see cref="RemoveLast"/> (undo/redo keep entry identity).</summary>
		public void Restore(ProtoEntry entry)
		{
			ProtoObjectType type = entry.Type;
			if (entry.Index != NextIndex(type))
			{
				throw new InvalidOperationException($"Entry {entry.Pid} cannot be restored: the next free index is {NextIndex(type)}.");
			}

			if (!_byType.TryGetValue(type, out List<ProtoEntry>? list))
			{
				list = [];
				_byType[type] = list;
			}

			list.Add(entry);
			GetList(type).Add(entry.FileName);
			_byPid[entry.Pid.Value] = entry;
			Changed?.Invoke(this, new CatalogChangedEventArgs(CatalogChangeKind.Added, entry));
		}

		/// <summary>Removes the last prototype of a type (undo of <see cref="Append"/>).</summary>
		public ProtoEntry? RemoveLast(ProtoObjectType type)
		{
			if (!_byType.TryGetValue(type, out List<ProtoEntry>? list) || list.Count == 0)
			{
				return null;
			}

			ProtoEntry entry = list[^1];
			list.RemoveAt(list.Count - 1);
			GetList(type).RemoveLast();
			_byPid.Remove(entry.Pid.Value);
			Changed?.Invoke(this, new CatalogChangedEventArgs(CatalogChangeKind.Removed, entry));
			return entry;
		}

		public void AcceptListChanges()
		{
			foreach (ProtoObjectType type in _lists.Keys.ToList())
			{
				_originalLists[type] = _lists[type].Clone();
			}
		}
	}
}