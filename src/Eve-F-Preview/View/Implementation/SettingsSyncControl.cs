using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using EveFPreview.Configuration;
using EveFPreview.Services;

namespace EveFPreview.View
{
	sealed class SettingsSyncCharacterEntry
	{
		public string DisplayName { get; set; }
		public long CharacterId { get; set; }
		public long AccountId { get; set; }

		public override string ToString()
		{
			if (this.AccountId > 0)
			{
				return $"{this.DisplayName}  (char {this.CharacterId}, acct {this.AccountId})";
			}

			return $"{this.DisplayName}  (char {this.CharacterId}, acct unknown)";
		}
	}

	sealed class SettingsSyncControl : UserControl
	{
		private IThumbnailConfiguration _configuration;
		private readonly Button _backupButton;
		private readonly Button _openFolderButton;
		private readonly Button _deleteBackupsButton;
		private readonly Label _profileLabel;
		private readonly ComboBox _profileCombo;
		private readonly Button _refreshButton;
		private readonly Button _syncButton;
		private readonly ListBox _characterList;
		private readonly CheckedListBox _channelList;
		private readonly Label _channelLabel;
		private readonly CheckBox _preserveModuleStateCheckBox;
		private readonly Label _statusLabel;
		private readonly Label _autoSyncProfileLabel;
		private readonly List<SettingsSyncCharacterEntry> _characters = new List<SettingsSyncCharacterEntry>();
		private bool _suppressPersist;

		public Action PersistConfiguration { get; set; }

		public SettingsSyncControl()
		{
			this.SuspendLayout();
			this.AutoScaleMode = AutoScaleMode.Inherit;

			var panel = new Panel
			{
				BorderStyle = BorderStyle.FixedSingle,
				Dock = DockStyle.Fill,
				Margin = new Padding(4),
				AutoScroll = true
			};

			this._backupButton = new Button
			{
				Text = "Back up settings"
			};
			this._backupButton.Click += this.BackupButton_Click;

			this._openFolderButton = new Button
			{
				Text = "Open folder"
			};
			this._openFolderButton.Click += this.OpenFolderButton_Click;

			this._deleteBackupsButton = new Button
			{
				Text = "Delete backups"
			};
			this._deleteBackupsButton.Click += this.DeleteBackupsButton_Click;

			this._profileLabel = new Label
			{
				AutoSize = true,
				Text = "Settings profile"
			};

			this._profileCombo = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList
			};
			this._profileCombo.SelectedIndexChanged += this.ProfileCombo_SelectedIndexChanged;

			var listLabel = new Label
			{
				AutoSize = true,
				Text = "Source character"
			};

			this._characterList = new ListBox
			{
				IntegralHeight = false
			};
			this._characterList.SelectedIndexChanged += this.CharacterList_SelectedIndexChanged;

			this._channelLabel = new Label
			{
				AutoSize = true,
				Text = "Channels to keep on copy"
			};

			this._channelList = new CheckedListBox
			{
				IntegralHeight = false,
				CheckOnClick = true
			};
			this._channelList.ItemCheck += this.ChannelList_ItemCheck;

			this._preserveModuleStateCheckBox = new CheckBox
			{
				AutoSize = true,
				Text = "Keep each alt's own ship module layout",
				Checked = true
			};
			this._preserveModuleStateCheckBox.CheckedChanged += this.PreserveModuleStateCheckBox_CheckedChanged;

			this._refreshButton = new Button
			{
				Text = "Refresh list"
			};
			this._refreshButton.Click += (_, __) => this.RefreshLists();

			this._syncButton = new Button
			{
				Text = "Sync…",
				Enabled = false
			};
			this._syncButton.Click += this.SyncButton_Click;

			this._autoSyncProfileLabel = new Label
			{
				AutoSize = true,
				MaximumSize = new Size(360, 0)
			};

			this._statusLabel = new Label
			{
				AutoSize = true,
				ForeColor = SystemColors.GrayText,
				MaximumSize = new Size(360, 0)
			};

