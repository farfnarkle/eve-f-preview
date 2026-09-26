using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using EveFPreview.Configuration;
using EveFPreview.Services;
using EveFPreview.Services.Interop;
using EveFPreview.UI.Hotkeys;
using Color = System.Drawing.Color;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace EveFPreview.View
{
	/// <summary>
	/// A thumbnail window - the destination DWM draws the live client preview into. Positions and
	/// sizes are physical screen pixels throughout (that is what the config stores), so geometry goes
	/// through Win32 directly rather than WPF's DPI-scaled Left/Top/Width/Height.
	/// </summary>
	public abstract class ThumbnailView : Window, IThumbnailView
	{
		#region Private constants
		private const double OPACITY_THRESHOLD = 0.9;
		private const double OPACITY_EPSILON = 0.1;

		private const int WM_GETMINMAXINFO = 0x0024;
		private const int WM_STYLECHANGING = 0x007C;
		private const int WM_SIZING = 0x0214;
		private const int WM_ENTERSIZEMOVE = 0x0231;
		private const int WMSZ_TOP = 3;
		private const int WMSZ_TOPLEFT = 4;
		private const int WMSZ_TOPRIGHT = 5;
		private const int WMSZ_BOTTOM = 6;
		private const int SM_CXMAXTRACK = 59;
		private const int SM_CYMAXTRACK = 60;
		#endregion

		#region Private fields
		private readonly ThumbnailOverlay _overlay;
		private readonly IntPtr _handle;

		// Part of the logic (namely current size / position management)
		// was moved to the view due to the performance reasons
		private bool _isOverlayVisible;
		private bool _isOverlayLabelEnabled;
		private bool _isTopMost;
		private bool _isHighlightEnabled;
		private bool _isHighlightRequested;
		private int _highlightWidth;

		private bool _isLocationChanged;
		private bool _isSizeChanged;

		private bool _isCustomMouseModeActive;
		private UIElement _mouseCaptureElement;

		private double _opacity;

		private DateTime _suppressResizeEventsTimestamp;
		private Size _baseZoomSize;
		private Point _baseZoomLocation;
		private Point _baseMousePosition;
		private Size _baseZoomMaximumSize;

		private HotkeyHandler _hotkeyHandler;

		private IThumbnailConfiguration _config;

		// Captured when a frame drag starts, for "maintain aspect ratio".
		private double _sizingAspectRatio;
		private int _sizingFrameWidth;
		private int _sizingFrameHeight;
		private Lazy<Color> _myBorderColor;
		private Color _preventPreviewColorValue;
		private bool _preventPreviewsEnabled;
		private int _appliedPreventHighlightBorder = -1;
		private IThumbnailManager _thumbnailManager;
		private readonly ICharacterPortraitService _characterPortraitService;

		// Window state WinForms' Form used to track for us.
		private Size _minimumSize;
		private Size _maximumSize;
		private Rectangle _lastBounds;
		private Size _lastClientSize;
		private bool _isWindowShown;
		private bool _allowClose;
		#endregion

		protected ThumbnailView(IWindowManager windowManager, IThumbnailConfiguration config, IThumbnailManager thumbnailManager, ICharacterPortraitService characterPortraitService)
		{
			this._config = config;
			this.SuppressResizeEvent();

			this.WindowManager = windowManager;

			this.IsActive = false;

			this.IsOverlayEnabled = false;
			this._isOverlayVisible = false;
			this.IsExcludedFromCycleGroup = false;

			this._isTopMost = false;
			this._isHighlightEnabled = false;
			this._isHighlightRequested = false;

			this._isLocationChanged = true;
			this._isSizeChanged = true;

			this._isCustomMouseModeActive = false;

			this._opacity = 0.1;

			this.InitializeWindow();
			this._handle = new WindowInteropHelper(this).EnsureHandle();

			HwndSource source = HwndSource.FromHwnd(this._handle);
			source.AddHook(this.WndProc);
			// Nothing to render here but a background colour (DWM draws the preview on top), and the
			// GPU is busy enough with the EVE clients themselves.
			source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;

			// Layered for Opacity (what WinForms' Form.Opacity did); tool window keeps it out of Alt+Tab.
			uint exStyle = User32NativeMethods.GetWindowLong(this._handle, InteropConstants.GWL_EXSTYLE);
			User32NativeMethods.SetWindowLong(this._handle, InteropConstants.GWL_EXSTYLE, exStyle | InteropConstants.WS_EX_LAYERED | InteropConstants.WS_EX_TOOLWINDOW);
			this.ApplyLayeredOpacity(this._opacity);

			this._minimumSize = new Size(20, 20);
			this._maximumSize = Size.Empty;
			this.SetClientSizePixels(new Size(153, 89));
			this._lastBounds = this.GetWindowBounds();
			this._lastClientSize = this.GetClientSizePixels();

			this._overlay = new ThumbnailOverlay(this);

			this._thumbnailManager = thumbnailManager;
			this._characterPortraitService = characterPortraitService;

			SetDefaultBorderColor();
			SetPreventPreviews();
		}

		private void InitializeWindow()
		{
			base.Title = "Preview";
			this.WindowStyle = WindowStyle.ToolWindow;
			this.ResizeMode = ResizeMode.CanResize;
			this.ShowInTaskbar = false;
			this.ShowActivated = false;
			this.Topmost = true;
			this.SizeToContent = SizeToContent.Manual;
			this.WindowStartupLocation = WindowStartupLocation.Manual;
			this.Background = Brushes.Black;

			this.MouseEnter += (_, _) => this.HandleMouseEnter();
			this.MouseLeave += (_, _) => this.HandleMouseLeave();
			this.MouseDown += (_, e) => this.HandleMouseDown(this, e.ChangedButton);
			this.MouseUp += (_, e) => this.HandleMouseUp(this, e.ChangedButton);
			this.MouseMove += (_, e) => this.HandleMouseMove(e.LeftButton == MouseButtonState.Pressed, e.RightButton == MouseButtonState.Pressed);
		}

		public IWindowManager WindowManager { get; }

		public IntPtr Id { get; set; }

		/// <summary>This thumbnail window's own HWND.</summary>
		protected IntPtr Handle => this._handle;

		public new string Title
		{
			get => base.Title;
			set
			{
				base.Title = value;
				this._overlayCharacterName = value.Replace("EVE - ", "").Replace("EVE Frontier - ", "*");
				this.RefreshOverlayIdentityLabel();
				SetDefaultBorderColor();
				SetPreventPreviews();
				this._overlay.SetCycleGroupIndicator(this.IsExcludedFromCycleGroup , _config.CycleGroupIndicatorAnchor);
			}
		}

		private string _overlayCharacterName = string.Empty;
		private string _overlaySystemName;
		private bool _lastShowSystemNameOnThumbnail;

		/// <summary>Whether the thumbnail is currently shown (not WPF's Window.IsActive).</summary>
		public new bool IsActive { get; set; }

		public bool IsOverlayEnabled { get; set; }
		public bool IsExcludedFromCycleGroup { get; set; }
		public ZoomAnchor ClientZoomAnchor { get; set; }

		public Point ThumbnailLocation
		{
			get => this.WindowLocation;
			set => this.WindowLocation = value;
		}

		public Size ThumbnailSize
		{
			get => this.GetClientSizePixels();
			set => this.SetClientSizePixels(value);
		}

		public Action<IntPtr> ThumbnailResized { get; set; }

		public Action<IntPtr> ThumbnailMoved { get; set; }

		public Action<IntPtr> ThumbnailFocused { get; set; }

		public Action<IntPtr> ThumbnailLostFocus { get; set; }

		public Action<IntPtr> ThumbnailActivated { get; set; }

		public Action<IntPtr, bool> ThumbnailDeactivated { get; set; }
		public Action<IntPtr> ThumbnailFocusedOverwatchToggle { get; set; }
		public Action<IntPtr> ThumbnailToggleCycleGroup { get; set; }

		private bool WindowMoved = false;

		public void SetDefaultBorderColor()
		{
			this._myBorderColor = new Lazy<Color>(() =>
			{
				if (this._config.PerClientActiveClientHighlightColor.Any(x => x.Key == this.Title))
				{
					return this._config.PerClientActiveClientHighlightColor[Title];
				}
				else
				{
					return _config.ActiveClientHighlightColor;
				}
			});
		}

		public bool IsPreventPreviews()
		{
			return this._preventPreviewsEnabled;
		}

		public void SetPreventPreviews()
		{
			if (this._config.PerClientPreventPreviews.TryGetValue(this.Title, out bool perClientPrevent))
			{
				this._preventPreviewsEnabled = perClientPrevent;
			}
			else
			{
				this._preventPreviewsEnabled = this._config.PreventPreviews;
			}

			if (this._config.PerClientPreventPreviewColor.TryGetValue(this.Title, out Color perClientColor))
			{
				this._preventPreviewColorValue = perClientColor;
			}
			else
			{
				this._preventPreviewColorValue = this._config.PreventPreviewColor;
			}

			this._appliedPreventHighlightBorder = -1;
			this.OnPreventPreviewsChanged();
		}

		public void RefreshPortraitOverlay()
		{
			if (!this.IsPreventPreviews())
			{
				this._overlay.ClearPortrait();
				return;
			}

			this._overlay.SetPortraitImage(this._characterPortraitService.TryLoadPortraitImage(this.Title));
		}

		protected virtual void OnPreventPreviewsChanged()
		{
			this.Refresh(true);
		}

		private void ApplyPreventPreviewVisuals(int highlightBorderWidth)
		{
			Color highlightColor = highlightBorderWidth > 0 ? this._myBorderColor.Value : this._preventPreviewColorValue;
			this._overlay.EnableFakePreview(
				true,
				false,
				0,
				0,
				0,
				0,
				this._preventPreviewColorValue,
				highlightBorderWidth,
				highlightColor);
		}

		public new void Show()
		{
			this.SuppressResizeEvent();

			base.Show();
			this._isWindowShown = true;

			this._isLocationChanged = true;
			this._isSizeChanged = true;
			this._isOverlayVisible = false;

			this.Refresh(true);

			this.IsActive = true;
		}

		public new void Hide()
		{
			this.SuppressResizeEvent();

			this.IsActive = false;
			this._isWindowShown = false;

			this._isOverlayVisible = false;
			this._overlay.Hide();
			base.Hide();
		}

		public new virtual void Close()
		{
			this.SuppressResizeEvent();

			this.IsActive = false;
			this._isWindowShown = false;
			this._overlay.CloseOverlay();
			this._allowClose = true;
			base.Close();
		}

		// This method is used to determine if the provided Handle is related to client or its thumbnail
		public bool IsKnownHandle(IntPtr handle)
		{
			return (this.Id == handle) || (this._handle == handle) || (this._overlay.Handle == handle);
		}

		public void SetSizeLimitations(Size minimumSize, Size maximumSize)
		{
			if (this._minimumSize == minimumSize && this._maximumSize == maximumSize)
			{
				return;
			}

			this.SetMinimumSize(minimumSize);
			this.SetMaximumSize(maximumSize);
		}

		public void SetOpacity(double opacity)
		{
			if (opacity >= OPACITY_THRESHOLD)
			{
				opacity = 1.0;
			}

			if (Math.Abs(opacity - this._opacity) < OPACITY_EPSILON)
			{
				return;
			}

			if (!this.ApplyLayeredOpacity(opacity))
			{
				// Opacity will be updated in the next cycle
				return;
			}

			// Overlay opacity settings
			// Of the thumbnail's opacity is almost full then set the overlay's one to
			// full. Otherwise set it to half of the thumbnail opacity
			// Opacity value is stored even if the overlay is not displayed atm
			this._overlay.Opacity = opacity > 0.8 ? 1.0 : 1.0 - (1.0 - opacity) / 2;

			this._opacity = opacity;
		}

		public void SetFrames(bool enable)
		{
			bool framed = this.WindowStyle == WindowStyle.ToolWindow;

			// No need to change the borders style if it is ALREADY correct
			if (framed == enable)
			{
				return;
			}

			this.SuppressResizeEvent();

			Size clientSize = this.GetClientSizePixels();

			if (enable)
			{
				this.WindowStyle = WindowStyle.ToolWindow;
				this.ResizeMode = ResizeMode.CanResize;
			}
			else
			{
				this.WindowStyle = WindowStyle.None;
				this.ResizeMode = ResizeMode.NoResize;
			}

			// The frame grows or shrinks the window around the preview, not the preview itself (as
			// WinForms' FormBorderStyle did) - WPF would otherwise keep the outer size instead.
			this.SetClientSizePixels(clientSize);
		}

		/// <summary>Re-applies the label font, colour and position from the config.</summary>
		public void SetOverlayLabel()
		{
			this._overlay.SetPropertiesOverlayLabel(this._config.OverlayLabelFont, this._config.OverlayLabelColor, this._config.OverlayLabelAnchor);
		}

		public void SetCycleGroupIndicator(bool displayCycleGroup, ZoomAnchor anchor)
		{
			this._overlay.SetCycleGroupIndicator(displayCycleGroup, anchor);
		}

		public void SetTopMost(bool enableTopmost)
		{
			if (this._isTopMost == enableTopmost)
			{
				return;
			}

			this._overlay.Topmost = enableTopmost;
			this.Topmost = enableTopmost;
			this._isTopMost = enableTopmost;
		}

		public void SetClickThrough(bool enable)
		{
			ThumbnailView.ApplyClickThrough(this._handle, enable);
			ThumbnailView.ApplyClickThrough(this._overlay.Handle, enable);
		}

		public void SetSystemName(string systemName)
		{
			string normalized = string.IsNullOrWhiteSpace(systemName) ? null : systemName.Trim();
			bool showSystem = this._config.ShowSystemNameOnThumbnail;
			if (string.Equals(this._overlaySystemName, normalized, StringComparison.Ordinal)
				&& showSystem == this._lastShowSystemNameOnThumbnail)
			{
				return;
			}

			this._overlaySystemName = normalized;
			this.RefreshOverlayIdentityLabel();
		}

		private void RefreshOverlayIdentityLabel()
		{
			string label = this._overlayCharacterName ?? string.Empty;
			this._lastShowSystemNameOnThumbnail = this._config.ShowSystemNameOnThumbnail;
			if (this._lastShowSystemNameOnThumbnail)
			{
				string systemLabel = string.IsNullOrEmpty(this._overlaySystemName)
					? "[unknown]"
					: "[" + this._overlaySystemName + "]";
				label = string.IsNullOrEmpty(label)
					? systemLabel
					: label + Environment.NewLine + systemLabel;

				if (WormholeStatics.TryGet(this._overlaySystemName, out WormholeSystemInfo wormholeInfo))
				{
					label += Environment.NewLine + wormholeInfo.Class + " : " + string.Join("/", wormholeInfo.Statics);
				}
			}

			this._overlay.SetOverlayLabel(label);
			this._overlay.SetPropertiesOverlayLabel(this._config.OverlayLabelFont, this._config.OverlayLabelColor, this._config.OverlayLabelAnchor);
		}

		private static void ApplyClickThrough(IntPtr handle, bool enable)
		{
			uint style = User32NativeMethods.GetWindowLong(handle, InteropConstants.GWL_EXSTYLE);
			bool isTransparent = (style & InteropConstants.WS_EX_TRANSPARENT) == InteropConstants.WS_EX_TRANSPARENT;

			if (enable == isTransparent)
			{
				return;
			}

			style = enable ? (style | InteropConstants.WS_EX_TRANSPARENT) : (style & ~InteropConstants.WS_EX_TRANSPARENT);
			User32NativeMethods.SetWindowLong(handle, InteropConstants.GWL_EXSTYLE, style);
		}

		public void SetHighlight()
		{
			SetHighlight(_config.EnableActiveClientHighlight, _config.ActiveClientHighlightThickness);
		}

		public void SetHighlight(bool enabled, int width)
		{
			if (this._isHighlightRequested == enabled && (!enabled || this._highlightWidth == width))
			{
				return;
			}

			if (enabled)
			{
				this._isHighlightRequested = true;
				this._highlightWidth = width;
				this.Background = ThumbnailView.ToBrush(this.IsPreventPreviews() ? Color.Black : _myBorderColor.Value);
			}
			else
			{
				this._isHighlightRequested = false;
				this.Background = Brushes.Black;
			}

			this._isSizeChanged = true;
		}

		public void ClearBorder()
		{
			if (this._isHighlightRequested)
			{
				this.SetHighlight(false, 0);
			}
			else if (this.IsPreventPreviews())
			{
				this._isSizeChanged = true;
			}

			this.Refresh(true);
		}

		public void ZoomIn(ViewZoomAnchor anchor, int zoomFactor)
		{
			int oldWidth = this._baseZoomSize.Width;
			int oldHeight = this._baseZoomSize.Height;

			Point location = this.WindowLocation;
			int locationX = location.X;
			int locationY = location.Y;

			Size windowSize = this.WindowSize;
			Size clientSize = this.GetClientSizePixels();
			int newWidth = (zoomFactor * clientSize.Width) + (windowSize.Width - clientSize.Width);
			int newHeight = (zoomFactor * clientSize.Height) + (windowSize.Height - clientSize.Height);

			// First change size, THEN move the window
			// Otherwise there is a chance to fail in a loop
			// Zoom required -> Moved the windows 1st -> Focus is lost -> Window is moved back -> Focus is back on -> Zoom required -> ...
			this.SetMaximumSize(Size.Empty);
			this.WindowSize = new Size(newWidth, newHeight);

			switch (anchor)
			{
				case ViewZoomAnchor.NW:
					break;
				case ViewZoomAnchor.N:
					this.WindowLocation = new Point(locationX - newWidth / 2 + oldWidth / 2, locationY);
					break;
				case ViewZoomAnchor.NE:
					this.WindowLocation = new Point(locationX - newWidth + oldWidth, locationY);
					break;

				case ViewZoomAnchor.W:
					this.WindowLocation = new Point(locationX, locationY - newHeight / 2 + oldHeight / 2);
					break;
				case ViewZoomAnchor.C:
					this.WindowLocation = new Point(locationX - newWidth / 2 + oldWidth / 2, locationY - newHeight / 2 + oldHeight / 2);
					break;
				case ViewZoomAnchor.E:
					this.WindowLocation = new Point(locationX - newWidth + oldWidth, locationY - newHeight / 2 + oldHeight / 2);
					break;

				case ViewZoomAnchor.SW:
					this.WindowLocation = new Point(locationX, locationY - newHeight + this._baseZoomSize.Height);
					break;
				case ViewZoomAnchor.S:
					this.WindowLocation = new Point(locationX - newWidth / 2 + oldWidth / 2, locationY - newHeight + oldHeight);
					break;
				case ViewZoomAnchor.SE:
					this.WindowLocation = new Point(locationX - newWidth + oldWidth, locationY - newHeight + oldHeight);
					break;
			}
		}

		public void ZoomOut()
		{
			this.RestoreWindowSizeAndLocation();
		}

		public void RegisterHotkey(Keys hotkey)
		{
			if (this._hotkeyHandler != null)
			{
				this.UnregisterHotkey();
			}

			if (hotkey == Keys.None)
			{
				return;
			}

			this._hotkeyHandler = new HotkeyHandler(this._handle, hotkey);
			this._hotkeyHandler.Pressed += HotkeyPressed_Handler;
			this._hotkeyHandler.Register();
		}

		public void UnregisterHotkey()
		{
			if (this._hotkeyHandler == null)
			{
				return;
			}

			this._hotkeyHandler.Unregister();
			this._hotkeyHandler.Pressed -= HotkeyPressed_Handler;
			this._hotkeyHandler.Dispose();
			this._hotkeyHandler = null;
		}

		public void Refresh(bool forceRefresh)
		{
			this.RefreshThumbnail(forceRefresh);
			this.HighlightThumbnail(forceRefresh || this._isSizeChanged);
			this.RefreshOverlay(forceRefresh || this._isSizeChanged || this._isLocationChanged);
			this._isSizeChanged = false;
		}

		protected abstract void RefreshThumbnail(bool forceRefresh);

		protected abstract void ResizeThumbnail(int baseWidth, int baseHeight, int highlightWidthTop, int highlightWidthRight, int highlightWidthBottom, int highlightWidthLeft);

		private void HighlightThumbnail(bool forceRefresh)
		{
			if (!forceRefresh && (this._isHighlightRequested == this._isHighlightEnabled))
			{
				// Nothing to do here
				return;
			}

			this._isHighlightEnabled = this._isHighlightRequested;

			Size clientSize = this.GetClientSizePixels();
			int baseWidth = clientSize.Width;
			int baseHeight = clientSize.Height;

			if (this.IsPreventPreviews())
			{
				int border = this._isHighlightRequested ? this._highlightWidth : 0;
				if (forceRefresh || border != this._appliedPreventHighlightBorder)
				{
					this.ResizeThumbnail(baseWidth, baseHeight, 0, 0, 0, 0);
					this.ApplyPreventPreviewVisuals(border);
					this._appliedPreventHighlightBorder = border;
				}

				return;
			}

			if (!this._isHighlightRequested)
			{
				//No highlighting enabled, so no math required
				this.ResizeThumbnail(baseWidth, baseHeight, 0, 0, 0, 0);
				this._overlay.EnableFakePreview(false, false, 0, 0, 0, 0, Color.Empty);
				return;
			}

			double baseAspectRatio = ((double)baseWidth) / baseHeight;

			int actualHeight = baseHeight - 2 * this._highlightWidth;
			double desiredWidth = actualHeight * baseAspectRatio;
			int actualWidth = (int)Math.Round(desiredWidth, MidpointRounding.AwayFromZero);
			int highlightWidthLeft = (baseWidth - actualWidth) / 2;
			int highlightWidthRight = baseWidth - actualWidth - highlightWidthLeft;

			this._overlay.EnableFakePreview(false, true, this._highlightWidth, highlightWidthRight, this._highlightWidth, highlightWidthLeft, Color.Empty);
			this.ResizeThumbnail(baseWidth, baseHeight, this._highlightWidth, highlightWidthRight, this._highlightWidth, highlightWidthLeft);
		}

		private void RefreshOverlay(bool forceRefresh)
		{
			// Unlike the WinForms version (which also showed it for a hidden thumbnail in "do not
			// display previews" mode, leaving a stray portrait behind), the overlay only ever
			// accompanies a thumbnail that is actually on screen.
			bool shouldShowOverlay = this._isWindowShown
				&& (this.IsOverlayEnabled || this.IsPreventPreviews())
				&& !this._config.IsThumbnailDisabled(this.Title);

			if (!shouldShowOverlay)
			{
				if (this._isOverlayVisible)
				{
					this._overlay.Hide();
					this._isOverlayVisible = false;
				}

				return;
			}

			// The WinForms version returned here before looking at the settings, so turning "Show
			// overlay" off (or the label on/off in portrait mode) didn't take effect until something
			// else forced a refresh.
			if (this._isOverlayVisible && !forceRefresh && this._isOverlayLabelEnabled == this.IsOverlayEnabled)
			{
				// No need to update anything. Everything is already set up
				return;
			}

			this._isOverlayLabelEnabled = this.IsOverlayEnabled;
			this._overlay.EnableOverlayLabel(this.IsOverlayEnabled);

			this._isLocationChanged = false;
			this._overlay.SetPropertiesOverlayLabel(this._config.OverlayLabelFont, this._config.OverlayLabelColor, this._config.OverlayLabelAnchor);
			this._overlay.SetBounds(this.GetClientScreenBounds());

			if (!this._isOverlayVisible)
			{
				this._overlay.Show();
				this._isOverlayVisible = true;
			}

			this._overlay.Topmost = this.Topmost;

			if (this.IsPreventPreviews())
			{
				this.RefreshPortraitOverlay();
				this._overlay.BringToFront();
			}
		}

		private void SuppressResizeEvent()
		{
			// Workaround for the Resize event being fired with inconsistent client size values while
			// the window is being shown, hidden or re-framed. Any Resize events fired before this
			// timestamp will be ignored
			this._suppressResizeEventsTimestamp = DateTime.UtcNow.AddMilliseconds(_config.ThumbnailResizeTimeoutPeriod);
		}

		#region Window geometry (physical pixels)
		private Rectangle GetWindowBounds()
		{
			User32NativeMethods.GetWindowRect(this._handle, out RECT rect);
			return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
		}

		private Size GetClientSizePixels()
		{
			User32NativeMethods.GetClientRect(this._handle, out RECT rect);
			return new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
		}

		private Rectangle GetClientScreenBounds()
		{
			var origin = new WindowNativeMethods.POINT(0, 0);
			ClientToScreen(this._handle, ref origin);
			return new Rectangle(new Point(origin.X, origin.Y), this.GetClientSizePixels());
		}

		private Point WindowLocation
		{
			get => this.GetWindowBounds().Location;
			set => WindowNativeMethods.SetWindowPos(this._handle, IntPtr.Zero, value.X, value.Y, 0, 0,
				WindowNativeMethods.SWP_NOSIZE | WindowNativeMethods.SWP_NOZORDER | WindowNativeMethods.SWP_NOACTIVATE);
		}

		/// <summary>Outer window size. Setting it applies the minimum/maximum size limits, like Form.Size did.</summary>
		private Size WindowSize
		{
			get => this.GetWindowBounds().Size;
			set
			{
				Size size = this.ApplySizeLimits(value);
				WindowNativeMethods.SetWindowPos(this._handle, IntPtr.Zero, 0, 0, size.Width, size.Height,
					WindowNativeMethods.SWP_NOMOVE | WindowNativeMethods.SWP_NOZORDER | WindowNativeMethods.SWP_NOACTIVATE);
			}
		}

		private void SetClientSizePixels(Size clientSize)
		{
			Size window = this.WindowSize;
			Size client = this.GetClientSizePixels();
			this.WindowSize = new Size(clientSize.Width + (window.Width - client.Width), clientSize.Height + (window.Height - client.Height));
		}

		private Size ApplySizeLimits(Size size)
		{
			int width = Math.Min(size.Width, GetSystemMetrics(SM_CXMAXTRACK));
			int height = Math.Min(size.Height, GetSystemMetrics(SM_CYMAXTRACK));

			// Zero means "no limit" for either dimension, as in WinForms; the minimum wins over the maximum.
			if (this._maximumSize.Width > 0)
			{
				width = Math.Min(width, this._maximumSize.Width);
			}

			if (this._maximumSize.Height > 0)
			{
				height = Math.Min(height, this._maximumSize.Height);
			}

			width = Math.Max(width, this._minimumSize.Width);
			height = Math.Max(height, this._minimumSize.Height);
			return new Size(width, height);
		}

		private void SetMinimumSize(Size value)
		{
			if (this._minimumSize == value)
			{
				return;
			}

			this._minimumSize = value;
			if (!this._maximumSize.IsEmpty && !value.IsEmpty)
			{
				this._maximumSize = new Size(
					this._maximumSize.Width > 0 ? Math.Max(this._maximumSize.Width, value.Width) : 0,
					this._maximumSize.Height > 0 ? Math.Max(this._maximumSize.Height, value.Height) : 0);
			}

			Size size = this.WindowSize;
			if (size.Width < value.Width || size.Height < value.Height)
			{
				this.WindowSize = new Size(Math.Max(size.Width, value.Width), Math.Max(size.Height, value.Height));
			}
		}

		private void SetMaximumSize(Size value)
		{
			if (this._maximumSize == value)
			{
				return;
			}

			this._maximumSize = value;
			if (!this._minimumSize.IsEmpty && !value.IsEmpty)
			{
				this._minimumSize = new Size(
					value.Width > 0 ? Math.Min(this._minimumSize.Width, value.Width) : this._minimumSize.Width,
					value.Height > 0 ? Math.Min(this._minimumSize.Height, value.Height) : this._minimumSize.Height);
			}

			Size size = this.WindowSize;
			if ((value.Width > 0 && size.Width > value.Width) || (value.Height > 0 && size.Height > value.Height))
			{
				this.WindowSize = size;
			}
		}

		private bool ApplyLayeredOpacity(double opacity)
		{
			byte alpha = (byte)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 255.0);
			return WindowNativeMethods.SetLayeredWindowAttributes(this._handle, 0, alpha, WindowNativeMethods.LWA_ALPHA);
		}
		#endregion

		#region GUI events
		private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			switch (msg)
			{
				case WM_STYLECHANGING:
					this.OnStyleChanging(wParam.ToInt32(), lParam, ref handled);
					break;

				case WM_GETMINMAXINFO:
					this.OnGetMinMaxInfo(lParam, ref handled);
					break;

				case WM_ENTERSIZEMOVE:
					this.OnEnterSizeMove();
					break;

				case WM_SIZING:
					this.OnSizing(wParam.ToInt32(), lParam, ref handled);
					break;

				case WindowNativeMethods.WM_WINDOWPOSCHANGED:
					this.OnWindowPositionChanged();
					break;

				case WindowNativeMethods.WM_DPICHANGED:
					// Thumbnails keep their pixel size on every monitor (that's what the layout stores);
					// WPF would otherwise rescale the window to keep its DPI-independent size.
					handled = true;
					break;
			}

			return IntPtr.Zero;
		}

		private void OnStyleChanging(int styleIndex, IntPtr lParam, ref bool handled)
		{
			var styles = Marshal.PtrToStructure<STYLESTRUCT>(lParam);
			if (styleIndex == InteropConstants.GWL_EXSTYLE)
			{
				// WPF strips WS_EX_LAYERED from any window without AllowsTransparency, but opacity
				// needs it (per-pixel AllowsTransparency windows can't have a native frame or host a
				// DWM thumbnail properly). Keep it - and the tool-window bit - whatever WPF wants.
				styles.StyleNew |= InteropConstants.WS_EX_LAYERED | InteropConstants.WS_EX_TOOLWINDOW;
			}
			else if (styleIndex == InteropConstants.GWL_STYLE)
			{
				// WPF always adds a system menu (close button) and, with CanResize, min/max boxes;
				// the WinForms frame had neither.
				styles.StyleNew &= ~(InteropConstants.WS_SYSMENU | InteropConstants.WS_MINIMIZEBOX | InteropConstants.WS_MAXIMIZEBOX);
			}
			else
			{
				return;
			}

			Marshal.StructureToPtr(styles, lParam, false);
			handled = true;
		}

		private void OnEnterSizeMove()
		{
			User32NativeMethods.GetWindowRect(this._handle, out RECT window);
			User32NativeMethods.GetClientRect(this._handle, out RECT client);
			int clientWidth = client.Right - client.Left;
			int clientHeight = client.Bottom - client.Top;
			this._sizingFrameWidth = (window.Right - window.Left) - clientWidth;
			this._sizingFrameHeight = (window.Bottom - window.Top) - clientHeight;
			this._sizingAspectRatio = clientWidth > 0 && clientHeight > 0 ? (double)clientWidth / clientHeight : 0;
		}

		/// <summary>With "maintain aspect ratio" on, dragging a frame edge resizes the other dimension to match.</summary>
		private void OnSizing(int edge, IntPtr lParam, ref bool handled)
		{
			if (!this._config.MaintainThumbnailAspectRatio || this._sizingAspectRatio <= 0)
			{
				return;
			}

			var rect = Marshal.PtrToStructure<RECT>(lParam);
			int clientWidth = (rect.Right - rect.Left) - this._sizingFrameWidth;
			int clientHeight = (rect.Bottom - rect.Top) - this._sizingFrameHeight;

			if (edge == WMSZ_TOP || edge == WMSZ_BOTTOM)
			{
				// Vertical drag: height leads, width follows.
				rect.Right = rect.Left + (int)Math.Round(clientHeight * this._sizingAspectRatio) + this._sizingFrameWidth;
			}
			else
			{
				// Side or corner drag: width leads, height follows (from the edge being dragged).
				int height = (int)Math.Round(clientWidth / this._sizingAspectRatio) + this._sizingFrameHeight;
				if (edge == WMSZ_TOPLEFT || edge == WMSZ_TOPRIGHT)
				{
					rect.Top = rect.Bottom - height;
				}
				else
				{
					rect.Bottom = rect.Top + height;
				}
			}

			Marshal.StructureToPtr(rect, lParam, false);
			handled = true;
		}

		private void OnGetMinMaxInfo(IntPtr lParam, ref bool handled)
		{
			var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
			if (this._minimumSize.Width > 0)
			{
				info.MinTrackSize.X = this._minimumSize.Width;
			}

			if (this._minimumSize.Height > 0)
			{
				info.MinTrackSize.Y = this._minimumSize.Height;
			}

			if (this._maximumSize.Width > 0)
			{
				info.MaxTrackSize.X = this._maximumSize.Width;
			}

			if (this._maximumSize.Height > 0)
			{
				info.MaxTrackSize.Y = this._maximumSize.Height;
			}

			Marshal.StructureToPtr(info, lParam, false);
			handled = true;
		}

		/// <summary>
		/// Raised synchronously while the window is being moved/resized - including from our own
		/// SetWindowPos calls - which is when WinForms raised Move/Resize, and what the thumbnail
		/// manager's "ignore view events while I'm moving things" logic relies on.
		/// </summary>
		private void OnWindowPositionChanged()
		{
			Rectangle bounds = this.GetWindowBounds();
			Size clientSize = this.GetClientSizePixels();

			bool moved = bounds.Location != this._lastBounds.Location;
			bool resized = bounds.Size != this._lastBounds.Size || clientSize != this._lastClientSize;

			this._lastBounds = bounds;
			this._lastClientSize = clientSize;

			if (moved)
			{
				this.Move_Handler();
			}

			if (resized)
			{
				this.Resize_Handler();
			}
		}

		private void Move_Handler()
		{
			this._isLocationChanged = true;
			this.ThumbnailMoved?.Invoke(this.Id);
		}

		private void Resize_Handler()
		{
			if (DateTime.UtcNow < this._suppressResizeEventsTimestamp)
			{
				return;
			}

			this._isSizeChanged = true;

			this.ThumbnailResized?.Invoke(this.Id);
		}

		internal void HandleMouseEnter()
		{
			this.ExitCustomMouseMode();
			this.SaveWindowSizeAndLocation();

			this.ThumbnailFocused?.Invoke(this.Id);
		}

		internal void HandleMouseLeave()
		{
			this.ThumbnailLostFocus?.Invoke(this.Id);
		}

		internal void HandleMouseDown(UIElement source, MouseButton button)
		{
			this.MouseDownEventHandler(source, button, KeyboardState.GetModifierKeys());
		}

		internal void HandleMouseMove(bool leftButton, bool rightButton)
		{
			if (this._isCustomMouseModeActive)
			{
				this.ProcessCustomMouseMode(leftButton, rightButton);
			}
		}

		internal void HandleMouseUp(UIElement source, MouseButton button)
		{
			if (button == MouseButton.Right)
			{
				this.FinishCustomMouseMode();
			}
		}

		private void FinishCustomMouseMode()
		{
			if (this.WindowMoved && _config.ThumbnailSnapToEdges)
			{
				this._thumbnailManager.SnapThumbnail(this.Id);
			}

			this.ExitCustomMouseMode();

			if (this.WindowMoved)
			{
				if (_config.ThumbnailSnapToEdges)
				{
					this.ThumbnailMoved?.Invoke(this.Id);
				}
				else if (_config.ThumbnailSnapToGrid)
				{
					Point location = this.WindowLocation;
					var x = (int)Math.Round((double)location.X / (double)_config.ThumbnailSnapToGridSizeX) * _config.ThumbnailSnapToGridSizeX;
					var y = (int)Math.Round((double)location.Y / (double)_config.ThumbnailSnapToGridSizeY) * _config.ThumbnailSnapToGridSizeY;
					this.WindowLocation = new Point(x, y);
					this._baseZoomLocation = this.WindowLocation;
				}

				this.WindowMoved = false;
			}
		}

		private void HotkeyPressed_Handler(object sender, HandledEventArgs e)
		{
			this.SetHighlight();
			this.ThumbnailActivated?.Invoke(this.Id);

			e.Handled = true;
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			// Thumbnails come and go with their clients; ignore anything else asking them to close
			// (Alt+F4 on a focused thumbnail, taskkill's WM_CLOSE broadcast, ...).
			if (!this._allowClose)
			{
				e.Cancel = true;
			}

			base.OnClosing(e);
		}
		#endregion

		#region Custom Mouse mode
		// This pair of methods saves/restores certain window properties
		// Methods are used to remove the 'Zoom' effect (if any) when the
		// custom resize/move mode is activated
		// Methods are kept on this level because moving to the presenter
		// the code that responds to the mouse events like movement
		// seems like a huge overkill
		private void SaveWindowSizeAndLocation()
		{
			this._baseZoomSize = this.WindowSize;
			this._baseZoomLocation = this.WindowLocation;
			this._baseZoomMaximumSize = this._maximumSize;
		}

		private void RestoreWindowSizeAndLocation()
		{
			this.WindowSize = this._baseZoomSize;
			this.SetMaximumSize(this._baseZoomMaximumSize);
			this.WindowLocation = this._baseZoomLocation;
		}

		private void EnterCustomMouseMode(UIElement source)
		{
			this.RestoreWindowSizeAndLocation();

			this._isCustomMouseModeActive = true;
			this._baseMousePosition = DisplayMonitors.GetCursorPosition();
			this._thumbnailManager.NotifyThumbnailDragStarted(this.Id);

			// WinForms captured the mouse implicitly on button down; WPF needs it asked for, or a fast
			// drag outruns the window and loses the button-up.
			this._mouseCaptureElement = source;
			source.LostMouseCapture += this.CaptureElement_LostMouseCapture;
			source.CaptureMouse();
		}

		private void ProcessCustomMouseMode(bool leftButton, bool rightButton)
		{
			Point mousePosition = DisplayMonitors.GetCursorPosition();
			int offsetX = mousePosition.X - this._baseMousePosition.X;
			int offsetY = mousePosition.Y - this._baseMousePosition.Y;
			this._baseMousePosition = mousePosition;

			if (!_config.LockThumbnailLocation)
			{
				// Left + Right buttons trigger thumbnail resize
				// Right button only trigger thumbnail movement
				if (leftButton && rightButton)
				{
					Size size = this.WindowSize;
					this.WindowSize = new Size(size.Width + offsetX, size.Height + offsetY);
					this._baseZoomSize = this.WindowSize;
				}
				else
				{
					Point location = this.WindowLocation;
					this.WindowLocation = new Point(location.X + offsetX, location.Y + offsetY);
					this._baseZoomLocation = this.WindowLocation;
					this.WindowMoved = true;
				}
			}
		}

		private void ExitCustomMouseMode()
		{
			this._isCustomMouseModeActive = false;
			this._thumbnailManager.NotifyThumbnailDragEnded(this.Id);
			this.ReleaseCustomMouseCapture();
		}

		private void ReleaseCustomMouseCapture()
		{
			UIElement element = this._mouseCaptureElement;
			if (element == null)
			{
				return;
			}

			this._mouseCaptureElement = null;
			element.LostMouseCapture -= this.CaptureElement_LostMouseCapture;
			element.ReleaseMouseCapture();
		}

		private void CaptureElement_LostMouseCapture(object sender, MouseEventArgs e)
		{
			// Capture taken away mid-drag (another window grabbed it, Alt+Tab, ...): end the drag as if
			// the button had been released, rather than leaving the thumbnail stuck to the cursor.
			if (this._isCustomMouseModeActive && this._mouseCaptureElement != null)
			{
				this.FinishCustomMouseMode();
			}
		}
		#endregion

		#region Custom GUI events
		protected virtual void MouseDownEventHandler(UIElement source, MouseButton mouseButton, Keys modifierKeys)
		{
			switch (mouseButton)
			{
				case MouseButton.Left when modifierKeys == (Keys.Control | Keys.Shift):
					this.ThumbnailDeactivated?.Invoke(this.Id, true);
					break;
				case MouseButton.Left when modifierKeys == (Keys.Control | Keys.Alt):
					this.ThumbnailDeactivated?.Invoke(this.Id, false);
					break;
				case MouseButton.Left when modifierKeys == Keys.Control:
					this.ThumbnailFocusedOverwatchToggle?.Invoke(this.Id);
					break;
				case MouseButton.Left when modifierKeys == Keys.Shift:
					this.ThumbnailToggleCycleGroup?.Invoke(this.Id);
					break;
				case MouseButton.Left:
					var oldWindow = this._thumbnailManager.GetActiveClient();
					this.ThumbnailActivated?.Invoke(this.Id);
					this.SetHighlight();
					this.Refresh(true);

					oldWindow?.ClearBorder();
					break;
				case MouseButton.Right:
					this.EnterCustomMouseMode(source);
					break;
			}
		}
		#endregion

		private static SolidColorBrush ToBrush(Color color)
		{
			var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
			brush.Freeze();
			return brush;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct STYLESTRUCT
		{
			public uint StyleOld;
			public uint StyleNew;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct MINMAXINFO
		{
			public WindowNativeMethods.POINT Reserved;
			public WindowNativeMethods.POINT MaxSize;
			public WindowNativeMethods.POINT MaxPosition;
			public WindowNativeMethods.POINT MinTrackSize;
			public WindowNativeMethods.POINT MaxTrackSize;
		}

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool ClientToScreen(IntPtr hWnd, ref WindowNativeMethods.POINT lpPoint);

		[DllImport("user32.dll")]
		private static extern int GetSystemMetrics(int nIndex);
	}
}
