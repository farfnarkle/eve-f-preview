using System.Collections.Generic;
using System.Drawing;
using EveFPreview.UI.Hotkeys;

namespace EveFPreview.Configuration
{
	public interface IThumbnailConfiguration
	{
		List<string> CycleGroup1ForwardHotkeys { get; set; }
		List<string> CycleGroup1BackwardHotkeys { get; set; }
		Dictionary<string, int> CycleGroup1ClientsOrder { get; set; }

		List<string> CycleGroup2ForwardHotkeys { get; set; }
		List<string> CycleGroup2BackwardHotkeys { get; set; }
		Dictionary<string, int> CycleGroup2ClientsOrder { get; set; }

		List<string> CycleGroup3ForwardHotkeys { get; set; }
		List<string> CycleGroup3BackwardHotkeys { get; set; }
		Dictionary<string, int> CycleGroup3ClientsOrder { get; set; }

		List<string> CycleGroup4ForwardHotkeys { get; set; }
		List<string> CycleGroup4BackwardHotkeys { get; set; }
		Dictionary<string, int> CycleGroup4ClientsOrder { get; set; }

		List<string> CycleGroup5ForwardHotkeys { get; set; }
		List<string> CycleGroup5BackwardHotkeys { get; set; }
		Dictionary<string, int> CycleGroup5ClientsOrder { get; set; }

		List<string> DynamicCycleForwardHotkeys { get; set; }
		List<string> DynamicCycleBackwardHotkeys { get; set; }

		Dictionary<string, Color> PerClientActiveClientHighlightColor { get; set; }
		Dictionary<string, Color> PerClientPreventPreviewColor { get; set; }
		Dictionary<string, bool> PerClientPreventPreviews { get; set; }
		Dictionary<string, Size> PerClientThumbnailSize { get; set; }
		Dictionary<string, bool> CycleGroupExclusions { get; set; }

		bool MinimizeToTray { get; set; }
		bool StartMinimized { get; set; }
		/// <summary>Periodically ask GitHub whether a newer release exists (notify only - never downloads anything).</summary>
		bool CheckForUpdates { get; set; }
		/// <summary>Release version the user chose "don't show again" for; the update popup skips it (a newer release shows again).</summary>
		string SkippedUpdateVersion { get; set; }
		int ThumbnailRefreshPeriod { get; set; }
		int ThumbnailResizeTimeoutPeriod { get; set; }
		bool EnableWineCompatibilityMode { get; set; }

		double ThumbnailOpacity { get; set; }

		bool EnableClientLayoutTracking { get; set; }
		bool HideActiveClientThumbnail { get; set; }
		bool HideLoginClientThumbnail { get; set; }
		bool MinimizeInactiveClients { get; set; }
		bool HideCaptionOnClients { get; set; }
		AnimationStyle WindowsAnimationStyle { get; set; }
		bool ShowThumbnailsAlwaysOnTop { get; set; }
		bool EnablePerClientThumbnailLayouts { get; set; }
		bool EnableAccountBasedThumbnailPositioning { get; set; }
		bool EnableAutoSettingsSync { get; set; }
		long AutoSettingsSyncSourceCharacterId { get; set; }
		long AutoSettingsSyncSourceUserId { get; set; }
		List<long> AutoSettingsSyncDestinationCharacterIds { get; set; }
		List<long> AutoSettingsSyncDestinationUserIds { get; set; }
		/// <summary>Default player chat channel keys to keep when syncing core_char (others are stripped). Used for any destination with no entry in AutoSettingsSyncChannelKeysToKeepByDestination.</summary>
		List<string> AutoSettingsSyncChannelKeysToKeep { get; set; }
		/// <summary>Per-destination override of which channel keys to keep, keyed by destination character ID (as a string). Falls back to AutoSettingsSyncChannelKeysToKeep when a destination has no entry.</summary>
		Dictionary<string, List<string>> AutoSettingsSyncChannelKeysToKeepByDestination { get; set; }
		/// <summary>Legacy: previously stored keys to strip. Migrated to Keep when UI loads.</summary>
		List<string> AutoSettingsSyncChannelKeysToStrip { get; set; }
		/// <summary>EVE settings profile folder name, e.g. settings_Farfnarkle.</summary>
		string AutoSettingsSyncProfileName { get; set; }
		/// <summary>Keep each destination's own per-module auto-repeat / auto-reload state when syncing core_char.</summary>
		bool PreserveShipModuleStateOnSync { get; set; }

		bool PreventPreviews { get; set; }
		bool HideThumbnailsOnLostFocus { get; set; }
		bool OnlyRegisterCycleHotkeysWhenEveFocused { get; set; }
		/// <summary>
		/// When a cycle hotkey is a mouse button, ignore a second click of that button that arrives
		/// within a short bounce window. Faulty side buttons often register one press as two clicks.
		/// </summary>
		bool EnableCycleMouseDoubleClickProtection { get; set; }
		bool DynamicCycleGroup { get; set; }
		int HideThumbnailsDelay { get; set; }

