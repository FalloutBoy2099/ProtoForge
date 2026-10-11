namespace ProtoForge.Core.Domain
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using Formats.Msg;

	/// <summary>
	/// The MSG files of the current game language, loaded on demand and edited in memory. Nothing is written until
	/// the user saves the workspace.
	/// </summary>
	public sealed class MessageStore
	{
		private readonly Dictionary<String, MsgFile> _files = new(StringComparer.OrdinalIgnoreCase);
		private readonly Func<String, MsgFile?> _loader;

		public MessageStore(Func<String, MsgFile?> loader, Encoding encoding)
		{
			_loader = loader;
			Encoding = encoding;
		}

		public Encoding Encoding { get; }

		public IEnumerable<KeyValuePair<String, MsgFile>> Loaded
		{
			get { return _files; }
		}

		public IEnumerable<String> DirtyFiles
		{
			get { return _files.Where(f => f.Value.IsDirty).Select(f => f.Key); }
		}

		/// <summary>Returns the file, loading it when needed. A missing file yields an empty one that will be created on save.</summary>
		public MsgFile GetOrCreate(String fileName)
		{
			if (!_files.TryGetValue(fileName, out MsgFile? file))
			{
				file = _loader(fileName) ?? new MsgFile(Encoding);
				_files[fileName] = file;
			}

			return file;
		}

		public MsgFile? TryGetLoaded(String fileName)
		{
			return _files.TryGetValue(fileName, out MsgFile? file) ? file : null;
		}

		public String? GetText(String fileName, Int32 id)
		{
			return GetOrCreate(fileName).GetText(id);
		}

		public void AcceptChanges()
		{
			foreach (MsgFile file in _files.Values)
			{
				file.AcceptChanges();
			}
		}
	}
}