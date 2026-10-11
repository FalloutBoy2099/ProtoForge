namespace ProtoForge.UI.Converters
{
	using System;
	using System.Collections.Generic;
	using System.Collections;
	using System.Globalization;
	using System.Windows;
	using System.Windows.Data;
	using System.Windows.Media;
	using Wpf.Ui.Controls;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Validation;
	using Services;

	/// <summary>Visible when the bound value is true (or not null / not zero / not empty); parameter "invert" flips the result.</summary>
	public sealed class VisibleWhenConverter : IValueConverter
	{
		public Object Convert(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			Boolean visible = value switch
			{
				null => false,
				Boolean b => b,
				String s => s.Length > 0,
				Int32 i => i != 0,
				Double d => d != 0,
				ICollection c => c.Count > 0,
				_ => true,
			};

			if (parameter is String text && text.Equals("invert", StringComparison.OrdinalIgnoreCase))
			{
				visible = !visible;
			}

			return visible ? Visibility.Visible : Visibility.Collapsed;
		}

		public Object ConvertBack(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}

	/// <summary>Localised name of a prototype kind or object type.</summary>
	public sealed class KindLabelConverter : IValueConverter
	{
		public Object Convert(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			return value switch
			{
				ProtoKind kind => Loc.Get("kind." + kind),
				ProtoObjectType type => Loc.Get("type." + type),
				_ => String.Empty,
			};
		}

		public Object ConvertBack(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}

	/// <summary>Hexadecimal PID, e.g. <c>0x0000001A</c>.</summary>
	public sealed class PidConverter : IValueConverter
	{
		public Object Convert(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			return value is ProtoId pid ? pid.ToString() : String.Empty;
		}

		public Object ConvertBack(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}

	/// <summary>Brush for a validation severity.</summary>
	public sealed class SeverityBrushConverter : IValueConverter
	{
		public Object Convert(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			return value switch
			{
				Severity.Error => new SolidColorBrush(Color.FromRgb(0xE8, 0x4C, 0x4C)),
				Severity.Warning => new SolidColorBrush(Color.FromRgb(0xF0, 0xA8, 0x30)),
				Severity.Info => new SolidColorBrush(Color.FromRgb(0x5B, 0xA7, 0xE8)),
				_ => Brushes.Transparent,
			};
		}

		public Object ConvertBack(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}

	/// <summary>Icon for a validation severity.</summary>
	public sealed class SeverityIconConverter : IValueConverter
	{
		public Object Convert(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			return value switch
			{
				Severity.Error => SymbolRegular.ErrorCircle24,
				Severity.Warning => SymbolRegular.Warning24,
				Severity.Info => SymbolRegular.Info24,
				_ => SymbolRegular.Empty,
			};
		}

		public Object ConvertBack(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}

	/// <summary>Small preview of a prototype for the list: the inventory icon of items, the stand pose of critters, the map art otherwise. Use with an asynchronous binding.</summary>
	public sealed class EntryThumbnailConverter : IValueConverter
	{
		public Object? Convert(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			if (value is not ProtoEntry { File: { } file })
			{
				return null;
			}

			Int32? inventory = file.TryGet(FieldKeys.InventoryFrmId);
			Int32? fid = inventory is { } inv && inv != -1 ? inv : file.TryGet(FieldKeys.FrmId);
			if (fid is null || fid.Value == -1)
			{
				return null;
			}

			ArtImageService art = (ArtImageService)App.Services.GetService(typeof(ArtImageService))!;
			return art.GetBitmap(new ProtoForge.Core.Formats.Frm.FrameId(fid.Value));
		}

		public Object ConvertBack(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}

	/// <summary>Short status text for a library row (NEW / MOD / ERR).</summary>
	public sealed class EntryStatusConverter : IMultiValueConverter
	{
		public Object Convert(Object[] values, Type targetType, Object? parameter, CultureInfo culture)
		{
			Boolean isNew = values.Length > 0 && values[0] is true;
			Boolean modified = values.Length > 1 && values[1] is true;
			Boolean broken = values.Length > 2 && values[2] is true;
			Severity? issue = values.Length > 3 ? values[3] as Severity? : null;

			if (broken)
			{
				return "✖";
			}

			if (isNew)
			{
				return "＋";
			}

			if (modified)
			{
				return "●";
			}

			return issue switch
			{
				Severity.Error => "!",
				Severity.Warning => "▲",
				_ => String.Empty,
			};
		}

		public Object[] ConvertBack(Object? value, Type[] targetTypes, Object? parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}

	/// <summary>Small attribute labels of a library row: read-only file, unreadable file, Fallout 1 critter, pending override deletion.</summary>
	public sealed class EntryAttributesConverter : IMultiValueConverter
	{
		public Object Convert(Object[] values, Type targetType, Object? parameter, CultureInfo culture)
		{
			List<String> labels = [];
			if (values.Length > 0 && values[0] is true)
			{
				labels.Add("R/O");
			}

			if (values.Length > 1 && values[1] is true)
			{
				labels.Add("BAD!");
			}

			if (values.Length > 2 && values[2] is true)
			{
				labels.Add("F1");
			}

			if (values.Length > 3 && values[3] is true)
			{
				labels.Add("DEL");
			}

			if (values.Length > 4 && values[4] is Int32 hp)
			{
				labels.Add("HP " + hp.ToString(CultureInfo.CurrentCulture));
			}

			return String.Join(" ", labels);
		}

		public Object[] ConvertBack(Object? value, Type[] targetTypes, Object? parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}

	/// <summary>Turns a boolean around (used to enable a control while a flag is NOT set).</summary>
	public sealed class InverseBooleanConverter : IValueConverter
	{
		public Object Convert(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			return value is not true;
		}

		public Object ConvertBack(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			return value is not true;
		}
	}

	/// <summary>True when a text is not empty.</summary>
	public sealed class NotEmptyConverter : IValueConverter
	{
		public Object Convert(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			return value is String { Length: > 0 };
		}

		public Object ConvertBack(Object? value, Type targetType, Object? parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}
}