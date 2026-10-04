using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using EveFPreview.Configuration;
using EveFPreview.Services.Interop;
using EveFPreview.UI.Hotkeys;

namespace EveFPreview.Services.Implementation
{
	public class WindowManager : IWindowManager
	{
		#region Private constants
		private const int WINDOW_SIZE_THRESHOLD = 300;
		private const int NO_ANIMATION = 0;
		#endregion

		public WindowManager(IThumbnailConfiguration configuration)
		{
			// Composition is always enabled for Windows 8+
			this.IsCompositionEnabled = 
				((Environment.OSVersion.Version.Major == 6) && (Environment.OSVersion.Version.Minor >= 2)) // Win 8 and Win 8.1
				|| (Environment.OSVersion.Version.Major >= 10) // Win 10
				|| DwmNativeMethods.DwmIsCompositionEnabled(); // In case of Win 7 an API call is requiredWin 7
			_animationParam.cbSize = (System.UInt32)Marshal.SizeOf(typeof(ANIMATIONINFO));
		}

		private int? _currentAnimationSetting = null;
		private ANIMATIONINFO _animationParam = new ANIMATIONINFO();

		public bool IsCompositionEnabled { get; }

		public IntPtr GetForegroundWindowHandle()
		{
			return User32NativeMethods.GetForegroundWindow();
		}

		/// <summary>
		/// A plain SetForegroundWindow call silently fails (window stays in the background) unless
		/// Windows considers the caller "allowed" - e.g. it just processed a RegisterHotKey WM_HOTKEY
		/// message, which gets an automatic grant. Our mouse-button cycle hotkeys come from a raw
		/// input hook instead, which gets no such grant, so cycling would update our own highlight/
		/// active-client tracking but never actually bring the EVE client to the foreground.
		/// See ForegroundActivator for how the grant is borrowed. <paramref name="onActivated"/>, if
		/// given, is told on the UI thread whether the window really did end up in the foreground.
		/// </summary>
		private static void ForceSetForegroundWindow(IntPtr handle, Action<bool> onActivated = null)
		{
			ForegroundActivator.Activate(handle, onActivated);
		}

		public void TurnOffAnimation()
		{
			var currentAnimationSetup = User32NativeMethods.SystemParametersInfo(User32NativeMethods.SPI_GETANIMATION, (System.Int32)Marshal.SizeOf(typeof(ANIMATIONINFO)), ref _animationParam, 0);
			if (_currentAnimationSetting == null)
			{
				// Store the current Animation Setting
				_currentAnimationSetting = _animationParam.iMinAnimate;
			}

			if (currentAnimationSetup != NO_ANIMATION)
			{
				// Turn off Animation
				_animationParam.iMinAnimate = NO_ANIMATION;
				var animationOffReturn = User32NativeMethods.SystemParametersInfo(User32NativeMethods.SPI_SETANIMATION, (System.Int32)Marshal.SizeOf(typeof(ANIMATIONINFO)), ref _animationParam, 0);
			}
		}

		public void RestoreAnimation()
		{
			var currentAnimationSetup = User32NativeMethods.SystemParametersInfo(User32NativeMethods.SPI_GETANIMATION, (System.Int32)Marshal.SizeOf(typeof(ANIMATIONINFO)), ref _animationParam, 0);
			// Restore current Animation Settings
			if (_animationParam.iMinAnimate != (int)_currentAnimationSetting)
			{
				_animationParam.iMinAnimate = (int)_currentAnimationSetting;
				var animationResetReturn = User32NativeMethods.SystemParametersInfo(User32NativeMethods.SPI_SETANIMATION, (System.Int32)Marshal.SizeOf(typeof(ANIMATIONINFO)), ref _animationParam, 0);
			}
		}


		public void ActivateWindow(IntPtr handle, AnimationStyle animation, Action<bool> onActivated = null)
		{
			WindowManager.ForceSetForegroundWindow(handle, onActivated);
			User32NativeMethods.SetFocus(handle);

			uint style = User32NativeMethods.GetWindowLong(handle, InteropConstants.GWL_STYLE);

			if ((style & InteropConstants.WS_MINIMIZE) == InteropConstants.WS_MINIMIZE)
			{
				switch (animation)
				{
					case AnimationStyle.OriginalAnimation:
						User32NativeMethods.ShowWindowAsync(handle, InteropConstants.SW_RESTORE);
						break;
					case AnimationStyle.NoAnimation:
						TurnOffAnimation();
						User32NativeMethods.ShowWindowAsync(handle, InteropConstants.SW_RESTORE);
						RestoreAnimation();
						break;
				}
			}
		}

