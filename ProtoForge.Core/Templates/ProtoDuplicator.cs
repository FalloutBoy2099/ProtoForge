namespace ProtoForge.Core.Templates
{
	using System;
	using Domain;
	using Formats.Pro;

	/// <summary>Creates new prototypes from existing ones.</summary>
	public static class ProtoDuplicator
	{
		/// <summary>
		/// Copies a prototype for the next free index: new PID, new message id (index × 100, the engine convention), same data.
		/// The name and description get a suffix so that the copy can be told apart in the lists.
		/// </summary>
		public static (ProtoFile File, String FileName, String Name, String Description) Duplicate(ProtoEntry source, Int32 newIndex, String copySuffix)
		{
			ProtoFile file = source.File?.Clone() ?? throw new InvalidOperationException(UserTexts.Format("err.cannotDuplicate", "The prototype cannot be duplicated: it has no readable content."));
			ProtoId pid = ProtoId.From(source.Type, newIndex);
			file.StoredProtoId = pid.Value;
			if (file.MessageId > 0 || source.Name.Length > 0)
			{
				file.TrySet(FieldKeys.MessageId, newIndex * 100);
			}

			String name = source.Name.Length > 0 ? source.Name + copySuffix : String.Empty;
			return (file, FormatFileName(newIndex), name, source.Description);
		}

		public static String FormatFileName(Int32 index)
		{
			return index.ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + ".pro";
		}
	}
}