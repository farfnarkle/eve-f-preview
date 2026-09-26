using System;
using System.ComponentModel;
using System.Globalization;

namespace EveFPreview.Configuration
{
	[Flags]
	public enum OverlayFontStyle
	{
		Regular = 0,
		Bold = 1,
		Italic = 2,
		Underline = 4,
		Strikeout = 8
	}

	/// <summary>Same members and meaning as System.Drawing.GraphicsUnit, minus the ones a font can't use.</summary>
	public enum OverlayFontUnit
	{
		World = 0,
		Pixel = 2,
		Point = 3,
		Inch = 4,
		Document = 5,
		Millimeter = 6
	}

	/// <summary>
	/// The overlay label font. Replaces System.Drawing.Font (GDI+) in the configuration, but is stored
	/// in exactly the text form System.Drawing's FontConverter used - "Microsoft Sans Serif, 10pt,
	/// style=Bold" - so existing config files load unchanged and stay readable by older builds.
	/// </summary>
	[TypeConverter(typeof(OverlayFontConverter))]
	public sealed class OverlayFont : IEquatable<OverlayFont>
	{
		public const string DefaultFamilyName = "Microsoft Sans Serif";

		public OverlayFont(string familyName, float size, OverlayFontStyle style = OverlayFontStyle.Regular, OverlayFontUnit unit = OverlayFontUnit.Point)
		{
			this.FamilyName = string.IsNullOrWhiteSpace(familyName) ? OverlayFont.DefaultFamilyName : familyName.Trim();
			this.Size = size > 0 ? size : 8.25F;
			this.Style = style;
			this.Unit = unit;
		}

		public string FamilyName { get; }

		/// <summary>Size in <see cref="Unit"/>.</summary>
		public float Size { get; }

		public OverlayFontStyle Style { get; }

		public OverlayFontUnit Unit { get; }

		public bool Bold => (this.Style & OverlayFontStyle.Bold) != 0;
		public bool Italic => (this.Style & OverlayFontStyle.Italic) != 0;
		public bool Underline => (this.Style & OverlayFontStyle.Underline) != 0;
		public bool Strikeout => (this.Style & OverlayFontStyle.Strikeout) != 0;

		public float SizeInPoints => this.Unit switch
		{
			OverlayFontUnit.Point => this.Size,
			OverlayFontUnit.Inch => this.Size * 72F,
			OverlayFontUnit.Document => this.Size * 72F / 300F,
			OverlayFontUnit.Millimeter => this.Size * 72F / 25.4F,
			// GDI+ treats World and Pixel as screen pixels at 96 DPI for this purpose.
			_ => this.Size * 72F / 96F
		};

		/// <summary>Size in WPF device-independent units (1/96 inch).</summary>
		public double SizeInDips => this.SizeInPoints * 96.0 / 72.0;

		public static OverlayFont CreateDefault()
		{
			return new OverlayFont(OverlayFont.DefaultFamilyName, 10.0F, OverlayFontStyle.Bold);
		}

		public OverlayFont WithSize(float size)
		{
			return new OverlayFont(this.FamilyName, size, this.Style, this.Unit);
		}

		public override string ToString()
		{
			return OverlayFontConverter.Format(this);
		}

		public bool Equals(OverlayFont other)
		{
			return other != null
				&& string.Equals(this.FamilyName, other.FamilyName, StringComparison.OrdinalIgnoreCase)
				&& this.Size.Equals(other.Size)
				&& this.Style == other.Style
				&& this.Unit == other.Unit;
		}

		public override bool Equals(object obj)
		{
			return this.Equals(obj as OverlayFont);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(this.FamilyName.ToUpperInvariant(), this.Size, this.Style, this.Unit);
		}
	}

	/// <summary>
	/// Text form of <see cref="OverlayFont"/>, following System.Drawing.FontConverter's invariant-culture
	/// rules: "Name, SIZEunit[, style=Flag, Flag]" with the size defaulting to 8.25pt.
	/// </summary>
	public sealed class OverlayFontConverter : TypeConverter
	{
		private const string StylePrefix = "style=";

		public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
		{
			return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
		}

