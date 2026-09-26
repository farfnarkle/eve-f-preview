using System.Windows;
using System.Windows.Controls;

namespace EveFPreview.View.Controls
{
	/// <summary>
	/// One line of a settings card: title and description on the left, the setting's control (its
	/// Content) on the right. Styled in Themes/Controls.xaml.
	/// </summary>
	public class SettingRow : ContentControl
	{
		public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
			nameof(Title), typeof(string), typeof(SettingRow), new PropertyMetadata(null));

		public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
			nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata(null));

		/// <summary>Indents the row under the one above it (an option that only applies when that one is on).</summary>
		public static readonly DependencyProperty IsSubSettingProperty = DependencyProperty.Register(
			nameof(IsSubSetting), typeof(bool), typeof(SettingRow), new PropertyMetadata(false));

		/// <summary>Maintained by <see cref="CardStack"/>: off for the last visible row, so it doesn't double up with the card's edge.</summary>
		public static readonly DependencyProperty ShowDividerProperty = DependencyProperty.Register(
			nameof(ShowDivider), typeof(bool), typeof(SettingRow), new PropertyMetadata(true));

		public string Title
		{
			get => (string)this.GetValue(SettingRow.TitleProperty);
			set => this.SetValue(SettingRow.TitleProperty, value);
		}

		public string Description
		{
			get => (string)this.GetValue(SettingRow.DescriptionProperty);
			set => this.SetValue(SettingRow.DescriptionProperty, value);
		}

		public bool IsSubSetting
		{
			get => (bool)this.GetValue(SettingRow.IsSubSettingProperty);
			set => this.SetValue(SettingRow.IsSubSettingProperty, value);
		}

		public bool ShowDivider
		{
			get => (bool)this.GetValue(SettingRow.ShowDividerProperty);
			set => this.SetValue(SettingRow.ShowDividerProperty, value);
		}
	}

	/// <summary>Vertical stack of <see cref="SettingRow"/>s inside a card; draws dividers between rows only.</summary>
	public class CardStack : StackPanel
	{
		protected override Size MeasureOverride(Size constraint)
		{
			UIElement lastVisible = null;
			foreach (UIElement child in this.InternalChildren)
			{
				if (child.Visibility != Visibility.Collapsed)
				{
					lastVisible = child;
				}
			}

			foreach (UIElement child in this.InternalChildren)
			{
				if (child is SettingRow row)
				{
					bool showDivider = child != lastVisible;
					if (row.ShowDivider != showDivider)
					{
						row.ShowDivider = showDivider;
					}
				}
			}

			return base.MeasureOverride(constraint);
		}
	}
}