		public void MinimizeWindow(IntPtr handle, AnimationStyle animation, bool enableAnimation)
		{
			if (enableAnimation)
			{
				switch (animation)
				{
					case AnimationStyle.OriginalAnimation:
						User32NativeMethods.SendMessage(handle, InteropConstants.WM_SYSCOMMAND, InteropConstants.SC_MINIMIZE, 0);
						break;
					case AnimationStyle.NoAnimation:
						TurnOffAnimation();
						User32NativeMethods.SendMessage(handle, InteropConstants.WM_SYSCOMMAND, InteropConstants.SC_MINIMIZE, 0);
						RestoreAnimation();
						break;
				}
			}
			else
			{
				switch (animation)
				{
					case AnimationStyle.OriginalAnimation:
						WINDOWPLACEMENT param = new WINDOWPLACEMENT();
						param.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
						User32NativeMethods.GetWindowPlacement(handle, ref param);
						param.showCmd = WINDOWPLACEMENT.SW_MINIMIZE;
						User32NativeMethods.SetWindowPlacement(handle, ref param);
						break;
					case AnimationStyle.NoAnimation:
						TurnOffAnimation();
						User32NativeMethods.SendMessage(handle, InteropConstants.WM_SYSCOMMAND, InteropConstants.SC_MINIMIZE, 0);
						RestoreAnimation();
						break;
				}
			}
		}

		public void MoveWindow(IntPtr handle, int left, int top, int width, int height)
		{
			User32NativeMethods.MoveWindow(handle, left, top, width, height, true);
		}

		public void MaximizeWindow(IntPtr handle)
		{
			User32NativeMethods.ShowWindowAsync(handle, InteropConstants.SW_SHOWMAXIMIZED);
        }

		public (int Left, int Top, int Right, int Bottom) GetWindowPosition(IntPtr handle)
		{
			User32NativeMethods.GetWindowRect(handle, out RECT windowRectangle);

			return (windowRectangle.Left, windowRectangle.Top, windowRectangle.Right, windowRectangle.Bottom);
		}

		public bool IsWindowMaximized(IntPtr handle)
		{
			return User32NativeMethods.IsZoomed(handle);
		}

		public bool IsWindowMinimized(IntPtr handle)
		{
			return User32NativeMethods.IsIconic(handle);
		}

		public IDwmThumbnail GetLiveThumbnail(IntPtr destination, IntPtr source)
		{
			IDwmThumbnail thumbnail = new DwmThumbnail(this);
			thumbnail.Register(destination, source);

			return thumbnail;
		}

		public BitmapSource GetStaticThumbnail(IntPtr source)
		{
			var sourceContext = User32NativeMethods.GetDC(source);

			User32NativeMethods.GetClientRect(source, out RECT windowRect);

			var width = windowRect.Right - windowRect.Left;
			var height = windowRect.Bottom - windowRect.Top;

			// Check if there is anything to make thumbnail of
			if ((width < WINDOW_SIZE_THRESHOLD) || (height < WINDOW_SIZE_THRESHOLD))
			{
                User32NativeMethods.ReleaseDC(source, sourceContext);

                return null;
			}

			var destContext = Gdi32NativeMethods.CreateCompatibleDC(sourceContext);
			var bitmap = Gdi32NativeMethods.CreateCompatibleBitmap(sourceContext, width, height);

			var oldBitmap = Gdi32NativeMethods.SelectObject(destContext, bitmap);
			Gdi32NativeMethods.BitBlt(destContext, 0, 0, width, height, sourceContext, 0, 0, Gdi32NativeMethods.SRCCOPY);
			Gdi32NativeMethods.SelectObject(destContext, oldBitmap);
			Gdi32NativeMethods.DeleteDC(destContext);
			User32NativeMethods.ReleaseDC(source, sourceContext);

			try
			{
				BitmapSource image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
				image.Freeze();
				return image;
			}
			finally
			{
				Gdi32NativeMethods.DeleteObject(bitmap);
			}
		}
	}
}