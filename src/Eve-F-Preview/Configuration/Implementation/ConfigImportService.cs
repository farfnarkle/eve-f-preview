// Imports settings from other previewer tools into a new EVE-F-Preview config profile.
//
// Supported source formats:
//   - EVE-O-Preview / EVE-F-Preview: same JSON schema, populated directly onto a fresh
//     ThumbnailConfiguration instance.
//   - EVE-X: different schema (global_Settings + _Profiles). Mapped best-effort onto the
//     closest equivalent EVE-F-Preview fields. Field names/casing for EVE-X are inferred from
//     the task description rather than a verified real-world sample, so lookups are done
//     case-insensitively and every mapping is wrapped so one bad/missing field cannot abort
//     the whole import. Hotkey groups are not imported (see ImportEveX for details).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EveFPreview.Configuration.Implementation
{
	public enum ExternalConfigFormat
	{
		Unknown,
		EveOOrFPreview,
		EveX
	}

	public static class ConfigImportService
	{
		public static ExternalConfigFormat DetectFormat(string path)
		{
			JObject root = TryParse(path);
			return ConfigImportService.DetectFormat(root);
		}

		public static ExternalConfigFormat DetectFormat(JObject root)
		{
			if (root == null)
			{
				return ExternalConfigFormat.Unknown;
			}

			if (FindProperty(root, "global_Settings") != null && FindProperty(root, "_Profiles") != null)
			{
				return ExternalConfigFormat.EveX;
			}

			if (FindProperty(root, "CycleGroup1ForwardHotkeys") != null
				|| FindProperty(root, "ThumbnailSize") != null
				|| FindProperty(root, "ConfigVersion") != null)
			{
				return ExternalConfigFormat.EveOOrFPreview;
			}

			return ExternalConfigFormat.Unknown;
		}

		/// <summary>
		/// Detects the source format, converts it into a fresh EVE-F-Preview config, and writes
		/// it to destinationPath. Returns the detected format and any non-fatal warnings.
		/// </summary>
		public static ExternalConfigFormat ImportToFile(string sourcePath, string destinationPath, out IList<string> warnings)
		{
			warnings = new List<string>();

			if (!File.Exists(sourcePath))
			{
				throw new FileNotFoundException("Source settings file not found.", sourcePath);
			}

			JObject root = TryParse(sourcePath)
				?? throw new InvalidDataException("Could not parse '" + Path.GetFileName(sourcePath) + "' as JSON.");

			ExternalConfigFormat format = DetectFormat(root);
			var config = new ThumbnailConfiguration();

			switch (format)
			{
				case ExternalConfigFormat.EveOOrFPreview:
					ImportEveOOrF(root, config);
					break;
				case ExternalConfigFormat.EveX:
					ImportEveX(root, config, warnings);
					break;
				default:
					throw new InvalidDataException(
						"Unrecognized settings format in '" + Path.GetFileName(sourcePath) +
						"'. Expected an EVE-O/EVE-F Preview config or an EVE-X config.");
			}

			config.ApplyRestrictions();

			string directory = Path.GetDirectoryName(destinationPath);
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}

			File.WriteAllText(destinationPath, JsonConvert.SerializeObject(config, Formatting.Indented));

			return format;
		}

		public static ExternalConfigFormat ImportToFile(string sourcePath, string destinationPath)
		{
			return ImportToFile(sourcePath, destinationPath, out _);
		}

		private static JObject TryParse(string path)
		{
			try
			{
				return JObject.Parse(File.ReadAllText(path));
			}
			catch
			{
				return null;
			}
		}

		#region EVE-O / EVE-F import

		private static void ImportEveOOrF(JObject root, ThumbnailConfiguration config)
		{
			var settings = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace };
			JsonConvert.PopulateObject(root.ToString(), config, settings);
		}

		#endregion

		#region EVE-X import

		/// <summary>
		/// Best-effort mapping from an EVE-X config onto EVE-F-Preview fields. EVE-X's exact
		/// property names/casing were not available to verify against a real sample, so every
		/// lookup below is case-insensitive and tried against both the selected profile object
		/// and the global_Settings object (whichever has it). Anything that fails to parse is
		/// skipped with a warning instead of aborting the import.
		/// Hotkey Groups (cycle groups) are intentionally not imported - the EVE-X grouping
		/// structure is not well-known enough to map safely.
		/// </summary>
		private static void ImportEveX(JObject root, IThumbnailConfiguration config, IList<string> warnings)
		{
			JObject globalSettings = FindProperty(root, "global_Settings") as JObject ?? new JObject();
			JObject profilesContainer = FindProperty(root, "_Profiles") as JObject;

			string lastUsedProfile = GetString(globalSettings, "LastUsedProfile", null);
			JObject profile = null;
			if (profilesContainer != null)
			{
				if (!string.IsNullOrEmpty(lastUsedProfile))
				{
					profile = FindProperty(profilesContainer, lastUsedProfile) as JObject;
				}

				if (profile == null)
				{
					profile = profilesContainer.Properties().Select(p => p.Value).OfType<JObject>().FirstOrDefault();
				}
			}

			profile ??= new JObject();

			JObject clientSettings = FindProperty(profile, "Client Settings") as JObject
				?? FindProperty(globalSettings, "Client Settings") as JObject
				?? new JObject();

			TrySet(warnings, "MinimizeInactiveClients", () =>
				config.MinimizeInactiveClients = GetBool(clientSettings, "MinimizeInactiveClients", config.MinimizeInactiveClients));

			TrySet(warnings, "EnableClientLayoutTracking", () =>
				config.EnableClientLayoutTracking = GetBool(profile, globalSettings, "TrackClientPossitions", config.EnableClientLayoutTracking));

			TrySet(warnings, "ShowThumbnailOverlays", () =>
				config.ShowThumbnailOverlays = GetBool(profile, globalSettings, "ShowThumbnailTextOverlay", config.ShowThumbnailOverlays));

			TrySet(warnings, "OverlayLabelColor", () =>
			{
				string hex = GetString(profile, globalSettings, "ThumbnailTextColor", null);
				if (TryParseHexColor(hex, out Color color))
				{
					config.OverlayLabelColor = color;
				}
			});

			TrySet(warnings, "OverlayLabelFont", () =>
			{
				JToken sizeToken = FindToken(profile, globalSettings, "ThumbnailTextSize");
				if (sizeToken != null && sizeToken.Type != JTokenType.Null && float.TryParse(sizeToken.ToString(), out float fontSize) && fontSize > 0)
				{
					Font existing = config.OverlayLabelFont ?? new Font(FontFamily.GenericSansSerif, 10.0F, FontStyle.Bold);
					config.OverlayLabelFont = new Font(existing.FontFamily, fontSize, existing.Style);
				}
			});

			TrySet(warnings, "EnableActiveClientHighlight", () =>
				config.EnableActiveClientHighlight = GetBool(profile, globalSettings, "ShowClientHighlightBorder", config.EnableActiveClientHighlight));

			TrySet(warnings, "ActiveClientHighlightColor", () =>
			{
				string hex = GetString(profile, globalSettings, "ClientHighligtColor", null);
				if (TryParseHexColor(hex, out Color color))
				{
					config.ActiveClientHighlightColor = color;
				}
			});

			TrySet(warnings, "ActiveClientHighlightThickness", () =>
			{
				int? thickness = GetInt(profile, globalSettings, "ClientHighligtBorderthickness", null);
				if (thickness.HasValue)
				{
					config.ActiveClientHighlightThickness = thickness.Value;
				}
			});

			TrySet(warnings, "HideThumbnailsOnLostFocus", () =>
				config.HideThumbnailsOnLostFocus = GetBool(profile, globalSettings, "HideThumbnailsOnLostFocus", config.HideThumbnailsOnLostFocus));

			TrySet(warnings, "ThumbnailOpacity", () =>
			{
				double? opacityPercent = GetDouble(profile, globalSettings, "ThumbnailOpacity", null);
				if (opacityPercent.HasValue)
				{
					config.ThumbnailOpacity = opacityPercent.Value / 100.0;
				}
			});

			TrySet(warnings, "ShowThumbnailsAlwaysOnTop", () =>
				config.ShowThumbnailsAlwaysOnTop = GetBool(profile, globalSettings, "ShowThumbnailsAlwaysOnTop", config.ShowThumbnailsAlwaysOnTop));

			TrySet(warnings, "ThumbnailSize/LoginThumbnailLocation", () =>
			{
				JObject startLocation = FindToken(profile, globalSettings, "ThumbnailStartLocation") as JObject;
				if (startLocation != null)
				{
					int x = GetInt(startLocation, "X", 0) ?? 0;
					int y = GetInt(startLocation, "Y", 0) ?? 0;
					int width = GetInt(startLocation, "Width", 0) ?? 0;
					int height = GetInt(startLocation, "Height", 0) ?? 0;

					config.LoginThumbnailLocation = new Point(x, y);
					if (width > 0 && height > 0)
					{
						config.ThumbnailSize = new Size(width, height);
					}
				}
			});

			TrySet(warnings, "EnableThumbnailSnap", () =>
				config.EnableThumbnailSnap = GetBool(profile, globalSettings, "ThumbnailSnap", config.EnableThumbnailSnap));

			TrySet(warnings, "ThumbnailMinimumSize", () =>
			{
				JObject minSize = FindToken(profile, globalSettings, "ThumbnailMinimumSize") as JObject;
				if (minSize != null)
				{
					int width = GetInt(minSize, "Width", 0) ?? 0;
					int height = GetInt(minSize, "Height", 0) ?? 0;
					if (width > 0 && height > 0)
					{
						config.ThumbnailMinimumSize = new Size(width, height);
					}
				}
			});

			TrySet(warnings, "FlatLayout (Thumbnail Positions)", () =>
			{
				JObject positions = FindProperty(profile, "Thumbnail Positions") as JObject;
				if (positions == null)
				{
					return;
				}

				foreach (JProperty entry in positions.Properties())
				{
					if (!(entry.Value is JObject point))
					{
						continue;
					}

					int x = GetInt(point, "X", 0) ?? 0;
					int y = GetInt(point, "Y", 0) ?? 0;
					config.SetThumbnailLocation(NormalizeClientName(entry.Name), null, new Point(x, y));
				}
			});

			TrySet(warnings, "ClientLayout (Client Possitions)", () =>
			{
				JObject clientPositions = FindProperty(profile, "Client Possitions") as JObject;
				if (clientPositions == null)
				{
					return;
				}

				foreach (JProperty entry in clientPositions.Properties())
				{
					if (!(entry.Value is JObject clientPos))
					{
						continue;
					}

					int x = GetInt(clientPos, "X", 0) ?? 0;
					int y = GetInt(clientPos, "Y", 0) ?? 0;
					int width = GetInt(clientPos, "Width", 0) ?? 0;
					int height = GetInt(clientPos, "Height", 0) ?? 0;
					bool maximized = GetBool(clientPos, "IsMaximized", false);

					config.SetClientLayout(NormalizeClientName(entry.Name), new ClientLayout(x, y, width, height, maximized));
				}
			});

			TrySet(warnings, "ClientHotkey (Hotkeys)", () => ImportEveXHotkeys(profile, config, warnings));

			TrySet(warnings, "DisableThumbnail (Thumbnail Visibility)", () =>
			{
				JObject visibility = FindProperty(profile, "Thumbnail Visibility") as JObject;
				if (visibility == null)
				{
					return;
				}

				foreach (JProperty entry in visibility.Properties())
				{
					if (entry.Value.Type == JTokenType.Boolean)
					{
						bool visible = entry.Value.Value<bool>();
						config.ToggleThumbnail(NormalizeClientName(entry.Name), !visible);
					}
				}
			});

			// "Hotkey Groups" (cycle groups) intentionally skipped - see method summary.
		}

		private static void ImportEveXHotkeys(JObject profile, IThumbnailConfiguration config, IList<string> warnings)
		{
			JToken hotkeysToken = FindProperty(profile, "Hotkeys");
			if (hotkeysToken == null)
			{
				return;
			}

			if (hotkeysToken is JObject hotkeysByName)
			{
				foreach (JProperty entry in hotkeysByName.Properties())
				{
					ImportOneEveXHotkey(entry.Name, entry.Value, config, warnings);
				}

				return;
			}

			if (hotkeysToken is JArray hotkeysArray)
			{
				foreach (JToken entry in hotkeysArray)
				{
					if (!(entry is JObject hotkeyObj))
					{
						continue;
					}

					string clientName = GetString(hotkeyObj, "Name", null)
						?? GetString(hotkeyObj, "Character", null)
						?? GetString(hotkeyObj, "Client", null)
						?? GetString(hotkeyObj, "Title", null);

					JToken hotkeyValue = FindProperty(hotkeyObj, "Key")
						?? FindProperty(hotkeyObj, "Hotkey")
						?? FindProperty(hotkeyObj, "Combo")
						?? FindProperty(hotkeyObj, "Value")
						?? FindProperty(hotkeyObj, "Modifiers");

					if (!string.IsNullOrEmpty(clientName) && hotkeyValue != null)
					{
						ImportOneEveXHotkey(clientName, hotkeyValue, config, warnings);
					}
				}
			}
		}

		private static void ImportOneEveXHotkey(string clientName, JToken hotkeyToken, IThumbnailConfiguration config, IList<string> warnings)
		{
			string raw = hotkeyToken?.Type == JTokenType.String ? hotkeyToken.Value<string>() : hotkeyToken?.ToString();
			if (string.IsNullOrWhiteSpace(clientName) || string.IsNullOrWhiteSpace(raw))
			{
				return;
			}

			if (TryConvertAhkHotkey(raw, out Keys keys))
			{
				config.SetClientHotkey(NormalizeClientName(clientName), keys);
			}
			else
			{
				warnings.Add("Skipped hotkey for '" + clientName + "': could not convert '" + raw + "'.");
			}
		}

		/// <summary>
		/// Converts AHK-style hotkey strings (e.g. "ctrl & 1", "^!F1", "XButton1") into a
		/// WinForms Keys value. Mouse 4/5 and middle click are supported as the primary key.
		/// Mouse+keyboard chords (e.g. "XButton1 & 1") are skipped.
		/// </summary>
		private static bool TryConvertAhkHotkey(string ahk, out Keys keys)
		{
			keys = Keys.None;
			if (string.IsNullOrWhiteSpace(ahk))
			{
				return false;
			}

			string s = ahk.Trim();
			Keys modifiers = Keys.None;

			while (s.Length > 0 && "^!+#".IndexOf(s[0]) >= 0)
			{
				switch (s[0])
				{
					case '^': modifiers |= Keys.Control; break;
					case '!': modifiers |= Keys.Alt; break;
					case '+': modifiers |= Keys.Shift; break;
					case '#': return false; // Windows key modifier is not supported as a WinForms hotkey.
				}

				s = s.Substring(1);
			}

			string[] parts = s.Split('&');
			string keyPart = parts[parts.Length - 1].Trim();

			for (int i = 0; i < parts.Length - 1; i++)
			{
				switch (parts[i].Trim().ToLowerInvariant())
				{
					case "ctrl":
					case "control":
						modifiers |= Keys.Control;
						break;
					case "alt":
						modifiers |= Keys.Alt;
						break;
					case "shift":
						modifiers |= Keys.Shift;
						break;
					default:
						// Unrecognized chord segment (e.g. a mouse+key combo like "XButton1 & 1").
						return false;
				}
			}

			Keys baseKey = ParseAhkKeyToken(keyPart);
			if (baseKey == Keys.None)
			{
				return false;
			}

			keys = baseKey | modifiers;
			return true;
		}

		private static Keys ParseAhkKeyToken(string token)
		{
			token = token?.Trim();
			if (string.IsNullOrEmpty(token))
			{
				return Keys.None;
			}

			if (token.Length == 1 && char.IsDigit(token[0]))
			{
				return Keys.D0 + (token[0] - '0');
			}

			if (token.Length == 1 && char.IsLetter(token[0]))
			{
				return Enum.TryParse(token.ToUpperInvariant(), out Keys letterKey) ? letterKey : Keys.None;
			}

			switch (token.ToLowerInvariant())
			{
				case "mouse4":
				case "xbutton1":
					return Keys.XButton1;
				case "mouse5":
				case "xbutton2":
					return Keys.XButton2;
				case "middle":
				case "mbutton":
					return Keys.MButton;
			}

			return Enum.TryParse(token, true, out Keys parsed) ? parsed : Keys.None;
		}

		private static string NormalizeClientName(string rawName)
		{
			if (string.IsNullOrWhiteSpace(rawName))
			{
				return rawName;
			}

			if (rawName.StartsWith("EVE - ", StringComparison.OrdinalIgnoreCase)
				|| rawName.StartsWith("EVE Frontier - ", StringComparison.OrdinalIgnoreCase))
			{
				return rawName;
			}

			return "EVE - " + rawName;
		}

		private static void TrySet(IList<string> warnings, string fieldName, Action action)
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				warnings.Add("Skipped '" + fieldName + "': " + ex.Message);
			}
		}

		#endregion

		#region JObject helpers

		private static JToken FindProperty(JObject obj, string name)
		{
			if (obj == null)
			{
				return null;
			}

			foreach (JProperty property in obj.Properties())
			{
				if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
				{
					return property.Value;
				}
			}

			return null;
		}

		private static JToken FindToken(JObject preferred, JObject fallback, string name)
		{
			return FindProperty(preferred, name) ?? FindProperty(fallback, name);
		}

		private static bool GetBool(JObject obj, string name, bool defaultValue)
		{
			JToken token = FindProperty(obj, name);
			if (token == null || token.Type == JTokenType.Null)
			{
				return defaultValue;
			}

			try
			{
				return token.Value<bool>();
			}
			catch
			{
				return defaultValue;
			}
		}

		private static bool GetBool(JObject preferred, JObject fallback, string name, bool defaultValue)
		{
			JToken token = FindToken(preferred, fallback, name);
			if (token == null || token.Type == JTokenType.Null)
			{
				return defaultValue;
			}

			try
			{
				return token.Value<bool>();
			}
			catch
			{
				return defaultValue;
			}
		}

		private static int? GetInt(JObject obj, string name, int? defaultValue)
		{
			JToken token = FindProperty(obj, name);
			if (token == null || token.Type == JTokenType.Null)
			{
				return defaultValue;
			}

			return int.TryParse(token.ToString(), out int value) ? value : defaultValue;
		}

		private static int? GetInt(JObject preferred, JObject fallback, string name, int? defaultValue)
		{
			JToken token = FindToken(preferred, fallback, name);
			if (token == null || token.Type == JTokenType.Null)
			{
				return defaultValue;
			}

			return int.TryParse(token.ToString(), out int value) ? value : defaultValue;
		}

		private static double? GetDouble(JObject preferred, JObject fallback, string name, double? defaultValue)
		{
			JToken token = FindToken(preferred, fallback, name);
			if (token == null || token.Type == JTokenType.Null)
			{
				return defaultValue;
			}

			return double.TryParse(token.ToString(), out double value) ? value : defaultValue;
		}

		private static string GetString(JObject obj, string name, string defaultValue)
		{
			JToken token = FindProperty(obj, name);
			return token != null && token.Type != JTokenType.Null ? token.ToString() : defaultValue;
		}

		private static string GetString(JObject preferred, JObject fallback, string name, string defaultValue)
		{
			JToken token = FindToken(preferred, fallback, name);
			return token != null && token.Type != JTokenType.Null ? token.ToString() : defaultValue;
		}

		private static bool TryParseHexColor(string hex, out Color color)
		{
			color = default;
			if (string.IsNullOrWhiteSpace(hex))
			{
				return false;
			}

			try
			{
				string cleaned = hex.Trim().TrimStart('#');
				if (cleaned.Length == 6)
				{
					color = Color.FromArgb(
						Convert.ToInt32(cleaned.Substring(0, 2), 16),
						Convert.ToInt32(cleaned.Substring(2, 2), 16),
						Convert.ToInt32(cleaned.Substring(4, 2), 16));
					return true;
				}

				if (cleaned.Length == 8)
				{
					color = Color.FromArgb(
						Convert.ToInt32(cleaned.Substring(0, 2), 16),
						Convert.ToInt32(cleaned.Substring(2, 2), 16),
						Convert.ToInt32(cleaned.Substring(4, 2), 16),
						Convert.ToInt32(cleaned.Substring(6, 2), 16));
					return true;
				}
			}
			catch
			{
				// fall through
			}

			return false;
		}

		#endregion
	}
}
