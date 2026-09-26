using System;
using System.Globalization;
using System.Windows.Data;

namespace EveFPreview.View.Controls
{
	/// <summary>true ⇄ false, both ways (e.g. a "shown" switch bound to a "hidden" flag).</summary>
	public sealed class InverseBooleanConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			return value is bool flag ? !flag : value;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			return value is bool flag ? !flag : value;
		}
	}
}
