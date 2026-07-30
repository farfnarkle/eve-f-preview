using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace EveFPreview.Services
{
	internal static class EveClientMetadataReader
	{
		private static readonly Regex LauncherDataPattern = new Regex(
			@"/LauncherData=([A-Za-z0-9+/=]+)",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private static readonly Regex AutoSelectCharacterPattern = new Regex(
			@"/autoSelectCharacter:(\d+)",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private static readonly Regex LauncherDataPayloadPattern = new Regex(
			@"::(\d+):(\d+)\s*$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		public static bool TryReadMetadata(IntPtr mainWindowHandle, out int accountId, out int characterId)
		{
			accountId = 0;
			characterId = 0;

			if (mainWindowHandle == IntPtr.Zero)
			{
				return false;
			}

			foreach (Process process in Process.GetProcesses())
			{
				using (process)
				{
					if (process.MainWindowHandle != mainWindowHandle)
					{
						continue;
					}

					return TryParseCommandLine(GetProcessCommandLine(process.Id), out accountId, out characterId);
				}
			}

			return false;
		}

		internal static bool TryParseCommandLine(string commandLine, out int accountId, out int characterId)
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
						accountId = int.Parse(payloadMatch.Groups[1].Value);
						characterId = int.Parse(payloadMatch.Groups[2].Value);
						return accountId > 0 && characterId > 0;
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
				&& int.TryParse(autoSelectMatch.Groups[1].Value, out characterId))
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

			return null;
#endif
		}
	}
}
