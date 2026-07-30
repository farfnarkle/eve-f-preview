// Parses Documents\EVE\logs\Chatlogs\Local_*.txt for each character's current solar system.
//
// The client re-joins Local on every system change, which appends one line per arrival:
//   [ 2026.07.29 16:38:03 ] EVE System > Channel changed to Local : C-N4OD
// Gate jumps, undocks, clone jumps and death clones all produce it, so no event needs
// to be modelled separately. Only lines spoken by "EVE System" count, so a player typing
// the same text in Local cannot move the overlay.
//
// Chat logs are UTF-16LE and EVE writes a BOM in front of every line. They exist only while
// chat logging is enabled in the client; without it the system stays unknown.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace EveFPreview.Services.Implementation
{
	public sealed class EveChatLogLocationService : IEveLocationService
	{
		private static readonly Regex FileNameRegex = new Regex(
			@"^Local_(?<stamp>\d{8}_\d{6})_(?<charId>\d+)\.txt$",
			RegexOptions.IgnoreCase | RegexOptions.Compiled);

		private static readonly Regex ListenerRegex = new Regex(
			@"^\s*Listener:\s*(?<name>.+?)\s*$",
			RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

		private static readonly Regex ChannelChangedRegex = new Regex(
			@"\]\s*EVE System\s*>\s*Channel changed to Local\s*:\s*(?<system>.+?)\s*$",
			RegexOptions.IgnoreCase | RegexOptions.Compiled);

		private static readonly TimeSpan DirectoryScanInterval = TimeSpan.FromSeconds(5);
		private const int MaxBackfillFiles = 5;

		private readonly object _sync = new object();
		private readonly Dictionary<long, CharacterLocationState> _byCharacterId =
			new Dictionary<long, CharacterLocationState>();
		private readonly Dictionary<string, long> _nameToCharacterId =
			new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<long, string> _newestFileByCharacter =
			new Dictionary<long, string>();

		private string _logsDirectory;
		private DateTime _lastDirectoryScanUtc = DateTime.MinValue;

		public void Refresh()
		{
			string dir = this.ResolveLogsDirectory();
			if (string.IsNullOrEmpty(dir))
			{
				return;
			}

			lock (this._sync)
			{
				// The Chatlogs folder accumulates thousands of files, so listing it is throttled;
				// tailing the already known session files is what has to happen every tick.
				if (DateTime.UtcNow - this._lastDirectoryScanUtc >= EveChatLogLocationService.DirectoryScanInterval)
				{
					this.ScanDirectory(dir);
				}

				foreach (KeyValuePair<long, string> entry in this._newestFileByCharacter)
				{
					this.TailFile(entry.Key, entry.Value);
				}
			}
		}

		public bool TryGetSystem(string windowTitle, int characterId, out string systemName)
		{
			systemName = null;
			string characterName = EveChatLogLocationService.StripEvePrefix(windowTitle);

			lock (this._sync)
			{
				if (characterId > 0
					&& this._byCharacterId.TryGetValue(characterId, out CharacterLocationState byId)
					&& !string.IsNullOrEmpty(byId.SystemName))
				{
					systemName = byId.SystemName;
					return true;
				}

				if (!string.IsNullOrEmpty(characterName)
					&& this._nameToCharacterId.TryGetValue(characterName, out long mappedId)
					&& this._byCharacterId.TryGetValue(mappedId, out CharacterLocationState byName)
					&& !string.IsNullOrEmpty(byName.SystemName))
				{
					systemName = byName.SystemName;
					return true;
				}

				return false;
			}
		}

		private void ScanDirectory(string dir)
		{
			string[] files;
			try
			{
				files = Directory.GetFiles(dir, "Local_*.txt");
			}
			catch (IOException)
			{
				return;
			}
			catch (UnauthorizedAccessException)
			{
				return;
			}

			this._lastDirectoryScanUtc = DateTime.UtcNow;
			this._newestFileByCharacter.Clear();

			var newestStamp = new Dictionary<long, string>();
			foreach (string path in files)
			{
				Match match = FileNameRegex.Match(Path.GetFileName(path));
				if (!match.Success
					|| !long.TryParse(match.Groups["charId"].Value, out long characterId)
					|| characterId <= 0)
				{
					continue;
				}

				// The session stamp in the name orders sessions reliably; directory timestamps go
				// stale while EVE holds the current log open.
				string stamp = match.Groups["stamp"].Value;
				if (!newestStamp.TryGetValue(characterId, out string existing)
					|| string.CompareOrdinal(stamp, existing) >= 0)
				{
					newestStamp[characterId] = stamp;
					this._newestFileByCharacter[characterId] = path;
				}
			}
		}

		private void TailFile(long characterId, string path)
		{
			if (!this._byCharacterId.TryGetValue(characterId, out CharacterLocationState state))
			{
				state = new CharacterLocationState();
				this._byCharacterId[characterId] = state;
			}

			if (!string.Equals(state.ActiveFilePath, path, StringComparison.OrdinalIgnoreCase))
			{
				// New session file: keep the prior system until this file states one.
				state.ActiveFilePath = path;
				state.ReadPosition = 0;
				state.PendingLine = string.Empty;
				state.NeedsListenerParse = true;
				state.BackfillAttempted = false;
			}

			if (!this.TryReadNewText(path, state, out string chunk))
			{
				return;
			}

			string text = state.PendingLine + chunk;
			int lastNewline = text.LastIndexOfAny(new[] { '\r', '\n' });
			if (lastNewline >= 0)
			{
				state.PendingLine = text.Substring(lastNewline + 1);
				text = text.Substring(0, lastNewline + 1);

				if (state.NeedsListenerParse)
				{
					Match listener = ListenerRegex.Match(text);
					if (listener.Success)
					{
						string name = listener.Groups["name"].Value.Trim();
						if (!string.IsNullOrEmpty(name))
						{
							state.CharacterName = name;
							this._nameToCharacterId[name] = characterId;
							state.NeedsListenerParse = false;
						}
					}
				}

				EveChatLogLocationService.ApplyLocationLines(state, text);
			}
			else
			{
				state.PendingLine = text;
			}

			if (string.IsNullOrEmpty(state.SystemName) && !state.BackfillAttempted)
			{
				this.BackfillFromOlderFiles(characterId, path, state);
				state.BackfillAttempted = true;
			}
		}

		/// <summary>
		/// Reads everything appended since the last pass. Returns false when there is nothing new.
		/// </summary>
		private bool TryReadNewText(string path, CharacterLocationState state, out string chunk)
		{
			chunk = null;

			try
			{
				using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
				{
					if (state.ReadPosition > stream.Length)
					{
						// Truncated or replaced in place
						state.ReadPosition = 0;
						state.PendingLine = string.Empty;
					}

					long available = stream.Length - state.ReadPosition;
					if (available <= 0)
					{
						return false;
					}

					stream.Seek(state.ReadPosition, SeekOrigin.Begin);

					var buffer = new byte[available];
					int read = 0;
					while (read < buffer.Length)
					{
						int step = stream.Read(buffer, read, buffer.Length - read);
						if (step <= 0)
						{
							break;
						}

						read += step;
					}

					// A half written UTF-16 code unit is left for the next pass
					int usable = read - (read % 2);
					state.ReadPosition += usable;

					if (usable <= 0)
					{
						return false;
					}

					chunk = EveChatLogLocationService.DecodeUtf16(buffer, usable);
					return true;
				}
			}
			catch (IOException)
			{
				return false;
			}
			catch (UnauthorizedAccessException)
			{
				return false;
			}
		}

		/// <summary>
		/// When the newest session file has no Local line yet (common right after login), walk a
		/// few older files for the same character id to recover the last known system.
		/// </summary>
		private void BackfillFromOlderFiles(long characterId, string newestPath, CharacterLocationState state)
		{
			string[] older;
			try
			{
				older = Directory
					.GetFiles(Path.GetDirectoryName(newestPath), "Local_*_" + characterId + ".txt")
					.Where(path => !string.Equals(path, newestPath, StringComparison.OrdinalIgnoreCase))
					.OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
					.Take(EveChatLogLocationService.MaxBackfillFiles)
					.ToArray();
			}
			catch (IOException)
			{
				return;
			}
			catch (UnauthorizedAccessException)
			{
				return;
			}

			foreach (string prior in older)
			{
				string content;
				try
				{
					using (var stream = new FileStream(prior, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
					{
						var buffer = new byte[stream.Length];
						int read = stream.Read(buffer, 0, buffer.Length);
						content = EveChatLogLocationService.DecodeUtf16(buffer, read - (read % 2));
					}
				}
				catch (IOException)
				{
					continue;
				}
				catch (UnauthorizedAccessException)
				{
					continue;
				}

				var candidate = new CharacterLocationState();
				EveChatLogLocationService.ApplyLocationLines(candidate, content);
				if (!string.IsNullOrEmpty(candidate.SystemName))
				{
					state.SystemName = candidate.SystemName;
					return;
				}
			}
		}

		private static void ApplyLocationLines(CharacterLocationState state, string text)
		{
			using (var reader = new StringReader(text))
			{
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					Match changed = ChannelChangedRegex.Match(line);
					if (changed.Success)
					{
						state.SystemName = EveChatLogLocationService.SanitizeSystemName(changed.Groups["system"].Value);
					}
				}
			}
		}

		private static string DecodeUtf16(byte[] buffer, int count)
		{
			if (count <= 0)
			{
				return string.Empty;
			}

			// EVE emits a BOM in front of every line, not just at the start of the file
			return Encoding.Unicode.GetString(buffer, 0, count).Replace("\uFEFF", string.Empty);
		}

		private string ResolveLogsDirectory()
		{
			if (!string.IsNullOrEmpty(this._logsDirectory) && Directory.Exists(this._logsDirectory))
			{
				return this._logsDirectory;
			}

			string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			string candidate = Path.Combine(documents, "EVE", "logs", "Chatlogs");
			if (Directory.Exists(candidate))
			{
				this._logsDirectory = candidate;
				return candidate;
			}

			return null;
		}

		private static string StripEvePrefix(string windowTitle)
		{
			if (string.IsNullOrEmpty(windowTitle))
			{
				return null;
			}

			const string prefix = "EVE - ";
			if (windowTitle.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return windowTitle.Substring(prefix.Length).Trim();
			}

			return windowTitle.Trim();
		}

		private static string SanitizeSystemName(string raw)
		{
			if (string.IsNullOrWhiteSpace(raw))
			{
				return null;
			}

			string cleaned = raw.Trim();
			// Strip any trailing HTML fragments that occasionally leak into log lines.
			int markup = cleaned.IndexOf('<');
			if (markup >= 0)
			{
				cleaned = cleaned.Substring(0, markup).Trim();
			}

			return string.IsNullOrEmpty(cleaned) ? null : cleaned;
		}

		private sealed class CharacterLocationState
		{
			public string ActiveFilePath;
			public long ReadPosition;
			public string PendingLine = string.Empty;
			public string CharacterName;
			public string SystemName;
			public bool NeedsListenerParse = true;
			public bool BackfillAttempted;
		}
	}
}
