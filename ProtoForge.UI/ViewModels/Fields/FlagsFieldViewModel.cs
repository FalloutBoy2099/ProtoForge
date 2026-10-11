namespace ProtoForge.UI.ViewModels.Fields
{
	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.Globalization;
	using System.Linq;
	using CommunityToolkit.Mvvm.ComponentModel;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Serialization;
	using Services;

	/// <summary>One check box of a bit field. <c>null</c> means "set on some of the selected prototypes only".</summary>
	public sealed partial class FlagItemViewModel : ObservableObject
	{
		private readonly FlagsFieldViewModel _owner;
		private Boolean _silent;

		[ObservableProperty]
		private Boolean? _isChecked = false;

		public FlagItemViewModel(FlagsFieldViewModel owner, FlagBit bit)
		{
			_owner = owner;
			Bit = bit;
		}

		public FlagBit Bit { get; }

		public String Label
		{
			get { return Loc.Flag(Bit.Name); }
		}

		public String Tooltip
		{
			get { return $"{Bit.Name} - 0x{Bit.Mask:X8}"; }
		}

		partial void OnIsCheckedChanged(Boolean? value)
		{
			if (!_silent)
			{
				_owner.OnBitToggled(Bit.Mask, value == true);
			}
		}

		internal void SetSilently(Boolean? value)
		{
			_silent = true;
			IsChecked = value;
			_silent = false;
		}
	}

	/// <summary>A set of flags shown as check boxes plus the raw hexadecimal value.</summary>
	public sealed partial class FlagsFieldViewModel : FieldViewModel
	{
		[ObservableProperty]
		private String _hexText = String.Empty;

		[ObservableProperty]
		private String _unknownBits = String.Empty;

		public FlagsFieldViewModel(ProtoField field, EditorContext context)
			: base(field, context)
		{
			FlagSet set = FlagSets.Get(field.FlagSet!);
			foreach (FlagBit bit in set.Bits)
			{
				Flags.Add(new FlagItemViewModel(this, bit));
			}

			KnownMask = set.KnownMask;
		}

		public ObservableCollection<FlagItemViewModel> Flags { get; } = [];

		public UInt32 KnownMask { get; }

		protected override void Display(IReadOnlyList<Int32> values)
		{
			foreach (FlagItemViewModel item in Flags)
			{
				Int32 on = values.Count(v => (unchecked((UInt32)v) & item.Bit.Mask) == item.Bit.Mask);
				item.SetSilently(values.Count == 0 || on == 0 ? false : on == values.Count ? true : null);
			}

			if (values.Count == 0 || IsMixed)
			{
				HexText = IsMixed ? Loc.Get("field.mixed") : String.Empty;
				UnknownBits = String.Empty;
				return;
			}

			UInt32 raw = unchecked((UInt32)values[0]);
			HexText = "0x" + raw.ToString("X8", CultureInfo.InvariantCulture);
			UInt32 unknown = raw & ~KnownMask;
			if (Field.IsVirtual)
			{
				unknown &= Field.Mask;
			}

			UnknownBits = unknown == 0 ? String.Empty : Loc.Format("field.unknownBits", unknown.ToString("X8", CultureInfo.InvariantCulture));
		}

		partial void OnHexTextChanged(String value)
		{
			if (!IsRefreshing && ProtoJson.TryParseInt32(value, out Int32 parsed))
			{
				Commit(parsed);
			}
		}

		internal void OnBitToggled(UInt32 mask, Boolean on)
		{
			if (IsRefreshing || Entries.Count == 0)
			{
				return;
			}

			Context.Workspace.SetFlag(Entries, Field.Key, mask, on);
		}
	}
}