using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using EveFPreview.Configuration;

namespace EveFPreview.View
{
	/// <summary>
	/// The Windows common colour and font dialogs. WPF ships neither; these are the same system
	/// dialogs WinForms' ColorDialog/FontDialog wrapped, so they look and behave as before.
	/// </summary>
	static class NativeDialogs
	{
		private const int CC_RGBINIT = 0x00000001;

		private const int CF_SCREENFONTS = 0x00000001;
		private const int CF_ENABLEHOOK = 0x00000008;
		private const int CF_INITTOLOGFONTSTRUCT = 0x00000040;
		private const int CF_EFFECTS = 0x00000100;

		private const int WM_INITDIALOG = 0x0110;
		private const int SW_HIDE = 0;
		private const int FontDialogColorComboId = 0x473; // cmb4
		private const int FontDialogColorLabelId = 0x443; // stc4

		private const int LOGPIXELSY = 90;
		private const int FW_NORMAL = 400;
		private const int FW_BOLD = 700;
		private const int FW_SEMIBOLD = 600;
		private const byte DEFAULT_CHARSET = 1;

		// Kept for the session so the "custom colours" row survives between dialogs.
		private static readonly int[] CustomColors = new int[16];

		private delegate IntPtr HookProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct CHOOSECOLOR
		{
			public int lStructSize;
			public IntPtr hwndOwner;
			public IntPtr hInstance;
			public int rgbResult;
			public IntPtr lpCustColors;
			public int Flags;
			public IntPtr lCustData;
			public IntPtr lpfnHook;
			public IntPtr lpTemplateName;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct CHOOSEFONT
		{
			public int lStructSize;
			public IntPtr hwndOwner;
			public IntPtr hDC;
			public IntPtr lpLogFont;
			public int iPointSize;
			public int Flags;
			public int rgbColors;
			public IntPtr lCustData;
			public IntPtr lpfnHook;
			public IntPtr lpTemplateName;
			public IntPtr hInstance;
			public IntPtr lpszStyle;
			public short nFontType;
			private short alignment;
			public int nSizeMin;
			public int nSizeMax;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct LOGFONT
		{
			public int lfHeight;
			public int lfWidth;
			public int lfEscapement;
			public int lfOrientation;
			public int lfWeight;
			public byte lfItalic;
			public byte lfUnderline;
			public byte lfStrikeOut;
			public byte lfCharSet;
			public byte lfOutPrecision;
			public byte lfClipPrecision;
			public byte lfQuality;
			public byte lfPitchAndFamily;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
			public string lfFaceName;
		}

		[DllImport("comdlg32.dll", EntryPoint = "ChooseColorW", CharSet = CharSet.Unicode)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool ChooseColor(ref CHOOSECOLOR cc);

		[DllImport("comdlg32.dll", EntryPoint = "ChooseFontW", CharSet = CharSet.Unicode)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool ChooseFont(ref CHOOSEFONT cf);

		[DllImport("user32.dll")]
		private static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		[DllImport("user32.dll")]
		private static extern IntPtr GetDC(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

		[DllImport("gdi32.dll")]
		private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

		public static bool TryPickColor(Window owner, Color initial, out Color picked)
		{
			picked = initial;
			IntPtr customColors = Marshal.AllocHGlobal(sizeof(int) * NativeDialogs.CustomColors.Length);
			try
			{
				Marshal.Copy(NativeDialogs.CustomColors, 0, customColors, NativeDialogs.CustomColors.Length);
				var cc = new CHOOSECOLOR
				{
					lStructSize = Marshal.SizeOf<CHOOSECOLOR>(),
					hwndOwner = NativeDialogs.GetOwnerHandle(owner),
					rgbResult = ColorTranslator.ToWin32(initial),
					lpCustColors = customColors,
					Flags = CC_RGBINIT
				};

				if (!ChooseColor(ref cc))
				{
					return false;
				}

				Marshal.Copy(customColors, NativeDialogs.CustomColors, 0, NativeDialogs.CustomColors.Length);

				// FromWin32 maps to a named colour where one matches (as WinForms' ColorDialog did),
				// which keeps the config file readable ("Orange" rather than "255, 165, 0").
				picked = ColorTranslator.FromWin32(cc.rgbResult);
				return true;
			}
			finally
			{
				Marshal.FreeHGlobal(customColors);
			}
		}

		public static bool TryPickFont(Window owner, OverlayFont initial, out OverlayFont picked)
		{
			picked = initial;
			initial ??= OverlayFont.CreateDefault();

			int dpiY = NativeDialogs.GetScreenDpiY();
			var logFont = new LOGFONT
			{
				lfHeight = -(int)Math.Round(initial.SizeInPoints * dpiY / 72.0),
				lfWeight = initial.Bold ? FW_BOLD : FW_NORMAL,
				lfItalic = (byte)(initial.Italic ? 1 : 0),
				lfUnderline = (byte)(initial.Underline ? 1 : 0),
				lfStrikeOut = (byte)(initial.Strikeout ? 1 : 0),
				lfCharSet = DEFAULT_CHARSET,
				lfFaceName = initial.FamilyName.Length > 31 ? initial.FamilyName.Substring(0, 31) : initial.FamilyName
			};

			// The hook only hides the colour picker the dialog shows alongside the effects
			// checkboxes - the label colour has its own setting (WinForms' ShowColor = false).
			HookProc hook = NativeDialogs.FontDialogHook;
			IntPtr logFontMemory = Marshal.AllocHGlobal(Marshal.SizeOf<LOGFONT>());
			try
			{
				Marshal.StructureToPtr(logFont, logFontMemory, false);
				var cf = new CHOOSEFONT
				{
					lStructSize = Marshal.SizeOf<CHOOSEFONT>(),
					hwndOwner = NativeDialogs.GetOwnerHandle(owner),
					lpLogFont = logFontMemory,
					Flags = CF_SCREENFONTS | CF_EFFECTS | CF_INITTOLOGFONTSTRUCT | CF_ENABLEHOOK,
					lpfnHook = Marshal.GetFunctionPointerForDelegate(hook)
				};

				if (!ChooseFont(ref cf))
				{
					return false;
				}

				LOGFONT result = Marshal.PtrToStructure<LOGFONT>(logFontMemory);
				OverlayFontStyle style = OverlayFontStyle.Regular;
				if (result.lfWeight >= FW_SEMIBOLD)
				{
					style |= OverlayFontStyle.Bold;
				}

				if (result.lfItalic != 0)
				{
					style |= OverlayFontStyle.Italic;
				}

				if (result.lfUnderline != 0)
				{
					style |= OverlayFontStyle.Underline;
				}

				if (result.lfStrikeOut != 0)
				{
					style |= OverlayFontStyle.Strikeout;
				}

				// Same conversion GDI+ used for Font.FromLogFont: character height in pixels -> points.
				float points = (float)Math.Round(Math.Abs(result.lfHeight) * 72.0 / dpiY, 2);
				picked = new OverlayFont(result.lfFaceName, points, style);
				return true;
			}
			finally
			{
				Marshal.FreeHGlobal(logFontMemory);
				GC.KeepAlive(hook);
			}
		}

		private static IntPtr FontDialogHook(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
		{
			if (msg == WM_INITDIALOG)
			{
				ShowWindow(GetDlgItem(hWnd, FontDialogColorComboId), SW_HIDE);
				ShowWindow(GetDlgItem(hWnd, FontDialogColorLabelId), SW_HIDE);
			}

			return IntPtr.Zero;
		}

		private static IntPtr GetOwnerHandle(Window owner)
		{
			return owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
		}

		private static int GetScreenDpiY()
		{
			IntPtr screen = GetDC(IntPtr.Zero);
			try
			{
				int dpi = GetDeviceCaps(screen, LOGPIXELSY);
				return dpi > 0 ? dpi : 96;
			}
			finally
			{
				ReleaseDC(IntPtr.Zero, screen);
			}
		}
	}
}
