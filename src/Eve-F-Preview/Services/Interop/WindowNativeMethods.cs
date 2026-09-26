using System;
using System.Runtime.InteropServices;

namespace EveFPreview.Services.Interop
{
	/// <summary>
	/// Win32 window/monitor/input calls the WPF views need to work in physical pixels - the unit every
	/// stored thumbnail position and size is in - instead of WPF's DPI-scaled units.
	/// </summary>
	static class WindowNativeMethods
	{
		public const uint SWP_NOSIZE = 0x0001;
		public const uint SWP_NOMOVE = 0x0002;
		public const uint SWP_NOZORDER = 0x0004;
		public const uint SWP_NOACTIVATE = 0x0010;
		public const uint SWP_FRAMECHANGED = 0x0020;
		public const uint SWP_NOOWNERZORDER = 0x0200;

		public static readonly IntPtr HWND_TOP = IntPtr.Zero;

		public const int WM_CLOSE = 0x0010;
		public const int WM_WINDOWPOSCHANGED = 0x0047;
		public const int WM_DPICHANGED = 0x02E0;

		public const uint LWA_ALPHA = 0x00000002;

		public const int VK_SHIFT = 0x10;
		public const int VK_CONTROL = 0x11;
		public const int VK_MENU = 0x12;

		private const int SM_XVIRTUALSCREEN = 76;
		private const int SM_YVIRTUALSCREEN = 77;
		private const int SM_CXVIRTUALSCREEN = 78;
		private const int SM_CYVIRTUALSCREEN = 79;

		public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

		[StructLayout(LayoutKind.Sequential)]
		public struct POINT
		{
			public int X;
			public int Y;

			public POINT(int x, int y)
			{
				this.X = x;
				this.Y = y;
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct MONITORINFO
		{
			public int cbSize;
			public RECT rcMonitor;
			public RECT rcWork;
			public uint dwFlags;
		}

		public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetCursorPos(out POINT point);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

		[DllImport("user32.dll")]
		public static extern short GetKeyState(int nVirtKey);

		[DllImport("user32.dll")]
		private static extern int GetSystemMetrics(int nIndex);

		[DllImport("user32.dll")]
		public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

		[DllImport("user32.dll")]
		public static extern uint GetDpiForWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool IsWindowVisible(IntPtr hWnd);

		[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
		public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
		public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

		public static System.Drawing.Rectangle GetVirtualScreen()
		{
			return new System.Drawing.Rectangle(
				GetSystemMetrics(SM_XVIRTUALSCREEN),
				GetSystemMetrics(SM_YVIRTUALSCREEN),
				GetSystemMetrics(SM_CXVIRTUALSCREEN),
				GetSystemMetrics(SM_CYVIRTUALSCREEN));
		}

		public static bool IsKeyDown(int virtualKey)
		{
			return (GetKeyState(virtualKey) & 0x8000) != 0;
		}
	}
}
