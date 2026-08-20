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
	/// </summary>
	static class ForegroundActivator
	{
		private const byte VirtualKeyCode = 0xE8;
		private const int FirstHotkeyId = 0xBF00; // High range, unlikely to collide with HotkeyHandler's own IDs.

		private static readonly uint[] ModifierCombinations = BuildModifierCombinations();

		private static readonly object Sync = new object();
		private static readonly List<InternalHotkeyFilter> Filters = new List<InternalHotkeyFilter>();
		private static bool _registered;
		private static IntPtr _pendingHandle;

		/// <summary>Brings the given window to the foreground, working around the permission restriction above.</summary>
		public static void Activate(IntPtr handle)
		{
			if (handle == IntPtr.Zero)
			{
				return;
			}

			lock (Sync)
			{
				EnsureRegistered();
				_pendingHandle = handle;
			}

			User32NativeMethods.keybd_event(VirtualKeyCode, 0, 0, UIntPtr.Zero);
			User32NativeMethods.keybd_event(VirtualKeyCode, 0, User32NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
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
			IntPtr handle;
			lock (Sync)
			{
				handle = _pendingHandle;
				_pendingHandle = IntPtr.Zero;
			}

			if (handle == IntPtr.Zero)
			{
				return;
			}

			// Two attempts, mirroring the same defensive retry other activation-via-hotkey tools use.
			if (!User32NativeMethods.SetForegroundWindow(handle))
			{
				User32NativeMethods.SetForegroundWindow(handle);
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
