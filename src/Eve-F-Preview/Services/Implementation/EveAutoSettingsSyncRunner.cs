using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveFPreview.Configuration;

namespace EveFPreview.Services
{
	/// <summary>
	/// Runs the saved Settings Sync profile when auto-sync is enabled at app startup (EVE must not be running).
	/// Profile is written by a successful manual sync on the Settings Sync tab.
	/// </summary>
	public static class EveAutoSettingsSyncRunner
	{
		public static bool HasConfiguredProfile(IThumbnailConfiguration configuration)
		{
			if (configuration == null)
			{
				return false;
			}

			return configuration.AutoSettingsSyncSourceCharacterId > 0
				&& configuration.AutoSettingsSyncSourceUserId > 0
				&& configuration.AutoSettingsSyncDestinationCharacterIds != null
				&& configuration.AutoSettingsSyncDestinationCharacterIds.Count > 0
				&& !string.IsNullOrWhiteSpace(configuration.AutoSettingsSyncProfileName);
		}

		public static string DescribeProfile(IThumbnailConfiguration configuration)
		{
			if (!HasConfiguredProfile(configuration))
			{
				return "No auto-sync profile yet. Run a manual Sync once to save source, destinations, and channels.";
			}

			int destCount = configuration.AutoSettingsSyncDestinationCharacterIds?.Count ?? 0;
			int keepCount = configuration.AutoSettingsSyncChannelKeysToKeep?.Count ?? 0;
			return $"Profile: {configuration.AutoSettingsSyncProfileName}, char {configuration.AutoSettingsSyncSourceCharacterId} → {destCount} destination(s), keep {keepCount} channel(s).";
		}

		public static void SaveProfile(
			IThumbnailConfiguration configuration,
			string settingsProfileName,
			long sourceCharacterId,
			long sourceUserId,
			IEnumerable<long> destinationCharacterIds,
			IEnumerable<long> destinationUserIds,
			IEnumerable<string> channelKeysToKeep)
		{
			configuration.AutoSettingsSyncProfileName = settingsProfileName ?? string.Empty;
			configuration.AutoSettingsSyncSourceCharacterId = sourceCharacterId;
			configuration.AutoSettingsSyncSourceUserId = sourceUserId;
			configuration.AutoSettingsSyncDestinationCharacterIds = (destinationCharacterIds ?? Array.Empty<long>())
				.Where(id => id > 0 && id != sourceCharacterId)
				.Distinct()
				.ToList();
			configuration.AutoSettingsSyncDestinationUserIds = (destinationUserIds ?? Array.Empty<long>())
				.Where(id => id > 0 && id != sourceUserId)
				.Distinct()
				.ToList();
			SaveChannelKeysToKeep(configuration, channelKeysToKeep);
		}

		public static void SaveChannelKeysToKeep(IThumbnailConfiguration configuration, IEnumerable<string> channelKeysToKeep)
		{
			if (configuration == null)
			{
				return;
			}

			configuration.AutoSettingsSyncChannelKeysToKeep = (channelKeysToKeep ?? Array.Empty<string>())
				.Where(k => !string.IsNullOrWhiteSpace(k))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			// Clear legacy strip list once keep is authoritative.
			configuration.AutoSettingsSyncChannelKeysToStrip = new List<string>();
		}

		public static void SaveSourceSelection(
			IThumbnailConfiguration configuration,
			string settingsProfileName,
			long sourceCharacterId,
			long sourceUserId)
		{
			if (configuration == null)
			{
				return;
			}

			if (!string.IsNullOrWhiteSpace(settingsProfileName))
			{
				configuration.AutoSettingsSyncProfileName = settingsProfileName;
			}

			configuration.AutoSettingsSyncSourceCharacterId = sourceCharacterId;
			if (sourceUserId > 0)
			{
				configuration.AutoSettingsSyncSourceUserId = sourceUserId;
			}
		}

		/// <summary>
		/// Drops auto-sync destinations that have no core_char/core_user file in the selected EVE settings profile.
		/// Returns how many destination entries were removed.
		/// </summary>
		public static int PruneMissingDestinations(IThumbnailConfiguration configuration, string settingsProfileName)
		{
			if (configuration == null || string.IsNullOrWhiteSpace(settingsProfileName))
			{
				return 0;
			}

			HashSet<long> charIds = EveSettingsSync.GetCharacterIdsInProfile(settingsProfileName);
			HashSet<long> userIds = EveSettingsSync.GetUserIdsInProfile(settingsProfileName);

			int removed = 0;
			var destChars = configuration.AutoSettingsSyncDestinationCharacterIds ?? new List<long>();
			int beforeChars = destChars.Count;
			configuration.AutoSettingsSyncDestinationCharacterIds = destChars
				.Where(id => id > 0 && charIds.Contains(id))
				.Distinct()
				.ToList();
			removed += beforeChars - configuration.AutoSettingsSyncDestinationCharacterIds.Count;

			var destUsers = configuration.AutoSettingsSyncDestinationUserIds ?? new List<long>();
			int beforeUsers = destUsers.Count;
			configuration.AutoSettingsSyncDestinationUserIds = destUsers
				.Where(id => id > 0 && userIds.Contains(id))
				.Distinct()
				.ToList();
			removed += beforeUsers - configuration.AutoSettingsSyncDestinationUserIds.Count;

			if (configuration.AutoSettingsSyncSourceCharacterId > 0
				&& !charIds.Contains(configuration.AutoSettingsSyncSourceCharacterId))
			{
				configuration.AutoSettingsSyncSourceCharacterId = 0;
				configuration.AutoSettingsSyncSourceUserId = 0;
			}

			return removed;
		}

