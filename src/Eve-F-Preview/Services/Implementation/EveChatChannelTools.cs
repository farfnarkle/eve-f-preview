using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EveFPreview.Services
{
	public sealed class EveChatChannelInfo
	{
		public string Key { get; set; }
		public string DisplayName { get; set; }
		public bool IsBuiltin { get; set; }

		public override string ToString() =>
			string.IsNullOrEmpty(this.DisplayName) ? this.Key : $"{this.DisplayName}  ({this.Key})";
	}

	public static class EveChatChannelTools
	{
		private static readonly string[] BuiltinPrefixes = { "local", "corp", "alliance" };

		public static bool IsBuiltinChannelKey(string key)
		{
			if (string.IsNullOrEmpty(key))
			{
				return false;
			}

			return BuiltinPrefixes.Any(p =>
				string.Equals(key, p, StringComparison.OrdinalIgnoreCase)
				|| key.StartsWith(p + "_", StringComparison.OrdinalIgnoreCase));
		}

		public static IList<EveChatChannelInfo> ListChannels(string coreCharPath)
		{
			object root = EveBlueMarshal.Load(coreCharPath);
			return ListChannels(root);
		}

		public static IList<EveChatChannelInfo> ListChannels(object root)
		{
			var result = new List<EveChatChannelInfo>();
			if (!TryGetChannels(root, out _, out IList<object> entries))
			{
				return result;
			}

			foreach (object entry in entries)
			{
				object[] parts = AsSequence(entry);
				if (parts.Length == 0)
				{
					continue;
				}

				string key = parts[0]?.ToString() ?? string.Empty;
				string name = parts.Length > 0 ? parts[parts.Length - 1]?.ToString() ?? key : key;
				result.Add(new EveChatChannelInfo
				{
					Key = key,
					DisplayName = name,
					IsBuiltin = IsBuiltinChannelKey(key)
				});
			}

			return result;
		}

		/// <summary>Removes entries whose channel key is in keysToRemove. Builtins are never removed.</summary>
		public static IList<string> StripChannels(object root, IEnumerable<string> keysToRemove)
		{
			var remove = new HashSet<string>(keysToRemove ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			var removedNames = new List<string>();

			if (!TryGetChannels(root, out object timestamp, out IList<object> entries))
			{
				return removedNames;
			}

			var kept = new List<object>();
			foreach (object entry in entries)
			{
				object[] parts = AsSequence(entry);
				string key = parts.Length > 0 ? parts[0]?.ToString() ?? string.Empty : string.Empty;
				string name = parts.Length > 0 ? parts[parts.Length - 1]?.ToString() ?? key : key;

				if (!IsBuiltinChannelKey(key) && remove.Contains(key))
				{
					removedNames.Add(name);
					continue;
				}

				kept.Add(entry);
			}

			SetChannels(root, timestamp, kept);
			return removedNames;
		}

		public static byte[] LoadStripAndEncode(string sourcePath, IEnumerable<string> keysToRemove, out IList<string> removedNames)
		{
			return PrepareCoreCharCopy(sourcePath, keysToRemove, out removedNames, out _);
		}

		/// <summary>
		/// Decode source core_char, strip selected channels, clear identity-leaking UI
		/// (e.g. contract owner filter with the source character name), then encode.
		/// </summary>
		public static byte[] PrepareCoreCharCopy(
			string sourcePath,
			IEnumerable<string> keysToRemove,
			out IList<string> removedNames,
			out IList<string> sanitizedFields)
		{
			object root = EveBlueMarshal.Load(sourcePath);
			removedNames = StripChannels(root, keysToRemove);
			sanitizedFields = SanitizeIdentityUi(root);
			byte[] blob = EveBlueMarshal.Dumps(root);
			EveBlueMarshal.Loads(blob);
			return blob;
		}

		/// <summary>
		/// Remove UI prefs that embed the source character's identity so alts do not
		/// show the main's name (e.g. Contracts → filter owner).
		/// </summary>
		public static IList<string> SanitizeIdentityUi(object root)
		{
			var cleared = new List<string>();
			if (!(root is Dictionary<object, object> top)
				|| !TryGetDictValue(top, "ui", out object uiObj)
				|| !(uiObj is Dictionary<object, object> ui))
			{
				return cleared;
			}

			// Contract window "Owner" filter stores the source character name.
			if (RemoveDictKey(ui, "mycontracts_filter_owner"))
			{
				cleared.Add("mycontracts_filter_owner");
			}

			return cleared;
		}

		/// <summary>
		/// Decode source core_user, scrub identity-leaking history (e.g. ui.editHistory entries
		/// mentioning the source character's name, such as mail/search boxes), then encode.
		/// Used when copying an account's core_user file onto an alt so the alt does not inherit
		/// text fields that reveal the main's name.
		/// </summary>
		public static byte[] PrepareCoreUserCopy(
			string sourcePath,
			string sourceCharacterName,
			out IList<string> sanitizedFields)
		{
			object root = EveBlueMarshal.Load(sourcePath);
			sanitizedFields = SanitizeCoreUserEditHistory(root, sourceCharacterName);
			byte[] blob = EveBlueMarshal.Dumps(root);
			EveBlueMarshal.Loads(blob);
			return blob;
		}

		/// <summary>
		/// Walks ui.editHistory (a dict of history lists keyed by control/widget id) and removes
		/// any entry that mentions sourceCharacterName. Simple flat lists of strings/tuples are
		/// scrubbed item-by-item; anything nested too deeply to edit safely is cleared entirely
		/// for that control id. Also clears other concrete ui string fields whose key looks like
		/// an owner/filter box (e.g. contract/mail owner filters) if their value is the source name.
		/// </summary>
		public static IList<string> SanitizeCoreUserEditHistory(object root, string sourceCharacterName)
		{
			var cleared = new List<string>();

			string needle = sourceCharacterName?.Trim();
			if (string.IsNullOrEmpty(needle))
			{
				return cleared;
			}

			if (!(root is Dictionary<object, object> top)
				|| !TryGetDictValue(top, "ui", out object uiObj)
				|| !(uiObj is Dictionary<object, object> ui))
			{
				return cleared;
			}

			if (TryGetDictValueCaseInsensitive(ui, "editHistory", out object matchedKey, out object editHistoryObj)
				&& TryUnwrapTimedDict(editHistoryObj, out object timestamp, out Dictionary<object, object> editHistory))
			{
				foreach (object key in editHistory.Keys.ToList())
				{
					object scrubbed = ScrubHistoryEntry(editHistory[key], needle, out bool changed);
					if (!changed)
					{
						continue;
					}

					editHistory[key] = scrubbed;
					cleared.Add("editHistory." + (key?.ToString() ?? "?"));
				}

				// Keep the (timestamp, dict) envelope EVE expects.
				SetDictValue(ui, matchedKey?.ToString() ?? "editHistory", new object[] { timestamp, editHistory });
			}

			// Other concrete string fields whose key looks like an owner/filter box
			// (e.g. contract or mail owner filters) that happen to hold the source name.
			foreach (object key in ui.Keys.ToList())
			{
				string keyName = key?.ToString() ?? string.Empty;
				if (keyName.IndexOf("filter", StringComparison.OrdinalIgnoreCase) < 0
					|| keyName.IndexOf("owner", StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}

				if (ui[key] is string strValue && strValue.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					ui[key] = string.Empty;
					cleared.Add(keyName);
				}
			}

			return cleared;
		}

		/// <summary>
		/// Scrubs a single editHistory value (a list of strings and/or small nested tuples).
		/// Returns the (possibly) modified value; changed is true if anything was removed/cleared.
		/// Falls back to clearing the whole entry when the shape is too ambiguous to edit safely.
		/// </summary>
		private static object ScrubHistoryEntry(object value, string needle, out bool changed)
		{
			changed = false;

			if (!ContainsNameDeep(value, needle))
			{
				return value;
			}

			// Some controls store history as (timestamp, [entries...]).
			object[] pair = AsSequence(value);
			if (pair.Length == 2 && (pair[1] is List<object> || pair[1] is object[]))
			{
				object scrubbedInner = ScrubHistoryEntry(pair[1], needle, out changed);
				if (!changed)
				{
					return value;
				}

				return new object[] { pair[0], scrubbedInner };
			}

			if (TryScrubSequenceShallow(value, needle, out object scrubbed))
			{
				changed = true;
				return scrubbed;
			}

			changed = true;
			return MakeEmptyLike(value);
		}

		/// <summary>Attempts a shallow, item-by-item scrub of a flat list/tuple. Returns false if the shape is too complex to edit safely (caller should clear the whole entry instead).</summary>
		private static bool TryScrubSequenceShallow(object value, string needle, out object result)
		{
			result = null;

			bool isArray;
			IEnumerable<object> items;
			switch (value)
			{
				case object[] arr:
					isArray = true;
					items = arr;
					break;
				case List<object> list:
					isArray = false;
					items = list;
					break;
				default:
					// Plain string or unrecognized shape (e.g. dict) - too messy to edit in place.
					return false;
			}

			var kept = new List<object>();
			foreach (object item in items)
			{
				if (item is string itemStr)
				{
					if (itemStr.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						continue;
					}

					kept.Add(item);
					continue;
				}

				if (item is object[] || item is List<object>)
				{
					if (ContainsNameDeep(item, needle))
					{
						// Drop the whole nested tuple/list rather than editing inside it.
						continue;
					}

					kept.Add(item);
					continue;
				}

				if (ContainsNameDeep(item, needle))
				{
					// Unrecognized nested shape (e.g. dict) mentions the name - too messy to edit item-by-item.
					return false;
				}

				kept.Add(item);
			}

			result = isArray ? (object)kept.ToArray() : new List<object>(kept);
			return true;
		}

		private static object MakeEmptyLike(object value)
		{
			switch (value)
			{
				case object[] _:
					return Array.Empty<object>();
				case List<object> _:
					return new List<object>();
				case string _:
					return string.Empty;
				default:
					return null;
			}
		}

		private static bool ContainsNameDeep(object node, string needle)
		{
			switch (node)
			{
				case null:
					return false;
				case string s:
					return s.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
				case object[] arr:
					return arr.Any(x => ContainsNameDeep(x, needle));
				case List<object> list:
					return list.Any(x => ContainsNameDeep(x, needle));
				case Dictionary<object, object> dict:
					return dict.Values.Any(v => ContainsNameDeep(v, needle)) || dict.Keys.Any(k => ContainsNameDeep(k, needle));
				default:
					return false;
			}
		}

		/// <summary>
		/// Channels to strip = non-builtin channels on the source character that are not in keysToKeep.
		/// Empty keep list means strip all player channels.
		/// </summary>
		public static IList<string> ResolveKeysToStrip(
			long characterId,
			IEnumerable<string> keysToKeep,
			string profileName = null)
		{
			var keep = new HashSet<string>(keysToKeep ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			string path = FindNewestCoreCharPath(characterId, profileName: profileName);
			if (string.IsNullOrEmpty(path))
			{
				return new List<string>();
			}

			try
			{
				return ListChannels(path)
					.Where(c => !c.IsBuiltin && !string.IsNullOrEmpty(c.Key) && !keep.Contains(c.Key))
					.Select(c => c.Key)
					.ToList();
			}
			catch
			{
				return new List<string>();
			}
		}

		public static string FindNewestCoreCharPath(
			long characterId,
			string eveDataRoot = null,
			string serverPattern = "*_tranquility",
			string profileName = null)
		{
			IEnumerable<EveSettingsFileInfo> files = EveSettingsSync.DiscoverSettingsFiles(eveDataRoot, serverPattern)
				.Where(f => f.Type == "char" && f.Id == characterId);

			if (!string.IsNullOrEmpty(profileName))
			{
				files = files.Where(f => string.Equals(f.Profile, profileName, StringComparison.OrdinalIgnoreCase));
			}

			return files
				.OrderByDescending(f => f.LastWrite)
				.Select(f => f.Path)
				.FirstOrDefault();
		}

		private static bool TryUnwrapTimedDict(object value, out object timestamp, out Dictionary<object, object> dict)
		{
			timestamp = null;
			dict = null;

			// Common CCP shape: (timestamp, { ... })
			object[] pair = AsSequence(value);
			if (pair.Length >= 2 && pair[1] is Dictionary<object, object> nested)
			{
				timestamp = pair[0];
				dict = nested;
				return true;
			}

			// Rare: bare dict without timestamp envelope.
			if (value is Dictionary<object, object> bare)
			{
				timestamp = 0;
				dict = bare;
				return true;
			}

			return false;
		}

		private static bool TryGetChannels(object root, out object timestamp, out IList<object> entries)
		{
			timestamp = null;
			entries = Array.Empty<object>();

			if (!(root is Dictionary<object, object> top))
			{
				return false;
			}

			if (!TryGetDictValue(top, "ui", out object uiObj) || !(uiObj is Dictionary<object, object> ui))
			{
				return false;
			}

			if (!TryGetDictValue(ui, "chatchannels", out object cc) || cc == null)
			{
				return false;
			}

			object[] pair = AsSequence(cc);
			if (pair.Length < 2)
			{
				return false;
			}

			timestamp = pair[0];
			entries = AsSequence(pair[1]).ToList();
			return true;
		}

		private static void SetChannels(object root, object timestamp, IList<object> entries)
		{
			var top = (Dictionary<object, object>)root;
			var ui = (Dictionary<object, object>)GetDictValue(top, "ui");
			SetDictValue(ui, "chatchannels", new object[] { timestamp, entries.ToList() });
		}

		private static bool TryGetDictValue(Dictionary<object, object> dict, string key, out object value)
		{
			foreach (KeyValuePair<object, object> kv in dict)
			{
				if (string.Equals(kv.Key?.ToString(), key, StringComparison.Ordinal))
				{
					value = kv.Value;
					return true;
				}
			}

			value = null;
			return false;
		}

		private static bool TryGetDictValueCaseInsensitive(Dictionary<object, object> dict, string key, out object matchedKey, out object value)
		{
			foreach (KeyValuePair<object, object> kv in dict)
			{
				if (string.Equals(kv.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase))
				{
					matchedKey = kv.Key;
					value = kv.Value;
					return true;
				}
			}

			matchedKey = null;
			value = null;
			return false;
		}

		private static object GetDictValue(Dictionary<object, object> dict, string key)
		{
			if (TryGetDictValue(dict, key, out object value))
			{
				return value;
			}

			throw new InvalidDataException("missing key " + key);
		}

		private static void SetDictValue(Dictionary<object, object> dict, string key, object value)
		{
			object existingKey = dict.Keys.FirstOrDefault(k => string.Equals(k?.ToString(), key, StringComparison.Ordinal));
			if (existingKey != null || dict.ContainsKey(key))
			{
				dict[existingKey ?? key] = value;
			}
			else
			{
				dict[key] = value;
			}
		}

		private static bool RemoveDictKey(Dictionary<object, object> dict, string key)
		{
			object existingKey = dict.Keys.FirstOrDefault(k => string.Equals(k?.ToString(), key, StringComparison.Ordinal));
			if (existingKey == null && !dict.ContainsKey(key))
			{
				return false;
			}

			dict.Remove(existingKey ?? key);
			return true;
		}

		private static object[] AsSequence(object o)
		{
			if (o is object[] arr)
			{
				return arr;
			}

			if (o is List<object> list)
			{
				return list.ToArray();
			}

			if (o == null)
			{
				return Array.Empty<object>();
			}

			throw new InvalidDataException("expected sequence for channel data, got " + o.GetType().Name);
		}
	}
}
