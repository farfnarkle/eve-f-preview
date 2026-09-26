using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using EveFPreview.Services.Interop;

namespace EveFPreview.Services
{
	/// <summary>
	/// Monitor geometry in physical screen pixels (what WinForms' Screen/SystemInformation used to
	/// provide). The process is per-monitor DPI aware, so these match window coordinates directly.
	/// </summary>
	static class DisplayMonitors
	{
		public static Rectangle VirtualScreen => WindowNativeMethods.GetVirtualScreen();

		public static IReadOnlyList<Rectangle> GetMonitorBounds()
		{
			var bounds = new List<Rectangle>();
			WindowNativeMethods.MonitorEnumProc callback = (IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
			{
				bounds.Add(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
				return true;
			};

			WindowNativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
			GC.KeepAlive(callback);
			return bounds;
		}

		/// <summary>Working area (excluding the taskbar) of the monitor nearest to <paramref name="point"/>.</summary>
		public static Rectangle GetWorkingArea(Point point)
		{
			IntPtr monitor = WindowNativeMethods.MonitorFromPoint(new WindowNativeMethods.POINT(point.X, point.Y), WindowNativeMethods.MONITOR_DEFAULTTONEAREST);
			var info = new WindowNativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<WindowNativeMethods.MONITORINFO>() };
			if (monitor == IntPtr.Zero || !WindowNativeMethods.GetMonitorInfo(monitor, ref info))
			{
				return DisplayMonitors.VirtualScreen;
			}

			return Rectangle.FromLTRB(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
		}

		public static Point GetCursorPosition()
		{
			return WindowNativeMethods.GetCursorPos(out WindowNativeMethods.POINT point)
				? new Point(point.X, point.Y)
				: Point.Empty;
		}
	}
}
