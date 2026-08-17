using EveFPreview.Configuration;
using EveFPreview.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace EveFPreview.View
{
	public partial class MainForm : Form, IMainFormView
	{
		#region Private fields
		private readonly ApplicationContext _context;
		private readonly Dictionary<ViewZoomAnchor, RadioButton> _zoomAnchorMap;
		private readonly Dictionary<ViewZoomAnchor, RadioButton> _overlayLabelMap;
		private readonly Dictionary<ViewZoomAnchor, RadioButton> _cycleGroupIndicatorMap;
		private ViewZoomAnchor _cachedThumbnailZoomAnchor;
		private ViewZoomAnchor _cachedOverlayLabelAnchor;
		private ViewZoomAnchor _cachedCycleGroupIndicatorAnchor;
		private bool _suppressEvents;
		private Size _minimumSize;
		private Size _maximumSize;
		private string _iconName;
		private IConfigurationStorage _configurationStorage;
		private System.Windows.Forms.ComboBox _configProfileCombo;
		private System.Windows.Forms.Button _loadConfigProfileButton;
		private System.Windows.Forms.Button _saveConfigProfileAsButton;
		private System.Windows.Forms.Button _importConfigProfileButton;
		private Panel _configProfilePanel;
		#endregion

		public MainForm(ApplicationContext context)
		{
			this._context = context;
			this._zoomAnchorMap = new Dictionary<ViewZoomAnchor, RadioButton>();
			this._overlayLabelMap = new Dictionary<ViewZoomAnchor, RadioButton>();
			this._cycleGroupIndicatorMap = new Dictionary<ViewZoomAnchor, RadioButton>();
			this._cachedThumbnailZoomAnchor = ViewZoomAnchor.NW;
			this._suppressEvents = false;
			this._minimumSize = new Size(20, 20);
			this._maximumSize = new Size(20, 20);

			InitializeComponent();

			this.ThumbnailsList.DisplayMember = "Title";

			this.InitZoomAnchorMap();
			this.InitOverlayLabelMap();
			this.InitCycleGroupIndicatorMap();
			this.InitFormSize();

			this.AnimationStyleCombo.DataSource = Enum.GetValues(typeof(AnimationStyle));

			this.InitShortcutsTab();
			this.InitCycleGroupsTab();
			this.InitSettingsSyncTab();
			this.InitConfigProfileControls();
			this.InitPackedTabLayouts();
			this.ApplyScaledSettingsButtons();
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			this.ApplyScaledSettingsButtons();
		}

		protected override void OnDpiChanged(DpiChangedEventArgs e)
		{
			base.OnDpiChanged(e);
			this.ApplyScaledSettingsButtons();
		}

		private void ApplyScaledSettingsButtons()
		{
			SettingsHelp.ApplyScaledButtonSizes(
				this,
				this.CloseAllEveClientsButton,
				this.RefreshPortraitsButton,
				this.btnLabelFont,
				this._loadConfigProfileButton,
				this._saveConfigProfileAsButton,
				this._importConfigProfileButton);
		}

		private void InitConfigProfileControls()
		{
			this._configProfilePanel = new Panel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Margin = new Padding(0)
			};

			var layout = new TableLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				ColumnCount = 3,
				Dock = DockStyle.Top,
				Margin = new Padding(0)
			};
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			var label = new Label
			{
				AutoSize = true,
				Margin = new Padding(0, 0, 0, 4),
				Text = "Config profile"
			};
			layout.Controls.Add(label, 0, 0);
			layout.SetColumnSpan(label, 3);

			this._configProfileCombo = new System.Windows.Forms.ComboBox
			{
				Dock = DockStyle.Fill,
				DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList,
				Margin = new Padding(0, 0, 0, 8)
			};
			layout.Controls.Add(this._configProfileCombo, 0, 1);
			layout.SetColumnSpan(this._configProfileCombo, 3);

			this._loadConfigProfileButton = new System.Windows.Forms.Button
			{
				Text = "Load"
			};
			SettingsHelp.StyleActionButton(this, this._loadConfigProfileButton, fillWidth: true);
			this._loadConfigProfileButton.Margin = new Padding(0, 0, 4, 0);
			this._loadConfigProfileButton.Click += this.LoadConfigProfileButton_Click;

			this._saveConfigProfileAsButton = new System.Windows.Forms.Button
			{
				Text = "Save As…"
			};
			SettingsHelp.StyleActionButton(this, this._saveConfigProfileAsButton, fillWidth: true);
			this._saveConfigProfileAsButton.Margin = new Padding(2, 0, 2, 0);
			this._saveConfigProfileAsButton.Click += this.SaveConfigProfileAsButton_Click;

			this._importConfigProfileButton = new System.Windows.Forms.Button
			{
				Text = "Import…"
			};
			SettingsHelp.StyleActionButton(this, this._importConfigProfileButton, fillWidth: true);
			this._importConfigProfileButton.Margin = new Padding(4, 0, 0, 0);
			this._importConfigProfileButton.Click += this.ImportConfigProfileButton_Click;

			layout.Controls.Add(this._loadConfigProfileButton, 0, 2);
			layout.Controls.Add(this._saveConfigProfileAsButton, 1, 2);
			layout.Controls.Add(this._importConfigProfileButton, 2, 2);

			this._configProfilePanel.Controls.Add(layout);
		}

		private void InitPackedTabLayouts()
		{
			this.LayoutGeneralTab();
			this.LayoutThumbnailTab();
			this.LayoutOverlayTab();
			this.LayoutZoomTab();
		}

		private T FindNamed<T>(string name) where T : Control
		{
			return (T)this.Controls.Find(name, true).First();
		}

		private void LayoutGeneralTab()
		{
			Panel panel = this.FindNamed<Panel>("GeneralSettingsPanel");
			TableLayoutPanel table = SettingsHelp.CreateScrollTable();

			SettingsHelp.AddRow(table, this.MinimizeToTrayCheckBox);
			SettingsHelp.AddRow(table, this.StartMinimizedCheckBox);
			SettingsHelp.AddRow(table, this.EnableClientLayoutTrackingCheckBox, SettingsHelp.Text.TrackClientLocations);
			SettingsHelp.AddRow(table, this.HideActiveClientThumbnailCheckBox);
			SettingsHelp.AddRow(table, this.HideCaptionOnClientsCheckBox, SettingsHelp.Text.HideCaptionBar);
			SettingsHelp.AddRow(table, this.ShowThumbnailsAlwaysOnTopCheckBox);
			SettingsHelp.AddRow(table, this.HideThumbnailsOnLostFocusCheckBox);
			SettingsHelp.AddRow(table, this.OnlyRegisterCycleHotkeysWhenEveFocusedCheckBox, SettingsHelp.Text.CycleHotkeysWhenEveActive);
			SettingsHelp.AddRow(table, this.DynamicCycleGroupCheckBox, SettingsHelp.Text.DynamicCycleGroup);
			SettingsHelp.AddRow(table, this.EnableAccountBasedThumbnailPositioningCheckBox, SettingsHelp.Text.AccountBasedPositioning);
			SettingsHelp.AddRow(table, this.EnablePerClientThumbnailsLayoutsCheckBox, SettingsHelp.Text.UniqueLayout);
			SettingsHelp.AddRow(table, this.MinimizeInactiveClientsCheckBox);
			SettingsHelp.AddRow(
				table,
				SettingsHelp.CreateLabeledFill(this.FindNamed<Label>("label4"), this.AnimationStyleCombo),
				SettingsHelp.Text.AnimationStyle);
			SettingsHelp.AddRow(table, this.EnableAutoSettingsSyncCheckBox, SettingsHelp.Text.AutoSettingsSync);
			this.AutoSettingsSyncStatusLabel.AutoSize = true;
			this.AutoSettingsSyncStatusLabel.MaximumSize = new Size(360, 0);
			SettingsHelp.AddRow(table, this.AutoSettingsSyncStatusLabel);
			SettingsHelp.AddRow(table, this._configProfilePanel, SettingsHelp.Text.ConfigProfile);
			SettingsHelp.AddFullWidthButton(table, this.CloseAllEveClientsButton);

			SettingsHelp.HostInScrollPanel(panel, table);
		}

		private void LayoutThumbnailTab()
		{
			Panel panel = this.FindNamed<Panel>("ThumbnailSettingsPanel");
			TableLayoutPanel table = SettingsHelp.CreateScrollTable();

			SettingsHelp.AddRow(
				table,
				SettingsHelp.CreateLabeledFill(this.FindNamed<Label>("OpacityLabel"), this.ThumbnailOpacityTrackBar));
			SettingsHelp.AddRow(
				table,
				SettingsHelp.CreateFlow(this.FindNamed<Label>("WidthLabel"), this.ThumbnailsWidthNumericEdit));
			SettingsHelp.AddRow(
				table,
				SettingsHelp.CreateFlow(this.FindNamed<Label>("HeigthLabel"), this.ThumbnailsHeightNumericEdit));
			SettingsHelp.AddRow(table, this.EnableOverwatchModeCheckBox, SettingsHelp.Text.OverwatchMode);
			SettingsHelp.AddRow(
				table,
				SettingsHelp.CreateFlow(this.OverwatchWidthLabel, this.FocusedThumbnailWidthNumericEdit));
			SettingsHelp.AddRow(
				table,
				SettingsHelp.CreateFlow(this.OverwatchHeightLabel, this.FocusedThumbnailHeightNumericEdit));
			SettingsHelp.AddRow(
				table,
				SettingsHelp.CreateFlow(
					this.OverwatchPosXLabel,
					this.FocusedThumbnailLocationXNumericEdit,
					this.OverwatchPosYLabel,
					this.FocusedThumbnailLocationYNumericEdit));
			SettingsHelp.AddRow(table, this.LockThumbnailLocationCheckbox, SettingsHelp.Text.LockThumbnailLocation);
			SettingsHelp.AddRow(table, this.ThumbnailSnapToEdgesCheckBox, SettingsHelp.Text.SnapToEdges);
			SettingsHelp.AddRow(table, this.ThumbnailSnapToGridCheckBox, SettingsHelp.Text.SnapToGrid);
			SettingsHelp.AddRow(
				table,
				SettingsHelp.CreateFlow(
					this.SnapXLabel,
					this.ThumbnailSnapToGridSizeXNumericEdit,
					this.SnapYLabel,
					this.ThumbnailSnapToGridSizeYNumericEdit));
			SettingsHelp.AddRow(
				table,
				SettingsHelp.CreateFlow(
					this.PreventPreviewsCheckBox,
					this.FindNamed<Label>("label1"),
					this.PreventPreviewColorButton),
				SettingsHelp.Text.DoNotDisplayPreviews);
			SettingsHelp.AddFullWidthButton(table, this.RefreshPortraitsButton);

			SettingsHelp.HostInScrollPanel(panel, table);

			this.SpawnXLabel.Parent = null;
			this.SpawnYLabel.Parent = null;
			this.NewPreviewSpawnLocationXNumericEdit.Parent = null;
			this.NewPreviewSpawnLocationYNumericEdit.Parent = null;
			this.NewPreviewAutoTileCheckBox.Parent = null;
		}

		private void LayoutOverlayTab()
		{
			Panel panel = this.FindNamed<Panel>("OverlaySettingsPanel");
			TableLayoutPanel table = SettingsHelp.CreateScrollTable();

			SettingsHelp.AddRow(table, this.ShowThumbnailOverlaysCheckBox, SettingsHelp.Text.ShowOverlay);
			SettingsHelp.AddRow(table, this.ShowSystemNameOnThumbnailCheckBox, SettingsHelp.Text.ShowSystemName);
			SettingsHelp.AddRow(table, this.ShowThumbnailFramesCheckBox, SettingsHelp.Text.ShowFrames);
			SettingsHelp.AddRow(table, this.EnableActiveClientHighlightCheckBox);
			SettingsHelp.AddRow(
				table,
				SettingsHelp.CreateFlow(this.HighlightColorLabel, this.ActiveClientHighlightColorButton));
			SettingsHelp.AddRow(
				table,
				SettingsHelp.CreateFlow(this.btnLabelFont, this.LabelOverlayLabelFont));
			SettingsHelp.AddRow(
				table,
				SettingsHelp.CreateFlow(this.FindNamed<Label>("label2"), this.OverlayLabelColorButton));
			SettingsHelp.AddRow(table, this.FindNamed<Label>("label3"));
			SettingsHelp.AddRow(table, this.FindNamed<Panel>("panel1"));
			SettingsHelp.AddRow(table, this.FindNamed<Label>("label5"), SettingsHelp.Text.CycleGroupIndicator);
			SettingsHelp.AddRow(table, this.FindNamed<Panel>("panel2"));

			SettingsHelp.HostInScrollPanel(panel, table);
		}

		private void LayoutZoomTab()
		{
			Panel panel = this.FindNamed<Panel>("ZoomSettingsPanel");
			TableLayoutPanel table = SettingsHelp.CreateScrollTable();

			SettingsHelp.AddRow(table, this.EnableThumbnailZoomCheckBox);
			SettingsHelp.AddRow(
				table,
				SettingsHelp.CreateLabeledFill(this.FindNamed<Label>("ZoomFactorLabel"), this.ThumbnailZoomFactorNumericEdit));
			SettingsHelp.AddRow(
				table,
				SettingsHelp.CreateFlow(this.FindNamed<Label>("ZoomAnchorLabel"), this.ZoomAnchorPanel));

			SettingsHelp.HostInScrollPanel(panel, table);
		}

		public void SetConfigurationStorage(IConfigurationStorage configurationStorage)
		{
			this._configurationStorage = configurationStorage;
			this.RefreshConfigProfileList();
		}

		public Action ConfigProfileChanged { get; set; }

		private void RefreshConfigProfileList(string preferredSelection = null)
		{
			if (this._configProfileCombo == null || this._configurationStorage == null)
			{
				return;
			}

			string activeFileName = Path.GetFileName(this._configurationStorage.ActiveConfigPath);
			string select = preferredSelection ?? activeFileName;

			this._configProfileCombo.BeginUpdate();
			try
			{
				this._configProfileCombo.Items.Clear();
				foreach (string profile in this._configurationStorage.ListConfigProfiles())
				{
					this._configProfileCombo.Items.Add(profile);
				}

				if (!string.IsNullOrEmpty(select))
				{
					int index = this._configProfileCombo.Items.IndexOf(select);
					if (index < 0)
					{
						// Not on disk as a recognizable config yet (e.g. brand-new file) - show it anyway.
						this._configProfileCombo.Items.Add(select);
						index = this._configProfileCombo.Items.Count - 1;
					}

					this._configProfileCombo.SelectedIndex = index;
				}
			}
			finally
			{
				this._configProfileCombo.EndUpdate();
			}
		}

		private void LoadConfigProfileButton_Click(object sender, EventArgs e)
		{
			if (this._configurationStorage == null || !(this._configProfileCombo.SelectedItem is string profile))
			{
				return;
			}

			try
			{
				this.ApplicationSettingsChanged?.Invoke();
				this._configurationStorage.SwitchTo(profile);
				this.ConfigProfileChanged?.Invoke();
				this.RefreshConfigProfileList(profile);
				MessageBox.Show(this, "Loaded profile: " + profile, "Config profile",
					MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, "Could not load profile:\n" + ex.Message, "Config profile",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void SaveConfigProfileAsButton_Click(object sender, EventArgs e)
		{
			if (this._configurationStorage == null)
			{
				return;
			}

			using (var dialog = new SaveFileDialog
			{
				Filter = "JSON config (*.json)|*.json|All files (*.*)|*.*",
				InitialDirectory = AppContext.BaseDirectory,
				FileName = "EVE-F-Preview-profile.json"
			})
			{
				if (dialog.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}

				try
				{
					string fileName = Path.GetFileName(dialog.FileName);
					this.ApplicationSettingsChanged?.Invoke();
					this._configurationStorage.SaveAs(fileName);
					this.RefreshConfigProfileList(fileName);
					MessageBox.Show(this, "Saved current settings as: " + fileName, "Config profile",
						MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
				catch (Exception ex)
				{
					MessageBox.Show(this, "Could not save profile:\n" + ex.Message, "Config profile",
						MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		private void ImportConfigProfileButton_Click(object sender, EventArgs e)
		{
			if (this._configurationStorage == null)
			{
				return;
			}

			string sourcePath;
			using (var openDialog = new OpenFileDialog
			{
				Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
				Title = "Select EVE-O / EVE-F / EVE-X settings file to import"
			})
			{
				if (openDialog.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}

				sourcePath = openDialog.FileName;
			}

			string destinationFileName;
			using (var saveDialog = new SaveFileDialog
			{
				Filter = "JSON config (*.json)|*.json",
				InitialDirectory = AppContext.BaseDirectory,
				FileName = "EVE-F-Preview-imported.json",
				Title = "Save imported profile as"
			})
			{
				if (saveDialog.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}

				destinationFileName = Path.GetFileName(saveDialog.FileName);
			}

			try
			{
				this._configurationStorage.ImportFrom(sourcePath, destinationFileName);
				this.RefreshConfigProfileList(destinationFileName);

				DialogResult switchNow = MessageBox.Show(this,
					"Imported settings into " + destinationFileName + ".\n\nSwitch to this profile now?",
					"Import settings",
					MessageBoxButtons.YesNo,
					MessageBoxIcon.Question);

				if (switchNow == DialogResult.Yes)
				{
					this.ApplicationSettingsChanged?.Invoke();
					this._configurationStorage.SwitchTo(destinationFileName);
					this.ConfigProfileChanged?.Invoke();
					this.RefreshConfigProfileList(destinationFileName);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, "Import failed:\n" + ex.Message, "Import settings",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void InitShortcutsTab()
		{
			var tabControl = (TabControl)this.Controls.Find("ContentTabControl", true).First();
			var shortcutsTabPage = tabControl.TabPages.Cast<TabPage>().First(page => page.Name == "ShortcutsTabPage");

			this.ShortcutsSettingsControl = new ShortcutsSettingsControl
			{
				Dock = DockStyle.Fill
			};
			shortcutsTabPage.Controls.Add(this.ShortcutsSettingsControl);
			this.ShortcutsSettingsControl.SettingsChanged += this.ShortcutsSettingsChanged_Handler;
		}

		private void InitSettingsSyncTab()
		{
			var tabControl = (TabControl)this.Controls.Find("ContentTabControl", true).First();
			var settingsSyncTabPage = tabControl.TabPages.Cast<TabPage>().First(page => page.Name == "SettingsSyncTabPage");

			this.SettingsSyncControl = new SettingsSyncControl
			{
				Dock = DockStyle.Fill
			};
			settingsSyncTabPage.Controls.Add(this.SettingsSyncControl);
		}

		public void SetSettingsSyncConfiguration(IThumbnailConfiguration configuration, Action persistConfiguration = null)
		{
			if (this.SettingsSyncControl == null)
			{
				return;
			}

			this.SettingsSyncControl.PersistConfiguration = persistConfiguration;
			this.SettingsSyncControl.SetConfiguration(configuration);
		}

		private void InitCycleGroupsTab()
		{
			var tabControl = (TabControl)this.Controls.Find("ContentTabControl", true).First();
			var cycleGroupsTabPage = tabControl.TabPages.Cast<TabPage>().First(page => page.Name == "CycleGroupsTabPage");

			this.CycleGroupsSettingsControl = new CycleGroupsSettingsControl
			{
				Dock = DockStyle.Fill
			};
			cycleGroupsTabPage.Controls.Add(this.CycleGroupsSettingsControl);
		}

		public void SetCycleGroupsConfiguration(IThumbnailConfiguration configuration, Action persistConfiguration = null)
		{
			if (this.CycleGroupsSettingsControl == null)
			{
				return;
			}

			this.CycleGroupsSettingsControl.PersistConfiguration = persistConfiguration;
			this.CycleGroupsSettingsControl.SetConfiguration(configuration);
		}

		public GlobalShortcutSettings GetGlobalShortcutSettings()
		{
			return this.ShortcutsSettingsControl.GetSettings();
		}

		public void SetGlobalShortcutSettings(GlobalShortcutSettings settings)
		{
			if (this.ShortcutsSettingsControl == null || settings == null)
			{
				return;
			}

			this.ShortcutsSettingsControl.SetSettings(settings);
		}

		public void ConfigureShortcutHotkeyRecording(Action suspendGlobalHotkeys, Action resumeGlobalHotkeys)
		{
			if (this.ShortcutsSettingsControl == null)
			{
				return;
			}

			this.ShortcutsSettingsControl.SuspendGlobalHotkeys = suspendGlobalHotkeys;
			this.ShortcutsSettingsControl.ResumeGlobalHotkeys = resumeGlobalHotkeys;
		}

		public Action GlobalShortcutSettingsChanged { get; set; }

		private void ShortcutsSettingsChanged_Handler()
		{
			if (this._suppressEvents)
			{
				return;
			}

			this.GlobalShortcutSettingsChanged?.Invoke();
		}

		public bool MinimizeToTray
		{
			get => this.MinimizeToTrayCheckBox.Checked;
			set => this.MinimizeToTrayCheckBox.Checked = value;
		}

		public bool StartMinimized
		{
			get => this.StartMinimizedCheckBox.Checked;
			set => this.StartMinimizedCheckBox.Checked = value;
		}

		public string IconName
		{
			get => this._iconName;
			set
			{


				this._iconName = value;

				// Set Icon 
				System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
				if (this._iconName == null || ((resources.GetObject(this._iconName))) == null)
				{
					this._iconName = "IconOriginal";
				}

				// pull icon from resources
				try
				{
					var iconBytes = (byte[])resources.GetObject(this._iconName);
					using (MemoryStream ms = new MemoryStream(iconBytes))
					{
						this.Icon = new Icon(ms);
						this.NotifyIcon.Icon = this.Icon;
					}
				}
				catch (Exception ex)
				{
					// Log ?
				}

				if (value != "")
				{
					this.ApplicationSettingsChanged?.Invoke();
				}
			}
		}

		public double ThumbnailOpacity
		{
			get => Math.Min(this.ThumbnailOpacityTrackBar.Value / 100.00, 1.00);
			set
			{
				int barValue = (int)(100.0 * value);
				if (barValue > 100)
				{
					barValue = 100;
				}
				else if (barValue < 10)
				{
					barValue = 10;
				}

				this.ThumbnailOpacityTrackBar.Value = barValue;
			}
		}

		public bool EnableClientLayoutTracking
		{
			get => this.EnableClientLayoutTrackingCheckBox.Checked;
			set => this.EnableClientLayoutTrackingCheckBox.Checked = value;
		}

		public bool HideActiveClientThumbnail
		{
			get => this.HideActiveClientThumbnailCheckBox.Checked;
			set => this.HideActiveClientThumbnailCheckBox.Checked = value;
		}

		public bool MinimizeInactiveClients
		{
			get => this.MinimizeInactiveClientsCheckBox.Checked;
			set => this.MinimizeInactiveClientsCheckBox.Checked = value;
		}
		public bool HideCaptionOnClients
		{
			get => this.HideCaptionOnClientsCheckBox.Checked;
			set => this.HideCaptionOnClientsCheckBox.Checked = value;
		}
		public ViewAnimationStyle WindowsAnimationStyle
		{
			get => (ViewAnimationStyle)this.AnimationStyleCombo.SelectedItem;
			set => this.AnimationStyleCombo.SelectedIndex = (int)value;
		}

		public bool ShowThumbnailsAlwaysOnTop
		{
			get => this.ShowThumbnailsAlwaysOnTopCheckBox.Checked;
			set => this.ShowThumbnailsAlwaysOnTopCheckBox.Checked = value;
		}
		public bool PreventPreviews
		{
			get => this.PreventPreviewsCheckBox.Checked;
			set => this.PreventPreviewsCheckBox.Checked = value;
		}

		public bool HideThumbnailsOnLostFocus
		{
			get => this.HideThumbnailsOnLostFocusCheckBox.Checked;
			set => this.HideThumbnailsOnLostFocusCheckBox.Checked = value;
		}

		public bool OnlyRegisterCycleHotkeysWhenEveFocused
		{
			get => this.OnlyRegisterCycleHotkeysWhenEveFocusedCheckBox.Checked;
			set => this.OnlyRegisterCycleHotkeysWhenEveFocusedCheckBox.Checked = value;
		}

		public bool DynamicCycleGroup
		{
			get => this.DynamicCycleGroupCheckBox.Checked;
			set => this.DynamicCycleGroupCheckBox.Checked = value;
		}

		public bool EnableAccountBasedThumbnailPositioning
		{
			get => this.EnableAccountBasedThumbnailPositioningCheckBox.Checked;
			set => this.EnableAccountBasedThumbnailPositioningCheckBox.Checked = value;
		}

		public bool EnableAutoSettingsSync
		{
			get => this.EnableAutoSettingsSyncCheckBox.Checked;
			set => this.EnableAutoSettingsSyncCheckBox.Checked = value;
		}

		public void SetAutoSettingsSyncStatus(bool success, string message)
		{
			void Apply()
			{
				this.AutoSettingsSyncStatusLabel.Text = message ?? string.Empty;
				this.AutoSettingsSyncStatusLabel.ForeColor = success
					? Color.FromArgb(0, 128, 0)
					: Color.FromArgb(180, 100, 0);
			}

			if (this.InvokeRequired)
			{
				this.BeginInvoke((Action)Apply);
			}
			else
			{
				Apply();
			}
		}

		public bool EnablePerClientThumbnailLayouts
		{
			get => this.EnablePerClientThumbnailsLayoutsCheckBox.Checked;
			set => this.EnablePerClientThumbnailsLayoutsCheckBox.Checked = value;
		}

		public Size ThumbnailSize
		{
			get => new Size((int)this.ThumbnailsWidthNumericEdit.Value, (int)this.ThumbnailsHeightNumericEdit.Value);
			set
			{
				this.ThumbnailsWidthNumericEdit.Value = value.Width;
				this.ThumbnailsHeightNumericEdit.Value = value.Height;
			}
		}

		public bool EnableOverwatchMode
		{
			get => this.EnableOverwatchModeCheckBox.Checked;
			set => this.EnableOverwatchModeCheckBox.Checked = value;
		}

		public Size FocusedThumbnailSize
		{
			get => new Size((int)this.FocusedThumbnailWidthNumericEdit.Value, (int)this.FocusedThumbnailHeightNumericEdit.Value);
			set
			{
				this.FocusedThumbnailWidthNumericEdit.Value = value.Width;
				this.FocusedThumbnailHeightNumericEdit.Value = value.Height;
			}
		}

		public Point FocusedThumbnailLocation
		{
			get => new Point((int)this.FocusedThumbnailLocationXNumericEdit.Value, (int)this.FocusedThumbnailLocationYNumericEdit.Value);
			set
			{
				this.FocusedThumbnailLocationXNumericEdit.Value = value.X;
				this.FocusedThumbnailLocationYNumericEdit.Value = value.Y;
			}
		}

		public Point NewPreviewSpawnLocation
		{
			get => new Point((int)this.NewPreviewSpawnLocationXNumericEdit.Value, (int)this.NewPreviewSpawnLocationYNumericEdit.Value);
			set
			{
				this.NewPreviewSpawnLocationXNumericEdit.Value = value.X;
				this.NewPreviewSpawnLocationYNumericEdit.Value = value.Y;
			}
		}

		public bool NewPreviewAutoTile
		{
			get => this.NewPreviewAutoTileCheckBox.Checked;
			set => this.NewPreviewAutoTileCheckBox.Checked = value;
		}

		public bool EnableThumbnailZoom
		{
			get => this.EnableThumbnailZoomCheckBox.Checked;
			set
			{
				this.EnableThumbnailZoomCheckBox.Checked = value;
				this.RefreshZoomSettings();
			}
		}

		public int ThumbnailZoomFactor
		{
			get => (int)this.ThumbnailZoomFactorNumericEdit.Value;
			set => this.ThumbnailZoomFactorNumericEdit.Value = value;
		}

		public ViewZoomAnchor ThumbnailZoomAnchor
		{
			get
			{
				if (this._zoomAnchorMap[this._cachedThumbnailZoomAnchor].Checked)
				{
					return this._cachedThumbnailZoomAnchor;
				}

				foreach (KeyValuePair<ViewZoomAnchor, RadioButton> valuePair in this._zoomAnchorMap)
				{
					if (!valuePair.Value.Checked)
					{
						continue;
					}

					this._cachedThumbnailZoomAnchor = valuePair.Key;
					return this._cachedThumbnailZoomAnchor;
				}

				// Default value
				return ViewZoomAnchor.NW;
			}
			set
			{
				this._cachedThumbnailZoomAnchor = value;
				this._zoomAnchorMap[this._cachedThumbnailZoomAnchor].Checked = true;
			}
		}

		public ViewZoomAnchor OverlayLabelAnchor
		{
			get
			{
				if (this._overlayLabelMap[this._cachedOverlayLabelAnchor].Checked)
				{
					return this._cachedOverlayLabelAnchor;
				}

				foreach (KeyValuePair<ViewZoomAnchor, RadioButton> valuePair in this._overlayLabelMap)
				{
					if (!valuePair.Value.Checked)
					{
						continue;
					}

					this._cachedOverlayLabelAnchor = valuePair.Key;
					return this._cachedOverlayLabelAnchor;
				}

				// Default Value
				return ViewZoomAnchor.NW;
			}
			set
			{
				this._cachedOverlayLabelAnchor = value;
				this._overlayLabelMap[this._cachedOverlayLabelAnchor].Checked = true;
			}
		}

		public ViewZoomAnchor CycleGroupIndicatorAnchor
		{
			get
			{
				if (this._cycleGroupIndicatorMap[this._cachedCycleGroupIndicatorAnchor].Checked)
				{
					return this._cachedCycleGroupIndicatorAnchor;
				}

				foreach (KeyValuePair<ViewZoomAnchor, RadioButton> valuePair in this._cycleGroupIndicatorMap)
				{
					if (!valuePair.Value.Checked)
					{
						continue;
					}

					this._cachedCycleGroupIndicatorAnchor = valuePair.Key;
					return this._cachedCycleGroupIndicatorAnchor;
				}

				// Default Value
				return ViewZoomAnchor.NW;
			}
			set
			{
				this._cachedCycleGroupIndicatorAnchor = value;
				this._cycleGroupIndicatorMap[this._cachedCycleGroupIndicatorAnchor].Checked = true;
			}
		}

		public bool ShowThumbnailOverlays
		{
			get => this.ShowThumbnailOverlaysCheckBox.Checked;
			set => this.ShowThumbnailOverlaysCheckBox.Checked = value;
		}

		public bool ShowThumbnailFrames
		{
			get => this.ShowThumbnailFramesCheckBox.Checked;
			set => this.ShowThumbnailFramesCheckBox.Checked = value;
		}

		public bool ShowSystemNameOnThumbnail
		{
			get => this.ShowSystemNameOnThumbnailCheckBox.Checked;
			set => this.ShowSystemNameOnThumbnailCheckBox.Checked = value;
		}
		public bool LockThumbnailLocation
		{
			get => this.LockThumbnailLocationCheckbox.Checked;
			set => this.LockThumbnailLocationCheckbox.Checked = value;
		}
		public bool ThumbnailSnapToEdges
		{
			get => this.ThumbnailSnapToEdgesCheckBox.Checked;
			set => this.ThumbnailSnapToEdgesCheckBox.Checked = value;
		}

		public bool ThumbnailSnapToGrid
		{
			get => this.ThumbnailSnapToGridCheckBox.Checked;
			set => this.ThumbnailSnapToGridCheckBox.Checked = value;
		}
		public int ThumbnailSnapToGridSizeX
		{
			get => (int)ThumbnailSnapToGridSizeXNumericEdit.Value;
			set => ThumbnailSnapToGridSizeXNumericEdit.Value = value;
		}
		public int ThumbnailSnapToGridSizeY
		{
			get => (int)ThumbnailSnapToGridSizeYNumericEdit.Value;
			set => ThumbnailSnapToGridSizeYNumericEdit.Value = value;
		}

		public bool EnableActiveClientHighlight
		{
			get => this.EnableActiveClientHighlightCheckBox.Checked;
			set => this.EnableActiveClientHighlightCheckBox.Checked = value;
		}

		public Color ActiveClientHighlightColor
		{
			get => this._activeClientHighlightColor;
			set
			{
				this._activeClientHighlightColor = value;
				this.ActiveClientHighlightColorButton.BackColor = value;
			}
		}
		private Color _activeClientHighlightColor;

		public Color PreventPreviewColor
		{
			get => this._preventPreviewColor;
			set
			{
				this._preventPreviewColor = value;
				this.PreventPreviewColorButton.BackColor = value;
			}
		}
		private Color _preventPreviewColor;

		public Color OverlayLabelColor
		{
			get => this._OverlayLabelColor;
			set
			{
				this._OverlayLabelColor = value;
				this.OverlayLabelColorButton.BackColor = value;
			}
		}
		private Color _OverlayLabelColor;

		public Font OverlayLabelFont
		{
			get => (Font)this._OverlayLabelFont;
			set
			{
				this._OverlayLabelFont = value;
				this.LabelOverlayLabelFont.Font = value;
			}
		}
		private Font _OverlayLabelFont;

		public new void Show()
		{
			// Registers the current instance as the application's Main Form
			this._context.MainForm = this;

			this._suppressEvents = true;
			this.FormActivated?.Invoke();
			this._suppressEvents = false;

			Application.Run(this._context);
		}

		public void BeginLoadSettings()
		{
			this._suppressEvents = true;
		}

		public void EndLoadSettings()
		{
			if (this.ThumbnailSnapToEdgesCheckBox.Checked)
			{
				this.ThumbnailSnapToGridCheckBox.Checked = false;
			}

			this.UpdateThumbnailSnapControlsState();
			this.UpdateOverwatchControlsState();
			this.UpdateCycleModeDependentUi();
			this._suppressEvents = false;
		}

		public void SetThumbnailSizeLimitations(Size minimumSize, Size maximumSize)
		{
			this._minimumSize = minimumSize;
			this._maximumSize = maximumSize;

			// Gate overwatch can be larger than normal preview caps; keep UI limits generous so values are not clipped to ThumbnailMaximumSize.
			const int focusedMaxDimension = 16384;
			this.FocusedThumbnailWidthNumericEdit.Minimum = minimumSize.Width;
			this.FocusedThumbnailWidthNumericEdit.Maximum = Math.Max(maximumSize.Width, focusedMaxDimension);
			this.FocusedThumbnailHeightNumericEdit.Minimum = minimumSize.Height;
			this.FocusedThumbnailHeightNumericEdit.Maximum = Math.Max(maximumSize.Height, focusedMaxDimension);
		}

		public void Minimize()
		{
			this.WindowState = FormWindowState.Minimized;
		}

		public void SetVersionInfo(string version)
		{
			this.VersionLabel.Text = version;
		}

		public void SetDocumentationUrl(string url)
		{
			const string forkUrl = "https://github.com/farfnarkle/eve-f-preview";
			const string upstreamUrl = "https://github.com/Proopai/eve-o-preview";
			string forumUrl = string.IsNullOrWhiteSpace(url)
				? "https://forums.eveonline.com/t/eve-o-preview-v8-0-2-0/463600"
				: url;

			string text =
				"EVE-F-Preview on GitHub\n" +
				"EVE-O Preview upstream\n" +
				"Original forum thread";

			this.DocumentationLink.Links.Clear();
			this.DocumentationLink.Text = text;

			int forkStart = text.IndexOf("EVE-F-Preview on GitHub", StringComparison.Ordinal);
			this.DocumentationLink.Links.Add(forkStart, "EVE-F-Preview on GitHub".Length, forkUrl);

			int upstreamStart = text.IndexOf("EVE-O Preview upstream", StringComparison.Ordinal);
			this.DocumentationLink.Links.Add(upstreamStart, "EVE-O Preview upstream".Length, upstreamUrl);

			int forumStart = text.IndexOf("Original forum thread", StringComparison.Ordinal);
			this.DocumentationLink.Links.Add(forumStart, "Original forum thread".Length, forumUrl);
		}

		public void AddThumbnails(IList<IThumbnailDescription> thumbnails)
		{
			this.ThumbnailsList.BeginUpdate();

			foreach (IThumbnailDescription view in thumbnails)
			{
				this.ThumbnailsList.SetItemChecked(this.ThumbnailsList.Items.Add(view), view.IsDisabled);
			}

			this.ThumbnailsList.EndUpdate();
			this.RefreshCycleGroupsActiveClients();
		}

		public void RemoveThumbnails(IList<IThumbnailDescription> thumbnails)
		{
			this.ThumbnailsList.BeginUpdate();

			foreach (IThumbnailDescription view in thumbnails)
			{
				this.ThumbnailsList.Items.Remove(view);
			}

			this.ThumbnailsList.EndUpdate();
			this.RefreshCycleGroupsActiveClients();
		}

		private void RefreshCycleGroupsActiveClients()
		{
			this.CycleGroupsSettingsControl?.SetActiveClientTitles(
				this.ThumbnailsList.Items.Cast<IThumbnailDescription>().Select(item => item.Title));
		}

		public void RefreshZoomSettings()
		{
			bool enableControls = this.EnableThumbnailZoom;
			this.ThumbnailZoomFactorNumericEdit.Enabled = enableControls;
			this.ZoomAnchorPanel.Enabled = enableControls;
		}

		public Action ApplicationExitRequested { get; set; }

		public Action FormActivated { get; set; }

		public Action FormMinimized { get; set; }

		public Action<ViewCloseRequest> FormCloseRequested { get; set; }

		public Action ApplicationSettingsChanged { get; set; }

		public Action ThumbnailsSizeChanged { get; set; }

		public Action<string> ThumbnailStateChanged { get; set; }

		public Action<string> DocumentationLinkActivated { get; set; }

		public Action CloseAllEveClientsRequested { get; set; }
		public Action RefreshPortraitsRequested { get; set; }

		public void SetRefreshPortraitsEnabled(bool enabled)
		{
			if (this.RefreshPortraitsButton == null)
			{
				return;
			}

			this.RefreshPortraitsButton.Enabled = enabled;
		}

		#region UI events
		private void CloseAllEveClients_Handler(object sender, EventArgs e)
		{
			this.CloseAllEveClientsRequested?.Invoke();
		}

		private void ContentTabControl_DrawItem(object sender, DrawItemEventArgs e)
		{
			TabControl control = (TabControl)sender;
			TabPage page = control.TabPages[e.Index];
			Rectangle bounds = control.GetTabRect(e.Index);

			Graphics graphics = e.Graphics;

			Brush textBrush = new SolidBrush(SystemColors.ActiveCaptionText);
			Brush backgroundBrush = (e.State == DrawItemState.Selected)
										? new SolidBrush(SystemColors.Control)
										: new SolidBrush(SystemColors.ControlDark);
			graphics.FillRectangle(backgroundBrush, e.Bounds);

			// Use our own font
			Font font = new Font("Arial", this.Font.Size * 1.5f, FontStyle.Bold, GraphicsUnit.Pixel);

			// Draw string and center the text
			StringFormat stringFlags = new StringFormat();
			stringFlags.Alignment = StringAlignment.Center;
			stringFlags.LineAlignment = StringAlignment.Center;

			graphics.DrawString(page.Text, font, textBrush, bounds, stringFlags);
		}

		private void OptionChanged_Handler(object sender, EventArgs e)
		{
			if (sender == this.DynamicCycleGroupCheckBox)
			{
				this.UpdateCycleModeDependentUi();
			}

			if (this._suppressEvents)
			{
				return;
			}

			this.ApplicationSettingsChanged?.Invoke();
		}

		private void UpdateCycleModeDependentUi()
		{
			this.ShortcutsSettingsControl?.SetDynamicCycleEnabled(this.DynamicCycleGroupCheckBox.Checked);
		}

		private void RefreshPortraitsButton_Click(object sender, EventArgs e)
		{
			this.RefreshPortraitsRequested?.Invoke();
		}

		private void ThumbnailSnapToEdgesCheckBox_CheckedChanged(object sender, EventArgs e)
		{
			if (this._suppressEvents)
			{
				return;
			}

			if (this.ThumbnailSnapToEdgesCheckBox.Checked)
			{
				this._suppressEvents = true;
				this.ThumbnailSnapToGridCheckBox.Checked = false;
				this._suppressEvents = false;
			}

			this.UpdateThumbnailSnapControlsState();
			this.OptionChanged_Handler(sender, e);
		}

		private void ThumbnailSnapToGridCheckBox_CheckedChanged(object sender, EventArgs e)
		{
			if (this._suppressEvents)
			{
				return;
			}

			if (this.ThumbnailSnapToGridCheckBox.Checked)
			{
				this._suppressEvents = true;
				this.ThumbnailSnapToEdgesCheckBox.Checked = false;
				this._suppressEvents = false;
			}

			this.UpdateThumbnailSnapControlsState();
			this.OptionChanged_Handler(sender, e);
		}

		private void EnableOverwatchModeCheckBox_CheckedChanged(object sender, EventArgs e)
		{
			if (this._suppressEvents)
			{
				return;
			}

			this.UpdateOverwatchControlsState();
			this.OptionChanged_Handler(sender, e);
		}

		private void UpdateOverwatchControlsState()
		{
			bool enabled = this.EnableOverwatchModeCheckBox.Checked;
			this.OverwatchWidthLabel.Enabled = enabled;
			this.OverwatchHeightLabel.Enabled = enabled;
			this.OverwatchPosXLabel.Enabled = enabled;
			this.OverwatchPosYLabel.Enabled = enabled;
			this.FocusedThumbnailWidthNumericEdit.Enabled = enabled;
			this.FocusedThumbnailHeightNumericEdit.Enabled = enabled;
			this.FocusedThumbnailLocationXNumericEdit.Enabled = enabled;
			this.FocusedThumbnailLocationYNumericEdit.Enabled = enabled;
		}

		private void UpdateThumbnailSnapControlsState()
		{
			bool edgeSnap = this.ThumbnailSnapToEdgesCheckBox.Checked;
			this.ThumbnailSnapToGridCheckBox.Enabled = !edgeSnap;
			bool gridSnapEnabled = !edgeSnap && this.ThumbnailSnapToGridCheckBox.Checked;
			this.ThumbnailSnapToGridSizeXNumericEdit.Enabled = gridSnapEnabled;
			this.ThumbnailSnapToGridSizeYNumericEdit.Enabled = gridSnapEnabled;
			this.SnapXLabel.Enabled = gridSnapEnabled;
			this.SnapYLabel.Enabled = gridSnapEnabled;
		}

		private void ThumbnailSizeChanged_Handler(object sender, EventArgs e)
		{
			if (this._suppressEvents)
			{
				return;
			}

			// Perform some View work that is not properly done in the Control
			this._suppressEvents = true;
			Size thumbnailSize = this.ThumbnailSize;
			thumbnailSize.Width = Math.Min(Math.Max(thumbnailSize.Width, this._minimumSize.Width), this._maximumSize.Width);
			thumbnailSize.Height = Math.Min(Math.Max(thumbnailSize.Height, this._minimumSize.Height), this._maximumSize.Height);
			this.ThumbnailSize = thumbnailSize;
			this._suppressEvents = false;

			this.ThumbnailsSizeChanged?.Invoke();
		}

		private void ActiveClientHighlightColorButton_Click(object sender, EventArgs e)
		{
			using (ColorDialog dialog = new ColorDialog())
			{
				dialog.Color = this.ActiveClientHighlightColor;

				if (dialog.ShowDialog() != DialogResult.OK)
				{
					return;
				}

				this.ActiveClientHighlightColor = dialog.Color;
			}

			this.OptionChanged_Handler(sender, e);
		}

		private void OverlayLabelColorButton_Click(object sender, EventArgs e)
		{
			using (ColorDialog dialog = new ColorDialog())
			{
				dialog.Color = this.OverlayLabelColor;

				if (dialog.ShowDialog() != DialogResult.OK)
				{
					return;
				}
				this.OverlayLabelColor = dialog.Color;
			}

			this.OptionChanged_Handler(sender, e);
		}

		private void ThumbnailsList_ItemCheck_Handler(object sender, ItemCheckEventArgs e)
		{
			if (!(this.ThumbnailsList.Items[e.Index] is IThumbnailDescription selectedItem))
			{
				return;
			}

			selectedItem.IsDisabled = (e.NewValue == CheckState.Checked);

			this.ThumbnailStateChanged?.Invoke(selectedItem.Title);
		}

		private void DocumentationLinkClicked_Handler(object sender, LinkLabelLinkClickedEventArgs e)
		{
			string url = e.Link?.LinkData as string;
			if (!string.IsNullOrWhiteSpace(url))
			{
				this.DocumentationLinkActivated?.Invoke(url);
			}
		}

		private void MainFormResize_Handler(object sender, EventArgs e)
		{
			if (this.WindowState != FormWindowState.Minimized)
			{
				return;
			}

			this.FormMinimized?.Invoke();
		}

		private void MainFormClosing_Handler(object sender, FormClosingEventArgs e)
		{
			ViewCloseRequest request = new ViewCloseRequest();

			this.FormCloseRequested?.Invoke(request);

			e.Cancel = !request.Allow;
		}

		private void RestoreMainForm_Handler(object sender, EventArgs e)
		{
			// This is form's GUI lifecycle event that is invariant to the Form data
			base.Show();
			this.WindowState = FormWindowState.Normal;
			this.BringToFront();
		}

		private void ExitMenuItemClick_Handler(object sender, EventArgs e)
		{
			this.ApplicationExitRequested?.Invoke();
		}
		#endregion

		private void InitZoomAnchorMap()
		{
			this._zoomAnchorMap[ViewZoomAnchor.NW] = this.ZoomAanchorNWRadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.N] = this.ZoomAanchorNRadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.NE] = this.ZoomAanchorNERadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.W] = this.ZoomAanchorWRadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.C] = this.ZoomAanchorCRadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.E] = this.ZoomAanchorERadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.SW] = this.ZoomAanchorSWRadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.S] = this.ZoomAanchorSRadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.SE] = this.ZoomAanchorSERadioButton;
		}
		private void InitOverlayLabelMap()
		{
			this._overlayLabelMap[ViewZoomAnchor.NW] = this.OverlayLabelNWRadioButton;
			this._overlayLabelMap[ViewZoomAnchor.N] = this.OverlayLabelNRadioButton;
			this._overlayLabelMap[ViewZoomAnchor.NE] = this.OverlayLabelNERadioButton;
			this._overlayLabelMap[ViewZoomAnchor.W] = this.OverlayLabelWRadioButton;
			this._overlayLabelMap[ViewZoomAnchor.C] = this.OverlayLabelCRadioButton;
			this._overlayLabelMap[ViewZoomAnchor.E] = this.OverlayLabelERadioButton;
			this._overlayLabelMap[ViewZoomAnchor.SW] = this.OverlayLabelSWRadioButton;
			this._overlayLabelMap[ViewZoomAnchor.S] = this.OverlayLabelSRadioButton;
			this._overlayLabelMap[ViewZoomAnchor.SE] = this.OverlayLabelSERadioButton;
		}
		private void InitCycleGroupIndicatorMap()
		{
			this._cycleGroupIndicatorMap[ViewZoomAnchor.NW] = this.CycleGroupIndicatorNWRadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.N] = this.CycleGroupIndicatorNRadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.NE] = this.CycleGroupIndicatorNERadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.W] = this.CycleGroupIndicatorWRadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.C] = this.CycleGroupIndicatorCRadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.E] = this.CycleGroupIndicatorERadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.SW] = this.CycleGroupIndicatorSWRadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.S] = this.CycleGroupIndicatorSRadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.SE] = this.CycleGroupIndicatorSERadioButton;
		}

		private void InitFormSize()
		{
			const int BUFFER_PIXEL_AMOUNT = 8;
			// resize form height based on tabbed control item height
			var tabControl = (System.Windows.Forms.TabControl)this.Controls.Find("ContentTabControl", false).First();
			if (tabControl != null)
			{
				var furnitureSize = this.Height - tabControl.Height;
				var calculatedHeight = (tabControl.ItemSize.Width * tabControl.Controls.Count) + furnitureSize + BUFFER_PIXEL_AMOUNT;
				if (this.Height < calculatedHeight)
				{
					this.Height = calculatedHeight;
				}
			}
		}

		private void btnLabelFont_Click(object sender, EventArgs e)
		{
			FontDialog fontSelector = new FontDialog();
			fontSelector.Font = OverlayLabelFont;
			fontSelector.ShowColor = false;
			fontSelector.ShowApply = false;
			fontSelector.ShowHelp = false;
			if (fontSelector.ShowDialog() != DialogResult.Cancel)
			{
				OverlayLabelFont = fontSelector.Font;
				LabelOverlayLabelFont.Font = fontSelector.Font;
				this.OptionChanged_Handler(sender, e);
			}
		}

		private void PreventPreviewColorButton_Click(object sender, EventArgs e)
		{
			using (ColorDialog dialog = new ColorDialog())
			{
				dialog.Color = this.PreventPreviewColor;

				if (dialog.ShowDialog() != DialogResult.OK)
				{
					return;
				}

				this.PreventPreviewColor = dialog.Color;
			}

			this.OptionChanged_Handler(sender, e);

		}
	}
}