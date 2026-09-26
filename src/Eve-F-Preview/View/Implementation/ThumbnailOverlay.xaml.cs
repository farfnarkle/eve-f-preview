using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using EveFPreview.Configuration;
using EveFPreview.Services;
using EveFPreview.Services.Interop;
using DrawingColor = System.Drawing.Color;
using Rectangle = System.Drawing.Rectangle;

namespace EveFPreview.View
{
	/// <summary>
	/// Per-pixel transparent window laid over a thumbnail: character label, cycle-group badge and,
	/// with "do not display previews", the portrait. It has to be a separate window because DWM draws
	/// the live thumbnail on top of anything rendered inside the thumbnail window itself.
	/// </summary>
	public partial class ThumbnailOverlay : Window
	{
		#region Private fields
		private static readonly Brush LiveBackground = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

		private readonly ThumbnailView _owner;
		private readonly IntPtr _handle;
		private readonly HwndSource _source;
		private Rectangle _bounds;
		private bool _allowClose;

		private bool _fakePreviewEnabled;
		private DrawingColor _fakePreviewBackgroundColor;
		private int _fakePreviewBorderWidth;
		private DrawingColor _fakePreviewBorderColor;
		private bool _fakePreviewLayoutApplied;
		private (int Top, int Right, int Bottom, int Left) _previewInset;

		private bool _indicatorVisible;
		private ZoomAnchor _indicatorAnchor;
		private bool _indicatorLayoutDirty;

		private OverlayFont _labelFont;
		private DrawingColor _labelColor;
		private ZoomAnchor? _labelAnchor;
		#endregion

		public ThumbnailOverlay(ThumbnailView owner)
		{
			this._owner = owner;
			this.InitializeComponent();

			this.Owner = owner;
			this._handle = new WindowInteropHelper(this).EnsureHandle();
			this._source = HwndSource.FromHwnd(this._handle);
			this._source.AddHook(this.WndProc);

			uint exStyle = User32NativeMethods.GetWindowLong(this._handle, InteropConstants.GWL_EXSTYLE);
			User32NativeMethods.SetWindowLong(this._handle, InteropConstants.GWL_EXSTYLE, exStyle | InteropConstants.WS_EX_TOOLWINDOW);

			this.MouseEnter += (_, _) => this._owner.HandleMouseEnter();
			this.MouseLeave += (_, _) => this._owner.HandleMouseLeave();
			this.MouseDown += (_, e) => this._owner.HandleMouseDown(this, e.ChangedButton);
			this.MouseUp += (_, e) => this._owner.HandleMouseUp(this, e.ChangedButton);
			this.MouseMove += (_, e) => this._owner.HandleMouseMove(e.LeftButton == MouseButtonState.Pressed, e.RightButton == MouseButtonState.Pressed);

			this.RootGrid.SizeChanged += (_, _) => this.InvalidateIndicatorLayout();
			this.OverlayLabel.SizeChanged += (_, _) => this.InvalidateIndicatorLayout();
			this.LayoutUpdated += (_, _) => this.UpdateCycleGroupIndicatorLayout();
		}

		public IntPtr Handle => this._handle;

		/// <summary>Places the overlay in physical screen pixels (the thumbnail's client area).</summary>
		public void SetBounds(Rectangle bounds)
		{
			this._bounds = bounds;
			WindowNativeMethods.SetWindowPos(this._handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height,
				WindowNativeMethods.SWP_NOZORDER | WindowNativeMethods.SWP_NOACTIVATE);
		}

		public void BringToFront()
		{
			WindowNativeMethods.SetWindowPos(this._handle, WindowNativeMethods.HWND_TOP, 0, 0, 0, 0,
				WindowNativeMethods.SWP_NOMOVE | WindowNativeMethods.SWP_NOSIZE | WindowNativeMethods.SWP_NOACTIVATE);
		}

		/// <summary>Closes the overlay for good (a plain Close() from elsewhere, e.g. WM_CLOSE, is ignored).</summary>
		public void CloseOverlay()
		{
			this._allowClose = true;
			this.Close();
		}

		public void SetOverlayLabel(string label)
		{
			if (this.OverlayLabel.Text == label)
			{
				return;
			}

			this.OverlayLabel.Text = label;
			this.InvalidateIndicatorLayout();
		}

		public void SetCycleGroupIndicator(bool displayCycleGroup, ZoomAnchor anchor)
		{
			this._indicatorVisible = displayCycleGroup;
			this._indicatorAnchor = anchor;
			this.CycleGroupIndicator.Visibility = displayCycleGroup ? Visibility.Visible : Visibility.Collapsed;
			this.InvalidateIndicatorLayout();
		}