			TableLayoutPanel table = SettingsHelp.CreateScrollTable();
			SettingsHelp.AddRow(table, SettingsHelp.CreateFlow(this._backupButton, this._openFolderButton));
			SettingsHelp.AddFullWidthButton(table, this._deleteBackupsButton);
			SettingsHelp.AddRow(table, this._profileLabel, SettingsHelp.Text.SettingsProfile);
			SettingsHelp.AddRow(table, this._profileCombo);
			SettingsHelp.AddRow(table, listLabel);
			SettingsHelp.AddFixedHeight(table, this._characterList, 110);
			SettingsHelp.AddRow(table, this._channelLabel, SettingsHelp.Text.ChannelsToKeep);
			SettingsHelp.AddFixedHeight(table, this._channelList, 150);
			SettingsHelp.AddRow(table, this._preserveModuleStateCheckBox, SettingsHelp.Text.PreserveModuleLayout);
			SettingsHelp.AddRow(table, SettingsHelp.CreateFlow(this._refreshButton, this._syncButton));
			SettingsHelp.AddRow(table, this._statusLabel);
			SettingsHelp.AddRow(table, this._autoSyncProfileLabel);

			SettingsHelp.HostInScrollPanel(panel, table);
			this.Controls.Add(panel);

			this.ResumeLayout(false);
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			this.ApplyScaledButtonSizes();
		}

		protected override void OnDpiChangedAfterParent(EventArgs e)
		{
			base.OnDpiChangedAfterParent(e);
			this.ApplyScaledButtonSizes();
		}

		private void ApplyScaledButtonSizes()
		{
			SettingsHelp.ApplyScaledButtonSizes(
				this,
				this._backupButton,
				this._openFolderButton,
				this._deleteBackupsButton,
				this._refreshButton,
				this._syncButton);
		}

		public void SetConfiguration(IThumbnailConfiguration configuration)
		{
			this._configuration = configuration;

			this._suppressPersist = true;
			try
			{
				this._preserveModuleStateCheckBox.Checked = configuration?.PreserveShipModuleStateOnSync ?? true;
			}
			finally
			{
				this._suppressPersist = false;
			}

			this.RefreshLists();
			this.UpdateAutoSyncProfileLabel();
		}

		private void PreserveModuleStateCheckBox_CheckedChanged(object sender, EventArgs e)
		{
			if (this._suppressPersist || this._configuration == null)
			{
				return;
			}

			this._configuration.PreserveShipModuleStateOnSync = this._preserveModuleStateCheckBox.Checked;
			this.PersistConfiguration?.Invoke();
		}

		private string SelectedProfileName => this._profileCombo.SelectedItem as string;

