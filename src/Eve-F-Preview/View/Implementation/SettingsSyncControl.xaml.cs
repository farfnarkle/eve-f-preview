using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using EveFPreview.Configuration;
using EveFPreview.Services;

namespace EveFPreview.View
{
	sealed class SettingsSyncCharacterEntry
	{
		public string DisplayName { get; set; }
		public long CharacterId { get; set; }
		public long AccountId { get; set; }

		public string AccountText => this.AccountId > 0 ? $"Account {this.AccountId}" : "Account ID unknown - right-click to set";

		public string Details => $"Character {this.CharacterId} · {this.AccountText}";

		public override string ToString()
		{
			return this.AccountId > 0
				? $"{this.DisplayName}  (char {this.CharacterId}, acct {this.AccountId})"
				: $"{this.DisplayName}  (char {this.CharacterId}, acct unknown - right-click to set)";
		}
	}

	public sealed partial class SettingsSyncControl : UserControl
	{
		private IThumbnailConfiguration _configuration;
		private readonly List<SettingsSyncCharacterEntry> _characters = new List<SettingsSyncCharacterEntry>();
		private List<CheckableItem<SettingsSyncCharacterEntry>> _destinations = new List<CheckableItem<SettingsSyncCharacterEntry>>();
		private List<CheckableItem<EveChatChannelInfo>> _channels = new List<CheckableItem<EveChatChannelInfo>>();
		private bool _suppressPersist;

		public Action PersistConfiguration { get; set; }

		public SettingsSyncControl()
		{
			this.InitializeComponent();
		}

		public void SetConfiguration(IThumbnailConfiguration configuration)
		{
			this._configuration = configuration;

			this._suppressPersist = true;
			try
			{
				this.PreserveModuleStateCheckBox.IsChecked = configuration?.PreserveShipModuleStateOnSync ?? true;
			}
			finally
			{
				this._suppressPersist = false;
			}

			this.RefreshLists();
			this.UpdateAutoSyncProfileLabel();
		}

		private void PreserveModuleStateCheckBox_Changed(object sender, RoutedEventArgs e)
		{
			if (this._suppressPersist || this._configuration == null)
			{
				return;
			}

			this._configuration.PreserveShipModuleStateOnSync = this.PreserveModuleStateCheckBox.IsChecked == true;
			this.PersistConfiguration?.Invoke();
		}

		private string SelectedProfileName => this.ProfileCombo.SelectedItem as string;

		private SettingsSyncCharacterEntry SelectedSource => this.SourceCombo.SelectedItem as SettingsSyncCharacterEntry;

		private SettingsSyncCharacterEntry SelectedDestination => (this.DestinationList.SelectedItem as CheckableItem<SettingsSyncCharacterEntry>)?.Value;

		private List<SettingsSyncCharacterEntry> CheckedDestinations => this._destinations
			.Where(item => item.IsChecked)
			.Select(item => item.Value)
			.ToList();

		private void UpdateAutoSyncProfileLabel()
		{
			if (this._configuration == null)
			{
				this.AutoSyncProfileLabel.Text = string.Empty;
				return;
			}

			string enabled = this._configuration.EnableAutoSettingsSync ? "ON" : "OFF";
			this.AutoSyncProfileLabel.Text =
				$"Auto-sync: {enabled}. {EveAutoSettingsSyncRunner.DescribeProfile(this._configuration)}";
		}

		private void RefreshButton_Click(object sender, RoutedEventArgs e)
		{
			this.RefreshLists();
		}

		public void RefreshLists()
		{
			this.RefreshProfileList();
			this.RefreshCharacterList();
		}

		private void RefreshProfileList()
		{
			this._suppressPersist = true;
			try
			{
				string preferred = this._configuration?.AutoSettingsSyncProfileName;
				List<string> profiles = EveSettingsSync.DiscoverProfileNames().ToList();

				this.ProfileCombo.ItemsSource = profiles;

				int index = -1;
				if (!string.IsNullOrEmpty(preferred))
				{
					index = profiles.FindIndex(profile => string.Equals(profile, preferred, StringComparison.OrdinalIgnoreCase));
				}

				if (index < 0 && profiles.Count > 0)
				{
					// Prefer the profile with the most character files.
					index = 0;
					int bestCount = -1;
					for (int i = 0; i < profiles.Count; i++)
					{
						int count = EveSettingsSync.GetCharacterIdsInProfile(profiles[i]).Count;
						if (count > bestCount)
						{
							bestCount = count;
							index = i;
						}
					}
				}

				this.ProfileCombo.SelectedIndex = index;
			}
			finally
			{
				this._suppressPersist = false;
			}
		}

