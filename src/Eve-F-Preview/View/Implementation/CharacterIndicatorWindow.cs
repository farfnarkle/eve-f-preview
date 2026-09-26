using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using EveFPreview.Services;
using EveFPreview.Services.Interop;
using DrawingPoint = System.Drawing.Point;

namespace EveFPreview.View
{
	/// <summary>
	/// Small borderless, always-on-top window showing a grid of squares that mirrors the
	/// current thumbnail layout - soft grey for each tracked client, gold for whichever one is
	/// active. Drag anywhere on it to reposition; the new spot is reported via
	/// <see cref="LocationDragged"/> for the caller to persist.
	/// </summary>
	sealed class CharacterIndicatorWindow : Window
	{
		private const double SquareSize = 18;
		private const double SquareGap = 4;
		private const double EdgePadding = 6;
		private const double CornerRadius = 4;

		private const int WM_STYLECHANGING = 0x007C;

		/// <summary>Screen-pixel movement under which a press+release is treated as a click, not a drag.</summary>
		private const int ClickDragThresholdPixels = 4;

		private static readonly Brush BackgroundBrush = CharacterIndicatorWindow.Frozen(Color.FromArgb(225, 30, 30, 30));
		private static readonly Brush SquareBrush = CharacterIndicatorWindow.Frozen(Color.FromArgb(255, 125, 125, 125));
		private static readonly Brush ActiveSquareBrush = CharacterIndicatorWindow.Frozen(Color.FromArgb(255, 219, 172, 52));
		private static readonly Brush ExcludedSquareBrush = CharacterIndicatorWindow.Frozen(Color.FromArgb(255, 70, 70, 70));

		private readonly IntPtr _handle;
		private readonly Canvas _canvas;
		private IReadOnlyList<IReadOnlyList<CharacterIndicatorCell>> _rows = Array.Empty<IReadOnlyList<CharacterIndicatorCell>>();
		private bool _dragging;
		private bool _shiftHeldOnDown;
		private DrawingPoint _dragMouseStart;
		private DrawingPoint _dragFormStart;
		private Point _mouseDownClientLocation;
		private bool _clickToActivate;

		/// <summary>Fired (with the new top-left, in screen pixels) after the window is dragged to a new spot.</summary>
		public Action<DrawingPoint> LocationDragged { get; set; }

		/// <summary>Fired (with the client's window handle) when a square is clicked while <see cref="ClickToActivate"/> is on.</summary>
		public Action<IntPtr> CellClicked { get; set; }

		/// <summary>Fired (with the client's window handle) when a square is shift-clicked. Always active, independent of <see cref="ClickToActivate"/> - mirrors shift-clicking the real thumbnail.</summary>
		public Action<IntPtr> CellShiftClicked { get; set; }

		/// <summary>When true, dragging is disabled - the window stays put.</summary>
		public bool Locked { get; set; }

		/// <summary>When true, clicking a square (without dragging) activates that client.</summary>
		public bool ClickToActivate
		{
			get => this._clickToActivate;
			set
			{
				this._clickToActivate = value;
				this.Cursor = value ? Cursors.Hand : Cursors.Arrow;
			}
		}

		public CharacterIndicatorWindow()
		{
			this.Title = "EVE-F-Preview character indicator";
			this.WindowStyle = WindowStyle.None;
			this.ResizeMode = ResizeMode.NoResize;
			this.AllowsTransparency = true;
			this.Background = Brushes.Transparent;
			this.ShowInTaskbar = false;
			this.ShowActivated = false;
			this.Topmost = true;
			this.SizeToContent = SizeToContent.WidthAndHeight;
			this.WindowStartupLocation = WindowStartupLocation.Manual;

			this._canvas = new Canvas();
			this.Content = new Border
			{
				Background = CharacterIndicatorWindow.BackgroundBrush,
				CornerRadius = new CornerRadius(CornerRadius),
				Child = this._canvas
			};

			this._handle = new WindowInteropHelper(this).EnsureHandle();
			HwndSource.FromHwnd(this._handle).AddHook(this.WndProc);

			// TOOLWINDOW keeps it off the taskbar/alt-tab; NOACTIVATE keeps clicking or
			// dragging it from ever making it the foreground window - it's a passive readout,
			// and stealing focus would immediately hide it again (see "only show it when an
			// EVE client has focus" in CharacterIndicatorManager).
			uint exStyle = User32NativeMethods.GetWindowLong(this._handle, InteropConstants.GWL_EXSTYLE);
			User32NativeMethods.SetWindowLong(this._handle, InteropConstants.GWL_EXSTYLE, exStyle | InteropConstants.WS_EX_TOOLWINDOW | InteropConstants.WS_EX_NOACTIVATE);

			this.MouseLeftButtonDown += this.CharacterIndicatorWindow_MouseLeftButtonDown;
			this.MouseMove += this.CharacterIndicatorWindow_MouseMove;
			this.MouseLeftButtonUp += this.CharacterIndicatorWindow_MouseLeftButtonUp;

			this.SetRows(this._rows);
		}

		/// <summary>Moves the window's top-left to <paramref name="location"/> (screen pixels).</summary>
		public void SetLocation(DrawingPoint location)
		{
			WindowNativeMethods.SetWindowPos(this._handle, IntPtr.Zero, location.X, location.Y, 0, 0,
				WindowNativeMethods.SWP_NOSIZE | WindowNativeMethods.SWP_NOZORDER | WindowNativeMethods.SWP_NOACTIVATE);
		}

		private DrawingPoint GetLocation()
		{
			User32NativeMethods.GetWindowRect(this._handle, out RECT rect);
			return new DrawingPoint(rect.Left, rect.Top);
		}

