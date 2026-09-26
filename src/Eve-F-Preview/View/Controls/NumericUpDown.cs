using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EveFPreview.View.Controls
{
	/// <summary>
	/// Integer spin box (WPF has no NumericUpDown). Typed text is committed on Enter or when focus
	/// leaves; arrows, the Up/Down keys and the mouse wheel step by <see cref="Increment"/>. Out of
	/// range values are clamped rather than rejected.
	/// </summary>
	public class NumericUpDown : UserControl
	{
		public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
			nameof(Value), typeof(decimal), typeof(NumericUpDown),
			new FrameworkPropertyMetadata(0m, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

		public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
			nameof(Minimum), typeof(decimal), typeof(NumericUpDown), new PropertyMetadata(0m, OnRangeChanged));

		public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
			nameof(Maximum), typeof(decimal), typeof(NumericUpDown), new PropertyMetadata(100m, OnRangeChanged));

		public static readonly DependencyProperty IncrementProperty = DependencyProperty.Register(
			nameof(Increment), typeof(decimal), typeof(NumericUpDown), new PropertyMetadata(1m));

		private readonly TextBox _textBox;
		private readonly Border _frame;

		public event EventHandler ValueChanged;

		public NumericUpDown()
		{
			this._textBox = new TextBox
			{
				HorizontalContentAlignment = HorizontalAlignment.Right,
				BorderThickness = new Thickness(0),
				Background = Brushes.Transparent,
				MinHeight = 0,
				Padding = new Thickness(8, 0, 2, 0),
				Text = "0"
			};
			this._textBox.LostKeyboardFocus += (_, _) => this.CommitText();
			this._textBox.PreviewKeyDown += this.TextBox_PreviewKeyDown;

			RepeatButton up = NumericUpDown.CreateSpinButton("\uE70E");
			up.Click += (_, _) => this.Step(+1);
			RepeatButton down = NumericUpDown.CreateSpinButton("\uE70D");
			down.Click += (_, _) => this.Step(-1);

			var buttons = new Grid { Width = 22, Margin = new Thickness(0, 2, 2, 2) };
			buttons.RowDefinitions.Add(new RowDefinition());
			buttons.RowDefinitions.Add(new RowDefinition());
			Grid.SetRow(down, 1);
			buttons.Children.Add(up);
			buttons.Children.Add(down);

			var layout = new DockPanel();
			DockPanel.SetDock(buttons, Dock.Right);
			layout.Children.Add(buttons);
			layout.Children.Add(this._textBox);

			this._frame = new Border
			{
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(6),
				SnapsToDevicePixels = true,
				Child = layout
			};
			this._frame.SetResourceReference(Border.BackgroundProperty, "InputFillBrush");
			this.UpdateFrameBorder();
			this.Content = this._frame;

			this.Width = 104;
			this.Height = 32;
			this.Focusable = false;
			this.IsTabStop = false;
			this.IsKeyboardFocusWithinChanged += (_, _) => this.UpdateFrameBorder();
			this.MouseEnter += (_, _) => this.UpdateFrameBorder();
			this.MouseLeave += (_, _) => this.UpdateFrameBorder();
			this.MouseWheel += this.NumericUpDown_MouseWheel;
			this.IsEnabledChanged += (_, _) => this.Opacity = this.IsEnabled ? 1.0 : 0.45;
		}

		public decimal Value
		{
			get => (decimal)this.GetValue(NumericUpDown.ValueProperty);
			set => this.SetValue(NumericUpDown.ValueProperty, value);
		}

		public decimal Minimum
		{
			get => (decimal)this.GetValue(NumericUpDown.MinimumProperty);
			set => this.SetValue(NumericUpDown.MinimumProperty, value);
		}

		public decimal Maximum
		{
			get => (decimal)this.GetValue(NumericUpDown.MaximumProperty);
			set => this.SetValue(NumericUpDown.MaximumProperty, value);
		}

		public decimal Increment
		{
			get => (decimal)this.GetValue(NumericUpDown.IncrementProperty);
			set => this.SetValue(NumericUpDown.IncrementProperty, value);
		}

		/// <summary>Puts the caret in the text field.</summary>
		public new bool Focus()
		{
			return this._textBox.Focus();
		}

		private static RepeatButton CreateSpinButton(string glyph)
		{
			var button = new RepeatButton
			{
				Content = new TextBlock
				{
					Text = glyph,
					FontSize = 8,
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center
				}
			};
			((TextBlock)button.Content).SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
			button.SetResourceReference(FrameworkElement.StyleProperty, "SpinButton");
			return button;
		}

		private void UpdateFrameBorder()
		{
			string brush = this.IsKeyboardFocusWithin
				? "AccentBrush"
				: this.IsMouseOver ? "ControlBorderHoverBrush" : "ControlBorderBrush";
			this._frame.SetResourceReference(Border.BorderBrushProperty, brush);
		}

		private static object CoerceValue(DependencyObject d, object baseValue)
		{
			var control = (NumericUpDown)d;
			decimal value = decimal.Truncate((decimal)baseValue);
			return Math.Min(Math.Max(value, control.Minimum), Math.Max(control.Minimum, control.Maximum));
		}

		private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var control = (NumericUpDown)d;
			control.ShowValue();
			control.ValueChanged?.Invoke(control, EventArgs.Empty);
		}

		private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			d.CoerceValue(NumericUpDown.ValueProperty);
		}

		private void ShowValue()
		{
			this._textBox.Text = this.Value.ToString("0", CultureInfo.CurrentCulture);
		}

		private void Step(int direction)
		{
			this.CommitText();
			this.Value += direction * this.Increment;
		}

		private void CommitText()
		{
			if (decimal.TryParse(this._textBox.Text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out decimal typed))
			{
				this.Value = typed;
			}

			// Always re-show: covers invalid input and values that were clamped to the same number.
			this.ShowValue();
		}

		private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			switch (e.Key)
			{
				case Key.Enter:
					// Not handled: Enter should still reach a dialog's default button, as with WinForms.
					this.CommitText();
					this._textBox.SelectAll();
					break;
				case Key.Up:
					this.Step(+1);
					e.Handled = true;
					break;
				case Key.Down:
					this.Step(-1);
					e.Handled = true;
					break;
			}
		}

		private void NumericUpDown_MouseWheel(object sender, MouseWheelEventArgs e)
		{
			if (!this.IsKeyboardFocusWithin)
			{
				// Like WinForms: only a focused spin box reacts to the wheel, so scrolling the page doesn't change values.
				return;
			}

			this.Step(e.Delta > 0 ? +1 : -1);
			e.Handled = true;
		}
	}
}
