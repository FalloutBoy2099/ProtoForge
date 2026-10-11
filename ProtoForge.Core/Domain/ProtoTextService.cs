namespace ProtoForge.Core.Domain
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Formats.Msg;
	using Formats.Pro;

	/// <summary>
	/// Links prototypes with their texts: the name lives at message id <c>MessageId</c> and the description at <c>MessageId + 1</c>
	/// of <c>pro_item.msg</c>, <c>pro_crit.msg</c>, ... depending on the object type.
	/// </summary>
	public sealed class ProtoTextService
	{
		/// <summary>
		/// Recent sfall versions turn the two characters backslash and n of a message into a line break. The description is shown with a
		/// real line break and stored with that escape; the name never has one.
		/// </summary>
		private const String LineBreakEscape = "\\n";

		/// <summary>The game shows no more than two lines for a description.</summary>
		public const Int32 MaxDescriptionLines = 2;

		private readonly MessageStore _store;

		public ProtoTextService(MessageStore store)
		{
			_store = store;
		}

		/// <summary>The description as shown in the editor: the escapes become line breaks.</summary>
		public static String ToDisplay(String stored)
		{
			return stored.Replace(LineBreakEscape, "\n", StringComparison.Ordinal);
		}

		/// <summary>The description as stored in the MSG file: line breaks become the escape (two lines at most, the others are joined with spaces).</summary>
		public static String ToStored(String display)
		{
			String[] lines = display.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
			if (lines.Length > MaxDescriptionLines)
			{
				lines = [.. lines.Take(MaxDescriptionLines - 1), String.Join(' ', lines.Skip(MaxDescriptionLines - 1).Where(l => l.Length > 0))];
			}

			return String.Join(LineBreakEscape, lines);
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
			return (file.GetText(id) ?? String.Empty, ToDisplay(file.GetText(id + 1) ?? String.Empty));
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

			// An empty text for an id the file does not have (the state before a first edit, restored by Undo) must not create an entry.
			if (name.Length > 0 || file.Find(id) is not null)
			{
				file.SetText(id, name);
			}

			if (description.Length > 0 || file.Find(id + 1) is not null)
			{
				file.SetText(id + 1, ToStored(description));
			}

			// The file stores the sanitised text (no braces): show what is really stored.
			entry.SetTexts(file.GetText(id) ?? String.Empty, ToDisplay(file.GetText(id + 1) ?? String.Empty), asBaseline: false);
			return true;
		}
	}
}