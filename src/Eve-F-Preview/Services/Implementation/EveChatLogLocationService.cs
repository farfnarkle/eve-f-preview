// Parses Documents\EVE\logs\Chatlogs\Local_*.txt for each character's current solar system.
//
// The client re-joins Local on every system change, which appends one line per arrival:
//   [ 2026.07.29 16:38:03 ] EVE System > Channel changed to Local : C-N4OD
// Gate jumps, undocks, clone jumps and death clones all produce it, so no event needs
// to be modelled separately. Only lines spoken by "EVE System" count, so a player typing
// the same text in Local cannot move the overlay (the match is anchored to the line's own
// timestamp and speaker, so quoting the system line inside a message does not count either).
//
// Chat logs are UTF-16LE and EVE writes a BOM in front of every line. They exist only while
// chat logging is enabled in the client; without it the system stays unknown.
//
// The logs are read on a worker thread, and only for the characters whose clients are running:
// the Chatlogs folder holds a Local log for every character ever played (dozens), and reading
// them all on the UI thread every refresh tick cost ~1 ms per tick plus a ~40 ms stall on the
// first one.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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

		// Anchored to the line's own "[ timestamp ] speaker >" prefix. Unanchored, a player typing
		// "] EVE System > Channel changed to Local : Jita" would match inside their own message.
		private static readonly Regex ChannelChangedRegex = new Regex(
			@"^\s*\[[^\]]*\]\s*EVE System\s*>\s*Channel changed to Local\s*:\s*(?<system>.+?)\s*$",
			RegexOptions.IgnoreCase | RegexOptions.Compiled);

		private static readonly TimeSpan DirectoryScanInterval = TimeSpan.FromSeconds(5);
		private const int MaxBackfillFiles = 5;

		// The "Listener:" line is in the header EVE writes when it creates the file.
		private const int ListenerHeaderBytes = 8192;

		// Worker state. Only the one refresh pass allowed to run at a time (_refreshRunning) touches it.
		private readonly Dictionary<long, CharacterLocationState> _byCharacterId =
			new Dictionary<long, CharacterLocationState>();
		private readonly Dictionary<long, string> _newestFileByCharacter =
			new Dictionary<long, string>();
		private readonly Dictionary<string, string> _listenerByFile =
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private string _logsDirectory;
		private DateTime _lastDirectoryScanUtc = DateTime.MinValue;
		private int _refreshRunning;

		// Results, read by the UI thread while a pass may be running.
		private readonly ConcurrentDictionary<long, string> _systemByCharacterId =
			new ConcurrentDictionary<long, string>();
		private readonly ConcurrentDictionary<string, long> _nameToCharacterId =
			new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);

		public event Action SystemsChanged;

		public void RequestRefresh(IEnumerable<(string WindowTitle, long CharacterId)> clients)
		{
			// Taken now, on the caller's thread: the worker must not touch the caller's collections.
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var characterIds = new HashSet<long>();
			foreach ((string windowTitle, long characterId) in clients)
			{
				if (EveClient.TryGetCharacterName(windowTitle, out string name))
				{
					names.Add(name);
				}

				if (characterId > 0)
				{
					characterIds.Add(characterId);
				}
			}

			// A pass still running (slow disk) just finishes; the next tick asks again.
			if (Interlocked.Exchange(ref this._refreshRunning, 1) == 1)
			{
				return;
			}

			Task.Run(() =>
			{
				bool changed = false;
				try
				{
					changed = this.RefreshCore(names, characterIds);
				}
				catch (Exception ex)
				{
					// File access is already guarded; this only stops anything unexpected from taking the
					// overlay down for good (the next tick tries again).
					Debug.WriteLine(ex);
				}
				finally
				{
					Volatile.Write(ref this._refreshRunning, 0);
				}

				if (changed)
				{
					this.SystemsChanged?.Invoke();
				}
			});
		}

		public bool TryGetSystem(string windowTitle, long characterId, out string systemName)
		{
			if (characterId > 0
				&& this._systemByCharacterId.TryGetValue(characterId, out systemName)
				&& !string.IsNullOrEmpty(systemName))
			{
				return true;
			}

			string characterName = EveClient.StripTitlePrefix(windowTitle);
			if (!string.IsNullOrEmpty(characterName)
				&& this._nameToCharacterId.TryGetValue(characterName, out long mappedId)
				&& this._systemByCharacterId.TryGetValue(mappedId, out systemName)
				&& !string.IsNullOrEmpty(systemName))
			{
				return true;
			}

			systemName = null;
			return false;
		}

		/// <summary>Tails the running characters' current Local logs. Returns whether any system changed.</summary>
		private bool RefreshCore(ISet<string> runningNames, ISet<long> runningCharacterIds)
		{
			string dir = this.ResolveLogsDirectory();
			if (string.IsNullOrEmpty(dir))
			{
				return false;
			}

			// The Chatlogs folder accumulates thousands of files, so listing it is throttled;
			// tailing the running characters' session files is what has to happen every tick.
			if (DateTime.UtcNow - this._lastDirectoryScanUtc >= EveChatLogLocationService.DirectoryScanInterval)
			{
				this.ScanDirectory(dir);
			}

			bool changed = false;
			foreach (KeyValuePair<long, string> entry in this._newestFileByCharacter)
			{
				if (!runningCharacterIds.Contains(entry.Key))
				{
					string listener = this.GetListenerName(entry.Key, entry.Value);
					if (listener == null || !runningNames.Contains(listener))
					{
						continue;
					}
				}

				changed |= this.TailFile(entry.Key, entry.Value);
			}

			return changed;
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

			// Only the current session files' listener names are ever needed again.
			var current = new HashSet<string>(this._newestFileByCharacter.Values, StringComparer.OrdinalIgnoreCase);
			foreach (string stale in this._listenerByFile.Keys.Where(path => !current.Contains(path)).ToList())
			{
				this._listenerByFile.Remove(stale);
			}
		}

		/// <summary>
		/// The character name a session file belongs to, from the "Listener:" line in its header -
		/// read once per file (not the whole file), so a character can be matched to a running
		/// client without tailing every character's log.
		/// </summary>
		private string GetListenerName(long characterId, string path)
		{
			if (this._listenerByFile.TryGetValue(path, out string cached))
			{
				return cached.Length > 0 ? cached : null;
			}

			string header;
			long length;
			try
			{
				using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
				{
					length = stream.Length;
					var buffer = new byte[(int)Math.Min(length, EveChatLogLocationService.ListenerHeaderBytes)];
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

					header = EveChatLogLocationService.DecodeUtf16(buffer, read - (read % 2));
				}
			}
			catch (IOException)
			{
				return null;
			}
			catch (UnauthorizedAccessException)
			{
				return null;
			}

			Match listener = ListenerRegex.Match(header);
			string name = listener.Success ? listener.Groups["name"].Value.Trim() : string.Empty;
			if (name.Length > 0)
			{
				this._nameToCharacterId[name] = characterId;
			}
			else if (length < EveChatLogLocationService.ListenerHeaderBytes)
			{
				// Possibly still being written: look again next time instead of remembering "none".
				return null;
			}

			this._listenerByFile[path] = name;
			return name.Length > 0 ? name : null;
		}

		/// <summary>Reads what's new in the character's session file. Returns whether its system changed.</summary>
		private bool TailFile(long characterId, string path)
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

			if (this.TryReadNewText(path, state, out string chunk))
			{
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
			}

			if (string.IsNullOrEmpty(state.SystemName) && !state.BackfillAttempted)
			{
				this.BackfillFromOlderFiles(characterId, path, state);
				state.BackfillAttempted = true;
			}

			if (string.IsNullOrEmpty(state.SystemName)
				|| (this._systemByCharacterId.TryGetValue(characterId, out string published)
					&& string.Equals(published, state.SystemName, StringComparison.Ordinal)))
			{
				return false;
			}

			this._systemByCharacterId[characterId] = state.SystemName;
			return true;
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
			return Encoding.Unicode.GetString(buffer, 0, count).Replace("﻿", string.Empty);
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
