using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace EveFPreview.View
{
	/// <summary>
	/// Switches the settings UI between the dark and light palettes at runtime. Controls take their
	/// colours from the palette via DynamicResource, so swapping the dictionary restyles every open
	/// window; windows marked with <see cref="UseThemedTitleBarProperty"/> also get a matching title bar.
	/// </summary>
	public static class ThemeManager
	{
		public const string Dark = "Dark";
		public const string Light = "Light";

		private const string DarkPaletteFile = "Colors.Dark.xaml";
		private const string LightPaletteFile = "Colors.Light.xaml";
		private const string PaletteFolder = "pack://application:,,,/EVE-F-Preview;component/View/Themes/";

		private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
		private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

		public static readonly DependencyProperty UseThemedTitleBarProperty = DependencyProperty.RegisterAttached(
			"UseThemedTitleBar", typeof(bool), typeof(ThemeManager), new PropertyMetadata(false, OnUseThemedTitleBarChanged));

		public static bool IsDark { get; private set; } = true;

		public static bool GetUseThemedTitleBar(DependencyObject element) => (bool)element.GetValue(ThemeManager.UseThemedTitleBarProperty);

		public static void SetUseThemedTitleBar(DependencyObject element, bool value) => element.SetValue(ThemeManager.UseThemedTitleBarProperty, value);

		/// <summary>"Dark" / "Light" as stored in the config; anything else (e.g. empty) follows the Windows app theme.</summary>
		public static bool ResolveIsDark(string preference)
		{
			if (string.Equals(preference, ThemeManager.Dark, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (string.Equals(preference, ThemeManager.Light, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			return ThemeManager.IsWindowsInDarkMode();
		}

		public static bool IsWindowsInDarkMode()
		{
			try
			{
				using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
				return key?.GetValue("AppsUseLightTheme") is int useLight && useLight == 0;
			}
			catch (Exception)
			{
				return false;
			}
		}

		public static void Apply(bool dark)
		{
			Application application = Application.Current;
			if (application == null)
			{
				return;
			}

			Collection<ResourceDictionary> dictionaries = application.Resources.MergedDictionaries;
			string wanted = dark ? DarkPaletteFile : LightPaletteFile;
			int paletteIndex = -1;
			for (int i = 0; i < dictionaries.Count; i++)
			{
				string source = dictionaries[i].Source?.OriginalString ?? string.Empty;
				if (source.EndsWith(DarkPaletteFile, StringComparison.OrdinalIgnoreCase) || source.EndsWith(LightPaletteFile, StringComparison.OrdinalIgnoreCase))
				{
					paletteIndex = i;
					break;
				}
			}

			bool alreadyApplied = paletteIndex >= 0
				&& (dictionaries[paletteIndex].Source?.OriginalString ?? string.Empty).EndsWith(wanted, StringComparison.OrdinalIgnoreCase);
			if (!alreadyApplied)
			{
				var palette = new ResourceDictionary { Source = new Uri(PaletteFolder + wanted) };
				if (paletteIndex >= 0)
				{
					dictionaries[paletteIndex] = palette;
				}
				else
				{
					dictionaries.Insert(0, palette);
				}
			}

			ThemeManager.IsDark = dark;
			foreach (Window window in application.Windows)
			{
				if (ThemeManager.GetUseThemedTitleBar(window))
				{
					ThemeManager.ApplyTitleBar(window);
				}
			}
		}

		private static void OnUseThemedTitleBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is not Window window || !(bool)e.NewValue)
			{
				return;
			}

			if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
			{
				ThemeManager.ApplyTitleBar(window);
			}
			else
			{
				window.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(window);
			}
		}

		private static void ApplyTitleBar(Window window)
		{
			IntPtr handle = new WindowInteropHelper(window).Handle;
			if (handle == IntPtr.Zero)
			{
				return;
			}

			int useDark = ThemeManager.IsDark ? 1 : 0;
			if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)) != 0)
			{
				DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDark, sizeof(int));
			}
		}

		[DllImport("dwmapi.dll")]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
	}
}