		Size ThumbnailSize { get; set; }
		bool EnableOverwatchMode { get; set; }
		Size FocusedThumbnailSize { get; set; }
		Point FocusedThumbnailLocation { get; set; }
		Size ThumbnailMinimumSize { get; set; }
		Size ThumbnailMaximumSize { get; set; }

		bool EnableThumbnailSnap { get; set; }

		bool ThumbnailZoomEnabled { get; set; }
		int ThumbnailZoomFactor { get; set; }
		ZoomAnchor ThumbnailZoomAnchor { get; set; }
		ZoomAnchor OverlayLabelAnchor { get; set; }
		ZoomAnchor CycleGroupIndicatorAnchor { get; set; }

		bool ShowThumbnailOverlays { get; set; }
		bool ShowThumbnailFrames { get; set; }
		/// <summary>
		/// Stub option only (feature 8): shows a placeholder for the client's current system name
		/// on its thumbnail. Not wired to any data source yet - see ThumbnailManager for why.
		/// </summary>
		bool ShowSystemNameOnThumbnail { get; set; }
		bool LockThumbnailLocation { get; set; }
		bool ThumbnailSnapToGrid {  get; set; }
		int ThumbnailSnapToGridSizeX { get; set; }
		int ThumbnailSnapToGridSizeY { get; set; }
		bool ThumbnailSnapToEdges { get; set; }

		bool EnableActiveClientHighlight { get; set; }
		Color ActiveClientHighlightColor { get; set; }
		Color PreventPreviewColor { get; set; }
		int ActiveClientHighlightThickness { get; set; }
		Color OverlayLabelColor { get; set; }
		OverlayFont OverlayLabelFont { get; set; }

		string IconName { get; set; }

		/// <summary>Settings window theme: "Dark", "Light", or empty to follow the Windows app theme.</summary>
		string UiTheme { get; set; }

		/// <summary>Last size of the settings window in DIPs; empty means the default size.</summary>
		Size SettingsWindowSize { get; set; }

		bool SettingsWindowTopmost { get; set; }

		/// <summary>Changing the thumbnail width also changes the height (and back), keeping their ratio.</summary>
		bool MaintainThumbnailAspectRatio { get; set; }
		List<string> MinimizeAllClientsHotkeys { get; set; }
		List<string> ToggleThumbnailsHotkeys { get; set; }
		List<string> ClickThroughModifierHotkeys { get; set; }

		Point LoginThumbnailLocation { get; set; }

		/// <summary>Shows a small always-on-top grid of squares mirroring the thumbnail layout, with the active client's square highlighted.</summary>
		bool EnableCharacterIndicator { get; set; }
		/// <summary>Top-left of the character indicator window, remembered across restarts.</summary>
		Point CharacterIndicatorLocation { get; set; }
		/// <summary>Prevent dragging the character indicator to a new position.</summary>
		bool LockCharacterIndicatorLocation { get; set; }
		/// <summary>Clicking a square in the character indicator activates that client, the same as clicking its thumbnail.</summary>
		bool EnableCharacterIndicatorClickToActivate { get; set; }

		Point NewPreviewSpawnLocation { get; set; }
		bool NewPreviewAutoTile { get; set; }

		/// <summary>Full path to the directory where cached character portrait images are stored (typically "thumbs" next to the executable).</summary>
		string PortraitThumbnailsDirectory { get; set; }

		/// <summary>Maps EVE window title (e.g. "EVE - Character Name") to the full path of the cached portrait image file.</summary>
		Dictionary<string, string> ClientPortraitPaths { get; set; }

		Point GetThumbnailLocation(string currentClient, string activeClient, Point defaultLocation);
		Size GetThumbnailSize(string currentClient, string activeClient, Size defaultSize);
		ZoomAnchor GetZoomAnchor(string currentClient, ZoomAnchor defaultZoomAnchor);
		void SetThumbnailLocation(string currentClient, string activeClient, Point location);

		Point GetAccountThumbnailLocation(int accountId, Point defaultLocation);
		void SetAccountThumbnailLocation(int accountId, Point location);
		bool TryGetCharacterId(string windowTitle, out int characterId);
		bool TryGetAccountIdForCharacter(int characterId, out int accountId);
		void RecordCharacterAccount(int characterId, int accountId);

		/// <summary>Explicit user override for a character's account ID (e.g. Settings Sync "Set account ID…"). Unlike RecordCharacterAccount, always takes effect. Pass accountId &lt;= 0 to clear.</summary>
		void SetCharacterAccount(int characterId, int accountId);

		ClientLayout GetClientLayout(string currentClient);
		void SetClientLayout(string currentClient, ClientLayout layout);

		Keys GetClientHotkey(string currentClient);
		void SetClientHotkey(string currentClient, Keys hotkey);
		Keys StringToKey(string hotkey);
		bool IsPriorityClient(string currentClient);
		bool IsExecutableToPreview(string processName);

		bool IsThumbnailDisabled(string currentClient);
		void ToggleThumbnail(string currentClient, bool isDisabled);

		void ApplyRestrictions();
	}
}