		private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			string profile = this.SelectedProfileName;
			if (!this._suppressPersist && this._configuration != null && !string.IsNullOrEmpty(profile))
			{
				int pruned = EveAutoSettingsSyncRunner.PruneMissingDestinations(this._configuration, profile);
				this._configuration.AutoSettingsSyncProfileName = profile;
				this.PersistConfiguration?.Invoke();
				if (pruned > 0)
				{
					this.StatusLabel.Text = $"Removed {pruned} missing destination(s) not present in {profile}.";
				}
			}

			this.RefreshCharacterList();
			this.UpdateAutoSyncProfileLabel();
		}

		public void RefreshCharacterList()
		{
			this._suppressPersist = true;
			try
			{
				string profile = this.SelectedProfileName;
				long preferredSourceId = this._configuration?.AutoSettingsSyncSourceCharacterId ?? 0;

				this._characters.Clear();
				this.SourceCombo.ItemsSource = null;

				if (string.IsNullOrEmpty(profile))
				{
					this.RefreshDestinationList();
					this.LoadChannelsForSelectedDestination();
					this.StatusLabel.Text = "No EVE settings profiles found under LocalAppData\\CCP\\EVE.";
					this.UpdateSyncEnabled();
					return;
				}

				Dictionary<long, string> namesById = this.BuildCharacterDisplayNames();
				HashSet<long> idsInProfile = EveSettingsSync.GetCharacterIdsInProfile(profile);

				foreach (long characterId in idsInProfile)
				{
					namesById.TryGetValue(characterId, out string displayName);
					int accountId = 0;
					this._configuration?.TryGetAccountIdForCharacter((int)characterId, out accountId);
					this._characters.Add(new SettingsSyncCharacterEntry
					{
						DisplayName = string.IsNullOrEmpty(displayName) ? "Character " + characterId : displayName,
						CharacterId = characterId,
						AccountId = accountId
					});
				}

				this._characters.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

				this.SourceCombo.ItemsSource = this._characters.ToList();

				int sourceIndex = -1;
				if (preferredSourceId > 0)
				{
					sourceIndex = this._characters.FindIndex(character => character.CharacterId == preferredSourceId);
				}

				if (sourceIndex < 0 && this._characters.Count > 0)
				{
					sourceIndex = 0;
				}

				this.SourceCombo.SelectedIndex = sourceIndex;

				this.RefreshDestinationList();
				this.LoadChannelsForSelectedDestination();

				int pruned = 0;
				if (this._configuration != null)
				{
					pruned = EveAutoSettingsSyncRunner.PruneMissingDestinations(this._configuration, profile);
					if (pruned > 0)
					{
						this.PersistConfiguration?.Invoke();
						this.RefreshDestinationList();
					}
				}

				this.StatusLabel.Text = this._characters.Count == 0
					? $"No characters in {profile}. Log those clients in on this settings profile once."
					: $"{this._characters.Count} character(s) in {profile}."
					  + (pruned > 0 ? $" Removed {pruned} missing destination(s)." : "")
					  + " Check a destination to include it in Sync; click one to edit its kept channels.";
				this.UpdateAutoSyncProfileLabel();
				this.UpdateSyncEnabled();
			}
			finally
			{
				this._suppressPersist = false;
			}
		}

		private Dictionary<long, string> BuildCharacterDisplayNames()
		{
			var names = new Dictionary<long, string>();
			if (this._configuration?.ClientPortraitPaths == null)
			{
				return names;
			}

			foreach (KeyValuePair<string, string> entry in this._configuration.ClientPortraitPaths)
			{
				if (!this._configuration.TryGetCharacterId(entry.Key, out int characterId) || characterId <= 0)
				{
					continue;
				}

				names[characterId] = StripEvePrefix(entry.Key);
			}

			return names;
		}

