using System;
using System.Collections.Generic;
using System.Windows.Forms;
using EveFPreview.Services.Interop;

namespace EveFPreview.UI.Hotkeys
{
	/// <summary>
	/// Windows only grants SetForegroundWindow to a caller that just handled a WM_HOTKEY message it
	/// registered via RegisterHotKey. Our cycle hotkeys need to activate a window from contexts that
	/// don't get that automatic grant (most reliably reproduced with mouse-button hotkeys, which are
	/// driven by a raw input hook, not RegisterHotKey), so a plain SetForegroundWindow call there
	/// silently no-ops - our own active-client tracking updates but the OS foreground never moves.
	///
	/// This claims an unused virtual key (0xE8, "unassigned" in the VK table) as a real RegisterHotKey
	/// binding across every modifier combination, then "presses" it via keybd_event immediately before
	/// activating - borrowing the grant that a genuine WM_HOTKEY handler gets. Same trick EVE-X Preview
	/// uses for the same problem.
	///
	/// Requests are served one at a time from a queue instead of a single shared slot: firing several
	/// synthetic presses back-to-back let one request's press arrive after another request had already
	/// overwritten the pending target, silently losing the first activation. Here the next press isn't
	/// fired until the previous one's WM_HOTKEY has actually been consumed, and every completion is
	/// verified against the real foreground window (not just trusted) so the caller knows immediately
	/// whether it actually needs to fall back to something else.
	/// </summary>
	static class ForegroundActivator
	{
		private const byte VirtualKeyCode = 0xE8;
		private const int FirstHotkeyId = 0xBF00; // High range, unlikely to collide with HotkeyHandler's own IDs.
		private const int WaitForForegroundMilliseconds = 150; // Upper bound for the OS to finish a foreground switch (observed: usually <25ms, occasionally >40ms).
		private const int MismatchGraceMilliseconds = 20; // How long a different, real foreground window may persist before we call the switch refused.
		private const int WatchdogMilliseconds = 500; // Recovery only: a synthetic press should be consumed in well under this.

		private static readonly uint[] ModifierCombinations = BuildModifierCombinations();

		private static readonly object Sync = new object();
		private static readonly List<InternalHotkeyFilter> Filters = new List<InternalHotkeyFilter>();
		private static readonly Queue<PendingActivation> Pending = new Queue<PendingActivation>();
		private static Timer _watchdog;
		private static bool _registered;
		private static bool _pressInFlight;

		private sealed class PendingActivation
		{
			public IntPtr Handle;
			public Action<bool> OnCompleted;
		}

		/// <summary>
		/// Brings the given window to the foreground, working around the permission restriction
		/// above. <paramref name="onCompleted"/> (if given) is invoked on the UI thread once this
		/// request has actually been processed, with whether the foreground window really is now
		/// <paramref name="handle"/> - not just whether SetForegroundWindow claimed success.
		/// </summary>
		public static void Activate(IntPtr handle, Action<bool> onCompleted = null)
		{
			if (handle == IntPtr.Zero)
			{
				onCompleted?.Invoke(false);
				return;
			}

			// RegisterHotKey ties the hotkey to the calling thread's message queue, and
			// Application.AddMessageFilter only sees messages pumped by the UI thread's message
			// loop. Callers can reach us from a background thread - registering there would tie the
			// hotkey to a thread that never pumps messages, silently breaking activation for the
			// rest of the process's lifetime.
			if (Application.OpenForms.Count > 0)
			{
				Form host = Application.OpenForms[0];
				if (host.InvokeRequired)
				{
					host.BeginInvoke(new Action(() => ForegroundActivator.Enqueue(handle, onCompleted)));
					return;
				}
			}

			Enqueue(handle, onCompleted);
		}

