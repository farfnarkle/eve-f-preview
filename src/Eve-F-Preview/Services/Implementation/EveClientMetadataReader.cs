using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using EveFPreview.Services.Interop;

namespace EveFPreview.Services
{
	/// <summary>
	/// Reads the account / character ids the EVE launcher passes on a client's command line.
	/// A process's command line never changes, so each window is looked up once and cached: the
	/// lookup is a WMI query (tens of milliseconds), so callers run the first one off the UI thread.
	/// Safe to call from any thread.
	/// </summary>
	internal static class EveClientMetadataReader
	{
		private sealed class CachedMetadata
		{
			public uint ProcessId;
			public bool Success;
			public long AccountId;
			public long CharacterId;
		}

		private static readonly object Sync = new object();
		private static readonly Dictionary<IntPtr, CachedMetadata> Cache = new Dictionary<IntPtr, CachedMetadata>();

		private static readonly Regex LauncherDataPattern = new Regex(
			@"/LauncherData=([A-Za-z0-9+/=]+)",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private static readonly Regex AutoSelectCharacterPattern = new Regex(
			@"/autoSelectCharacter:(\d+)",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private static readonly Regex LauncherDataPayloadPattern = new Regex(
			@"::(\d+):(\d+)\s*$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		public static bool TryReadMetadata(IntPtr mainWindowHandle, out long accountId, out long characterId)
		{
			accountId = 0;
			characterId = 0;

			if (mainWindowHandle == IntPtr.Zero)
			{
				return false;
			}

			// The window already tells us its process - no need to enumerate every process on the
			// system (and every window of each) to find the one that owns it.
			User32NativeMethods.GetWindowThreadProcessId(mainWindowHandle, out uint processId);
			if (processId == 0)
			{
				return false;
			}

			lock (Sync)
			{
				// The process id check guards against a recycled window handle.
				if (Cache.TryGetValue(mainWindowHandle, out CachedMetadata cached) && cached.ProcessId == processId)
				{
					accountId = cached.AccountId;
					characterId = cached.CharacterId;
					return cached.Success;
				}
			}

			string commandLine = GetProcessCommandLine((int)processId);
			bool success = TryParseCommandLine(commandLine, out accountId, out characterId);

			// Only cache an actual answer. A failed read (WMI hiccup) is retried on the next call,
			// which only happens when the window is added or changes title - never per refresh tick.
			if (commandLine != null)
			{
				lock (Sync)
				{
					Cache[mainWindowHandle] = new CachedMetadata
					{
						ProcessId = processId,
						Success = success,
						AccountId = accountId,
						CharacterId = characterId
					};
				}
			}

			return success;
		}

		/// <summary>Whether <see cref="TryReadMetadata"/> can answer for this window from the cache, without a WMI query.</summary>
		public static bool IsCached(IntPtr mainWindowHandle)
		{
			User32NativeMethods.GetWindowThreadProcessId(mainWindowHandle, out uint processId);
			lock (Sync)
			{
				return processId != 0
					&& Cache.TryGetValue(mainWindowHandle, out CachedMetadata cached)
					&& cached.ProcessId == processId;
			}
		}

		/// <summary>Drops the cached entry for a window that has gone away.</summary>
		public static void Forget(IntPtr mainWindowHandle)
		{
			lock (Sync)
			{
				Cache.Remove(mainWindowHandle);
			}
		}

		internal static bool TryParseCommandLine(string commandLine, out long accountId, out long characterId)
		{
			accountId = 0;
			characterId = 0;

			if (string.IsNullOrEmpty(commandLine))
			{
				return false;
			}

			if (LauncherDataPattern.Match(commandLine) is Match launcherDataMatch && launcherDataMatch.Success)
			{
				try
				{
					string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(launcherDataMatch.Groups[1].Value));
					if (LauncherDataPayloadPattern.Match(decoded) is Match payloadMatch && payloadMatch.Success)
					{
						// TryParse: an id too large for a long must not throw (this runs on the UI thread).
						return long.TryParse(payloadMatch.Groups[1].Value, out accountId)
							&& long.TryParse(payloadMatch.Groups[2].Value, out characterId)
							&& accountId > 0 && characterId > 0;
					}
				}
				catch (FormatException)
				{
				}
				catch (DecoderFallbackException)
				{
				}
			}

			if (AutoSelectCharacterPattern.Match(commandLine) is Match autoSelectMatch && autoSelectMatch.Success
				&& long.TryParse(autoSelectMatch.Groups[1].Value, out characterId))
			{
				return characterId > 0;
			}

			return false;
		}

		private static string GetProcessCommandLine(int processId)
		{
#if LINUX
			return null;
#else
			try
			{
				using var searcher = new System.Management.ManagementObjectSearcher(
					$"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
				using System.Management.ManagementObjectCollection results = searcher.Get();
				foreach (System.Management.ManagementBaseObject result in results)
				{
					using (result)
					{
						return result["CommandLine"] as string;
					}
				}
			}
			catch (System.Management.ManagementException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
			catch (System.Runtime.InteropServices.COMException)
			{
				// WMI service unavailable or busy
			}

			return null;
#endif
		}
	}
}
