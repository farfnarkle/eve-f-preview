using System;
using System.Collections.Generic;
using System.Drawing;
using EveFPreview.Configuration;
using EveFPreview.View;

namespace EveFPreview.Services
{
	sealed class CharacterIndicatorManager : ICharacterIndicatorManager
	{
		// Focus can genuinely bounce through a handful of transient windows for a moment while a
		// click is still being processed (e.g. clicking the settings window while EVE was active).
		// Without this, each of those transient reads independently drove a Show()/Hide(), and
		// hiding then re-showing a NOACTIVATE-but-just-created-or-shown window can itself nudge
		// which window the OS reports as foreground next - a self-sustaining flicker. Only acting
		// on a focus loss that's still true after this many ms breaks that loop, while regaining
		// focus still shows the window immediately (no delay on the way back in).
		private const int HideDelayMilliseconds = 350;

		private readonly IThumbnailConfiguration _configuration;
		private CharacterIndicatorForm _form;
		private Action<IntPtr> _cellClicked;
		private Action<IntPtr> _cellShiftClicked;
		private DateTime? _focusLostAtUtc;

		public CharacterIndicatorManager(IThumbnailConfiguration configuration)
		{
			this._configuration = configuration;
		}

		public Action<IntPtr> CellClicked
		{
			get => this._cellClicked;
			set
			{
				this._cellClicked = value;
				if (this._form != null)
				{
					this._form.CellClicked = value;
				}
			}
		}

		public Action<IntPtr> CellShiftClicked
		{
			get => this._cellShiftClicked;
			set
			{
				this._cellShiftClicked = value;
				if (this._form != null)
				{
					this._form.CellShiftClicked = value;
				}
			}
		}

		public void Start()
		{
			// The window itself is created lazily on the first UpdateLayout call where the
			// feature is actually enabled - nothing to do on startup otherwise.
		}

		public void Stop()
		{
			if (this._form == null)
			{
				return;
			}

			this._form.Close();
			this._form.Dispose();
			this._form = null;
		}

		public void UpdateLayout(IReadOnlyList<IReadOnlyList<CharacterIndicatorCell>> rows, bool eveHasFocus)
		{
			if (!this._configuration.EnableCharacterIndicator)
			{
				this._focusLostAtUtc = null;
				if (this._form != null && this._form.Visible)
				{
					this._form.Hide();
				}

				return;
			}

			if (eveHasFocus)
			{
				this._focusLostAtUtc = null;
			}
			else
			{
				bool alreadyVisible = this._form != null && this._form.Visible;
				if (!alreadyVisible)
				{
					// Nothing currently shown to bridge over - stay hidden, no need to start a timer.
					this._focusLostAtUtc = null;
					return;
				}

				this._focusLostAtUtc ??= DateTime.UtcNow;
				bool pastGracePeriod = (DateTime.UtcNow - this._focusLostAtUtc.Value).TotalMilliseconds >= HideDelayMilliseconds;
				if (pastGracePeriod)
				{
					this._form.Hide();
					this._focusLostAtUtc = null;
					return;
				}

				// Still within the grace period - keep it showing and fall through to refresh it.
			}

			this.EnsureFormCreated();

			if (!this._form.Visible)
			{
				this._form.Show();
			}

			this._form.Locked = this._configuration.LockCharacterIndicatorLocation;
			this._form.ClickToActivate = this._configuration.EnableCharacterIndicatorClickToActivate;
			this._form.SetRows(rows);
		}

		private void EnsureFormCreated()
		{
			if (this._form != null)
			{
				return;
			}

			this._form = new CharacterIndicatorForm();
			this._form.LocationDragged = location => this._configuration.CharacterIndicatorLocation = location;
			this._form.CellClicked = this._cellClicked;
			this._form.CellShiftClicked = this._cellShiftClicked;

			Point savedLocation = this._configuration.CharacterIndicatorLocation;
			this._form.Location = savedLocation != Point.Empty ? savedLocation : new Point(40, 40);
		}
	}
}
