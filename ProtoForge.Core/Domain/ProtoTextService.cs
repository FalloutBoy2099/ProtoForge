namespace ProtoForge.Core.Domain
{
	using System;
	using System.Collections.Generic;
	using Formats.Msg;
	using Formats.Pro;

	/// <summary>
	/// Links prototypes with their texts: the name lives at message id <c>MessageId</c> and the description at <c>MessageId + 1</c>
	/// of <c>pro_item.msg</c>, <c>pro_crit.msg</c>, ... depending on the object type.
	/// </summary>
	public sealed class ProtoTextService
	{
		private readonly MessageStore _store;

		public ProtoTextService(MessageStore store)
		{
			_store = store;
		}

		public MsgFile GetFile(ProtoObjectType type)
		{
			return _store.GetOrCreate(type.GetMessageFileName());
		}

		public void Load(ProtoEntry entry)
		{
			(String name, String description) = Read(entry);
			entry.SetTexts(name, description, asBaseline: true);
		}

		public void LoadAll(IEnumerable<ProtoEntry> entries)
		{
			foreach (ProtoEntry entry in entries)
			{
				Load(entry);
			}
		}

		/// <summary>Re-reads the texts of the entries after a MSG file was edited directly (the change counts as an edit, not as the reference state).</summary>
		public void Refresh(IEnumerable<ProtoEntry> entries)
		{
			foreach (ProtoEntry entry in entries)
			{
				(String name, String description) = Read(entry);
				if (name != entry.Name || description != entry.Description)
				{
					entry.SetTexts(name, description, asBaseline: false);
				}
			}
		}

		public (String Name, String Description) Read(ProtoEntry entry)
		{
			if (entry.File is null)
			{
				return (String.Empty, String.Empty);
			}

			Int32 id = entry.File.MessageId;
			if (id <= 0)
			{
				return (String.Empty, String.Empty);
			}

			MsgFile file = GetFile(entry.Type);
			return (file.GetText(id) ?? String.Empty, file.GetText(id + 1) ?? String.Empty);
		}

		/// <summary>True when the prototype has a usable message id, so its texts can be edited.</summary>
		public Boolean CanEditTexts(ProtoEntry entry)
		{
			return entry.File is not null && entry.File.MessageId > 0;
		}

		public Boolean Write(ProtoEntry entry, String name, String description)
		{
			if (!CanEditTexts(entry))
			{
				return false;
			}

			Int32 id = entry.File!.MessageId;
			MsgFile file = GetFile(entry.Type);
			file.SetText(id, name);
			file.SetText(id + 1, description);
			entry.SetTexts(name, description, asBaseline: false);
			return true;
		}
	}
}