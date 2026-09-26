using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace EveFPreview.View
{
	/// <summary>
	/// Notification-area icon with a WPF context menu. WPF has no NotifyIcon, so this talks to
	/// Shell_NotifyIcon directly from a hidden message window - including re-adding the icon when
	/// Explorer restarts ("TaskbarCreated"), which WinForms' NotifyIcon used to do for us.
	/// </summary>
	sealed class TrayIcon : IDisposable
	{
		private const int WM_APP = 0x8000;
		private const int CallbackMessage = WM_APP + 0x11;
		private const int WM_LBUTTONDBLCLK = 0x0203;
		private const int WM_RBUTTONUP = 0x0205;

		private const int NIM_ADD = 0x0;
		private const int NIM_MODIFY = 0x1;
		private const int NIM_DELETE = 0x2;
		private const int NIF_MESSAGE = 0x1;
		private const int NIF_ICON = 0x2;
		private const int NIF_TIP = 0x4;

		private const int SM_CXSMICON = 49;
		private const int SM_CYSMICON = 50;

		private static readonly int TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

		private readonly HwndSource _messageWindow;
		private IntPtr _iconHandle;
		private string _toolTip = string.Empty;
		private bool _added;
		private bool _disposed;

		public TrayIcon()
		{
			// A hidden top-level window rather than a message-only one: TaskbarCreated is broadcast
			// to top-level windows only.
			this._messageWindow = new HwndSource(new HwndSourceParameters("EVE-F-Preview tray")
			{
				WindowStyle = 0,
				Width = 0,
				Height = 0
			});
			this._messageWindow.AddHook(this.WndProc);
		}

		/// <summary>Raised on double-click of the icon.</summary>
		public event EventHandler DoubleClick;

		public ContextMenu ContextMenu { get; set; }

		public string ToolTip
		{
			get => this._toolTip;
			set
			{
				this._toolTip = value ?? string.Empty;
				this.Update();
			}
		}

		/// <summary>Replaces the icon from the bytes of an .ico file, picking the image that best fits the tray.</summary>
		public void SetIcon(byte[] icoFile)
		{
			IntPtr newIcon = TrayIcon.CreateIconFromIcoFile(icoFile, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON));
			if (newIcon == IntPtr.Zero)
			{
				return;
			}

			IntPtr oldIcon = this._iconHandle;
			this._iconHandle = newIcon;
			this.Update();

			if (oldIcon != IntPtr.Zero)
			{
				DestroyIcon(oldIcon);
			}
		}

		public void Show()
		{
			if (this._disposed)
			{
				return;
			}

			var data = this.CreateData();
			this._added = Shell_NotifyIcon(this._added ? NIM_MODIFY : NIM_ADD, ref data) || this._added;
		}

		public void Dispose()
		{
			if (this._disposed)
			{
				return;
			}

			this._disposed = true;
			if (this._added)
			{
				var data = this.CreateData();
				Shell_NotifyIcon(NIM_DELETE, ref data);
				this._added = false;
			}

			if (this._iconHandle != IntPtr.Zero)
			{
				DestroyIcon(this._iconHandle);
				this._iconHandle = IntPtr.Zero;
			}

			this._messageWindow.RemoveHook(this.WndProc);
			this._messageWindow.Dispose();
		}

		private void Update()
		{
			if (this._added && !this._disposed)
			{
				var data = this.CreateData();
				Shell_NotifyIcon(NIM_MODIFY, ref data);
			}
		}

		private NOTIFYICONDATA CreateData()
		{
			return new NOTIFYICONDATA
			{
				cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
				hWnd = this._messageWindow.Handle,
				uID = 1,
				uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
				uCallbackMessage = CallbackMessage,
				hIcon = this._iconHandle,
				szTip = this._toolTip.Length > 127 ? this._toolTip.Substring(0, 127) : this._toolTip
			};
		}

		private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (msg == CallbackMessage)
			{
				switch (lParam.ToInt32() & 0xFFFF)
				{
					case WM_LBUTTONDBLCLK:
						this.DoubleClick?.Invoke(this, EventArgs.Empty);
						break;
					case WM_RBUTTONUP:
						this.ShowContextMenu();
						break;
				}

				handled = true;
			}
			else if (msg == TrayIcon.TaskbarCreatedMessage && this._added)
			{
				// Explorer restarted and forgot every icon - put ours back.
				this._added = false;
				this.Show();
			}

			return IntPtr.Zero;
		}

		private void ShowContextMenu()
		{
			ContextMenu menu = this.ContextMenu;
			if (menu == null)
			{
				return;
			}

			menu.Placement = PlacementMode.MousePoint;
			menu.IsOpen = true;

			// Without making the menu's popup the foreground window it never sees the click-away
			// that should close it (the classic tray menu gotcha).
			if (PresentationSource.FromVisual(menu) is HwndSource source)
			{
				SetForegroundWindow(source.Handle);
			}
		}

		private static IntPtr CreateIconFromIcoFile(byte[] ico, int desiredWidth, int desiredHeight)
		{
			if (ico == null || ico.Length < 6)
			{
				return IntPtr.Zero;
			}

			using var reader = new BinaryReader(new MemoryStream(ico));
			reader.ReadUInt16(); // reserved
			if (reader.ReadUInt16() != 1)
			{
				return IntPtr.Zero; // not an icon
			}

			int count = reader.ReadUInt16();
			int bestOffset = -1;
			int bestSize = 0;
			int bestScore = int.MaxValue;
			for (int i = 0; i < count; i++)
			{
				int width = reader.ReadByte();
				int height = reader.ReadByte();
				reader.ReadByte(); // colour count
				reader.ReadByte(); // reserved
				reader.ReadUInt16(); // planes
				int bitCount = reader.ReadUInt16();
				int size = reader.ReadInt32();
				int offset = reader.ReadInt32();

				width = width == 0 ? 256 : width;
				height = height == 0 ? 256 : height;

				// Prefer the smallest image at least as big as the tray slot (it scales down cleanly),
				// then the deepest colour.
				int score = (width >= desiredWidth ? width - desiredWidth : 1000 + desiredWidth - width) * 64 + (32 - Math.Min(bitCount, 32));
				if (score < bestScore && offset > 0 && offset + size <= ico.Length)
				{
					bestScore = score;
					bestOffset = offset;
					bestSize = size;
				}
			}

			if (bestOffset < 0)
			{
				return IntPtr.Zero;
			}

			byte[] image = new byte[bestSize];
			Array.Copy(ico, bestOffset, image, 0, bestSize);
			return CreateIconFromResourceEx(image, bestSize, true, 0x00030000, desiredWidth, desiredHeight, 0);
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct NOTIFYICONDATA
		{
			public int cbSize;
			public IntPtr hWnd;
			public int uID;
			public int uFlags;
			public int uCallbackMessage;
			public IntPtr hIcon;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
			public string szTip;
			public int dwState;
			public int dwStateMask;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
			public string szInfo;
			public int uTimeoutOrVersion;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
			public string szInfoTitle;
			public int dwInfoFlags;
			public Guid guidItem;
			public IntPtr hBalloonIcon;
		}

		[DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

		[DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
		private static extern int RegisterWindowMessage(string lpString);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetForegroundWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern int GetSystemMetrics(int nIndex);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr CreateIconFromResourceEx(byte[] presbits, int dwResSize, bool fIcon, int dwVer, int cxDesired, int cyDesired, int flags);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool DestroyIcon(IntPtr hIcon);
	}
}