		private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			this.RefreshDestinationList();
			this.LoadChannelsForSelectedDestination();
			this.UpdateSyncEnabled();

			SettingsSyncCharacterEntry selected = this.SelectedSource;
			if (!this._suppressPersist && selected != null && selected.CharacterId > 0 && this._configuration != null)
			{
				EveAutoSettingsSyncRunner.SaveSourceSelection(
					this._configuration,
					this.SelectedProfileName,
					selected.CharacterId,
					selected.AccountId);
				this.PersistConfiguration?.Invoke();
				this.UpdateAutoSyncProfileLabel();
			}
		}

		private void SetSourceAccountIdButton_Click(object sender, RoutedEventArgs e)
		{
			this.EditAccountId(this.SelectedSource);
		}

		private void SetDestinationAccountIdMenuItem_Click(object sender, RoutedEventArgs e)
		{
			this.EditAccountId(this.SelectedDestination);
		}

		private void EditAccountId(SettingsSyncCharacterEntry selected)
		{
			if (selected == null || this._configuration == null)
			{
				return;
			}

			var dialog = new SettingsSyncAccountIdDialog(selected.DisplayName, selected.AccountId)
			{
				Owner = Window.GetWindow(this)
			};

			if (dialog.ShowDialog() != true)
			{
				return;
			}

			this._configuration.SetCharacterAccount((int)selected.CharacterId, dialog.AccountId);
			this.PersistConfiguration?.Invoke();
			this.RefreshCharacterList();
		}

		/// <summary>Rebuilds the destination checklist from the current character set, excluding the selected source, preserving check state and the currently-edited selection where possible.</summary>
		private void RefreshDestinationList()
		{
			SettingsSyncCharacterEntry selectedSource = this.SelectedSource;
			var configuredDestinationIds = new HashSet<long>(this._configuration?.AutoSettingsSyncDestinationCharacterIds ?? new List<long>());
			SettingsSyncCharacterEntry previouslyEditingDestination = this.SelectedDestination;

			bool wasSuppressed = this._suppressPersist;
			this._suppressPersist = true;
			try
			{
				this._destinations = this._characters
					.Where(character => selectedSource == null || character.CharacterId != selectedSource.CharacterId)
					.Select(character => new CheckableItem<SettingsSyncCharacterEntry>(
						character,
						character.ToString(),
						configuredDestinationIds.Contains(character.CharacterId),
						this.DestinationChecked))
					.ToList();
				this.DestinationList.ItemsSource = this._destinations;

				int restoreIndex = -1;
				if (previouslyEditingDestination != null)
				{
					restoreIndex = this._destinations.FindIndex(item => item.Value.CharacterId == previouslyEditingDestination.CharacterId);
				}

				if (restoreIndex < 0)
				{
					restoreIndex = this._destinations.FindIndex(item => item.IsChecked);
				}

				this.DestinationList.SelectedIndex = restoreIndex;
			}
			finally
			{
				this._suppressPersist = wasSuppressed;
			}
		}

		private void DestinationChecked(CheckableItem<SettingsSyncCharacterEntry> item)
		{
			if (this._suppressPersist)
			{
				return;
			}

			this.PersistDestinationSelection();
		}

		private void PersistDestinationSelection()
		{
			if (this._suppressPersist || this._configuration == null)
			{
				return;
			}

			List<SettingsSyncCharacterEntry> checkedDestinations = this.CheckedDestinations;

			this._configuration.AutoSettingsSyncDestinationCharacterIds = checkedDestinations
				.Select(d => d.CharacterId)
				.Distinct()
				.ToList();
			this._configuration.AutoSettingsSyncDestinationUserIds = checkedDestinations
				.Where(d => d.AccountId > 0)
				.Select(d => d.AccountId)
				.Distinct()
				.ToList();

			this.PersistConfiguration?.Invoke();
			this.UpdateAutoSyncProfileLabel();
			this.UpdateSyncEnabled();
		}

		private void DestinationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			this.LoadChannelsForSelectedDestination();
		}

		private void ChannelChecked(CheckableItem<EveChatChannelInfo> item)
		{
			if (this._suppressPersist)
			{
				return;
			}

			this.PersistChannelKeepSelection();
		}

		private void PersistChannelKeepSelection()
		{
			if (this._suppressPersist || this._configuration == null)
			{
				return;
			}

			SettingsSyncCharacterEntry destination = this.SelectedDestination;
			if (destination == null)
			{
				return;
			}

			EveAutoSettingsSyncRunner.SaveChannelKeysToKeepForDestination(
				this._configuration, destination.CharacterId, this.GetSelectedChannelKeysToKeep());
			this.PersistConfiguration?.Invoke();
			this.UpdateAutoSyncProfileLabel();
		}

		/// <summary>
		/// Channel names always come from the source character (whose core_char is what actually
		/// gets copied); which ones are checked comes from the selected destination's own keep list
		/// (or the shared default if that destination has no override yet).
		/// </summary>
		private void LoadChannelsForSelectedDestination()
		{
			bool wasSuppressed = this._suppressPersist;
			this._suppressPersist = true;
			try
			{
				this._channels = new List<CheckableItem<EveChatChannelInfo>>();
				this.ChannelList.ItemsSource = this._channels;

				SettingsSyncCharacterEntry source = this.SelectedSource;
				if (source == null || source.CharacterId <= 0)
				{
					this.ChannelLabel.Text = "Channels to keep on copy";
					return;
				}

				SettingsSyncCharacterEntry destination = this.SelectedDestination;
				if (destination == null)
				{
					this.ChannelLabel.Text = "Channels to keep (select a destination above)";
					return;
				}

				string path = EveChatChannelTools.FindNewestCoreCharPath(
					source.CharacterId, profileName: this.SelectedProfileName);
				if (string.IsNullOrEmpty(path))
				{
					this.ChannelLabel.Text = "Channels to keep (no core_char file found for source)";
					return;
				}

				try
				{
					IList<EveChatChannelInfo> channels = EveChatChannelTools.ListChannels(path)
						.Where(c => !c.IsBuiltin)
						.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
						.ToList();

					this.ChannelLabel.Text = channels.Count == 0
						? $"Channels to keep for {destination.DisplayName} (none / only builtins)"
						: $"Channels to keep for {destination.DisplayName} ({channels.Count})";

					HashSet<string> keepKeys = new HashSet<string>(
						EveAutoSettingsSyncRunner.GetChannelKeysToKeepForDestination(this._configuration, destination.CharacterId),
						StringComparer.OrdinalIgnoreCase);

					this._channels = channels
						.Select(channel => new CheckableItem<EveChatChannelInfo>(
							channel,
							channel.ToString(),
							!string.IsNullOrEmpty(channel.Key) && keepKeys.Contains(channel.Key),
							this.ChannelChecked))
						.ToList();
					this.ChannelList.ItemsSource = this._channels;
				}
				catch (Exception ex)
				{
					this.ChannelLabel.Text = "Channels to keep (failed to read)";
					this.StatusLabel.Text = "Could not read channels: " + ex.Message;
				}
			}
			finally
			{
				this._suppressPersist = wasSuppressed;
			}
		}

		private IList<string> GetSelectedChannelKeysToKeep()
		{
			return this._channels
				.Where(item => item.IsChecked)
				.Select(item => item.Value.Key)
				.Where(k => !string.IsNullOrEmpty(k))
				.ToList();
		}

		private Window GetDialogOwner()
		{
			return Window.GetWindow(this);
		}

		private void UpdateSyncEnabled()
		{
			SettingsSyncCharacterEntry source = this.SelectedSource;
			bool hasDestination = this._destinations.Any(item => item.IsChecked);
			this.SyncButton.IsEnabled = !string.IsNullOrEmpty(this.SelectedProfileName)
				&& source != null
				&& source.CharacterId > 0
				&& source.AccountId > 0
				&& hasDestination;
		}

		private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
		{
			Window owner = this.GetDialogOwner();
			string profile = this.SelectedProfileName;
			string path = !string.IsNullOrEmpty(profile)
				? EveSettingsSync.FindProfileDirectory(profile)
				: EveSettingsSync.GetEveDataRoot();

			if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
			{
				MessageBox.Show(owner,
					"Could not find the EVE settings folder." +
					(string.IsNullOrEmpty(profile) ? "" : $"\n\nProfile: {profile}"),
					"Settings Sync",
					MessageBoxButton.OK,
					MessageBoxImage.Information);
				return;
			}

			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = path,
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				MessageBox.Show(owner, "Could not open folder:\n" + ex.Message, "Settings Sync",
					MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}

		private void DeleteBackupsButton_Click(object sender, RoutedEventArgs e)
		{
			Window owner = this.GetDialogOwner();
			string profile = this.SelectedProfileName;
			if (string.IsNullOrEmpty(profile))
			{
				MessageBox.Show(owner, "Select a settings profile first.", "Settings Sync",
					MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}

			int count = EveSettingsSync.CountBackupFiles(profile);
			if (count == 0)
			{
				MessageBox.Show(owner, $"No sync backups found in {profile}.", "Delete backups",
					MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}

			MessageBoxResult confirm = MessageBox.Show(owner,
				$"Delete {count} sync backup file(s) in {profile}?\n\n" +
				"This removes *_sync_backup_N.dat and *_sync_auto_backup*.dat only.\n" +
				"Live core_char / core_user settings are not deleted.",
				"Delete backups",
				MessageBoxButton.OKCancel,
				MessageBoxImage.Warning);
			if (confirm != MessageBoxResult.OK)
			{
				return;
			}

			EveSettingsSyncReport report = EveSettingsSync.DeleteBackups(profile);
			this.StatusLabel.Text = report.Warnings.Count > 0
				? $"Deleted {report.FilesBackedUp} backup(s), {report.Warnings.Count} error(s)."
				: $"Deleted {report.FilesBackedUp} backup(s) in {profile}.";

			if (report.Warnings.Count > 0)
			{
				MessageBox.Show(owner,
					$"Deleted: {report.FilesBackedUp}\n\nErrors:\n" + string.Join(Environment.NewLine, report.Warnings.Take(20)),
					"Delete backups",
					MessageBoxButton.OK,
					MessageBoxImage.Warning);
			}
			else
			{
				MessageBox.Show(owner, $"Deleted {report.FilesBackedUp} backup file(s).", "Delete backups",
					MessageBoxButton.OK, MessageBoxImage.Information);
			}
		}

		private void BackupButton_Click(object sender, RoutedEventArgs e)
		{
			Window owner = this.GetDialogOwner();
			if (EveSettingsSync.IsEveRunning())
			{
				MessageBox.Show(owner,
					"EVE appears to be running. Close all clients and the launcher before backing up — they rewrite settings on exit.",
					"Settings Sync",
					MessageBoxButton.OK,
					MessageBoxImage.Warning);
				return;
			}

			MessageBoxResult confirm = MessageBox.Show(owner,
				"Create a new numbered backup of every core_char_*.dat and core_user_*.dat under LocalAppData\\CCP\\EVE?\n\n" +
				"Manual backups are never overwritten (*_sync_backup_1.dat, _2.dat, …).\n" +
				"Sync keeps up to 5 dated auto-backups per file (*_sync_auto_backup_yyyyMMdd_HHmmss.dat) and deletes older ones.",
				"Back up settings",
				MessageBoxButton.OKCancel,
				MessageBoxImage.Question);
			if (confirm != MessageBoxResult.OK)
			{
				return;
			}

			EveSettingsSyncReport report = EveSettingsSync.BackupAll(mode: EveSettingsBackupMode.Manual);
			this.ShowReport("Backup complete", report);
		}

		private void SyncButton_Click(object sender, RoutedEventArgs e)
		{
			Window owner = this.GetDialogOwner();
			string profile = this.SelectedProfileName;
			SettingsSyncCharacterEntry source = this.SelectedSource;
			if (source == null || string.IsNullOrEmpty(profile))
			{
				return;
			}

			if (source.AccountId <= 0)
			{
				MessageBox.Show(owner,
					"This character has no known account ID yet. Right-click it to set one, or log that client in once so it can be detected automatically.",
					"Settings Sync",
					MessageBoxButton.OK,
					MessageBoxImage.Information);
				return;
			}

			List<SettingsSyncCharacterEntry> destinations = this.CheckedDestinations;

			if (destinations.Count == 0)
			{
				MessageBox.Show(owner, "Check at least one destination character below.", "Settings Sync",
					MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}

			if (EveSettingsSync.IsEveRunning())
			{
				MessageBox.Show(owner,
					"EVE appears to be running. Close all clients and the launcher before syncing — they rewrite settings on exit and will stomp your copies.",
					"Settings Sync",
					MessageBoxButton.OK,
					MessageBoxImage.Warning);
				return;
			}

			var missingAccount = destinations.Where(d => d.AccountId <= 0).ToList();
			if (missingAccount.Count > 0)
			{
				MessageBox.Show(owner,
					"These destinations have no account ID and cannot receive account (core_user) settings:\n\n" +
					string.Join("\n", missingAccount.Select(d => d.DisplayName)),
					"Settings Sync",
					MessageBoxButton.OK,
					MessageBoxImage.Warning);
			}

			// Each destination keeps its own channel selection, so sync one destination at a time
			// instead of batching them all under one shared set of channels to strip. Several
			// characters can share the same EVE account, though - only sync each account's
			// core_user once per click, or the second pass re-backs-up the same file within the
			// same second and collides with the first pass's timestamped backup name.
			var aggregate = new EveSettingsSyncReport();
			var accountsAlreadySynced = new HashSet<long>();

			foreach (SettingsSyncCharacterEntry destination in destinations)
			{
				IList<string> channelsToKeep = EveAutoSettingsSyncRunner.GetChannelKeysToKeepForDestination(this._configuration, destination.CharacterId);
				IList<string> channelsToStrip = EveChatChannelTools.ResolveKeysToStrip(source.CharacterId, channelsToKeep, profile);

				bool syncAccountThisPass = destination.AccountId > 0 && accountsAlreadySynced.Add(destination.AccountId);

				var options = new EveSettingsSyncOptions
				{
					SourceCharacterId = source.CharacterId,
					SourceUserId = source.AccountId,
					SourceCharacterName = source.DisplayName,
					DestinationCharacterIds = new List<long> { destination.CharacterId },
					DestinationUserIds = syncAccountThisPass ? new List<long> { destination.AccountId } : new List<long>(),
					ChannelKeysToStrip = channelsToStrip,
					ProfileName = profile,
					PreserveModuleState = this.PreserveModuleStateCheckBox.IsChecked == true,
					Mode = EveSettingsSyncMode.Copy
				};

				EveSettingsSyncReport destinationReport = new EveSettingsSync(options).Run();
				aggregate.Actions.AddRange(destinationReport.Actions);
				aggregate.Warnings.AddRange(destinationReport.Warnings);
				aggregate.FilesSynced += destinationReport.FilesSynced;
				aggregate.FilesBackedUp += destinationReport.FilesBackedUp;

				if (this._configuration != null)
				{
					EveAutoSettingsSyncRunner.SaveChannelKeysToKeepForDestination(this._configuration, destination.CharacterId, channelsToKeep);
				}
			}

			if (this._configuration != null && aggregate.FilesSynced > 0)
			{
				EveAutoSettingsSyncRunner.SaveProfile(
					this._configuration,
					profile,
					source.CharacterId,
					source.AccountId,
					destinations.Select(d => d.CharacterId),
					destinations.Where(d => d.AccountId > 0).Select(d => d.AccountId));
				this.PersistConfiguration?.Invoke();
				this.UpdateAutoSyncProfileLabel();
			}

			this.ShowReport("Sync complete", aggregate);
		}

		private void ShowReport(string title, EveSettingsSyncReport report)
		{
			var lines = new List<string>
			{
				$"Synced: {report.FilesSynced}",
				$"Backed up: {report.FilesBackedUp}"
			};

			if (report.Warnings.Count > 0)
			{
				lines.Add("");
				lines.Add("Errors:");
				lines.AddRange(report.Warnings.Take(30));
				if (report.Warnings.Count > 30)
				{
					lines.Add($"… and {report.Warnings.Count - 30} more");
				}
			}

			this.StatusLabel.Text = report.Warnings.Count > 0
				? $"{title}: {report.FilesSynced} synced, {report.Warnings.Count} error(s)."
				: $"{title}: {report.FilesSynced} synced, {report.FilesBackedUp} backed up.";

			MessageBox.Show(this.GetDialogOwner(), string.Join(Environment.NewLine, lines), title,
				MessageBoxButton.OK,
				report.Warnings.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
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
	}
}
