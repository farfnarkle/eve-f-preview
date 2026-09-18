using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EveFPreview.Services;

namespace EveFPreview.View
{
	/// <summary>
	/// Small borderless, always-on-top window showing a grid of squares that mirrors the
	/// current thumbnail layout - soft grey for each tracked client, gold for whichever one is
	/// active. Drag anywhere on it to reposition; the new spot is reported via
	/// <see cref="LocationDragged"/> for the caller to persist.
	/// </summary>
	sealed class CharacterIndicatorForm : Form
	{
		private const int SquareSize = 18;
		private const int SquareGap = 4;
		private const int EdgePadding = 6;
		private const int CornerRadius = 4;

		/// <summary>Screen-pixel movement under which a press+release is treated as a click, not a drag.</summary>
		private const int ClickDragThresholdPixels = 4;

		private static readonly Color BackgroundColor = Color.FromArgb(225, 30, 30, 30);
		private static readonly Color SquareColor = Color.FromArgb(255, 125, 125, 125);
		private static readonly Color ActiveSquareColor = Color.FromArgb(255, 219, 172, 52);
		private static readonly Color DisabledSquareColor = Color.FromArgb(255, 199, 62, 52);
		private static readonly Color ExcludedSquareColor = Color.FromArgb(255, 70, 70, 70);

		private IReadOnlyList<IReadOnlyList<CharacterIndicatorCell>> _rows = Array.Empty<IReadOnlyList<CharacterIndicatorCell>>();
		private bool _dragging;
		private bool _shiftHeldOnDown;
		private Point _dragMouseStart;
		private Point _dragFormStart;
		private Point _mouseDownClientLocation;
		private bool _clickToActivate;

		/// <summary>Fired (with the new top-left) after the window is dragged to a new spot.</summary>
		public Action<Point> LocationDragged { get; set; }

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
				this.Cursor = value ? Cursors.Hand : Cursors.Default;
			}
		}

		public CharacterIndicatorForm()
		{
			this.FormBorderStyle = FormBorderStyle.None;
			this.ShowInTaskbar = false;
			this.TopMost = true;
			this.StartPosition = FormStartPosition.Manual;
			this.BackColor = Color.Black;
			this.DoubleBuffered = true;
			this.MinimumSize = new Size(EdgePadding * 2 + SquareSize, EdgePadding * 2 + SquareSize);

			this.MouseDown += this.CharacterIndicatorForm_MouseDown;
			this.MouseMove += this.CharacterIndicatorForm_MouseMove;
			this.MouseUp += this.CharacterIndicatorForm_MouseUp;
			this.Paint += this.CharacterIndicatorForm_Paint;
		}

		protected override CreateParams CreateParams
		{
			get
			{
				CreateParams p = base.CreateParams;
				// TOOLWINDOW keeps it off the taskbar/alt-tab; NOACTIVATE keeps clicking or
				// dragging it from ever making it the foreground window - it's a passive readout,
				// and stealing focus would immediately hide it again (see "only show it when an
				// EVE client has focus" in CharacterIndicatorManager).
				p.ExStyle |= (int)InteropConstants.WS_EX_TOOLWINDOW | (int)InteropConstants.WS_EX_NOACTIVATE;
				return p;
			}
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

			int width = (EdgePadding * 2) + (Math.Max(colCount, 1) * SquareSize) + (Math.Max(colCount - 1, 0) * SquareGap);
			int height = (EdgePadding * 2) + (Math.Max(rowCount, 1) * SquareSize) + (Math.Max(rowCount - 1, 0) * SquareGap);

			if (this.Size.Width != width || this.Size.Height != height)
			{
				this.Size = new Size(width, height);
			}

			this.Invalidate();
		}

		private void CharacterIndicatorForm_Paint(object sender, PaintEventArgs e)
		{
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

			using (SolidBrush background = new SolidBrush(CharacterIndicatorForm.BackgroundColor))
			{
				e.Graphics.FillRectangle(background, this.ClientRectangle);
			}

			for (int rowIndex = 0; rowIndex < this._rows.Count; rowIndex++)
			{
				IReadOnlyList<CharacterIndicatorCell> row = this._rows[rowIndex];
				for (int colIndex = 0; colIndex < row.Count; colIndex++)
				{
					CharacterIndicatorCell cell = row[colIndex];

					int x = EdgePadding + (colIndex * (SquareSize + SquareGap));
					int y = EdgePadding + (rowIndex * (SquareSize + SquareGap));
					Rectangle bounds = new Rectangle(x, y, SquareSize, SquareSize);
					Color color = cell.IsDisabled
						? CharacterIndicatorForm.DisabledSquareColor
						: cell.IsActive
							? CharacterIndicatorForm.ActiveSquareColor
							: cell.IsExcludedFromCycleGroup
								? CharacterIndicatorForm.ExcludedSquareColor
								: CharacterIndicatorForm.SquareColor;

					using (GraphicsPath path = CharacterIndicatorForm.RoundedRect(bounds, CornerRadius))
					using (SolidBrush brush = new SolidBrush(color))
					{
						e.Graphics.FillPath(brush, path);
					}
				}
			}
		}

		private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
		{
			int diameter = radius * 2;
			GraphicsPath path = new GraphicsPath();

			path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
			path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
			path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
			path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
			path.CloseFigure();

			return path;
		}

		private void CharacterIndicatorForm_MouseDown(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Left)
			{
				return;
			}

			this._mouseDownClientLocation = e.Location;
			this._dragMouseStart = Cursor.Position;
			this._dragFormStart = this.Location;
			this._shiftHeldOnDown = (ModifierKeys & Keys.Shift) == Keys.Shift;
			// Shift-click is a gesture, not a drag start - same as the real thumbnail, holding
			// shift never moves anything.
			this._dragging = !this.Locked && !this._shiftHeldOnDown;
		}

		private void CharacterIndicatorForm_MouseMove(object sender, MouseEventArgs e)
		{
			if (!this._dragging)
			{
				return;
			}

			Point current = Cursor.Position;
			int dx = current.X - this._dragMouseStart.X;
			int dy = current.Y - this._dragMouseStart.Y;

			this.Location = new Point(this._dragFormStart.X + dx, this._dragFormStart.Y + dy);
		}

		private void CharacterIndicatorForm_MouseUp(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Left)
			{
				return;
			}

			bool wasDragging = this._dragging;
			this._dragging = false;

			Point current = Cursor.Position;
			bool moved = Math.Abs(current.X - this._dragMouseStart.X) > ClickDragThresholdPixels
				|| Math.Abs(current.Y - this._dragMouseStart.Y) > ClickDragThresholdPixels;

			if (wasDragging && moved)
			{
				this.LocationDragged?.Invoke(this.Location);
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
				int y = EdgePadding + (rowIndex * (SquareSize + SquareGap));
				if (clientLocation.Y < y || clientLocation.Y > y + SquareSize)
				{
					continue;
				}

				IReadOnlyList<CharacterIndicatorCell> row = this._rows[rowIndex];
				for (int colIndex = 0; colIndex < row.Count; colIndex++)
				{
					int x = EdgePadding + (colIndex * (SquareSize + SquareGap));
					if (clientLocation.X < x || clientLocation.X > x + SquareSize)
					{
						continue;
					}

					handler(row[colIndex].Handle);
					return;
				}
			}
		}
	}
}