		public void SetPropertiesOverlayLabel(OverlayFont font, DrawingColor color, ZoomAnchor anchor)
		{
			if (font != null && !font.Equals(this._labelFont))
			{
				this._labelFont = font;
				this.OverlayLabel.FontFamily = new FontFamily(font.FamilyName);
				this.OverlayLabel.FontSize = font.SizeInDips;
				this.OverlayLabel.FontWeight = font.Bold ? FontWeights.Bold : FontWeights.Normal;
				this.OverlayLabel.FontStyle = font.Italic ? FontStyles.Italic : FontStyles.Normal;

				var decorations = new TextDecorationCollection();
				if (font.Underline)
				{
					decorations.Add(TextDecorations.Underline);
				}

				if (font.Strikeout)
				{
					decorations.Add(TextDecorations.Strikethrough);
				}

				this.OverlayLabel.TextDecorations = decorations;
			}

			if (color != this._labelColor)
			{
				this._labelColor = color;
				this.OverlayLabel.Foreground = ThumbnailOverlay.ToBrush(color);
			}

			if (anchor != this._labelAnchor)
			{
				this._labelAnchor = anchor;
				(HorizontalAlignment horizontal, VerticalAlignment vertical) = ThumbnailOverlay.ToAlignment(anchor);
				this.OverlayLabel.HorizontalAlignment = horizontal;
				this.OverlayLabel.VerticalAlignment = vertical;
				this.InvalidateIndicatorLayout();
			}
		}

		public void EnableOverlayLabel(bool enable)
		{
			// Hidden rather than collapsed: the badge is centred on the label's box even when the
			// text itself isn't shown.
			this.OverlayLabel.Visibility = enable ? Visibility.Visible : Visibility.Hidden;
		}

		public void SetPortraitImage(ImageSource image)
		{
			this.PortraitImage.Source = image;
		}

		public void ClearPortrait()
		{
			this.PortraitImage.Source = null;
		}

		public void EnableFakePreview(bool enable, bool resizeForHighlight, int insetTop, int insetRight, int insetBottom, int insetLeft, DrawingColor bgColor, int opaqueBorderWidth = 0, DrawingColor opaqueBorderColor = default)
		{
			int borderWidth = Math.Max(0, opaqueBorderWidth);

			if (!enable)
			{
				this._fakePreviewLayoutApplied = false;
				this._fakePreviewEnabled = false;
				this.FakePreviewBorder.Visibility = Visibility.Collapsed;
				this.ClearPortrait();
				this.SetPreviewInset(resizeForHighlight ? (insetTop, insetRight, insetBottom, insetLeft) : (0, 0, 0, 0));
				return;
			}

			if (this._fakePreviewLayoutApplied
				&& this._fakePreviewEnabled
				&& this._fakePreviewBackgroundColor == bgColor
				&& this._fakePreviewBorderWidth == borderWidth
				&& this._fakePreviewBorderColor == opaqueBorderColor)
			{
				return;
			}

			this._fakePreviewEnabled = true;
			this._fakePreviewBackgroundColor = bgColor;
			this._fakePreviewBorderWidth = borderWidth;
			this._fakePreviewBorderColor = opaqueBorderColor;
			this._fakePreviewLayoutApplied = true;

			// Portrait / prevent-preview: an opaque surface filling the overlay, with the highlight
			// drawn as a frame around it.
			this.FakePreviewBorder.Background = ThumbnailOverlay.ToBrush(bgColor);
			this.FakePreviewBorder.BorderBrush = ThumbnailOverlay.ToBrush(opaqueBorderColor);
			this.FakePreviewBorder.BorderThickness = this.PixelsToDips(borderWidth, borderWidth, borderWidth, borderWidth);
			this.FakePreviewBorder.Visibility = Visibility.Visible;
			this.SetPreviewInset((0, 0, 0, 0));
		}

		private void SetPreviewInset((int Top, int Right, int Bottom, int Left) inset)
		{
			if (this._previewInset == inset)
			{
				return;
			}

			this._previewInset = inset;
			this.IndicatorLayer.Margin = this.PixelsToDips(inset.Left, inset.Top, inset.Right, inset.Bottom);
			this.InvalidateIndicatorLayout();
		}

		private void InvalidateIndicatorLayout()
		{
			this._indicatorLayoutDirty = true;
		}

