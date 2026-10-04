using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using EveFPreview.Services.Interop;

namespace EveFPreview.UI.Hotkeys
{
	/// <summary>
	/// Windows RegisterHotKey cannot bind mouse buttons. Extra buttons (side / middle)
	/// are observed through a process-wide low-level mouse hook instead.
	///
	/// The hook lives on a thread of its own. Windows waits for a low-level hook's answer before
	/// moving the cursor at all, so a hook on the UI thread froze the mouse system-wide whenever the
	/// UI was busy - and Windows silently removes a hook that keeps it waiting too long. Here the
	/// hook thread only decides whether a click is one of ours (and swallows it); the handlers run
	/// on the UI thread.
	/// </summary>
	static class MouseButtonHotkeyMonitor
	{
		private static readonly object Sync = new object();
		private static readonly List<HotkeyHandler> Handlers = new List<HotkeyHandler>();
		private static readonly HotkeyHandlerNativeMethods.LowLevelMouseProc HookCallback = HookProc;

		private static Thread _hookThread;
		private static uint _hookThreadId;
		private static IntPtr _hookHandle;
		private static Action<Keys> _captureCallback;
		private static SynchronizationContext _captureSync;

		public static bool IsExtraMouseButton(Keys keys)
		{
			Keys code = keys & Keys.KeyCode;
			return code == Keys.XButton1
				|| code == Keys.XButton2
				|| code == Keys.MButton;
		}

		public static bool Register(HotkeyHandler handler)
		{
			if (handler == null)
			{
				return false;
			}

			lock (Sync)
			{
				if (!EnsureHook())
				{
					return false;
				}

				if (!Handlers.Contains(handler))
				{
					Handlers.Add(handler);
				}

				return true;
			}
		}

		public static void Unregister(HotkeyHandler handler)
		{
			lock (Sync)
			{
				Handlers.Remove(handler);
				UnhookIfIdle();
			}
		}

		public static bool BeginCapture(Action<Keys> onCaptured)
		{
			if (onCaptured == null)
			{
				return false;
			}

			lock (Sync)
			{
				if (!EnsureHook())
				{
					return false;
				}

				_captureCallback = onCaptured;
				_captureSync = SynchronizationContext.Current;
				return true;
			}
		}

		public static void EndCapture()
		{
			lock (Sync)
			{
				_captureCallback = null;
				_captureSync = null;
				UnhookIfIdle();
			}
		}

		/// <summary>Starts the hook thread (if it isn't running) and waits until its hook is installed. Call under Sync.</summary>
		private static bool EnsureHook()
		{
			if (_hookThread != null)
			{
				return true;
			}

			using var ready = new ManualResetEventSlim(false);
			IntPtr installedHook = IntPtr.Zero;

			var thread = new Thread(() =>
			{
				_hookThreadId = HotkeyHandlerNativeMethods.GetCurrentThreadId();
				installedHook = HotkeyHandlerNativeMethods.SetWindowsHookEx(
					HotkeyHandlerNativeMethods.WH_MOUSE_LL,
					HookCallback,
					IntPtr.Zero,
					0);
				_hookHandle = installedHook;
				ready.Set();

				if (installedHook == IntPtr.Zero)
				{
					return;
				}

				// Low-level hook callbacks are delivered while this thread waits in GetMessage.
				// WM_QUIT (posted by UnhookIfIdle) ends the loop.
				while (HotkeyHandlerNativeMethods.GetMessage(out HotkeyHandlerNativeMethods.Msg _, IntPtr.Zero, 0, 0) > 0)
				{
				}

				HotkeyHandlerNativeMethods.UnhookWindowsHookEx(installedHook);
			})
			{
				IsBackground = true,
				Name = "Mouse hotkey hook",
				// The cursor waits on this thread for every mouse event; never let other work starve it.
				Priority = ThreadPriority.Highest
			};

			thread.Start();
			ready.Wait();

			if (installedHook == IntPtr.Zero)
			{
				return false;
			}

			_hookThread = thread;
			return true;
		}

