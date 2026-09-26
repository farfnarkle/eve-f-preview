using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace EveFPreview.View.Controls
{
	/// <summary>A 3x3 grid of radio buttons picking one of the nine <see cref="ViewZoomAnchor"/> positions.</summary>
	public class AnchorSelector : UserControl
	{
		private static int _groupCounter;

		private readonly RadioButton[] _buttons = new RadioButton[9];
		private ViewZoomAnchor _selectedAnchor = ViewZoomAnchor.NW;
		private bool _suppressChanged;

		public event EventHandler AnchorChanged;

		public AnchorSelector()
		{
			string groupName = "AnchorSelector" + (++AnchorSelector._groupCounter);
			var grid = new UniformGrid { Rows = 3, Columns = 3 };
			for (int i = 0; i < 9; i++)
			{
				var anchor = (ViewZoomAnchor)i;
				var button = new RadioButton
				{
					GroupName = groupName,
					ToolTip = AnchorSelector.Describe(anchor)
				};
				button.SetResourceReference(FrameworkElement.StyleProperty, "AnchorCell");
				button.Checked += (_, _) => this.OnButtonChecked(anchor);
				this._buttons[i] = button;
				grid.Children.Add(button);
			}

			this._buttons[(int)ViewZoomAnchor.NW].IsChecked = true;

			// Reads as a tiny thumbnail with the chosen corner/edge lit up.
			var frame = new Border
			{
				HorizontalAlignment = HorizontalAlignment.Left,
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(8),
				Padding = new Thickness(4),
				Child = grid
			};
			frame.SetResourceReference(Border.BackgroundProperty, "ControlFillBrush");
			frame.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
			this.Content = frame;
			this.IsEnabledChanged += (_, _) => this.Opacity = this.IsEnabled ? 1.0 : 0.45;
		}

		private static string Describe(ViewZoomAnchor anchor)
		{
			return anchor switch
			{
				ViewZoomAnchor.NW => "Top left",
				ViewZoomAnchor.N => "Top",
				ViewZoomAnchor.NE => "Top right",
				ViewZoomAnchor.W => "Left",
				ViewZoomAnchor.C => "Centre",
				ViewZoomAnchor.E => "Right",
				ViewZoomAnchor.SW => "Bottom left",
				ViewZoomAnchor.S => "Bottom",
				_ => "Bottom right"
			};
		}

		public ViewZoomAnchor SelectedAnchor
		{
			get => this._selectedAnchor;
			set
			{
				this._selectedAnchor = value;
				this._suppressChanged = true;
				try
				{
					this._buttons[(int)value].IsChecked = true;
				}
				finally
				{
					this._suppressChanged = false;
				}
			}
		}

		private void OnButtonChecked(ViewZoomAnchor anchor)
		{
			this._selectedAnchor = anchor;
			if (!this._suppressChanged)
			{
				this.AnchorChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}
}
