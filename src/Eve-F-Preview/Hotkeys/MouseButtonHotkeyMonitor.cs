using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace EveFPreview.UI.Hotkeys
{
	/// <summary>
	/// Windows RegisterHotKey cannot bind mouse buttons. Extra buttons (side / middle)
	/// are observed through a process-wide low-level mouse hook instead.
	/// </summary>
	static class MouseButtonHotkeyMonitor
	{
		private static readonly object Sync = new object();
		private static readonly List<HotkeyHandler> Handlers = new List<HotkeyHandler>();
		private static readonly HotkeyHandlerNativeMethods.LowLevelMouseProc HookCallback = HookProc;

		private static IntPtr _hookHandle;
		private static int _inHook;
		private static bool _unhookPending;
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
				RequestUnhookIfIdle();
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
				RequestUnhookIfIdle();
			}
		}

		private static bool EnsureHook()
		{
			if (_hookHandle != IntPtr.Zero)
			{
				_unhookPending = false;
				return true;
			}

			_hookHandle = HotkeyHandlerNativeMethods.SetWindowsHookEx(
				HotkeyHandlerNativeMethods.WH_MOUSE_LL,
				HookCallback,
				IntPtr.Zero,
				0);

			return _hookHandle != IntPtr.Zero;
		}

		private static void RequestUnhookIfIdle()
		{
			if (_hookHandle == IntPtr.Zero)
			{
				return;
			}

			if (Handlers.Count > 0 || _captureCallback != null)
			{
				return;
			}

			if (Volatile.Read(ref _inHook) > 0)
			{
				_unhookPending = true;
				return;
			}

			UnhookCore();
		}

		private static void UnhookCore()
		{
			if (_hookHandle == IntPtr.Zero)
			{
				return;
			}

			HotkeyHandlerNativeMethods.UnhookWindowsHookEx(_hookHandle);
			_hookHandle = IntPtr.Zero;
			_unhookPending = false;
		}

		private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
		{
			if (nCode < 0)
			{
				return HotkeyHandlerNativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
			}

			Interlocked.Increment(ref _inHook);
			bool consume = false;
			try
			{
				consume = HandleMouseMessage((int)wParam.ToInt64(), lParam);
			}
			finally
			{
				Interlocked.Decrement(ref _inHook);
				if (Volatile.Read(ref _inHook) == 0)
				{
					lock (Sync)
					{
						if (_unhookPending)
						{
							RequestUnhookIfIdle();
						}
					}
				}
			}

			if (consume)
			{
				return (IntPtr)1;
			}

			return HotkeyHandlerNativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
		}

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
			HotkeyHandler[] handlers;
			lock (Sync)
			{
				captureCallback = _captureCallback;
				captureSync = _captureSync;
				if (captureCallback != null)
				{
					_captureCallback = null;
					_captureSync = null;
				}

				handlers = Handlers.Count == 0 ? Array.Empty<HotkeyHandler>() : Handlers.ToArray();
			}

			if (captureCallback != null)
			{
				DispatchCapture(captureCallback, captureSync, combo);
				return true;
			}

			bool handled = false;
			foreach (HotkeyHandler handler in handlers)
			{
				if (handler.KeyCode == combo && handler.RaisePressed())
				{
					handled = true;
				}
			}

			return handled;
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
			return (HotkeyHandlerNativeMethods.GetAsyncKeyState((int)key) & 0x8000) != 0;
		}
	}
}