		private void UpdateAutoSyncProfileLabel()
		{
			if (this._configuration == null)
			{
				this._autoSyncProfileLabel.Text = string.Empty;
				return;
			}

			string enabled = this._configuration.EnableAutoSettingsSync ? "ON" : "OFF";
			this._autoSyncProfileLabel.Text =
				$"Auto-sync: {enabled}. {EveAutoSettingsSyncRunner.DescribeProfile(this._configuration)}";
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
				IList<string> profiles = EveSettingsSync.DiscoverProfileNames();

				this._profileCombo.Items.Clear();
				foreach (string profile in profiles)
				{
					this._profileCombo.Items.Add(profile);
				}

				int index = -1;
				if (!string.IsNullOrEmpty(preferred))
				{
					for (int i = 0; i < this._profileCombo.Items.Count; i++)
					{
						if (string.Equals((string)this._profileCombo.Items[i], preferred, StringComparison.OrdinalIgnoreCase))
						{
							index = i;
							break;
						}
					}
				}

				if (index < 0 && this._profileCombo.Items.Count > 0)
				{
					// Prefer the profile with the most character files.
					index = 0;
					int bestCount = -1;
					for (int i = 0; i < this._profileCombo.Items.Count; i++)
					{
						string name = (string)this._profileCombo.Items[i];
						int count = EveSettingsSync.GetCharacterIdsInProfile(name).Count;
						if (count > bestCount)
						{
							bestCount = count;
							index = i;
						}
					}
				}

				if (index >= 0)
				{
					this._profileCombo.SelectedIndex = index;
				}
			}
			finally
			{
				this._suppressPersist = false;
			}
		}

		private void ProfileCombo_SelectedIndexChanged(object sender, EventArgs e)
		{
			string profile = this.SelectedProfileName;
			if (!this._suppressPersist && this._configuration != null && !string.IsNullOrEmpty(profile))
			{
				int pruned = EveAutoSettingsSyncRunner.PruneMissingDestinations(this._configuration, profile);
				this._configuration.AutoSettingsSyncProfileName = profile;
				this.PersistConfiguration?.Invoke();
				if (pruned > 0)
				{
					this._statusLabel.Text = $"Removed {pruned} missing destination(s) not present in {profile}.";
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
				long preferredId = this._configuration?.AutoSettingsSyncSourceCharacterId ?? 0;

				this._characters.Clear();
				this._characterList.Items.Clear();

				if (string.IsNullOrEmpty(profile))
				{
					this.LoadChannelsForCharacter(null);
					this._statusLabel.Text = "No EVE settings profiles found under LocalAppData\\CCP\\EVE.";
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

				this._characters.Sort((a, b) =>
				{
					int preferred = (b.CharacterId == preferredId).CompareTo(a.CharacterId == preferredId);
					if (preferred != 0)
					{
						return preferred;
					}

					return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
				});

				foreach (SettingsSyncCharacterEntry character in this._characters)
				{
					this._characterList.Items.Add(character);
				}

				int selectedIndex = -1;
				if (preferredId > 0)
				{
					for (int i = 0; i < this._characterList.Items.Count; i++)
					{
						if (((SettingsSyncCharacterEntry)this._characterList.Items[i]).CharacterId == preferredId)
						{
							selectedIndex = i;
							break;
						}
					}
				}

				if (selectedIndex >= 0)
				{
					this._characterList.SelectedIndex = selectedIndex;
				}
				else
				{
					this.LoadChannelsForCharacter(null);
				}

				int pruned = 0;
				if (this._configuration != null)
				{
					pruned = EveAutoSettingsSyncRunner.PruneMissingDestinations(this._configuration, profile);
					if (pruned > 0)
					{
						this.PersistConfiguration?.Invoke();
					}
				}

				this._statusLabel.Text = this._characters.Count == 0
					? $"No characters in {profile}. Log those clients in on this settings profile once."
					: $"{this._characters.Count} character(s) in {profile}."
					  + (pruned > 0 ? $" Removed {pruned} missing destination(s)." : "")
					  + " Checked channels are kept; unchecked are stripped.";
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

		private void CharacterList_SelectedIndexChanged(object sender, EventArgs e)
		{
			var selected = this._characterList.SelectedItem as SettingsSyncCharacterEntry;
			this.LoadChannelsForCharacter(selected);
			this.UpdateSyncEnabled();

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

		private void ChannelList_ItemCheck(object sender, ItemCheckEventArgs e)
		{
			if (this._suppressPersist)
			{
				return;
			}

			this.BeginInvoke(new Action(this.PersistChannelKeepSelection));
		}

		private void PersistChannelKeepSelection()
		{
			if (this._suppressPersist || this._configuration == null)
			{
				return;
			}

			EveAutoSettingsSyncRunner.SaveChannelKeysToKeep(this._configuration, this.GetSelectedChannelKeysToKeep());
			this.PersistConfiguration?.Invoke();
			this.UpdateAutoSyncProfileLabel();
		}

		private void LoadChannelsForCharacter(SettingsSyncCharacterEntry character)
		{
			this._suppressPersist = true;
			try
			{
				this._channelList.Items.Clear();
				if (character == null || character.CharacterId <= 0)
				{
					this._channelLabel.Text = "Channels to keep on copy";
					return;
				}

				string path = EveChatChannelTools.FindNewestCoreCharPath(
					character.CharacterId, profileName: this.SelectedProfileName);
				if (string.IsNullOrEmpty(path))
				{
					this._channelLabel.Text = "Channels to keep (no core_char file found)";
					return;
				}

				try
				{
					IList<EveChatChannelInfo> channels = EveChatChannelTools.ListChannels(path)
						.Where(c => !c.IsBuiltin)
						.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
						.ToList();

					this._channelLabel.Text = channels.Count == 0
						? "Channels to keep (none / only builtins)"
						: $"Channels to keep on copy ({channels.Count})";

					HashSet<string> keepKeys = this.ResolveKeepKeysForUi(channels);
					foreach (EveChatChannelInfo channel in channels)
					{
						bool keep = !string.IsNullOrEmpty(channel.Key) && keepKeys.Contains(channel.Key);
						this._channelList.Items.Add(channel, keep);
					}
				}
				catch (Exception ex)
				{
					this._channelLabel.Text = "Channels to keep (failed to read)";
					this._statusLabel.Text = "Could not read channels: " + ex.Message;
				}
			}
			finally
			{
				this._suppressPersist = false;
			}
		}

		private HashSet<string> ResolveKeepKeysForUi(IList<EveChatChannelInfo> channels)
		{
			var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (this._configuration == null)
			{
				return keep;
			}

			IList<string> savedKeep = this._configuration.AutoSettingsSyncChannelKeysToKeep ?? new List<string>();
			IList<string> legacyStrip = this._configuration.AutoSettingsSyncChannelKeysToStrip ?? new List<string>();

			if (savedKeep.Count > 0 || legacyStrip.Count == 0)
			{
				foreach (string key in savedKeep)
				{
					if (!string.IsNullOrEmpty(key))
					{
						keep.Add(key);
					}
				}

				return keep;
			}

			var strip = new HashSet<string>(legacyStrip.Where(k => !string.IsNullOrEmpty(k)), StringComparer.OrdinalIgnoreCase);
			foreach (EveChatChannelInfo channel in channels)
			{
				if (!string.IsNullOrEmpty(channel.Key) && !strip.Contains(channel.Key))
				{
					keep.Add(channel.Key);
				}
			}

			EveAutoSettingsSyncRunner.SaveChannelKeysToKeep(this._configuration, keep);
			this.PersistConfiguration?.Invoke();
			return keep;
		}

		private IList<string> GetSelectedChannelKeysToKeep()
		{
			return this._channelList.CheckedItems
				.Cast<EveChatChannelInfo>()
				.Select(c => c.Key)
				.Where(k => !string.IsNullOrEmpty(k))
				.ToList();
		}

		private IWin32Window GetDialogOwner()
		{
			return this.FindForm() ?? (IWin32Window)this;
		}

		private void UpdateSyncEnabled()
		{
			var selected = this._characterList.SelectedItem as SettingsSyncCharacterEntry;
			this._syncButton.Enabled = !string.IsNullOrEmpty(this.SelectedProfileName)
				&& selected != null
				&& selected.CharacterId > 0
				&& selected.AccountId > 0;
		}

		private void OpenFolderButton_Click(object sender, EventArgs e)
		{
			IWin32Window owner = this.GetDialogOwner();
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
					MessageBoxButtons.OK,
					MessageBoxIcon.Information);
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
					MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		private void DeleteBackupsButton_Click(object sender, EventArgs e)
		{
			IWin32Window owner = this.GetDialogOwner();
			string profile = this.SelectedProfileName;
			if (string.IsNullOrEmpty(profile))
			{
				MessageBox.Show(owner, "Select a settings profile first.", "Settings Sync",
					MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			int count = EveSettingsSync.CountBackupFiles(profile);
			if (count == 0)
			{
				MessageBox.Show(owner, $"No sync backups found in {profile}.", "Delete backups",
					MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			DialogResult confirm = MessageBox.Show(owner,
				$"Delete {count} sync backup file(s) in {profile}?\n\n" +
				"This removes *_sync_backup_N.dat and *_sync_auto_backup*.dat only.\n" +
				"Live core_char / core_user settings are not deleted.",
				"Delete backups",
				MessageBoxButtons.OKCancel,
				MessageBoxIcon.Warning);
			if (confirm != DialogResult.OK)
			{
				return;
			}

			EveSettingsSyncReport report = EveSettingsSync.DeleteBackups(profile);
			this._statusLabel.Text = report.Warnings.Count > 0
				? $"Deleted {report.FilesBackedUp} backup(s), {report.Warnings.Count} error(s)."
				: $"Deleted {report.FilesBackedUp} backup(s) in {profile}.";

			if (report.Warnings.Count > 0)
			{
				MessageBox.Show(owner,
					$"Deleted: {report.FilesBackedUp}\n\nErrors:\n" + string.Join(Environment.NewLine, report.Warnings.Take(20)),
					"Delete backups",
					MessageBoxButtons.OK,
					MessageBoxIcon.Warning);
			}
			else
			{
				MessageBox.Show(owner, $"Deleted {report.FilesBackedUp} backup file(s).", "Delete backups",
					MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private void BackupButton_Click(object sender, EventArgs e)
		{
			IWin32Window owner = this.GetDialogOwner();
			if (EveSettingsSync.IsEveRunning())
			{
				MessageBox.Show(owner,
					"EVE appears to be running. Close all clients and the launcher before backing up — they rewrite settings on exit.",
					"Settings Sync",
					MessageBoxButtons.OK,
					MessageBoxIcon.Warning);
				return;
			}

			DialogResult confirm = MessageBox.Show(owner,
				"Create a new numbered backup of every core_char_*.dat and core_user_*.dat under LocalAppData\\CCP\\EVE?\n\n" +
				"Manual backups are never overwritten (*_sync_backup_1.dat, _2.dat, …).\n" +
				"Sync keeps up to 5 dated auto-backups per file (*_sync_auto_backup_yyyyMMdd_HHmmss.dat) and deletes older ones.",
				"Back up settings",
				MessageBoxButtons.OKCancel,
				MessageBoxIcon.Question);
			if (confirm != DialogResult.OK)
			{
				return;
			}

			EveSettingsSyncReport report = EveSettingsSync.BackupAll(mode: EveSettingsBackupMode.Manual);
			this.ShowReport("Backup complete", report);
		}

		private void SyncButton_Click(object sender, EventArgs e)
		{
			IWin32Window owner = this.GetDialogOwner();
			string profile = this.SelectedProfileName;
			var source = this._characterList.SelectedItem as SettingsSyncCharacterEntry;
			if (source == null || string.IsNullOrEmpty(profile))
			{
				return;
			}

			if (source.AccountId <= 0)
			{
				MessageBox.Show(owner,
					"This character has no known account ID yet. Log that client in once with account-based positioning enabled so the map can be recorded.",
					"Settings Sync",
					MessageBoxButtons.OK,
					MessageBoxIcon.Information);
				return;
			}

			if (EveSettingsSync.IsEveRunning())
			{
				MessageBox.Show(owner,
					"EVE appears to be running. Close all clients and the launcher before syncing — they rewrite settings on exit and will stomp your copies.",
					"Settings Sync",
					MessageBoxButtons.OK,
					MessageBoxIcon.Warning);
				return;
			}

			List<SettingsSyncCharacterEntry> destinations = this._characters
				.Where(c => c.CharacterId != source.CharacterId)
				.ToList();

			if (destinations.Count == 0)
			{
				MessageBox.Show(owner, "No other characters available as destinations in this profile.", "Settings Sync",
					MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			IList<string> channelsToKeep = this.GetSelectedChannelKeysToKeep();
			IList<string> channelsToStrip = EveChatChannelTools.ResolveKeysToStrip(
				source.CharacterId, channelsToKeep, profile);

			using (var dialog = new SettingsSyncDestinationDialog(
				source,
				destinations,
				channelsToKeep.Count,
				this._configuration?.AutoSettingsSyncDestinationCharacterIds))
			{
				if (dialog.ShowDialog(owner) != DialogResult.OK)
				{
					return;
				}

				List<SettingsSyncCharacterEntry> selectedDestinations = dialog.SelectedDestinations;
				if (selectedDestinations.Count == 0)
				{
					return;
				}

				var missingAccount = selectedDestinations.Where(d => d.AccountId <= 0).ToList();
				if (missingAccount.Count > 0)
				{
					MessageBox.Show(owner,
						"These destinations have no account ID and cannot receive account (core_user) settings:\n\n" +
						string.Join("\n", missingAccount.Select(d => d.DisplayName)),
						"Settings Sync",
						MessageBoxButtons.OK,
						MessageBoxIcon.Warning);
				}

				var options = new EveSettingsSyncOptions
				{
					SourceCharacterId = source.CharacterId,
					SourceUserId = source.AccountId,
					SourceCharacterName = source.DisplayName,
					DestinationCharacterIds = selectedDestinations.Select(d => d.CharacterId).ToList(),
					DestinationUserIds = selectedDestinations.Where(d => d.AccountId > 0).Select(d => d.AccountId).Distinct().ToList(),
					ChannelKeysToStrip = channelsToStrip,
					ProfileName = profile,
					PreserveModuleState = this._preserveModuleStateCheckBox.Checked,
					Mode = EveSettingsSyncMode.Copy
				};

				EveSettingsSyncReport report = new EveSettingsSync(options).Run();

				if (this._configuration != null && report.FilesSynced > 0)
				{
					EveAutoSettingsSyncRunner.SaveProfile(
						this._configuration,
						profile,
						options.SourceCharacterId,
						options.SourceUserId,
						options.DestinationCharacterIds,
						options.DestinationUserIds,
						channelsToKeep);
					this.PersistConfiguration?.Invoke();
					this.UpdateAutoSyncProfileLabel();
				}

				this.ShowReport("Sync complete", report);
			}
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

			this._statusLabel.Text = report.Warnings.Count > 0
				? $"{title}: {report.FilesSynced} synced, {report.Warnings.Count} error(s)."
				: $"{title}: {report.FilesSynced} synced, {report.FilesBackedUp} backed up.";

			MessageBox.Show(this.GetDialogOwner(), string.Join(Environment.NewLine, lines), title,
				MessageBoxButtons.OK,
				report.Warnings.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
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

	sealed class SettingsSyncDestinationDialog : Form
	{
		private readonly CheckedListBox _list;

		public List<SettingsSyncCharacterEntry> SelectedDestinations { get; private set; } = new List<SettingsSyncCharacterEntry>();

		public SettingsSyncDestinationDialog(
			SettingsSyncCharacterEntry source,
			IList<SettingsSyncCharacterEntry> destinations,
			int channelsToKeepCount,
			IList<long> rememberedDestinationIds = null)
		{
			this.Text = "Sync destinations";
			this.FormBorderStyle = FormBorderStyle.FixedDialog;
			this.StartPosition = FormStartPosition.CenterParent;
			this.MinimizeBox = false;
			this.MaximizeBox = false;
			this.ShowInTaskbar = false;
			this.TopMost = true;
			this.AutoScaleMode = AutoScaleMode.Dpi;
			this.Padding = new Padding(12);
			this.ClientSize = new Size(400, 420);

			var label = new Label
			{
				AutoSize = true,
				Dock = DockStyle.Top,
				Margin = new Padding(0, 0, 0, 8),
				Padding = new Padding(0, 0, 0, 8),
				Text = channelsToKeepCount > 0
					? $"Copy settings from {source.DisplayName} onto (keeping {channelsToKeepCount} channel(s)):"
					: $"Copy settings from {source.DisplayName} onto (keeping no player channels):"
			};

			var remembered = new HashSet<long>(rememberedDestinationIds ?? Array.Empty<long>());

			this._list = new CheckedListBox
			{
				CheckOnClick = true,
				Dock = DockStyle.Fill,
				IntegralHeight = false
			};

			foreach (SettingsSyncCharacterEntry destination in destinations
				.OrderByDescending(d => remembered.Contains(d.CharacterId))
				.ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase))
			{
				this._list.Items.Add(destination, remembered.Contains(destination.CharacterId));
			}

			var selectAll = new Button { Text = "Select all" };
			SettingsHelp.StyleActionButton(this, selectAll);
			selectAll.Click += (_, __) =>
			{
				for (int i = 0; i < this._list.Items.Count; i++)
				{
					this._list.SetItemChecked(i, true);
				}
			};

			var ok = new Button
			{
				Text = "Sync",
				DialogResult = DialogResult.OK
			};
			SettingsHelp.StyleActionButton(this, ok);
			ok.Click += this.Ok_Click;

			var cancel = new Button
			{
				Text = "Cancel",
				DialogResult = DialogResult.Cancel
			};
			SettingsHelp.StyleActionButton(this, cancel);

			var buttons = new FlowLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Dock = DockStyle.Bottom,
				FlowDirection = FlowDirection.LeftToRight,
				Padding = new Padding(0, 12, 0, 0),
				WrapContents = false
			};
			buttons.Controls.Add(selectAll);
			buttons.Controls.Add(ok);
			buttons.Controls.Add(cancel);

			this.Controls.Add(this._list);
			this.Controls.Add(buttons);
			this.Controls.Add(label);
			this.AcceptButton = ok;
			this.CancelButton = cancel;
		}

		private void Ok_Click(object sender, EventArgs e)
		{
			this.SelectedDestinations = this._list.CheckedItems
				.Cast<SettingsSyncCharacterEntry>()
				.ToList();

			if (this.SelectedDestinations.Count == 0)
			{
				MessageBox.Show(this, "Select at least one destination character.", "Settings Sync",
					MessageBoxButtons.OK, MessageBoxIcon.Information);
				this.DialogResult = DialogResult.None;
			}
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			this.Activate();
			this.BringToFront();
		}
	}
}