		/// <summary>
		/// The badge is a large square (the smaller side of the preview, minus a small margin) centred
		/// on the character label, so it covers the name; with no label it falls back to the
		/// configured corner.
		/// </summary>
		private void UpdateCycleGroupIndicatorLayout()
		{
			if (!this._indicatorLayoutDirty || !this._indicatorVisible)
			{
				return;
			}

			this._indicatorLayoutDirty = false;

			const double margin = 2;
			double hostWidth = this.IndicatorLayer.ActualWidth;
			double hostHeight = this.IndicatorLayer.ActualHeight;
			if (hostWidth <= 0 || hostHeight <= 0)
			{
				this._indicatorLayoutDirty = true;
				return;
			}

			double size = Math.Max(16, Math.Min(Math.Max(0, hostWidth - 2 * margin), Math.Max(0, hostHeight - 2 * margin)));
			double left;
			double top;

			bool labelOk = !string.IsNullOrEmpty(this.OverlayLabel.Text)
				&& this.OverlayLabel.ActualWidth > 0
				&& this.OverlayLabel.ActualHeight > 0;

			if (labelOk)
			{
				Point labelOrigin = this.OverlayLabel.TranslatePoint(new Point(0, 0), this.IndicatorLayer);
				left = labelOrigin.X + (this.OverlayLabel.ActualWidth - size) / 2;
				top = labelOrigin.Y + (this.OverlayLabel.ActualHeight - size) / 2;
				left = Math.Max(0, Math.Min(left, hostWidth - size));
				top = Math.Max(0, Math.Min(top, hostHeight - size));
			}
			else
			{
				(HorizontalAlignment horizontal, VerticalAlignment vertical) = ThumbnailOverlay.ToAlignment(this._indicatorAnchor);
				left = horizontal switch
				{
					HorizontalAlignment.Left => margin,
					HorizontalAlignment.Right => hostWidth - size - margin,
					_ => (hostWidth - size) / 2
				};
				top = vertical switch
				{
					VerticalAlignment.Top => margin,
					VerticalAlignment.Bottom => hostHeight - size - margin,
					_ => (hostHeight - size) / 2
				};
			}

			this.CycleGroupIndicator.Width = size;
			this.CycleGroupIndicator.Height = size;
			System.Windows.Controls.Canvas.SetLeft(this.CycleGroupIndicator, left);
			System.Windows.Controls.Canvas.SetTop(this.CycleGroupIndicator, top);
		}

		private Thickness PixelsToDips(int left, int top, int right, int bottom)
		{
			Matrix fromDevice = this._source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
			return new Thickness(left * fromDevice.M11, top * fromDevice.M22, right * fromDevice.M11, bottom * fromDevice.M22);
		}

		private static (HorizontalAlignment, VerticalAlignment) ToAlignment(ZoomAnchor anchor)
		{
			return anchor switch
			{
				ZoomAnchor.N => (HorizontalAlignment.Center, VerticalAlignment.Top),
				ZoomAnchor.NE => (HorizontalAlignment.Right, VerticalAlignment.Top),
				ZoomAnchor.W => (HorizontalAlignment.Left, VerticalAlignment.Center),
				ZoomAnchor.C => (HorizontalAlignment.Center, VerticalAlignment.Center),
				ZoomAnchor.E => (HorizontalAlignment.Right, VerticalAlignment.Center),
				ZoomAnchor.SW => (HorizontalAlignment.Left, VerticalAlignment.Bottom),
				ZoomAnchor.S => (HorizontalAlignment.Center, VerticalAlignment.Bottom),
				ZoomAnchor.SE => (HorizontalAlignment.Right, VerticalAlignment.Bottom),
				_ => (HorizontalAlignment.Left, VerticalAlignment.Top)
			};
		}

		private static SolidColorBrush ToBrush(DrawingColor color)
		{
			var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
			brush.Freeze();
			return brush;
		}

		protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
		{
			// Only the owning thumbnail decides when its overlay goes away.
			if (!this._allowClose)
			{
				e.Cancel = true;
			}

			base.OnClosing(e);
		}

		private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (msg == WindowNativeMethods.WM_DPICHANGED)
			{
				// Let WPF re-render the text for the new monitor's DPI, but it also resizes the window
				// to keep its DPI-independent size - the overlay has to match the thumbnail's pixels,
				// so put the bounds back once WPF is done.
				this.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
				{
					if (!this._bounds.IsEmpty)
					{
						this.SetBounds(this._bounds);
					}
				}));
			}

			return IntPtr.Zero;
		}
	}
}
