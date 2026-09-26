using System;
using System.Collections.Generic;
using System.Text;

namespace EveFPreview.UI.Hotkeys
{
	/// <summary>
	/// Converts <see cref="Keys"/> to and from text exactly the way WinForms' KeysConverter does with
	/// the invariant culture ("Ctrl+Alt+Shift+F1", "PgUp", "Del", ...). Every hotkey already saved in
	/// a config file was written by that converter, so this has to read and write the same format.
	/// Parsing is case-sensitive, like the original.
	/// </summary>
	static class KeysText
	{
		private const string NoneName = "(none)";

		// KeysConverter's own short names, in its display order. Everything else falls back to the
		// enum member name. Modifiers are emitted in this order too, hence "Ctrl+Alt+Shift".
		private static readonly (string Name, Keys Value)[] DisplayOrder =
		{
			("Enter", Keys.Return),
			("F12", Keys.F12),
			("F11", Keys.F11),
			("F10", Keys.F10),
			("F9", Keys.F9),
			("F8", Keys.F8),
			("F7", Keys.F7),
			("F6", Keys.F6),
			("F5", Keys.F5),
			("F4", Keys.F4),
			("F3", Keys.F3),
			("F2", Keys.F2),
			("F1", Keys.F1),
			("Del", Keys.Delete),
			("Home", Keys.Home),
			("End", Keys.End),
			("PgUp", Keys.Prior),
			("PgDn", Keys.Next),
			("0", Keys.D0),
			("1", Keys.D1),
			("2", Keys.D2),
			("3", Keys.D3),
			("4", Keys.D4),
			("5", Keys.D5),
			("6", Keys.D6),
			("7", Keys.D7),
			("8", Keys.D8),
			("9", Keys.D9),
			("Ctrl", Keys.Control),
			("Alt", Keys.Alt),
			("Shift", Keys.Shift),
			(NoneName, Keys.None)
		};

		// Codes with more than one enum name. Which alias Enum.ToString picks depends on metadata
		// order, so pin the ones KeysConverter actually produced instead of trusting reflection.
		private static readonly Dictionary<Keys, string> AliasedCodeNames = new Dictionary<Keys, string>
		{
			{ Keys.CapsLock, "CapsLock" },
			{ Keys.KanaMode, "HanguelMode" },
			{ Keys.HanjaMode, "HanjaMode" },
			{ Keys.IMEAccept, "IMEAccept" },
			{ Keys.PrintScreen, "PrintScreen" },
			{ Keys.Oem1, "Oem1" },
			{ Keys.OemQuestion, "OemQuestion" },
			{ Keys.Oem3, "Oem3" },
			{ Keys.OemOpenBrackets, "OemOpenBrackets" },
			{ Keys.Oem5, "Oem5" },
			{ Keys.Oem6, "Oem6" },
			{ Keys.OemQuotes, "OemQuotes" },
			{ Keys.Oem102, "Oem102" }
		};

		private static readonly Dictionary<string, Keys> NameLookup = BuildNameLookup();

		public static string ToText(Keys keys)
		{
			Keys modifiers = keys & Keys.Modifiers;
			Keys keyCode = keys & Keys.KeyCode;

			var text = new StringBuilder();
			foreach ((string name, Keys value) in KeysText.DisplayOrder)
			{
				if ((value & modifiers) != 0)
				{
					if (text.Length > 0)
					{
						text.Append('+');
					}

					text.Append(name);
				}
			}

			if (text.Length > 0)
			{
				text.Append('+');
			}

			foreach ((string name, Keys value) in KeysText.DisplayOrder)
			{
				if (value == keyCode)
				{
					return text.Append(name).ToString();
				}
			}

			if (KeysText.AliasedCodeNames.TryGetValue(keyCode, out string aliasName))
			{
				text.Append(aliasName);
			}
			else if (Enum.IsDefined(typeof(Keys), keyCode))
			{
				text.Append(keyCode.ToString());
			}

			return text.ToString();
		}

		/// <summary>False where KeysConverter would have thrown (unknown name, two key codes, empty segment).</summary>
		public static bool TryParse(string text, out Keys keys)
		{
			keys = Keys.None;
			if (text == null)
			{
				return false;
			}

			string trimmed = text.Trim();
			if (trimmed.Length == 0)
			{
				return false;
			}

			bool foundKeyCode = false;
			foreach (string rawToken in trimmed.Split('+'))
			{
				string token = rawToken.Trim();
				if (!KeysText.NameLookup.TryGetValue(token, out Keys value)
					&& !KeysText.TryParseEnumName(token, out value))
				{
					keys = Keys.None;
					return false;
				}

				if ((value & Keys.KeyCode) != 0)
				{
					if (foundKeyCode)
					{
						keys = Keys.None;
						return false;
					}

					foundKeyCode = true;
				}

				keys |= value;
			}

			return true;
		}

		private static bool TryParseEnumName(string token, out Keys value)
		{
			// Enum.Parse (not TryParse) semantics are what KeysConverter used, including accepting
			// raw numbers; it only differs from TryParse in throwing, which we turn into false.
			try
			{
				value = (Keys)Enum.Parse(typeof(Keys), token);
				return true;
			}
			catch (ArgumentException)
			{
				value = Keys.None;
				return false;
			}
			catch (OverflowException)
			{
				value = Keys.None;
				return false;
			}
		}

		private static Dictionary<string, Keys> BuildNameLookup()
		{
			var lookup = new Dictionary<string, Keys>(StringComparer.Ordinal);
			foreach ((string name, Keys value) in KeysText.DisplayOrder)
			{
				lookup[name] = value;
			}

			return lookup;
		}
	}
}
