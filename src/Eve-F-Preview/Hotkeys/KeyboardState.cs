using EveFPreview.Services.Interop;

namespace EveFPreview.UI.Hotkeys
{
	static class KeyboardState
	{
		/// <summary>Shift/Ctrl/Alt currently held, read the same way WinForms' Control.ModifierKeys did (GetKeyState).</summary>
		public static Keys GetModifierKeys()
		{
			Keys modifiers = Keys.None;
			if (WindowNativeMethods.IsKeyDown(WindowNativeMethods.VK_SHIFT))
			{
				modifiers |= Keys.Shift;
			}

			if (WindowNativeMethods.IsKeyDown(WindowNativeMethods.VK_CONTROL))
			{
				modifiers |= Keys.Control;
			}

			if (WindowNativeMethods.IsKeyDown(WindowNativeMethods.VK_MENU))
			{
				modifiers |= Keys.Alt;
			}

			return modifiers;
		}
	}
}
