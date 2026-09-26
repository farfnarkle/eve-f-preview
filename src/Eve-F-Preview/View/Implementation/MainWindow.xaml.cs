using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EveFPreview.Configuration;
using EveFPreview.View.Controls;
using DrawingColor = System.Drawing.Color;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;

namespace EveFPreview.View
{
	public partial class MainWindow : Window, IMainFormView
	{
		#region Private fields
		private const string DefaultIconResource = "original-icon.ico";

		// Tray/window icon choices (IconName in the config) and the file each one maps to.
		private static readonly Dictionary<string, string> IconResources = new Dictionary<string, string>
		{
			{ "IconAmber", "Icons/EVE-O_Amber.ico" },
			{ "IconBlue", "Icons/EVE-O_Blue.ico" },
			{ "IconCherry", "Icons/EVE-O_Cherry.ico" },
			{ "IconDal", "Icons/EVE-O_Dal.ico" },
			{ "IconDark", "Icons/EVE-O_Dark.ico" },
			{ "IconDefault", "Icons/EVE-O_Default.ico" },
			{ "IconMint", "Icons/EVE-O_Mint.ico" },
			{ "IconPurple", "Icons/EVE-O_Purple.ico" },
			{ "IconUrns", "Icons/EVE-O_urns.ico" }
		};

		private readonly ObservableCollection<CheckableItem<IThumbnailDescription>> _thumbnailItems = new ObservableCollection<CheckableItem<IThumbnailDescription>>();
		private readonly TrayIcon _trayIcon;
		private bool _suppressEvents;
		private DrawingSize _minimumSize;
		private DrawingSize _maximumSize;
		private string _iconName;
		private IConfigurationStorage _configurationStorage;
		private int _lastOpacityValue;

		// No longer shown in the UI, but still round-tripped through the config.
		private DrawingPoint _newPreviewSpawnLocation;
		private bool _newPreviewAutoTile;

		private DrawingColor _activeClientHighlightColor;
		private DrawingColor _preventPreviewColor;
		private DrawingColor _overlayLabelColor;
		private OverlayFont _overlayLabelFont;

		private string _updatePopupShownFor;
		private UpdateAvailableWindow _updatePopup;

		// Theme preference as stored in the config: "Dark", "Light" or empty (follow Windows).
		private string _uiTheme = string.Empty;
		private bool _suppressThemeToggle;

		// Width / height of the thumbnails, kept while "maintain aspect ratio" is on.
		private double _thumbnailAspectRatio;

		// Saves the window size once the user has finished resizing it.
		private readonly DispatcherTimer _windowSizeSaveTimer;
		#endregion

		public MainWindow()
		{
			this._suppressEvents = false;
			this._minimumSize = new DrawingSize(20, 20);
			this._maximumSize = new DrawingSize(20, 20);

			this.InitializeComponent();

			this.ThumbnailsList.ItemsSource = this._thumbnailItems;
			this._thumbnailItems.CollectionChanged += (_, _) =>
				this.NoClientsText.Visibility = this._thumbnailItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
			this.AnimationStyleCombo.ItemsSource = Enum.GetValues(typeof(ViewAnimationStyle));
			this.ShortcutsSettingsControl.SettingsChanged = this.ShortcutsSettingsChanged_Handler;
			this.ShowThemeToggleState();
			this.NavList.SelectedIndex = 0;

			this._windowSizeSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
			this._windowSizeSaveTimer.Tick += (_, _) =>
			{
				this._windowSizeSaveTimer.Stop();
				this.OptionChanged_Handler(this, EventArgs.Empty);
			};

			this._trayIcon = this.CreateTrayIcon();
		}

		public DrawingSize SettingsWindowSize
		{
			get
			{
				// Maximized or minimized: remember the normal size it goes back to.
				Rect bounds = this.WindowState != WindowState.Normal && !this.RestoreBounds.IsEmpty
					? this.RestoreBounds
					: new Rect(0, 0, this.Width, this.Height);
				return new DrawingSize((int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height));
			}
			set
			{
				if (value.Width <= 0 || value.Height <= 0)
				{
					return;
				}

				// Never larger than the screen (e.g. saved on a bigger monitor).
				Rect workArea = SystemParameters.WorkArea;
				this.Width = Math.Max(this.MinWidth, Math.Min(value.Width, workArea.Width));
				this.Height = Math.Max(this.MinHeight, Math.Min(value.Height, workArea.Height));
			}
		}

		public bool SettingsWindowTopmost
		{
			get => this.SettingsWindowTopmostCheckBox.IsChecked == true;
			set
			{
				this.SettingsWindowTopmostCheckBox.IsChecked = value;
				this.Topmost = value;
			}
		}

		public bool MaintainThumbnailAspectRatio
		{
			get => this.MaintainAspectRatioCheckBox.IsChecked == true;
			set => this.MaintainAspectRatioCheckBox.IsChecked = value;
		}