		/// <summary>
		/// Resolves channel keys to strip from the keep list (or legacy strip list if keep was never set).
		/// </summary>
		public static IList<string> ResolveChannelKeysToStrip(IThumbnailConfiguration configuration)
		{
			if (configuration == null || configuration.AutoSettingsSyncSourceCharacterId <= 0)
			{
				return new List<string>();
			}

			bool hasKeep = configuration.AutoSettingsSyncChannelKeysToKeep != null
				&& configuration.AutoSettingsSyncChannelKeysToKeep.Count > 0;
			bool hasLegacyStrip = configuration.AutoSettingsSyncChannelKeysToStrip != null
				&& configuration.AutoSettingsSyncChannelKeysToStrip.Count > 0;

			// Prefer keep model. Empty keep + no legacy strip => strip all player channels.
			if (hasKeep || !hasLegacyStrip)
			{
				return EveChatChannelTools.ResolveKeysToStrip(
					configuration.AutoSettingsSyncSourceCharacterId,
					configuration.AutoSettingsSyncChannelKeysToKeep ?? new List<string>(),
					configuration.AutoSettingsSyncProfileName);
			}

			return configuration.AutoSettingsSyncChannelKeysToStrip.ToList();
		}

		/// <summary>
		/// Attempts an automatic sync. Returns null if skipped; otherwise the sync report.
		/// </summary>
		public static EveSettingsSyncReport TryRun(IThumbnailConfiguration configuration, string reason, out string skipReason)
		{
			skipReason = null;

			if (configuration == null || !configuration.EnableAutoSettingsSync)
			{
				skipReason = "Auto settings sync is disabled.";
				return null;
			}

			if (!HasConfiguredProfile(configuration))
			{
				skipReason = "No auto-sync profile configured. Run a manual Sync first.";
				return null;
			}

			if (EveSettingsSync.IsEveRunning())
			{
				skipReason = "EVE is still running; auto-sync skipped.";
				return null;
			}

			PruneMissingDestinations(configuration, configuration.AutoSettingsSyncProfileName);
			if (configuration.AutoSettingsSyncDestinationCharacterIds.Count == 0)
			{
				skipReason = "No valid destinations left in " + configuration.AutoSettingsSyncProfileName + ".";
				return null;
			}

			var options = new EveSettingsSyncOptions
			{
				SourceCharacterId = configuration.AutoSettingsSyncSourceCharacterId,
				SourceUserId = configuration.AutoSettingsSyncSourceUserId,
				SourceCharacterName = ResolveSourceCharacterName(configuration),
				DestinationCharacterIds = configuration.AutoSettingsSyncDestinationCharacterIds.ToList(),
				DestinationUserIds = (configuration.AutoSettingsSyncDestinationUserIds ?? new List<long>()).ToList(),
				ChannelKeysToStrip = ResolveChannelKeysToStrip(configuration),
				ProfileName = configuration.AutoSettingsSyncProfileName,
				Mode = EveSettingsSyncMode.Copy
			};

			EveSettingsSyncReport report = new EveSettingsSync(options).Run();
			AppendLog(reason, report, null);
			return report;
		}

		/// <summary>Looks up the source character's display name from cached client portraits so core_user copies can be identity-scrubbed.</summary>
		private static string ResolveSourceCharacterName(IThumbnailConfiguration configuration)
		{
			if (configuration?.ClientPortraitPaths == null || configuration.AutoSettingsSyncSourceCharacterId <= 0)
			{
				return null;
			}

			foreach (KeyValuePair<string, string> entry in configuration.ClientPortraitPaths)
			{
				if (configuration.TryGetCharacterId(entry.Key, out int characterId)
					&& characterId == configuration.AutoSettingsSyncSourceCharacterId)
				{
					return StripEvePrefix(entry.Key);
				}
			}

			return null;
		}

		private static string StripEvePrefix(string windowTitle)
		{
			if (string.IsNullOrWhiteSpace(windowTitle))
			{
				return windowTitle;
			}

			const string evePrefix = "EVE - ";
			const string frontierPrefix = "EVE Frontier - ";
			if (windowTitle.StartsWith(frontierPrefix, StringComparison.OrdinalIgnoreCase))
			{
				return windowTitle.Substring(frontierPrefix.Length).Trim();
			}

			if (windowTitle.StartsWith(evePrefix, StringComparison.OrdinalIgnoreCase))
			{
				return windowTitle.Substring(evePrefix.Length).Trim();
			}

			return windowTitle;
		}

		public static void AppendLog(string reason, EveSettingsSyncReport report, string skipReason)
		{
			try
			{
				string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-sync.log");
				var lines = new List<string>
				{
					$"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] auto-sync ({reason})"
				};

				if (!string.IsNullOrEmpty(skipReason))
				{
					lines.Add("  skipped: " + skipReason);
				}
				else if (report != null)
				{
					lines.Add($"  synced={report.FilesSynced} backedUp={report.FilesBackedUp}");
					foreach (string action in report.Actions.Take(30))
					{
						lines.Add("  " + action);
					}

					foreach (string warning in report.Warnings.Take(20))
					{
						lines.Add("  !! " + warning);
					}
				}

				File.AppendAllLines(logPath, lines);
			}
			catch
			{
				// Logging must never break the app.
			}
		}
	}
}
