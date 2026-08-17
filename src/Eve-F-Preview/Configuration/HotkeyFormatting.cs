using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace EveFPreview.Configuration
{
	public static class HotkeyFormatting
	{
		private static readonly KeysConverter KeysConverter = new KeysConverter();

		public static string ToDisplayString(Keys keys)
		{
			if (keys == Keys.None)
			{
				return string.Empty;
			}

			Keys keyCode = keys & Keys.KeyCode;
			string mouseName = GetMouseButtonDisplayName(keyCode);
			if (mouseName != null)
			{
				return FormatWithModifiers(keys & Keys.Modifiers, mouseName);
			}

			return KeysConverter.ConvertToInvariantString(keys) ?? string.Empty;
		}

		public static Keys FromDisplayString(string hotkey)
		{
			if (string.IsNullOrWhiteSpace(hotkey))
			{
				return Keys.None;
			}

			string trimmed = NormalizeMouseButtonAliases(hotkey.Trim());
			try
			{
				object rawValue = KeysConverter.ConvertFromInvariantString(trimmed);
				if (rawValue is Keys keys)
				{
					return keys;
				}
			}
			catch (Exception)
			{
			}

			return ParseModifierAndKey(trimmed);
		}

		public static string GetPrimaryHotkey(IList<string> hotkeys)
		{
			if (hotkeys == null)
			{
				return string.Empty;
			}

			return hotkeys.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h)) ?? string.Empty;
		}

		public static void SetPrimaryHotkey(List<string> hotkeys, string value)
		{
			if (hotkeys == null)
			{
				return;
			}

			hotkeys.Clear();
			if (!string.IsNullOrWhiteSpace(value))
			{
				hotkeys.Add(value.Trim());
			}
		}

		private static string GetMouseButtonDisplayName(Keys keyCode)
		{
			return keyCode switch
			{
				Keys.XButton1 => "Mouse4",
				Keys.XButton2 => "Mouse5",
				Keys.MButton => "Middle",
				_ => null
			};
		}

		private static string FormatWithModifiers(Keys modifiers, string keyName)
		{
			var parts = new List<string>();
			if (modifiers.HasFlag(Keys.Control))
			{
				parts.Add("Ctrl");
			}

			if (modifiers.HasFlag(Keys.Shift))
			{
				parts.Add("Shift");
			}

			if (modifiers.HasFlag(Keys.Alt))
			{
				parts.Add("Alt");
			}

			parts.Add(keyName);
			return string.Join("+", parts);
		}

		private static Keys ParseModifierAndKey(string hotkey)
		{
			Keys modifiers = Keys.None;
			string remainder = hotkey;
			while (true)
			{
				if (remainder.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase)
					|| remainder.StartsWith("Control+", StringComparison.OrdinalIgnoreCase))
				{
					modifiers |= Keys.Control;
					remainder = remainder.Substring(remainder.IndexOf('+') + 1);
					continue;
				}

				if (remainder.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase))
				{
					modifiers |= Keys.Shift;
					remainder = remainder.Substring("Shift+".Length);
					continue;
				}

				if (remainder.StartsWith("Alt+", StringComparison.OrdinalIgnoreCase))
				{
					modifiers |= Keys.Alt;
					remainder = remainder.Substring("Alt+".Length);
					continue;
				}

				break;
			}

			Keys keyCode = remainder.ToLowerInvariant() switch
			{
				"xbutton1" => Keys.XButton1,
				"xbutton2" => Keys.XButton2,
				"mbutton" => Keys.MButton,
				_ => Keys.None
			};

			return keyCode == Keys.None ? Keys.None : keyCode | modifiers;
		}

		private static string NormalizeMouseButtonAliases(string hotkey)
		{
			string normalized = ReplaceInsensitive(hotkey, "Mouse 4", "XButton1");
			normalized = ReplaceInsensitive(normalized, "Mouse4", "XButton1");
			normalized = ReplaceInsensitive(normalized, "Mouse 5", "XButton2");
			normalized = ReplaceInsensitive(normalized, "Mouse5", "XButton2");
			normalized = ReplaceInsensitive(normalized, "Middle Click", "MButton");
			normalized = ReplaceInsensitive(normalized, "Middle", "MButton");
			return normalized;
		}

		private static string ReplaceInsensitive(string source, string oldValue, string newValue)
		{
			int index = source.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
			if (index < 0)
			{
				return source;
			}

			return source.Substring(0, index) + newValue + source.Substring(index + oldValue.Length);
		}
	}
}
