namespace EveFPreview.View
{
	/// <summary>
	/// Tooltip text for the "ⓘ" help icons on the settings tabs. Public so the XAML can reference it
	/// with {x:Static}. (The WinForms version also held table-layout helpers here; XAML does that now.)
	/// </summary>
	public static class SettingsHelpText
	{
		public const string CheckForUpdates = "Check GitHub for a newer release when the app starts. Only shows a notice - nothing is downloaded or installed.";
		public const string TrackClientLocations = "Restore EVE window positions when clients are detected.";
		public const string HideCaptionBar = "Hide the Windows title bar on EVE client windows.";
		public const string CycleHotkeysWhenEveActive = "Avoid stealing cycle hotkeys when you are not in EVE.";
		public const string CycleMouseDoubleClickProtection = "Some side buttons register one press as two clicks and skip a client. When this is on, only that extra click is ignored, so you can still cycle as fast as you press.";
		public const string DynamicCycleGroup = "Adds Dynamic cycle hotkeys that step through clients in on-screen thumbnail order. Numbered group hotkeys keep working; if one uses the same key, the dynamic hotkey wins.";
		public const string AccountBasedPositioning = "Remember thumbnail positions per EVE account.";
		public const string UniqueLayout = "Store a separate thumbnail layout for each EVE client.";
		public const string AutoSettingsSync = "Optional settings sync on startup when EVE is closed.";
		public const string ConfigProfile = "Load, Save As, or Import (EVE-O / EVE-X) a preview configuration profile.";
		public const string AnimationStyle = "How thumbnail windows animate when they are shown or hidden.";
		public const string OverwatchMode = "Enlarged focused preview. Ctrl+click a thumbnail to pin it.";
		public const string LockThumbnailLocation = "Prevent dragging thumbnails to a new position.";
		public const string SnapToEdges = "Snap thumbnails to screen and other thumbnail edges while dragging.";
		public const string SnapToGrid = "Snap thumbnail positions to the grid size below.";
		public const string DoNotDisplayPreviews = "Show character portraits instead of live client capture.";
		public const string ShowOverlay = "Show character name labels on thumbnails.";
		public const string ShowSystemName = "Show [SYSTEM] from Local chat. Requires chat logging in EVE.";
		public const string ShowFrames = "Draw a border around each thumbnail.";
		public const string CycleGroupIndicator = "Where the cycle-group badge is drawn on each thumbnail.";
		public const string CycleGroups = "Forward and backward hotkeys for each numbered cycle group.";
		public const string DynamicCycleHotkeys = "Cycle all non-excluded clients in on-screen thumbnail order. A group hotkey set to the same key is ignored.";
		public const string ClickThrough = "Hold this modifier to click through thumbnails to windows behind them (for example Ctrl+Shift).";
		public const string SettingsProfile = "Which EVE settings profile folder to copy from and to.";
		public const string SourceCharacter = "Character to copy settings from.";
		public const string DestinationCharacters = "Check a character to include it in Sync. Click one (checked or not) to edit which chat channels it keeps.";
		public const string ChannelsToKeep = "Chat channels to keep on the selected destination character when copying settings. Set separately per destination.";
		public const string PreserveModuleLayout = "Do not overwrite each alt's fitted module layout from the source character.";
		public const string CharacterIndicator = "Show a small always-on-top grid mirroring the thumbnail layout, with the active client's square highlighted gold. Drag it to reposition.";
		public const string LockCharacterIndicator = "Prevent dragging the character indicator to a new position.";
		public const string ClickToActivateCharacterIndicator = "Click a square in the character indicator to activate that client, the same as clicking its thumbnail.";
	}
}
