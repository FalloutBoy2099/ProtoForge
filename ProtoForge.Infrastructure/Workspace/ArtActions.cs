namespace ProtoForge.Infrastructure.Workspace
{
	using ProtoForge.Core;
	using System;
	using ProtoForge.Core.Editing;
	using ProtoForge.Core.Formats.Frm;
	using ProtoForge.Core.Formats.Lst;
	using ProtoForge.Core.Formats.Pro;
	using Layers;

	/// <summary>
	/// Adds an image to the art of the game (<c>art/&lt;folder&gt;/&lt;name&gt;.frm</c>) and appends its name to the matching list
	/// (<c>inven.lst</c>, <c>items.lst</c>...). Both files are kept in memory until the next save, but every reader already sees them.
	/// </summary>
	public sealed class AddArtAction : IUndoableAction
	{
		private readonly LayeredFileSystem _files;
		private readonly ArtResolver _art;
		private readonly ArtType _type;
		private readonly String _fileName;
		private readonly Byte[] _frm;
		private readonly Action? _changed;
		private Byte[]? _oldFrmPending;
		private Byte[]? _oldListPending;
		private Boolean _addedToList;

		public AddArtAction(LayeredFileSystem files, ArtResolver art, ArtType type, String fileName, Byte[] frm, Action? changed = null)
		{
			_files = files;
			_art = art;
			_type = type;
			_fileName = fileName;
			_frm = frm;
			_changed = changed;
			Description = UserTexts.Format("act.addArt", "Add art {0}", fileName);
		}

		public String Description { get; }

		/// <summary>The index of the entry in the list (the value to use in a frame id), set once the action ran.</summary>
		public Int32 Index { get; private set; } = -1;

		public String FrmPath
		{
			get { return $"art/{FrameId.GetFolderName(_type)}/{_fileName}"; }
		}

		public void Do()
		{
			String listPath = ArtResolver.GetListPath(_type);
			_oldFrmPending = _files.Pending.TryGetValue(FrmPath, out Byte[]? oldFrm) ? oldFrm : null;
			_oldListPending = _files.Pending.TryGetValue(listPath, out Byte[]? oldList) ? oldList : null;

			LstFile list = _art.GetList(_type)?.Clone() ?? new LstFile();
			Int32 existing = list.IndexOf(_fileName);
			if (existing >= 0)
			{
				_addedToList = false;
				Index = existing;
			}
			else
			{
				list.Add(_fileName);
				_addedToList = true;
				Index = list.Count - 1;
				_files.SetPending(listPath, list.ToBytes());
			}

			_files.SetPending(FrmPath, _frm);
			_art.Invalidate();
			_changed?.Invoke();
		}

		public void Undo()
		{
			if (_oldFrmPending is null)
			{
				_files.RemovePending(FrmPath);
			}
			else
			{
				_files.SetPending(FrmPath, _oldFrmPending);
			}

			if (_addedToList)
			{
				String listPath = ArtResolver.GetListPath(_type);
				if (_oldListPending is null)
				{
					_files.RemovePending(listPath);
				}
				else
				{
					_files.SetPending(listPath, _oldListPending);
				}
			}

			_art.Invalidate();
			_changed?.Invoke();
		}
	}
}