		/// <summary>Stops the hook thread once nothing needs mouse buttons any more. Call under Sync.</summary>
		private static void UnhookIfIdle()
		{
			if (_hookThread == null || Handlers.Count > 0 || _captureCallback != null)
			{
				return;
			}

			// The thread unhooks and exits after any callback it's in the middle of.
			HotkeyHandlerNativeMethods.PostThreadMessage(_hookThreadId, HotkeyHandlerNativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
			_hookThread = null;
		}

		private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
		{
			if (nCode >= 0 && HandleMouseMessage((int)wParam.ToInt64(), lParam))
			{
				return (IntPtr)1;
			}

			return HotkeyHandlerNativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
		}

		/// <summary>Runs on the hook thread: keep it quick. Returns whether to swallow the click.</summary>
		private static bool HandleMouseMessage(int message, IntPtr lParam)
		{
			Keys button = MouseMessageToKeys(message, lParam);
			if (button == Keys.None)
			{
				return false;
			}

			Keys combo = button | ReadModifierKeys();

			Action<Keys> captureCallback;
			SynchronizationContext captureSync;
			List<HotkeyHandler> matching = null;
			lock (Sync)
			{
				captureCallback = _captureCallback;
				captureSync = _captureSync;
				if (captureCallback != null)
				{
					_captureCallback = null;
					_captureSync = null;
				}
				else
				{
					foreach (HotkeyHandler handler in Handlers)
					{
						if (handler.KeyCode == combo)
						{
							(matching ??= new List<HotkeyHandler>()).Add(handler);
						}
					}
				}
			}

			if (captureCallback != null)
			{
				DispatchCapture(captureCallback, captureSync, combo);
				return true;
			}

			if (matching == null)
			{
				return false;
			}

			// Every handler takes the press (they all mark it handled), so it's swallowed here
			// without waiting for them; they run on the UI thread, where their work belongs.
			UiThread.Run(() =>
			{
				foreach (HotkeyHandler handler in matching)
				{
					handler.RaisePressed();
				}
			});

			return true;
		}

		private static void DispatchCapture(Action<Keys> callback, SynchronizationContext sync, Keys combo)
		{
			if (sync != null)
			{
				sync.Post(_ => callback(combo), null);
				return;
			}

			callback(combo);
		}

		private static Keys MouseMessageToKeys(int message, IntPtr lParam)
		{
			switch (message)
			{
				case HotkeyHandlerNativeMethods.WM_MBUTTONDOWN:
					return Keys.MButton;
				case HotkeyHandlerNativeMethods.WM_XBUTTONDOWN:
				case HotkeyHandlerNativeMethods.WM_XBUTTONDBLCLK:
					var info = Marshal.PtrToStructure<HotkeyHandlerNativeMethods.MsllHookStruct>(lParam);
					int xButton = (int)((info.MouseData >> 16) & 0xFFFF);
					if (xButton == HotkeyHandlerNativeMethods.XBUTTON1)
					{
						return Keys.XButton1;
					}

					if (xButton == HotkeyHandlerNativeMethods.XBUTTON2)
					{
						return Keys.XButton2;
					}

					return Keys.None;
				default:
					return Keys.None;
			}
		}

		private static Keys ReadModifierKeys()
		{
			Keys modifiers = Keys.None;
			if (IsKeyDown(Keys.ControlKey))
			{
				modifiers |= Keys.Control;
			}

			if (IsKeyDown(Keys.ShiftKey))
			{
				modifiers |= Keys.Shift;
			}

			if (IsKeyDown(Keys.Menu))
			{
				modifiers |= Keys.Alt;
			}

			return modifiers;
		}

		private static bool IsKeyDown(Keys key)
		{
			return User32NativeMethods.IsKeyPhysicallyDown((int)key);
		}
	}
}