		private void SettingsWindowTopmostCheckBox_Changed(object sender, RoutedEventArgs e)
		{
			this.Topmost = this.SettingsWindowTopmostCheckBox.IsChecked == true;
			this.OptionChanged_Handler(sender, e);
		}

		private void MaintainAspectRatioCheckBox_Changed(object sender, RoutedEventArgs e)
		{
			// Lock in the ratio the thumbnails have right now.
			this._thumbnailAspectRatio = MainWindow.AspectRatioOf(this.ThumbnailSize);
			this.OptionChanged_Handler(sender, e);
		}

		private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			// Only sizes the user picks: not the initial layout, and not maximizing.
			if (!this.IsLoaded || this.WindowState != WindowState.Normal || this._suppressEvents)
			{
				return;
			}

			this._windowSizeSaveTimer.Stop();
			this._windowSizeSaveTimer.Start();
		}

		private static double AspectRatioOf(DrawingSize size)
		{
			return size.Width > 0 && size.Height > 0 ? (double)size.Width / size.Height : 0;
		}

		private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			string pageName = (this.NavList.SelectedItem as ListBoxItem)?.Tag as string;
			foreach (UIElement page in this.PagesHost.Children)
			{
				page.Visibility = (page as FrameworkElement)?.Name == pageName ? Visibility.Visible : Visibility.Collapsed;
			}
		}

		public string UiTheme
		{
			get => this._uiTheme;
			set
			{
				this._uiTheme = value ?? string.Empty;
				ThemeManager.Apply(ThemeManager.ResolveIsDark(this._uiTheme));
				this.ShowThemeToggleState();
			}
		}

		private void ShowThemeToggleState()
		{
			this._suppressThemeToggle = true;
			try
			{
				this.DarkModeToggle.IsChecked = ThemeManager.IsDark;
				this.ThemeIcon.Text = ThemeManager.IsDark ? "\uE708" : "\uE706";
			}
			finally
			{
				this._suppressThemeToggle = false;
			}
		}

		private void DarkModeToggle_Changed(object sender, RoutedEventArgs e)
		{
			if (this._suppressThemeToggle)
			{
				return;
			}

			// An explicit choice from here on; until the user flips it, the theme follows Windows.
			this._uiTheme = this.DarkModeToggle.IsChecked == true ? ThemeManager.Dark : ThemeManager.Light;
			ThemeManager.Apply(this.DarkModeToggle.IsChecked == true);
			this.ShowThemeToggleState();
			this.OptionChanged_Handler(sender, e);
		}

		private TrayIcon CreateTrayIcon()
		{
			var menu = new ContextMenu();
			menu.Items.Add(new MenuItem { Header = "EVE-F-Preview", IsEnabled = false, FontWeight = FontWeights.SemiBold });

			var restoreItem = new MenuItem { Header = "Restore" };
			restoreItem.Click += (_, _) => this.RestoreMainWindow();
			menu.Items.Add(restoreItem);

			var closeAllItem = new MenuItem { Header = "Close all EVE clients" };
			closeAllItem.Click += this.CloseAllEveClients_Handler;
			menu.Items.Add(closeAllItem);

			menu.Items.Add(new Separator());

			var exitItem = new MenuItem { Header = "Exit" };
			exitItem.Click += (_, _) => this.ApplicationExitRequested?.Invoke();
			menu.Items.Add(exitItem);

			var trayIcon = new TrayIcon { ContextMenu = menu, ToolTip = "EVE-F-Preview" };
			trayIcon.DoubleClick += (_, _) => this.RestoreMainWindow();
			trayIcon.SetIcon(MainWindow.ReadResource(MainWindow.DefaultIconResource));
			trayIcon.Show();
			return trayIcon;
		}

		public void SetConfigurationStorage(IConfigurationStorage configurationStorage)
		{
			this._configurationStorage = configurationStorage;
			this.RefreshConfigProfileList();
		}

		public Action ConfigProfileChanged { get; set; }

		private void RefreshConfigProfileList(string preferredSelection = null)
		{
			if (this._configurationStorage == null)
			{
				return;
			}

			string activeFileName = Path.GetFileName(this._configurationStorage.ActiveConfigPath);
			string select = preferredSelection ?? activeFileName;

			List<string> profiles = this._configurationStorage.ListConfigProfiles().ToList();
			if (!string.IsNullOrEmpty(select) && !profiles.Contains(select))
			{
				// Not on disk as a recognizable config yet (e.g. brand-new file) - show it anyway.
				profiles.Add(select);
			}

			this.ConfigProfileCombo.ItemsSource = profiles;
			this.ConfigProfileCombo.SelectedItem = string.IsNullOrEmpty(select) ? null : select;
		}

		private void LoadConfigProfileButton_Click(object sender, RoutedEventArgs e)
		{
			if (this._configurationStorage == null || !(this.ConfigProfileCombo.SelectedItem is string profile))
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
					MessageBoxButton.OK, MessageBoxImage.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, "Could not load profile:\n" + ex.Message, "Config profile",
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void SaveConfigProfileAsButton_Click(object sender, RoutedEventArgs e)
		{
			if (this._configurationStorage == null)
			{
				return;
			}

			var dialog = new Microsoft.Win32.SaveFileDialog
			{
				Filter = "JSON config (*.json)|*.json|All files (*.*)|*.*",
				InitialDirectory = AppContext.BaseDirectory,
				FileName = "EVE-F-Preview-profile.json"
			};

			if (dialog.ShowDialog(this) != true)
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
					MessageBoxButton.OK, MessageBoxImage.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, "Could not save profile:\n" + ex.Message, "Config profile",
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void ImportConfigProfileButton_Click(object sender, RoutedEventArgs e)
		{
			if (this._configurationStorage == null)
			{
				return;
			}

			var openDialog = new Microsoft.Win32.OpenFileDialog
			{
				Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
				Title = "Select EVE-O / EVE-F / EVE-X settings file to import"
			};

			if (openDialog.ShowDialog(this) != true)
			{
				return;
			}

			string sourcePath = openDialog.FileName;

			var saveDialog = new Microsoft.Win32.SaveFileDialog
			{
				Filter = "JSON config (*.json)|*.json",
				InitialDirectory = AppContext.BaseDirectory,
				FileName = "EVE-F-Preview-imported.json",
				Title = "Save imported profile as"
			};

			if (saveDialog.ShowDialog(this) != true)
			{
				return;
			}

			string destinationFileName = Path.GetFileName(saveDialog.FileName);

			try
			{
				this._configurationStorage.ImportFrom(sourcePath, destinationFileName);
				this.RefreshConfigProfileList(destinationFileName);

				MessageBoxResult switchNow = MessageBox.Show(this,
					"Imported settings into " + destinationFileName + ".\n\nSwitch to this profile now?",
					"Import settings",
					MessageBoxButton.YesNo,
					MessageBoxImage.Question);

				if (switchNow == MessageBoxResult.Yes)
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
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		public void SetSettingsSyncConfiguration(IThumbnailConfiguration configuration, Action persistConfiguration = null)
		{
			this.SettingsSyncControl.PersistConfiguration = persistConfiguration;
			this.SettingsSyncControl.SetConfiguration(configuration);
		}

		public void SetCycleGroupsConfiguration(IThumbnailConfiguration configuration, Action persistConfiguration = null)
		{
			this.CycleGroupsSettingsControl.PersistConfiguration = persistConfiguration;
			this.CycleGroupsSettingsControl.SetConfiguration(configuration);
		}

		public GlobalShortcutSettings GetGlobalShortcutSettings()
		{
			return this.ShortcutsSettingsControl.GetSettings();
		}

		public void SetGlobalShortcutSettings(GlobalShortcutSettings settings)
		{
			if (settings == null)
			{
				return;
			}

			this.ShortcutsSettingsControl.SetSettings(settings);
		}

		public void ConfigureShortcutHotkeyRecording(Action suspendGlobalHotkeys, Action resumeGlobalHotkeys)
		{
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
			get => this.MinimizeToTrayCheckBox.IsChecked == true;
			set => this.MinimizeToTrayCheckBox.IsChecked = value;
		}

		public bool StartMinimized
		{
			get => this.StartMinimizedCheckBox.IsChecked == true;
			set => this.StartMinimizedCheckBox.IsChecked = value;
		}

		public bool CheckForUpdates
		{
			get => this.CheckForUpdatesCheckBox.IsChecked == true;
			set => this.CheckForUpdatesCheckBox.IsChecked = value;
		}

		public string IconName
		{
			get => this._iconName;
			set
			{
				this._iconName = value;

				// Unknown or empty names leave the current icon in place, as before.
				if (value != null && MainWindow.IconResources.TryGetValue(value, out string resource))
				{
					try
					{
						this.Icon = BitmapFrame.Create(new Uri("pack://application:,,,/EVE-F-Preview;component/" + resource));
						this._trayIcon.SetIcon(MainWindow.ReadResource(resource));
					}
					catch (Exception)
					{
						// A bad icon file shouldn't take the settings window down with it.
					}
				}

				if (value != "")
				{
					this.ApplicationSettingsChanged?.Invoke();
				}
			}
		}

		public double ThumbnailOpacity
		{
			get => Math.Min(this.ThumbnailOpacitySlider.Value / 100.00, 1.00);
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

				this.ThumbnailOpacitySlider.Value = barValue;
			}
		}

		public bool EnableClientLayoutTracking
		{
			get => this.EnableClientLayoutTrackingCheckBox.IsChecked == true;
			set => this.EnableClientLayoutTrackingCheckBox.IsChecked = value;
		}

		public bool HideActiveClientThumbnail
		{
			get => this.HideActiveClientThumbnailCheckBox.IsChecked == true;
			set => this.HideActiveClientThumbnailCheckBox.IsChecked = value;
		}

		public bool MinimizeInactiveClients
		{
			get => this.MinimizeInactiveClientsCheckBox.IsChecked == true;
			set => this.MinimizeInactiveClientsCheckBox.IsChecked = value;
		}

		public bool HideCaptionOnClients
		{
			get => this.HideCaptionOnClientsCheckBox.IsChecked == true;
			set => this.HideCaptionOnClientsCheckBox.IsChecked = value;
		}

		public ViewAnimationStyle WindowsAnimationStyle
		{
			get => this.AnimationStyleCombo.SelectedItem is ViewAnimationStyle style ? style : ViewAnimationStyle.OriginalAnimation;
			set => this.AnimationStyleCombo.SelectedItem = value;
		}

		public bool ShowThumbnailsAlwaysOnTop
		{
			get => this.ShowThumbnailsAlwaysOnTopCheckBox.IsChecked == true;
			set => this.ShowThumbnailsAlwaysOnTopCheckBox.IsChecked = value;
		}

		public bool PreventPreviews
		{
			get => this.PreventPreviewsCheckBox.IsChecked == true;
			set => this.PreventPreviewsCheckBox.IsChecked = value;
		}

		public bool HideThumbnailsOnLostFocus
		{
			get => this.HideThumbnailsOnLostFocusCheckBox.IsChecked == true;
			set => this.HideThumbnailsOnLostFocusCheckBox.IsChecked = value;
		}

		public bool OnlyRegisterCycleHotkeysWhenEveFocused
		{
			get => this.OnlyRegisterCycleHotkeysWhenEveFocusedCheckBox.IsChecked == true;
			set => this.OnlyRegisterCycleHotkeysWhenEveFocusedCheckBox.IsChecked = value;
		}

		public bool EnableCycleMouseDoubleClickProtection
		{
			get => this.EnableCycleMouseDoubleClickProtectionCheckBox.IsChecked == true;
			set => this.EnableCycleMouseDoubleClickProtectionCheckBox.IsChecked = value;
		}

		public bool DynamicCycleGroup
		{
			get => this.DynamicCycleGroupCheckBox.IsChecked == true;
			set => this.DynamicCycleGroupCheckBox.IsChecked = value;
		}

		public bool EnableAccountBasedThumbnailPositioning
		{
			get => this.EnableAccountBasedThumbnailPositioningCheckBox.IsChecked == true;
			set => this.EnableAccountBasedThumbnailPositioningCheckBox.IsChecked = value;
		}

		public bool EnableAutoSettingsSync
		{
			get => this.EnableAutoSettingsSyncCheckBox.IsChecked == true;
			set => this.EnableAutoSettingsSyncCheckBox.IsChecked = value;
		}

		public void SetAutoSettingsSyncStatus(bool success, string message)
		{
			UiThread.Run(() =>
			{
				this.AutoSettingsSyncStatusLabel.Text = message ?? string.Empty;
				this.AutoSettingsSyncStatusLabel.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
				this.AutoSettingsSyncStatusLabel.SetResourceReference(TextBlock.ForegroundProperty, success ? "SuccessTextBrush" : "WarningTextBrush");
			});
		}

		public bool EnablePerClientThumbnailLayouts
		{
			get => this.EnablePerClientThumbnailsLayoutsCheckBox.IsChecked == true;
			set => this.EnablePerClientThumbnailsLayoutsCheckBox.IsChecked = value;
		}

		public bool EnableCharacterIndicator
		{
			get => this.EnableCharacterIndicatorCheckBox.IsChecked == true;
			set => this.EnableCharacterIndicatorCheckBox.IsChecked = value;
		}

		public bool LockCharacterIndicatorLocation
		{
			get => this.LockCharacterIndicatorLocationCheckBox.IsChecked == true;
			set => this.LockCharacterIndicatorLocationCheckBox.IsChecked = value;
		}

		public bool EnableCharacterIndicatorClickToActivate
		{
			get => this.EnableCharacterIndicatorClickToActivateCheckBox.IsChecked == true;
			set => this.EnableCharacterIndicatorClickToActivateCheckBox.IsChecked = value;
		}

		public DrawingSize ThumbnailSize
		{
			get => new DrawingSize((int)this.ThumbnailsWidthNumericEdit.Value, (int)this.ThumbnailsHeightNumericEdit.Value);
			set
			{
				this.ThumbnailsWidthNumericEdit.Value = value.Width;
				this.ThumbnailsHeightNumericEdit.Value = value.Height;
				this._thumbnailAspectRatio = MainWindow.AspectRatioOf(value);
			}
		}

		public bool EnableOverwatchMode
		{
			get => this.EnableOverwatchModeCheckBox.IsChecked == true;
			set => this.EnableOverwatchModeCheckBox.IsChecked = value;
		}

		public DrawingSize FocusedThumbnailSize
		{
			get => new DrawingSize((int)this.FocusedThumbnailWidthNumericEdit.Value, (int)this.FocusedThumbnailHeightNumericEdit.Value);
			set
			{
				this.FocusedThumbnailWidthNumericEdit.Value = value.Width;
				this.FocusedThumbnailHeightNumericEdit.Value = value.Height;
			}
		}

		public DrawingPoint FocusedThumbnailLocation
		{
			get => new DrawingPoint((int)this.FocusedThumbnailLocationXNumericEdit.Value, (int)this.FocusedThumbnailLocationYNumericEdit.Value);
			set
			{
				this.FocusedThumbnailLocationXNumericEdit.Value = value.X;
				this.FocusedThumbnailLocationYNumericEdit.Value = value.Y;
			}
		}

		public DrawingPoint NewPreviewSpawnLocation
		{
			get => this._newPreviewSpawnLocation;
			set => this._newPreviewSpawnLocation = new DrawingPoint(
				Math.Clamp(value.X, -50000, 50000),
				Math.Clamp(value.Y, -50000, 50000));
		}

		public bool NewPreviewAutoTile
		{
			get => this._newPreviewAutoTile;
			set => this._newPreviewAutoTile = value;
		}

		public bool EnableThumbnailZoom
		{
			get => this.EnableThumbnailZoomCheckBox.IsChecked == true;
			set
			{
				this.EnableThumbnailZoomCheckBox.IsChecked = value;
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
			get => this.ZoomAnchorSelector.SelectedAnchor;
			set => this.ZoomAnchorSelector.SelectedAnchor = value;
		}

		public ViewZoomAnchor OverlayLabelAnchor
		{
			get => this.OverlayLabelAnchorSelector.SelectedAnchor;
			set => this.OverlayLabelAnchorSelector.SelectedAnchor = value;
		}

		public ViewZoomAnchor CycleGroupIndicatorAnchor
		{
			get => this.CycleGroupIndicatorAnchorSelector.SelectedAnchor;
			set => this.CycleGroupIndicatorAnchorSelector.SelectedAnchor = value;
		}

		public bool ShowThumbnailOverlays
		{
			get => this.ShowThumbnailOverlaysCheckBox.IsChecked == true;
			set => this.ShowThumbnailOverlaysCheckBox.IsChecked = value;
		}

		public bool ShowThumbnailFrames
		{
			get => this.ShowThumbnailFramesCheckBox.IsChecked == true;
			set => this.ShowThumbnailFramesCheckBox.IsChecked = value;
		}

		public bool ShowSystemNameOnThumbnail
		{
			get => this.ShowSystemNameOnThumbnailCheckBox.IsChecked == true;
			set => this.ShowSystemNameOnThumbnailCheckBox.IsChecked = value;
		}

		public bool LockThumbnailLocation
		{
			get => this.LockThumbnailLocationCheckbox.IsChecked == true;
			set => this.LockThumbnailLocationCheckbox.IsChecked = value;
		}

		public bool ThumbnailSnapToEdges
		{
			get => this.ThumbnailSnapToEdgesCheckBox.IsChecked == true;
			set => this.ThumbnailSnapToEdgesCheckBox.IsChecked = value;
		}

		public bool ThumbnailSnapToGrid
		{
			get => this.ThumbnailSnapToGridCheckBox.IsChecked == true;
			set => this.ThumbnailSnapToGridCheckBox.IsChecked = value;
		}

		public int ThumbnailSnapToGridSizeX
		{
			get => (int)this.ThumbnailSnapToGridSizeXNumericEdit.Value;
			set => this.ThumbnailSnapToGridSizeXNumericEdit.Value = value;
		}

		public int ThumbnailSnapToGridSizeY
		{
			get => (int)this.ThumbnailSnapToGridSizeYNumericEdit.Value;
			set => this.ThumbnailSnapToGridSizeYNumericEdit.Value = value;
		}

		public bool EnableActiveClientHighlight
		{
			get => this.EnableActiveClientHighlightCheckBox.IsChecked == true;
			set => this.EnableActiveClientHighlightCheckBox.IsChecked = value;
		}

		public DrawingColor ActiveClientHighlightColor
		{
			get => this._activeClientHighlightColor;
			set
			{
				this._activeClientHighlightColor = value;
				this.ActiveClientHighlightColorButton.Background = MainWindow.ToBrush(value);
			}
		}

		public DrawingColor PreventPreviewColor
		{
			get => this._preventPreviewColor;
			set
			{
				this._preventPreviewColor = value;
				this.PreventPreviewColorButton.Background = MainWindow.ToBrush(value);
			}
		}

		public DrawingColor OverlayLabelColor
		{
			get => this._overlayLabelColor;
			set
			{
				this._overlayLabelColor = value;
				this.OverlayLabelColorButton.Background = MainWindow.ToBrush(value);
				this.OverlayLabelFontPreview.Foreground = MainWindow.ToBrush(value);
			}
		}

		public OverlayFont OverlayLabelFont
		{
			get => this._overlayLabelFont;
			set
			{
				this._overlayLabelFont = value;
				this.ShowFontPreview(value);
			}
		}

		/// <summary>
		/// Starts the UI: loads settings into the window (via the presenter), then runs the WPF
		/// application with this as its main window until it closes.
		/// </summary>
		public new void Show()
		{
			Application.Current.MainWindow = this;

			this._suppressEvents = true;
			this.FormActivated?.Invoke();
			this._suppressEvents = false;

			Application.Current.Run(this);
		}

		public void BeginLoadSettings()
		{
			this._suppressEvents = true;
		}

		public void EndLoadSettings()
		{
			if (this.ThumbnailSnapToEdgesCheckBox.IsChecked == true)
			{
				this.ThumbnailSnapToGridCheckBox.IsChecked = false;
			}

			this.UpdateThumbnailSnapControlsState();
			this.UpdateOverwatchControlsState();
			this.UpdateCycleModeDependentUi();
			this.UpdateDependentRows();
			this._suppressEvents = false;
		}

		public void SetThumbnailSizeLimitations(DrawingSize minimumSize, DrawingSize maximumSize)
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
			this.WindowState = WindowState.Minimized;
		}

		public void SetVersionInfo(string version)
		{
			this.VersionLabel.Text = version;
		}

		public void SetUpdateAvailable(string version, string url, bool showPopup)
		{
			this.UpdateBannerTitle.Text = "Update available: " + version;
			this.UpdateReleaseButton.Tag = url;
			this.UpdateBanner.Visibility = Visibility.Visible;

			// The About tab link is easy to miss, so also pop up a small dialog - once per version
			// per run, and never for a version the user asked not to be told about again.
			if (!showPopup || this._updatePopupShownFor == version || this._updatePopup != null)
			{
				return;
			}

			this._updatePopupShownFor = version;
			this._updatePopup = new UpdateAvailableWindow(this.VersionLabel.Text, version, url, this.DocumentationLinkActivated);
			this._updatePopup.Closed += (s, e) =>
			{
				bool dontShowAgain = this._updatePopup != null && this._updatePopup.DontShowAgain;
				this._updatePopup = null;
				if (dontShowAgain)
				{
					this.UpdateVersionDismissed?.Invoke(version);
				}
			};
			this._updatePopup.Show();
		}

		public Action<string> UpdateVersionDismissed { get; set; }

		public void SetDocumentationUrl(string url)
		{
			const string forkUrl = "https://github.com/farfnarkle/eve-f-preview";
			const string upstreamUrl = "https://github.com/Proopai/eve-o-preview";
			string forumUrl = string.IsNullOrWhiteSpace(url)
				? "https://forums.eveonline.com/t/eve-o-preview-v8-0-2-0/463600"
				: url;

			this.ForkLinkButton.Tag = forkUrl;
			this.UpstreamLinkButton.Tag = upstreamUrl;
			this.ForumLinkButton.Tag = forumUrl;
		}

		public void AddThumbnails(IList<IThumbnailDescription> thumbnails)
		{
			foreach (IThumbnailDescription view in thumbnails)
			{
				this._thumbnailItems.Add(new CheckableItem<IThumbnailDescription>(view, view.Title, view.IsDisabled, this.ThumbnailItemChecked));
			}

			this.RefreshCycleGroupsActiveClients();
		}

		public void RemoveThumbnails(IList<IThumbnailDescription> thumbnails)
		{
			foreach (IThumbnailDescription view in thumbnails)
			{
				CheckableItem<IThumbnailDescription> item = this._thumbnailItems.FirstOrDefault(x => ReferenceEquals(x.Value, view));
				if (item != null)
				{
					this._thumbnailItems.Remove(item);
				}
			}

			this.RefreshCycleGroupsActiveClients();
		}

		private void RefreshCycleGroupsActiveClients()
		{
			this.CycleGroupsSettingsControl.SetActiveClientTitles(this._thumbnailItems.Select(item => item.Value.Title));
		}

		public void RefreshZoomSettings()
		{
			bool enableControls = this.EnableThumbnailZoom;
			this.ZoomFactorRow.IsEnabled = enableControls;
			this.ZoomAnchorRow.IsEnabled = enableControls;
		}

		/// <summary>Greys out options that only matter while the setting above them is on.</summary>
		private void UpdateDependentRows()
		{
			this.PreventPreviewColorRow.IsEnabled = this.PreventPreviewsCheckBox.IsChecked == true;
			this.HighlightColorRow.IsEnabled = this.EnableActiveClientHighlightCheckBox.IsChecked == true;
			bool indicator = this.EnableCharacterIndicatorCheckBox.IsChecked == true;
			this.IndicatorLockRow.IsEnabled = indicator;
			this.IndicatorClickRow.IsEnabled = indicator;
			bool labels = this.ShowThumbnailOverlaysCheckBox.IsChecked == true;
			this.LabelFontRow.IsEnabled = labels;
			this.LabelColorRow.IsEnabled = labels;
			this.LabelPositionRow.IsEnabled = labels;
			this.RefreshZoomSettings();
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
			this.RefreshPortraitsButton.IsEnabled = enabled;
		}

		#region UI events
		private void CloseAllEveClients_Handler(object sender, RoutedEventArgs e)
		{
			this.CloseAllEveClientsRequested?.Invoke();
		}

		private void OptionChanged_Handler(object sender, EventArgs e)
		{
			if (sender == this.DynamicCycleGroupCheckBox)
			{
				this.UpdateCycleModeDependentUi();
			}

			if (this.IsInitialized)
			{
				this.UpdateDependentRows();
			}

			if (this._suppressEvents)
			{
				return;
			}

			this.ApplicationSettingsChanged?.Invoke();
		}

		private void ThumbnailOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{
			// The old TrackBar only produced whole steps; keep it that way so dragging doesn't save the config on every sub-pixel move.
			double rounded = Math.Round(e.NewValue);
			if (rounded != e.NewValue)
			{
				this.ThumbnailOpacitySlider.Value = rounded;
				return;
			}

			int value = (int)rounded;
			if (this.OpacityValueText != null)
			{
				this.OpacityValueText.Text = value + "%";
			}

			if (value == this._lastOpacityValue)
			{
				return;
			}

			this._lastOpacityValue = value;
			this.OptionChanged_Handler(sender, e);
		}

		private void UpdateCycleModeDependentUi()
		{
			this.ShortcutsSettingsControl?.SetDynamicCycleEnabled(this.DynamicCycleGroupCheckBox.IsChecked == true);
		}

		private void RefreshPortraitsButton_Click(object sender, RoutedEventArgs e)
		{
			this.RefreshPortraitsRequested?.Invoke();
		}

		private void ThumbnailSnapToEdgesCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
		{
			if (this._suppressEvents)
			{
				return;
			}

			if (this.ThumbnailSnapToEdgesCheckBox.IsChecked == true)
			{
				this._suppressEvents = true;
				this.ThumbnailSnapToGridCheckBox.IsChecked = false;
				this._suppressEvents = false;
			}

			this.UpdateThumbnailSnapControlsState();
			this.OptionChanged_Handler(sender, e);
		}

		private void ThumbnailSnapToGridCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
		{
			if (this._suppressEvents)
			{
				return;
			}

			if (this.ThumbnailSnapToGridCheckBox.IsChecked == true)
			{
				this._suppressEvents = true;
				this.ThumbnailSnapToEdgesCheckBox.IsChecked = false;
				this._suppressEvents = false;
			}

			this.UpdateThumbnailSnapControlsState();
			this.OptionChanged_Handler(sender, e);
		}

		private void EnableOverwatchModeCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
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
			bool enabled = this.EnableOverwatchModeCheckBox.IsChecked == true;
			this.OverwatchSizeRow.IsEnabled = enabled;
			this.OverwatchPositionRow.IsEnabled = enabled;
		}

		private void UpdateThumbnailSnapControlsState()
		{
			bool edgeSnap = this.ThumbnailSnapToEdgesCheckBox.IsChecked == true;
			this.SnapToGridRow.IsEnabled = !edgeSnap;
			this.SnapGridSizeRow.IsEnabled = !edgeSnap && this.ThumbnailSnapToGridCheckBox.IsChecked == true;
		}

		private void ThumbnailSizeChanged_Handler(object sender, EventArgs e)
		{
			if (this._suppressEvents)
			{
				return;
			}

			// Perform some View work that is not properly done in the Control
			this._suppressEvents = true;
			DrawingSize thumbnailSize = this.ThumbnailSize;
			bool keepRatio = this.MaintainThumbnailAspectRatio && this._thumbnailAspectRatio > 0;
			if (keepRatio && sender == this.ThumbnailsWidthNumericEdit)
			{
				thumbnailSize.Height = (int)Math.Round(thumbnailSize.Width / this._thumbnailAspectRatio);
			}
			else if (keepRatio && sender == this.ThumbnailsHeightNumericEdit)
			{
				thumbnailSize.Width = (int)Math.Round(thumbnailSize.Height * this._thumbnailAspectRatio);
			}

			thumbnailSize.Width = Math.Min(Math.Max(thumbnailSize.Width, this._minimumSize.Width), this._maximumSize.Width);
			thumbnailSize.Height = Math.Min(Math.Max(thumbnailSize.Height, this._minimumSize.Height), this._maximumSize.Height);
			this.ThumbnailsWidthNumericEdit.Value = thumbnailSize.Width;
			this.ThumbnailsHeightNumericEdit.Value = thumbnailSize.Height;
			if (!keepRatio)
			{
				this._thumbnailAspectRatio = MainWindow.AspectRatioOf(thumbnailSize);
			}

			this._suppressEvents = false;

			this.ThumbnailsSizeChanged?.Invoke();
		}

		private void ActiveClientHighlightColorButton_Click(object sender, RoutedEventArgs e)
		{
			if (!NativeDialogs.TryPickColor(this, this.ActiveClientHighlightColor, out DrawingColor color))
			{
				return;
			}

			this.ActiveClientHighlightColor = color;
			this.OptionChanged_Handler(sender, e);
		}

		private void OverlayLabelColorButton_Click(object sender, RoutedEventArgs e)
		{
			if (!NativeDialogs.TryPickColor(this, this.OverlayLabelColor, out DrawingColor color))
			{
				return;
			}

			this.OverlayLabelColor = color;
			this.OptionChanged_Handler(sender, e);
		}

		private void PreventPreviewColorButton_Click(object sender, RoutedEventArgs e)
		{
			if (!NativeDialogs.TryPickColor(this, this.PreventPreviewColor, out DrawingColor color))
			{
				return;
			}

			this.PreventPreviewColor = color;
			this.OptionChanged_Handler(sender, e);
		}

		private void LabelFontButton_Click(object sender, RoutedEventArgs e)
		{
			if (!NativeDialogs.TryPickFont(this, this.OverlayLabelFont, out OverlayFont font))
			{
				return;
			}

			this.OverlayLabelFont = font;
			this.OptionChanged_Handler(sender, e);
		}

		private void ThumbnailItemChecked(CheckableItem<IThumbnailDescription> item)
		{
			item.Value.IsDisabled = item.IsChecked;

			this.ThumbnailStateChanged?.Invoke(item.Value.Title);
		}

		private void LinkButton_Click(object sender, RoutedEventArgs e)
		{
			if ((sender as FrameworkElement)?.Tag is string url && !string.IsNullOrWhiteSpace(url))
			{
				this.DocumentationLinkActivated?.Invoke(url);
			}
		}

		private void MainWindow_Loaded(object sender, RoutedEventArgs e)
		{
			// Started minimized: WPF raises no StateChanged for the initial state.
			if (this.WindowState == WindowState.Minimized)
			{
				this.FormMinimized?.Invoke();
			}
		}

		private void MainWindow_StateChanged(object sender, EventArgs e)
		{
			if (this.WindowState != WindowState.Minimized)
			{
				return;
			}

			this.FormMinimized?.Invoke();
		}

		private void MainWindow_Closing(object sender, CancelEventArgs e)
		{
			ViewCloseRequest request = new ViewCloseRequest();

			this.FormCloseRequested?.Invoke(request);

			e.Cancel = !request.Allow;
		}

		protected override void OnClosed(EventArgs e)
		{
			this._trayIcon.Dispose();
			base.OnClosed(e);
		}

		private void RestoreMainWindow()
		{
			// This is the window's GUI lifecycle event that is invariant to the view data
			base.Show();
			this.WindowState = WindowState.Normal;
			this.Activate();
		}
		#endregion

		private void ShowFontPreview(OverlayFont font)
		{
			if (font == null)
			{
				return;
			}

			this.OverlayLabelFontPreview.FontFamily = new FontFamily(font.FamilyName);
			this.OverlayLabelFontPreview.FontSize = font.SizeInDips;
			this.OverlayLabelFontPreview.FontWeight = font.Bold ? FontWeights.Bold : FontWeights.Normal;
			this.OverlayLabelFontPreview.FontStyle = font.Italic ? FontStyles.Italic : FontStyles.Normal;

			string style = font.Style == OverlayFontStyle.Regular ? string.Empty : " · " + font.Style.ToString().Replace(", ", " ");
			this.LabelFontRow.Description = $"{font.FamilyName} · {font.SizeInPoints:0.##} pt{style}";
		}

		private static SolidColorBrush ToBrush(DrawingColor color)
		{
			var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
			brush.Freeze();
			return brush;
		}

		private static byte[] ReadResource(string relativePath)
		{
			var info = Application.GetResourceStream(new Uri("pack://application:,,,/EVE-F-Preview;component/" + relativePath));
			if (info == null)
			{
				return null;
			}

			using (Stream stream = info.Stream)
			using (var memory = new MemoryStream())
			{
				stream.CopyTo(memory);
				return memory.ToArray();
			}
		}
	}
}