		/// <summary>
		/// True once the real foreground window is <paramref name="handle"/>. Right after a
		/// SetForegroundWindow the OS briefly reports NO foreground window (observed as 0x0 for
		/// roughly 15ms) while the switch completes, so a single immediate check reads that
		/// transient as a failure and needlessly escalates. Poll until it settles instead.
		/// </summary>
		private static bool WaitForForeground(IntPtr handle, int maxMilliseconds = WaitForForegroundMilliseconds)
		{
			long freq = System.Diagnostics.Stopwatch.Frequency;
			long start = System.Diagnostics.Stopwatch.GetTimestamp();
			long hardDeadline = start + (freq * maxMilliseconds / 1000);
			long mismatchSince = 0;

			while (true)
			{
				IntPtr foreground = User32NativeMethods.GetForegroundWindow();
				long now = System.Diagnostics.Stopwatch.GetTimestamp();

				if (foreground == handle)
				{
					return true;
				}

				if (foreground == IntPtr.Zero)
				{
					// Mid-switch: no foreground window yet. Not a refusal - keep waiting.
					mismatchSince = 0;
				}
				else
				{
					// Some other window is (still) in front. It may just not have flipped yet, but if
					// it stays that way for a moment the switch was refused.
					if (mismatchSince == 0)
					{
						mismatchSince = now;
					}
					else if (now - mismatchSince >= freq * MismatchGraceMilliseconds / 1000)
					{
						return false;
					}
				}

				if (now >= hardDeadline)
				{
					return false;
				}

				System.Threading.Thread.Sleep(0);
			}
		}

