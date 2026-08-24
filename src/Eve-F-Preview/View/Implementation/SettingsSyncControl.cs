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
			return this.AccountId > 0
				? $"{this.DisplayName}  (char {this.CharacterId}, acct {this.AccountId})"
				: $"{this.DisplayName}  (char {this.CharacterId}, acct unknown - right-click to set)";
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
		private readonly ComboBox _sourceCombo;
		private readonly Button _setSourceAccountIdButton;
		private readonly CheckedListBox _destinationList;
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

			var sourceLabel = new Label
			{
				AutoSize = true,
				Text = "Source character"
			};

			this._sourceCombo = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Width = 260
			};
			this._sourceCombo.SelectedIndexChanged += this.SourceCombo_SelectedIndexChanged;

			this._setSourceAccountIdButton = new Button
			{
				Text = "Set account ID…"
			};
			this._setSourceAccountIdButton.Click += this.SetSourceAccountIdButton_Click;

			var destinationLabel = new Label
			{
				AutoSize = true,
				Text = "Destination characters"
			};

			this._destinationList = new CheckedListBox
			{
				IntegralHeight = false,
				// Deliberately not CheckOnClick: clicking a row selects it to edit its channel
				// list below without also toggling whether it's included in the sync. The
				// checkbox itself still toggles on click (or Space) as usual.
				CheckOnClick = false
			};
			this._destinationList.ItemCheck += this.DestinationList_ItemCheck;
			this._destinationList.SelectedIndexChanged += this.DestinationList_SelectedIndexChanged;
			this._destinationList.MouseDown += this.DestinationList_MouseDown;

			var setDestinationAccountIdMenuItem = new ToolStripMenuItem("Set account ID…");
			setDestinationAccountIdMenuItem.Click += this.SetDestinationAccountIdMenuItem_Click;
			this._destinationList.ContextMenuStrip = new ContextMenuStrip();
			this._destinationList.ContextMenuStrip.Items.Add(setDestinationAccountIdMenuItem);

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
			SettingsHelp.AddRow(table, sourceLabel, SettingsHelp.Text.SourceCharacter);
			SettingsHelp.AddRow(table, SettingsHelp.CreateFlow(this._sourceCombo, this._setSourceAccountIdButton));
			SettingsHelp.AddRow(table, destinationLabel, SettingsHelp.Text.DestinationCharacters);
			SettingsHelp.AddFixedHeight(table, this._destinationList, 110);
			SettingsHelp.AddRow(table, this._channelLabel, SettingsHelp.Text.ChannelsToKeep);
			SettingsHelp.AddFixedHeight(table, this._channelList, 130);
			SettingsHelp.AddRow(table, this._preserveModuleStateCheckBox, SettingsHelp.Text.PreserveModuleLayout);
			SettingsHelp.AddRow(table, SettingsHelp.CreateFlow(this._refreshButton, this._syncButton));

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
				this._setSourceAccountIdButton,
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
				long preferredSourceId = this._configuration?.AutoSettingsSyncSourceCharacterId ?? 0;

				this._characters.Clear();
				this._sourceCombo.Items.Clear();

				if (string.IsNullOrEmpty(profile))
				{
					this.RefreshDestinationList();
					this.LoadChannelsForSelectedDestination();
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

				this._characters.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

				foreach (SettingsSyncCharacterEntry character in this._characters)
				{
					this._sourceCombo.Items.Add(character);
				}

				int sourceIndex = -1;
				if (preferredSourceId > 0)
				{
					for (int i = 0; i < this._sourceCombo.Items.Count; i++)
					{
						if (((SettingsSyncCharacterEntry)this._sourceCombo.Items[i]).CharacterId == preferredSourceId)
						{
							sourceIndex = i;
							break;
						}
					}
				}

				if (sourceIndex < 0 && this._sourceCombo.Items.Count > 0)
				{
					sourceIndex = 0;
				}

				if (sourceIndex >= 0)
				{
					this._sourceCombo.SelectedIndex = sourceIndex;
				}

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

				this._statusLabel.Text = this._characters.Count == 0
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

		private void SourceCombo_SelectedIndexChanged(object sender, EventArgs e)
		{
			this.RefreshDestinationList();
			this.LoadChannelsForSelectedDestination();
			this.UpdateSyncEnabled();

			var selected = this._sourceCombo.SelectedItem as SettingsSyncCharacterEntry;
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

		private void SetSourceAccountIdButton_Click(object sender, EventArgs e)
		{
			if (!(this._sourceCombo.SelectedItem is SettingsSyncCharacterEntry selected) || this._configuration == null)
			{
				return;
			}

			using (var dialog = new SettingsSyncAccountIdDialog(selected.DisplayName, selected.AccountId))
			{
				if (dialog.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}

				this._configuration.SetCharacterAccount((int)selected.CharacterId, dialog.AccountId);
				this.PersistConfiguration?.Invoke();
				this.RefreshCharacterList();
			}
		}

		/// <summary>Rebuilds the destination checklist from the current character set, excluding the selected source, preserving check state and the currently-edited selection where possible.</summary>
		private void RefreshDestinationList()
		{
			var selectedSource = this._sourceCombo.SelectedItem as SettingsSyncCharacterEntry;
			var configuredDestinationIds = new HashSet<long>(this._configuration?.AutoSettingsSyncDestinationCharacterIds ?? new List<long>());
			var previouslyEditingDestination = this._destinationList.SelectedItem as SettingsSyncCharacterEntry;

			bool wasSuppressed = this._suppressPersist;
			this._suppressPersist = true;
			try
			{
				this._destinationList.Items.Clear();

				foreach (SettingsSyncCharacterEntry character in this._characters)
				{
					if (selectedSource != null && character.CharacterId == selectedSource.CharacterId)
					{
						continue;
					}

					bool isDestination = configuredDestinationIds.Contains(character.CharacterId);
					this._destinationList.Items.Add(character, isDestination);
				}

				int restoreIndex = -1;
				if (previouslyEditingDestination != null)
				{
					for (int i = 0; i < this._destinationList.Items.Count; i++)
					{
						if (((SettingsSyncCharacterEntry)this._destinationList.Items[i]).CharacterId == previouslyEditingDestination.CharacterId)
						{
							restoreIndex = i;
							break;
						}
					}
				}

				if (restoreIndex < 0)
				{
					for (int i = 0; i < this._destinationList.Items.Count; i++)
					{
						if (this._destinationList.GetItemChecked(i))
						{
							restoreIndex = i;
							break;
						}
					}
				}

				this._destinationList.SelectedIndex = restoreIndex;
			}
			finally
			{
				this._suppressPersist = wasSuppressed;
			}
		}

		// Right-clicking a non-selected row should select it before the context menu opens,
		// matching standard Windows list behavior (ListBox/CheckedListBox does not do this itself).
		private void DestinationList_MouseDown(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Right)
			{
				return;
			}

			int index = this._destinationList.IndexFromPoint(e.Location);
			if (index >= 0)
			{
				this._destinationList.SelectedIndex = index;
			}
		}

		private void SetDestinationAccountIdMenuItem_Click(object sender, EventArgs e)
		{
			if (!(this._destinationList.SelectedItem is SettingsSyncCharacterEntry selected) || this._configuration == null)
			{
				return;
			}

			using (var dialog = new SettingsSyncAccountIdDialog(selected.DisplayName, selected.AccountId))
			{
				if (dialog.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}

				this._configuration.SetCharacterAccount((int)selected.CharacterId, dialog.AccountId);
				this.PersistConfiguration?.Invoke();
				this.RefreshCharacterList();
			}
		}

		private void DestinationList_ItemCheck(object sender, ItemCheckEventArgs e)
		{
			if (this._suppressPersist)
			{
				return;
			}

			this.BeginInvoke(new Action(this.PersistDestinationSelection));
		}

		private void PersistDestinationSelection()
		{
			if (this._suppressPersist || this._configuration == null)
			{
				return;
			}

			List<SettingsSyncCharacterEntry> checkedDestinations = this._destinationList.CheckedItems
				.Cast<SettingsSyncCharacterEntry>()
				.ToList();

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

		private void DestinationList_SelectedIndexChanged(object sender, EventArgs e)
		{
			this.LoadChannelsForSelectedDestination();
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

			var destination = this._destinationList.SelectedItem as SettingsSyncCharacterEntry;
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
				this._channelList.Items.Clear();

				var source = this._sourceCombo.SelectedItem as SettingsSyncCharacterEntry;
				if (source == null || source.CharacterId <= 0)
				{
					this._channelLabel.Text = "Channels to keep on copy";
					return;
				}

				var destination = this._destinationList.SelectedItem as SettingsSyncCharacterEntry;
				if (destination == null)
				{
					this._channelLabel.Text = "Channels to keep (select a destination below)";
					return;
				}

				string path = EveChatChannelTools.FindNewestCoreCharPath(
					source.CharacterId, profileName: this.SelectedProfileName);
				if (string.IsNullOrEmpty(path))
				{
					this._channelLabel.Text = "Channels to keep (no core_char file found for source)";
					return;
				}

				try
				{
					IList<EveChatChannelInfo> channels = EveChatChannelTools.ListChannels(path)
						.Where(c => !c.IsBuiltin)
						.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
						.ToList();

					this._channelLabel.Text = channels.Count == 0
						? $"Channels to keep for {destination.DisplayName} (none / only builtins)"
						: $"Channels to keep for {destination.DisplayName} ({channels.Count})";

					HashSet<string> keepKeys = new HashSet<string>(
						EveAutoSettingsSyncRunner.GetChannelKeysToKeepForDestination(this._configuration, destination.CharacterId),
						StringComparer.OrdinalIgnoreCase);

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
				this._suppressPersist = wasSuppressed;
			}
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
			var source = this._sourceCombo.SelectedItem as SettingsSyncCharacterEntry;
			bool hasDestination = this._destinationList.CheckedItems.Count > 0;
			this._syncButton.Enabled = !string.IsNullOrEmpty(this.SelectedProfileName)
				&& source != null
				&& source.CharacterId > 0
				&& source.AccountId > 0
				&& hasDestination;
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
			var source = this._sourceCombo.SelectedItem as SettingsSyncCharacterEntry;
			if (source == null || string.IsNullOrEmpty(profile))
			{
				return;
			}

			if (source.AccountId <= 0)
			{
				MessageBox.Show(owner,
					"This character has no known account ID yet. Right-click it to set one, or log that client in once so it can be detected automatically.",
					"Settings Sync",
					MessageBoxButtons.OK,
					MessageBoxIcon.Information);
				return;
			}

			List<SettingsSyncCharacterEntry> destinations = this._destinationList.CheckedItems
				.Cast<SettingsSyncCharacterEntry>()
				.ToList();

			if (destinations.Count == 0)
			{
				MessageBox.Show(owner, "Check at least one destination character below.", "Settings Sync",
					MessageBoxButtons.OK, MessageBoxIcon.Information);
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

			var missingAccount = destinations.Where(d => d.AccountId <= 0).ToList();
			if (missingAccount.Count > 0)
			{
				MessageBox.Show(owner,
					"These destinations have no account ID and cannot receive account (core_user) settings:\n\n" +
					string.Join("\n", missingAccount.Select(d => d.DisplayName)),
					"Settings Sync",
					MessageBoxButtons.OK,
					MessageBoxIcon.Warning);
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
					PreserveModuleState = this._preserveModuleStateCheckBox.Checked,
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

	/// <summary>
	/// Manual fallback for when auto-detection (reading /LauncherData= off the running client's
	/// command line) can't find a character's account ID - e.g. it was launched via a quick-login
	/// shortcut that only passes /autoSelectCharacter:, with no account info in the command line.
	/// </summary>
	sealed class SettingsSyncAccountIdDialog : Form
	{
		private readonly NumericUpDown _accountIdInput;

		public int AccountId { get; private set; }

		public SettingsSyncAccountIdDialog(string characterDisplayName, long currentAccountId)
		{
			this.Text = "Set account ID";
			this.FormBorderStyle = FormBorderStyle.FixedDialog;
			this.StartPosition = FormStartPosition.CenterParent;
			this.MinimizeBox = false;
			this.MaximizeBox = false;
			this.ShowInTaskbar = false;
			this.TopMost = true;
			this.AutoScaleMode = AutoScaleMode.Dpi;
			this.Padding = new Padding(12);
			this.ClientSize = new Size(340, 130);

			var label = new Label
			{
				AutoSize = true,
				Dock = DockStyle.Top,
				Margin = new Padding(0, 0, 0, 8),
				Padding = new Padding(0, 0, 0, 8),
				Text = $"Account ID for {characterDisplayName}:\n(shown in the EVE Launcher's account list, or Task Manager's command line for a running client)"
			};

			this._accountIdInput = new NumericUpDown
			{
				Dock = DockStyle.Top,
				Maximum = int.MaxValue,
				Minimum = 0,
				Value = currentAccountId > 0 ? Math.Min(currentAccountId, int.MaxValue) : 0
			};

			var ok = new Button
			{
				Text = "Save",
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
			buttons.Controls.Add(ok);
			buttons.Controls.Add(cancel);

			this.Controls.Add(this._accountIdInput);
			this.Controls.Add(buttons);
			this.Controls.Add(label);
			this.AcceptButton = ok;
			this.CancelButton = cancel;
		}

		private void Ok_Click(object sender, EventArgs e)
		{
			this.AccountId = (int)this._accountIdInput.Value;
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			this.Activate();
			this.BringToFront();
		}
	}
}
