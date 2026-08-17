using System;
using System.Runtime.InteropServices;

namespace EveFPreview.UI.Hotkeys
{
	static class HotkeyHandlerNativeMethods
	{
		[DllImport("user32.dll")]
		public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

		[DllImport("user32.dll")]
		public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

		public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool UnhookWindowsHookEx(IntPtr hhk);

		[DllImport("user32.dll")]
		public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll")]
		public static extern short GetAsyncKeyState(int vKey);

		public const uint WM_HOTKEY = 0x0312;
		public const int WM_MBUTTONDOWN = 0x0207;
		public const int WM_XBUTTONDOWN = 0x020B;
		public const int WM_XBUTTONDBLCLK = 0x020D;
		public const int WM_NCXBUTTONDOWN = 0x00AB;
		public const int WM_NCMBUTTONDOWN = 0x00A7;

		public const int WH_MOUSE_LL = 14;
		public const int XBUTTON1 = 0x0001;
		public const int XBUTTON2 = 0x0002;

		public const uint MOD_ALT = 0x1;
		public const uint MOD_CONTROL = 0x2;
		public const uint MOD_SHIFT = 0x4;
		public const uint MOD_WIN = 0x8;

		public const uint ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

		[StructLayout(LayoutKind.Sequential)]
		public struct MsllHookStruct
		{
			public int X;
			public int Y;
			public uint MouseData;
			public uint Flags;
			public uint Time;
			public IntPtr DwExtraInfo;
		}
	}
}