		public void SetRows(IReadOnlyList<IReadOnlyList<CharacterIndicatorCell>> rows)
		{
			this._rows = rows ?? Array.Empty<IReadOnlyList<CharacterIndicatorCell>>();

			int rowCount = this._rows.Count;
			int colCount = 0;
			foreach (IReadOnlyList<CharacterIndicatorCell> row in this._rows)
			{
				if (row.Count > colCount)
				{
					colCount = row.Count;
				}
			}

			this._canvas.Width = (EdgePadding * 2) + (Math.Max(colCount, 1) * SquareSize) + (Math.Max(colCount - 1, 0) * SquareGap);
			this._canvas.Height = (EdgePadding * 2) + (Math.Max(rowCount, 1) * SquareSize) + (Math.Max(rowCount - 1, 0) * SquareGap);

			this._canvas.Children.Clear();
			for (int rowIndex = 0; rowIndex < this._rows.Count; rowIndex++)
			{
				IReadOnlyList<CharacterIndicatorCell> row = this._rows[rowIndex];
				for (int colIndex = 0; colIndex < row.Count; colIndex++)
				{
					CharacterIndicatorCell cell = row[colIndex];
					var square = new Rectangle
					{
						Width = SquareSize,
						Height = SquareSize,
						RadiusX = CornerRadius,
						RadiusY = CornerRadius,
						Fill = cell.IsActive
							? CharacterIndicatorWindow.ActiveSquareBrush
							: cell.IsExcludedFromCycleGroup
								? CharacterIndicatorWindow.ExcludedSquareBrush
								: CharacterIndicatorWindow.SquareBrush,
						ToolTip = cell.Title
					};
					Canvas.SetLeft(square, EdgePadding + (colIndex * (SquareSize + SquareGap)));
					Canvas.SetTop(square, EdgePadding + (rowIndex * (SquareSize + SquareGap)));
					this._canvas.Children.Add(square);
				}
			}
		}

		private void CharacterIndicatorWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			this._mouseDownClientLocation = e.GetPosition(this._canvas);
			this._dragMouseStart = DisplayMonitors.GetCursorPosition();
			this._dragFormStart = this.GetLocation();
			this._shiftHeldOnDown = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift
				|| WindowNativeMethods.IsKeyDown(WindowNativeMethods.VK_SHIFT);
			// Shift-click is a gesture, not a drag start - same as the real thumbnail, holding
			// shift never moves anything.
			this._dragging = !this.Locked && !this._shiftHeldOnDown;
			this.CaptureMouse();
		}

		private void CharacterIndicatorWindow_MouseMove(object sender, MouseEventArgs e)
		{
			if (!this._dragging)
			{
				return;
			}

			DrawingPoint current = DisplayMonitors.GetCursorPosition();
			int dx = current.X - this._dragMouseStart.X;
			int dy = current.Y - this._dragMouseStart.Y;

			this.SetLocation(new DrawingPoint(this._dragFormStart.X + dx, this._dragFormStart.Y + dy));
		}

		private void CharacterIndicatorWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			this.ReleaseMouseCapture();

			bool wasDragging = this._dragging;
			this._dragging = false;

			DrawingPoint current = DisplayMonitors.GetCursorPosition();
			bool moved = Math.Abs(current.X - this._dragMouseStart.X) > ClickDragThresholdPixels
				|| Math.Abs(current.Y - this._dragMouseStart.Y) > ClickDragThresholdPixels;

			if (wasDragging && moved)
			{
				this.LocationDragged?.Invoke(this.GetLocation());
				return;
			}

			// Not enough movement to count as a drag (or dragging was locked/shifted) - treat it
			// as a click. Shift-click always works, regardless of ClickToActivate - it mirrors the
			// real thumbnail's shift-click, which isn't gated behind any indicator setting.
			if (this._shiftHeldOnDown)
			{
				this.HandleCellClick(this._mouseDownClientLocation, this.CellShiftClicked);
			}
			else if (this._clickToActivate)
			{
				this.HandleCellClick(this._mouseDownClientLocation, this.CellClicked);
			}
		}

		private void HandleCellClick(Point clientLocation, Action<IntPtr> handler)
		{
			if (handler == null)
			{
				return;
			}

			for (int rowIndex = 0; rowIndex < this._rows.Count; rowIndex++)
			{
				double y = EdgePadding + (rowIndex * (SquareSize + SquareGap));
				if (clientLocation.Y < y || clientLocation.Y > y + SquareSize)
				{
					continue;
				}

				IReadOnlyList<CharacterIndicatorCell> row = this._rows[rowIndex];
				for (int colIndex = 0; colIndex < row.Count; colIndex++)
				{
					double x = EdgePadding + (colIndex * (SquareSize + SquareGap));
					if (clientLocation.X < x || clientLocation.X > x + SquareSize)
					{
						continue;
					}

					handler(row[colIndex].Handle);
					return;
				}
			}
		}

		private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (msg == WM_STYLECHANGING && wParam.ToInt32() == InteropConstants.GWL_EXSTYLE)
			{
				// Keep the no-activate/tool-window bits through any restyle WPF does later.
				int[] styles = new int[2];
				Marshal.Copy(lParam, styles, 0, 2);
				styles[1] |= unchecked((int)(InteropConstants.WS_EX_TOOLWINDOW | InteropConstants.WS_EX_NOACTIVATE));
				Marshal.Copy(styles, 0, lParam, 2);
			}

			return IntPtr.Zero;
		}

		private static Brush Frozen(Color color)
		{
			var brush = new SolidColorBrush(color);
			brush.Freeze();
			return brush;
		}
	}
}
