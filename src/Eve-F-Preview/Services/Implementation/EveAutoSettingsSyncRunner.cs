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
			int overrideCount = configuration.AutoSettingsSyncChannelKeysToKeepByDestination?.Count ?? 0;
			string channelsNote = overrideCount > 0
				? $"{overrideCount} destination(s) with custom channel selections"
				: "default channel selection for all destinations";
			return $"Profile: {configuration.AutoSettingsSyncProfileName}, char {configuration.AutoSettingsSyncSourceCharacterId} → {destCount} destination(s), {channelsNote}.";
		}

		public static void SaveProfile(
			IThumbnailConfiguration configuration,
			string settingsProfileName,
			long sourceCharacterId,
			long sourceUserId,
			IEnumerable<long> destinationCharacterIds,
			IEnumerable<long> destinationUserIds)
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

		/// <summary>Channel keys to keep for one destination character - its own override if set, otherwise the default keep list.</summary>
		public static IList<string> GetChannelKeysToKeepForDestination(IThumbnailConfiguration configuration, long destinationCharacterId)
		{
			if (configuration?.AutoSettingsSyncChannelKeysToKeepByDestination != null
				&& destinationCharacterId > 0
				&& configuration.AutoSettingsSyncChannelKeysToKeepByDestination.TryGetValue(destinationCharacterId.ToString(), out List<string> keys))
			{
				return keys ?? new List<string>();
			}

			return configuration?.AutoSettingsSyncChannelKeysToKeep ?? new List<string>();
		}

		public static void SaveChannelKeysToKeepForDestination(IThumbnailConfiguration configuration, long destinationCharacterId, IEnumerable<string> channelKeysToKeep)
		{
			if (configuration == null || destinationCharacterId <= 0)
			{
				return;
			}

			configuration.AutoSettingsSyncChannelKeysToKeepByDestination ??= new Dictionary<string, List<string>>();
			configuration.AutoSettingsSyncChannelKeysToKeepByDestination[destinationCharacterId.ToString()] =
				(channelKeysToKeep ?? Array.Empty<string>())
					.Where(k => !string.IsNullOrWhiteSpace(k))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();
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

			var keepByDest = configuration.AutoSettingsSyncChannelKeysToKeepByDestination;
			if (keepByDest != null && keepByDest.Count > 0)
			{
				List<string> staleKeys = keepByDest.Keys
					.Where(key => !long.TryParse(key, out long id) || !charIds.Contains(id))
					.ToList();
				foreach (string key in staleKeys)
				{
					keepByDest.Remove(key);
				}
			}

			return removed;
		}

		/// <summary>
		/// Resolves channel keys to strip for one destination, from its own keep override (or the
		/// default keep list), falling back to the legacy strip list only if keep was never set at all.
		/// </summary>
		public static IList<string> ResolveChannelKeysToStripForDestination(IThumbnailConfiguration configuration, long destinationCharacterId)
		{
			if (configuration == null || configuration.AutoSettingsSyncSourceCharacterId <= 0)
			{
				return new List<string>();
			}

			bool hasKeep = configuration.AutoSettingsSyncChannelKeysToKeep != null
				&& configuration.AutoSettingsSyncChannelKeysToKeep.Count > 0;
			bool hasDestinationOverride = configuration.AutoSettingsSyncChannelKeysToKeepByDestination != null
				&& destinationCharacterId > 0
				&& configuration.AutoSettingsSyncChannelKeysToKeepByDestination.ContainsKey(destinationCharacterId.ToString());
			bool hasLegacyStrip = configuration.AutoSettingsSyncChannelKeysToStrip != null
				&& configuration.AutoSettingsSyncChannelKeysToStrip.Count > 0;

			// Prefer keep model. Empty keep + no legacy strip => strip all player channels.
			if (hasKeep || hasDestinationOverride || !hasLegacyStrip)
			{
				return EveChatChannelTools.ResolveKeysToStrip(
					configuration.AutoSettingsSyncSourceCharacterId,
					GetChannelKeysToKeepForDestination(configuration, destinationCharacterId),
					configuration.AutoSettingsSyncProfileName);
			}

			return configuration.AutoSettingsSyncChannelKeysToStrip.ToList();
		}

		/// <summary>
		/// Everything an automatic sync needs, copied out of the settings so the file work can run on a
		/// background thread without touching the (UI-thread-owned) configuration.
		/// </summary>
		public sealed class AutoSyncPlan
		{
			internal string ProfileName;
			internal long SourceCharacterId;
			internal long SourceUserId;
			internal string SourceCharacterName;
			internal bool PreserveModuleState;
			internal readonly List<AutoSyncDestination> Destinations = new List<AutoSyncDestination>();
		}

		internal sealed class AutoSyncDestination
		{
			internal long CharacterId;
			/// <summary>The destination's account, when its core_user should be synced in this run; otherwise 0.</summary>
			internal long AccountIdToSync;
			/// <summary>Channels to keep (strip every other player channel), or null to use <see cref="LegacyChannelKeysToStrip"/>.</summary>
			internal List<string> ChannelKeysToKeep;
			internal List<string> LegacyChannelKeysToStrip;
		}

		/// <summary>
		/// UI thread: checks the auto-sync settings, drops destinations that no longer exist (this
		/// changes the settings - the caller saves them), and snapshots what the sync needs.
		/// Returns null with <paramref name="skipReason"/> set when there is nothing to run.
		/// </summary>
		public static AutoSyncPlan TryPrepare(IThumbnailConfiguration configuration, out string skipReason)
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

			PruneMissingDestinations(configuration, configuration.AutoSettingsSyncProfileName);
			if (configuration.AutoSettingsSyncDestinationCharacterIds.Count == 0)
			{
				skipReason = "No valid destinations left in " + configuration.AutoSettingsSyncProfileName + ".";
				return null;
			}

			var plan = new AutoSyncPlan
			{
				ProfileName = configuration.AutoSettingsSyncProfileName,
				SourceCharacterId = configuration.AutoSettingsSyncSourceCharacterId,
				SourceUserId = configuration.AutoSettingsSyncSourceUserId,
				SourceCharacterName = ResolveSourceCharacterName(configuration),
				PreserveModuleState = configuration.PreserveShipModuleStateOnSync
			};

			// Several characters can share the same EVE account - only sync each account's core_user
			// once per run (a repeat pass would back up and rewrite the same file again).
			var accountsAlreadySynced = new HashSet<long>();
			bool hasKeep = configuration.AutoSettingsSyncChannelKeysToKeep != null
				&& configuration.AutoSettingsSyncChannelKeysToKeep.Count > 0;
			bool hasLegacyStrip = configuration.AutoSettingsSyncChannelKeysToStrip != null
				&& configuration.AutoSettingsSyncChannelKeysToStrip.Count > 0;

			foreach (long destinationCharacterId in configuration.AutoSettingsSyncDestinationCharacterIds)
			{
				configuration.TryGetAccountIdForCharacter((int)destinationCharacterId, out int destinationAccountId);
				bool hasDestinationOverride = configuration.AutoSettingsSyncChannelKeysToKeepByDestination != null
					&& configuration.AutoSettingsSyncChannelKeysToKeepByDestination.ContainsKey(destinationCharacterId.ToString());

				// Same rule as ResolveChannelKeysToStripForDestination: prefer the keep model; an empty
				// keep list with no legacy strip list means "strip all player channels".
				bool useKeepModel = hasKeep || hasDestinationOverride || !hasLegacyStrip;

				plan.Destinations.Add(new AutoSyncDestination
				{
					CharacterId = destinationCharacterId,
					AccountIdToSync = destinationAccountId > 0 && accountsAlreadySynced.Add(destinationAccountId) ? destinationAccountId : 0,
					ChannelKeysToKeep = useKeepModel ? GetChannelKeysToKeepForDestination(configuration, destinationCharacterId).ToList() : null,
					LegacyChannelKeysToStrip = useKeepModel ? null : configuration.AutoSettingsSyncChannelKeysToStrip.ToList()
				});
			}

			return plan;
		}

		/// <summary>
		/// Background thread: runs a prepared sync against the EVE settings files. Returns null with
		/// <paramref name="skipReason"/> set when EVE is running; otherwise the sync report.
		/// </summary>
		public static EveSettingsSyncReport Execute(AutoSyncPlan plan, string reason, out string skipReason)
		{
			skipReason = null;

			if (EveSettingsSync.IsEveRunning())
			{
				skipReason = "EVE is still running; auto-sync skipped.";
				return null;
			}

			// Each destination can keep a different set of channels, so sync one destination per run
			// instead of batching them all under one shared ChannelKeysToStrip.
			var report = new EveSettingsSyncReport();
			foreach (AutoSyncDestination destination in plan.Destinations)
			{
				IList<string> channelKeysToStrip = destination.ChannelKeysToKeep != null
					? EveChatChannelTools.ResolveKeysToStrip(plan.SourceCharacterId, destination.ChannelKeysToKeep, plan.ProfileName)
					: destination.LegacyChannelKeysToStrip;

				var options = new EveSettingsSyncOptions
				{
					SourceCharacterId = plan.SourceCharacterId,
					SourceUserId = plan.SourceUserId,
					SourceCharacterName = plan.SourceCharacterName,
					DestinationCharacterIds = new List<long> { destination.CharacterId },
					DestinationUserIds = destination.AccountIdToSync > 0 ? new List<long> { destination.AccountIdToSync } : new List<long>(),
					ChannelKeysToStrip = channelKeysToStrip,
					ProfileName = plan.ProfileName,
					PreserveModuleState = plan.PreserveModuleState,
					Mode = EveSettingsSyncMode.Copy
				};

				EveSettingsSyncReport destinationReport = new EveSettingsSync(options).Run();
				report.Actions.AddRange(destinationReport.Actions);
				report.Warnings.AddRange(destinationReport.Warnings);
				report.FilesSynced += destinationReport.FilesSynced;
				report.FilesBackedUp += destinationReport.FilesBackedUp;
			}

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
