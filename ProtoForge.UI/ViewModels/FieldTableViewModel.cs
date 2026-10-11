namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.ObjectModel;
	using System.Globalization;
	using System.Linq;
	using CommunityToolkit.Mvvm.ComponentModel;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Serialization;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	/// <summary>One line of the field table: the name, the position and the value as a decimal number and as hexadecimal.</summary>
	public sealed partial class FieldRowViewModel : ObservableObject
	{
		private readonly FieldTableViewModel _owner;
		private Boolean _refreshing;

		[ObservableProperty]
		private Double? _number;

		[ObservableProperty]
		private String _hex = String.Empty;

		[ObservableProperty]
		private Boolean _isChanged;

		public FieldRowViewModel(FieldTableViewModel owner, ProtoField field)
		{
			_owner = owner;
			Field = field;
		}

		public ProtoField Field { get; }

		public String Key
		{
			get { return Field.Key; }
		}

		public String Label
		{
			get { return Loc.Field(Field.Key); }
		}

		public String Position
		{
			get
			{
				String text = $"0x{Field.Offset:X2}";
				return Field.IsVirtual ? $"{text} ({Loc.Get("table.bits")} 0x{Field.Mask:X})" : $"{text} +{Field.Size}";
			}
		}

		public Boolean IsEditable
		{
			get { return !Field.IsReadOnly; }
		}

		public void Refresh(Int32? value, Boolean changed)
		{
			_refreshing = true;
			try
			{
				IsChanged = changed;
				if (value is null)
				{
					Number = null;
					Hex = String.Empty;
					return;
				}

				Number = value.Value;
				Hex = Format(value.Value);
			}
			finally
			{
				_refreshing = false;
			}
		}

		private String Format(Int32 value)
		{
			if (Field.IsVirtual)
			{
				return "0x" + (unchecked((UInt32)value)).ToString("X", CultureInfo.InvariantCulture);
			}

			return Field.Size == 1
				? "0x" + (unchecked((Byte)value)).ToString("X2", CultureInfo.InvariantCulture)
				: "0x" + (unchecked((UInt32)value)).ToString("X8", CultureInfo.InvariantCulture);
		}

		partial void OnNumberChanged(Double? value)
		{
			if (!_refreshing && value is { } number)
			{
				_owner.Commit(Field, (Int32)Math.Round(Math.Clamp(number, Int32.MinValue, Int32.MaxValue)));
			}
		}

		partial void OnHexChanged(String value)
		{
			if (!_refreshing && ProtoJson.TryParseInt32(value, out Int32 parsed))
			{
				_owner.Commit(Field, parsed);
			}
		}
	}

	/// <summary>Every field of the selected prototype as a table (name, offset, decimal, hexadecimal), the way the old text editor of Proto Manager showed it.</summary>
	public sealed partial class FieldTableViewModel : ObservableObject
	{
		private readonly GameWorkspace _workspace;
		private ProtoEntry? _entry;
		private ProtoKind? _kind;

		public FieldTableViewModel(GameWorkspace workspace)
		{
			_workspace = workspace;
		}

		public ObservableCollection<FieldRowViewModel> Rows { get; } = [];

		public void Load(ProtoEntry? entry)
		{
			_entry = entry;
			ProtoFile? file = entry?.File;
			if (file is null)
			{
				Rows.Clear();
				_kind = null;
				return;
			}

			if (_kind != file.Kind || Rows.Count == 0)
			{
				Rows.Clear();
				foreach (ProtoField field in file.Schema.Fields.OrderBy(f => f.Offset).ThenBy(f => f.IsVirtual))
				{
					Rows.Add(new FieldRowViewModel(this, field));
				}

				_kind = file.Kind;
			}

			ProtoFile? baseline = entry!.Baseline;
			foreach (FieldRowViewModel row in Rows)
			{
				Int32? value = file.TryGet(row.Field);
				Boolean changed = baseline is not null && baseline.Kind == file.Kind && baseline.TryGet(row.Field) != value;
				row.Refresh(value, changed);
			}
		}

		internal void Commit(ProtoField field, Int32 value)
		{
			if (_entry is not null)
			{
				_workspace.SetField([_entry], field.Key, value);
			}
		}
	}
}