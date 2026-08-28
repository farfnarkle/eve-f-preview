using EveFPreview.Configuration;
using System;
using System.Drawing;

namespace EveFPreview.Services
{
	public interface IWindowManager
	{
		bool IsCompositionEnabled { get; }

		IntPtr GetForegroundWindowHandle();
#if LINUX
		/// <summary>onActivated, if given, is told on the UI thread whether the window really did end up in the foreground.</summary>
		void ActivateWindow(IntPtr handle, string windowName, Action<bool> onActivated = null);
#else
		/// <summary>onActivated, if given, is told on the UI thread whether the window really did end up in the foreground.</summary>
		void ActivateWindow(IntPtr handle, AnimationStyle animation, Action<bool> onActivated = null);
#endif
		void MinimizeWindow(IntPtr handle, AnimationStyle animation, bool enableAnimation);
		void MoveWindow(IntPtr handle, int left, int top, int width, int height);
		void MaximizeWindow(IntPtr handle);
		(int Left, int Top, int Right, int Bottom) GetWindowPosition(IntPtr handle);
		bool IsWindowMaximized(IntPtr handle);
		bool IsWindowMinimized(IntPtr handle);
		IDwmThumbnail GetLiveThumbnail(IntPtr destination, IntPtr source);
		Image GetStaticThumbnail(IntPtr source);
	}
}