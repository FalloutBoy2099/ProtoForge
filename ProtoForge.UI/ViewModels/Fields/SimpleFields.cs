namespace ProtoForge.UI.ViewModels.Fields
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using CommunityToolkit.Mvvm.ComponentModel;
	using ProtoForge.Core.Formats.Pro;
	using Services;

	/// <summary>An integer shown in a number box.</summary>
	public sealed partial class NumberFieldViewModel : FieldViewModel
	{
		[ObservableProperty]
		private Double? _number;

		public NumberFieldViewModel(ProtoField field, EditorContext context)
			: base(field, context)
		{
		}

		public String Placeholder
		{
			get { return IsMixed ? Loc.Get("field.mixed") : String.Empty; }
		}

		protected override void Display(IReadOnlyList<Int32> values)
		{
			Number = values.Count > 0 && !IsMixed ? values[0] : null;
			OnPropertyChanged(nameof(Placeholder));
		}

		partial void OnNumberChanged(Double? value)
		{
			if (!IsRefreshing && value is { } number)
			{
				Commit((Int32)Math.Round(Math.Clamp(number, Int32.MinValue, Int32.MaxValue)));
			}
		}
	}

	/// <summary>The light intensity, stored as 0..65536 and edited as a percentage.</summary>
	public sealed partial class LightFieldViewModel : FieldViewModel
	{
		private const Double Full = 65536.0;

		[ObservableProperty]
		private Double? _percent;

		public LightFieldViewModel(ProtoField field, EditorContext context)
			: base(field, context)
		{
		}

		protected override void Display(IReadOnlyList<Int32> values)
		{
			Percent = values.Count > 0 && !IsMixed ? Math.Round(values[0] * 100.0 / Full, 1) : null;
		}

		partial void OnPercentChanged(Double? value)
		{
			if (!IsRefreshing && value is { } percent)
			{
				Commit((Int32)Math.Round(Math.Clamp(percent, 0, 100) * Full / 100.0));
			}
		}
	}

	/// <summary>A byte that is usually a character code (sound codes): edited as a character and as a number.</summary>
	public sealed partial class CharacterFieldViewModel : FieldViewModel
	{
		[ObservableProperty]
		private Double? _code;

		[ObservableProperty]
		private String _character = String.Empty;

		public CharacterFieldViewModel(ProtoField field, EditorContext context)
			: base(field, context)
		{
		}

		protected override void Display(IReadOnlyList<Int32> values)
		{
			Boolean single = values.Count > 0 && !IsMixed;
			Code = single ? values[0] : null;
			Character = single && values[0] is >= 32 and < 127 ? ((Char)values[0]).ToString() : String.Empty;
		}

		partial void OnCodeChanged(Double? value)
		{
			if (IsRefreshing || value is not { } number)
			{
				return;
			}

			Int32 code = (Int32)Math.Clamp(Math.Round(number), 0, 255);
			Character = code is >= 32 and < 127 ? ((Char)code).ToString() : String.Empty;
			Commit(code);
		}

		partial void OnCharacterChanged(String value)
		{
			if (IsRefreshing || value.Length != 1)
			{
				return;
			}

			Code = value[0] < 256 ? value[0] : Code;
		}

		public String CodeText
		{
			get { return Code is { } code ? "0x" + ((Int32)code).ToString("X2", CultureInfo.InvariantCulture) : String.Empty; }
		}
	}
}