		/// <summary>
		/// Tries progressively heavier ways of forcing <paramref name="handle"/> to the foreground,
		/// stopping as soon as the real foreground window matches. A plain SetForegroundWindow can
		/// still be refused (foreground lock, or the previous foreground thread owning input), so a
		/// single failed attempt used to leave the thumbnail/indicator advanced with the client
		/// not actually swapped.
		/// </summary>
		private static bool TryBringToForeground(IntPtr handle)
		{
			User32NativeMethods.SetForegroundWindow(handle);
			if (WaitForForeground(handle))
			{
				return true;
			}

			// Share input state with the current foreground thread so the OS treats us as allowed to change it.
			IntPtr foreground = User32NativeMethods.GetForegroundWindow();
			uint foregroundThread = foreground != IntPtr.Zero
				? User32NativeMethods.GetWindowThreadProcessId(foreground, out _)
				: 0;
			uint currentThread = User32NativeMethods.GetCurrentThreadId();

			bool attached = foregroundThread != 0
				&& foregroundThread != currentThread
				&& User32NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
			try
			{
				User32NativeMethods.BringWindowToTop(handle);
				User32NativeMethods.SetForegroundWindow(handle);
			}
			finally
			{
				if (attached)
				{
					User32NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
				}
			}

			if (WaitForForeground(handle))
			{
				return true;
			}

			// Last resort: a bare Alt tap is the classic way to lift the foreground lock.
			User32NativeMethods.keybd_event(User32NativeMethods.VK_MENU, 0, 0, UIntPtr.Zero);
			User32NativeMethods.keybd_event(User32NativeMethods.VK_MENU, 0, User32NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
			User32NativeMethods.SetForegroundWindow(handle);

			return WaitForForeground(handle);
		}

		private static void Enqueue(IntPtr handle, Action<bool> onCompleted)
		{
			bool shouldFire;
			bool directSucceeded = false;
			lock (Sync)
			{
				// Fast path: when nothing else is in flight, try a plain SetForegroundWindow first.
				// If we were called synchronously from inside a genuine WM_HOTKEY handler - which is
				// exactly what the keyboard cycle hotkeys are - Windows has ALREADY granted this
				// thread the right to move the foreground window. Spending that grant directly is
				// instant and can't race; kicking off a fresh synthetic-press round-trip instead
				// throws it away and gambles on the 0xE8 press being pumped back before the grant
				// goes stale. The synthetic press stays as the fallback for callers that never got
				// the automatic grant (the mouse-button hook path).
				if (!_pressInFlight && Pending.Count == 0)
				{
					if (!User32NativeMethods.SetForegroundWindow(handle))
					{
						User32NativeMethods.SetForegroundWindow(handle);
					}

					directSucceeded = WaitForForeground(handle);
				}

				if (directSucceeded)
				{
					shouldFire = false;
				}
				else
				{
					EnsureRegistered();
					Pending.Enqueue(new PendingActivation { Handle = handle, OnCompleted = onCompleted });
					shouldFire = !_pressInFlight;
					_pressInFlight = true;
				}
			}

			if (directSucceeded)
			{
				onCompleted?.Invoke(true);
				return;
			}

			if (shouldFire)
			{
				FireSyntheticPress();
			}
		}

		private static void FireSyntheticPress()
		{
			User32NativeMethods.keybd_event(VirtualKeyCode, 0, 0, UIntPtr.Zero);
			User32NativeMethods.keybd_event(VirtualKeyCode, 0, User32NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);

			_watchdog ??= new Timer();
			_watchdog.Interval = WatchdogMilliseconds;
			_watchdog.Tick -= Watchdog_Tick;
			_watchdog.Tick += Watchdog_Tick;
			_watchdog.Start();
		}

		private static void Watchdog_Tick(object sender, EventArgs e)
		{
			// The synthetic press never came back as a WM_HOTKEY (some other app may have grabbed
			// it, or the OS dropped it under load) - don't let the queue stall forever because of it.
			OnHotkeyFired();
		}

		private static uint[] BuildModifierCombinations()
		{
			uint[] bits =
			{
				HotkeyHandlerNativeMethods.MOD_CONTROL,
				HotkeyHandlerNativeMethods.MOD_ALT,
				HotkeyHandlerNativeMethods.MOD_SHIFT,
				HotkeyHandlerNativeMethods.MOD_WIN
			};

			// Every combination (including none) of the four modifiers, so whichever real modifier
			// keys happen to be physically held when we synthesize the key press still matches.
			var combinations = new uint[1 << bits.Length];
			for (int mask = 0; mask < combinations.Length; mask++)
			{
				uint combo = 0;
				for (int bitIndex = 0; bitIndex < bits.Length; bitIndex++)
				{
					if ((mask & (1 << bitIndex)) != 0)
					{
						combo |= bits[bitIndex];
					}
				}

				combinations[mask] = combo;
			}

			return combinations;
		}

		private static void EnsureRegistered()
		{
			if (_registered)
			{
				return;
			}

			int nextId = ForegroundActivator.FirstHotkeyId;
			foreach (uint modifiers in ModifierCombinations)
			{
				var filter = new InternalHotkeyFilter(nextId++, modifiers);
				if (filter.Register())
				{
					Filters.Add(filter);
					Application.AddMessageFilter(filter);
				}
			}

			_registered = true;
		}

		private static void OnHotkeyFired()
		{
			_watchdog?.Stop();

			PendingActivation activation;
			bool fireNext;
			lock (Sync)
			{
				if (Pending.Count == 0)
				{
					_pressInFlight = false;
					return;
				}

				activation = Pending.Dequeue();
				fireNext = Pending.Count > 0;
				_pressInFlight = fireNext;
			}

			bool confirmed = ForegroundActivator.TryBringToForeground(activation.Handle);
			activation.OnCompleted?.Invoke(confirmed);

			if (fireNext)
			{
				FireSyntheticPress();
			}
		}

		private sealed class InternalHotkeyFilter : IMessageFilter
		{
			private readonly int _id;
			private readonly uint _modifiers;

			public InternalHotkeyFilter(int id, uint modifiers)
			{
				this._id = id;
				this._modifiers = modifiers;
			}

			public bool Register()
			{
				return HotkeyHandlerNativeMethods.RegisterHotKey(IntPtr.Zero, this._id, this._modifiers, ForegroundActivator.VirtualKeyCode);
			}

			public bool PreFilterMessage(ref Message m)
			{
				if (m.Msg != HotkeyHandlerNativeMethods.WM_HOTKEY || m.WParam.ToInt32() != this._id)
				{
					return false;
				}

				ForegroundActivator.OnHotkeyFired();
				return true;
			}
		}
	}
}