		public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
		{
			return destinationType == typeof(string) || base.CanConvertTo(context, destinationType);
		}

		public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
		{
			return value is string text ? OverlayFontConverter.Parse(text, culture) : base.ConvertFrom(context, culture, value);
		}

		public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
		{
			if (destinationType == typeof(string) && value is OverlayFont font)
			{
				return OverlayFontConverter.Format(font, culture);
			}

			return base.ConvertTo(context, culture, value, destinationType);
		}

		public static string Format(OverlayFont font, CultureInfo culture = null)
		{
			culture ??= CultureInfo.InvariantCulture;
			char separator = culture.TextInfo.ListSeparator[0];

			string text = font.FamilyName + separator + " " + font.Size.ToString(culture.NumberFormat) + OverlayFontConverter.UnitSuffix(font.Unit);
			if (font.Style != OverlayFontStyle.Regular)
			{
				text += separator + " " + StylePrefix + font.Style;
			}

			return text;
		}

		public static OverlayFont Parse(string text, CultureInfo culture = null)
		{
			string font = text?.Trim();
			if (string.IsNullOrEmpty(font))
			{
				return null;
			}

			culture ??= CultureInfo.InvariantCulture;
			char separator = culture.TextInfo.ListSeparator[0];

			float size = 8.25F;
			OverlayFontStyle style = OverlayFontStyle.Regular;
			OverlayFontUnit unit = OverlayFontUnit.Point;

			int nameEnd = font.IndexOf(separator);
			if (nameEnd < 0)
			{
				return new OverlayFont(font, size, style, unit);
			}

			string name = font.Substring(0, nameEnd);
			if (nameEnd < font.Length - 1)
			{
				int styleIndex = culture.CompareInfo.IndexOf(font, StylePrefix, CompareOptions.IgnoreCase);
				string sizeText;
				string styleText = null;
				if (styleIndex != -1)
				{
					styleText = font.Substring(styleIndex + StylePrefix.Length);
					sizeText = font.Substring(nameEnd + 1, styleIndex - nameEnd - 1);
				}
				else
				{
					sizeText = font.Substring(nameEnd + 1);
				}

				(string number, string unitText) = OverlayFontConverter.SplitSize(sizeText, separator);
				if (number != null)
				{
					size = float.Parse(number, NumberStyles.Float, culture.NumberFormat);
				}

				if (unitText != null)
				{
					unit = OverlayFontConverter.ParseUnit(unitText);
				}

				if (styleText != null)
				{
					foreach (string token in styleText.Split(separator))
					{
						style |= Enum.Parse<OverlayFontStyle>(token.Trim(), ignoreCase: true);
					}
				}
			}

			return new OverlayFont(name, size, style, unit);
		}

		private static (string Number, string Unit) SplitSize(string text, char separator)
		{
			text = text.Trim();
			if (text.Length == 0)
			{
				return (null, null);
			}

			int split = 0;
			while (split < text.Length && !char.IsLetter(text[split]))
			{
				split++;
			}

			char[] trimChars = { separator, ' ' };
			string number = split > 0 ? text.Substring(0, split).Trim(trimChars) : null;
			string unit = split < text.Length ? text.Substring(split).TrimEnd(trimChars) : null;
			return (string.IsNullOrEmpty(number) ? null : number, unit);
		}

		private static OverlayFontUnit ParseUnit(string unit)
		{
			return unit.ToLowerInvariant() switch
			{
				"pt" => OverlayFontUnit.Point,
				"px" => OverlayFontUnit.Pixel,
				"in" => OverlayFontUnit.Inch,
				"mm" => OverlayFontUnit.Millimeter,
				"doc" => OverlayFontUnit.Document,
				"world" => OverlayFontUnit.World,
				_ => throw new ArgumentException("Unknown font size unit: " + unit)
			};
		}

		private static string UnitSuffix(OverlayFontUnit unit)
		{
			return unit switch
			{
				OverlayFontUnit.Pixel => "px",
				OverlayFontUnit.Inch => "in",
				OverlayFontUnit.Millimeter => "mm",
				OverlayFontUnit.Document => "doc",
				OverlayFontUnit.World => "world",
				_ => "pt"
			};
		}
	}
}
