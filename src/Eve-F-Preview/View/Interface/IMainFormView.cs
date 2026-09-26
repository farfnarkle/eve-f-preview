using System;
using System.Collections.Generic;
using System.Drawing;
using EveFPreview.Configuration;

namespace EveFPreview.View
{
	/// <summary>
	/// Main view interface
	/// Presenter uses it to access GUI properties
	/// </summary>
	public interface IMainFormView : IView
	{
		bool MinimizeToTray { get; set; }
		bool StartMinimized { get; set; }
		bool CheckForUpdates { get; set; }

		/// <summary>Shows that a newer release exists: a link on the About tab, plus a popup when <paramref name="showPopup"/> is set.</summary>
		void SetUpdateAvailable(string version, string url, bool showPopup);

		/// <summary>Raised (with the version) when the user closes the update popup with "don't show again" ticked.</summary>
		Action<string> UpdateVersionDismissed { get; set; }

		double ThumbnailOpacity { get; set; }

		bool EnableClientLayoutTracking { get; set; }
		bool HideActiveClientThumbnail { get; set; }
		bool MinimizeInactiveClients { get; set; }
		bool HideCaptionOnClients { get; set; }
		ViewAnimationStyle WindowsAnimationStyle { get; set; }
        bool ShowThumbnailsAlwaysOnTop { get; set; }
		bool PreventPreviews { get; set; }
		bool HideThumbnailsOnLostFocus { get; set; }
		bool OnlyRegisterCycleHotkeysWhenEveFocused { get; set; }
		bool EnableCycleMouseDoubleClickProtection { get; set; }
		bool DynamicCycleGroup { get; set; }
		bool EnableAccountBasedThumbnailPositioning { get; set; }
		bool EnableAutoSettingsSync { get; set; }
		bool EnablePerClientThumbnailLayouts { get; set; }
		bool EnableCharacterIndicator { get; set; }
		bool LockCharacterIndicatorLocation { get; set; }
		bool EnableCharacterIndicatorClickToActivate { get; set; }

		void SetAutoSettingsSyncStatus(bool success, string message);

		Size ThumbnailSize { get; set; }
		bool EnableOverwatchMode { get; set; }
		Size FocusedThumbnailSize { get; set; }
		Point FocusedThumbnailLocation { get; set; }

		Point NewPreviewSpawnLocation { get; set; }
		bool NewPreviewAutoTile { get; set; }

		bool EnableThumbnailZoom { get; set; }
		int ThumbnailZoomFactor { get; set; }
		ViewZoomAnchor ThumbnailZoomAnchor { get; set; }
		ViewZoomAnchor OverlayLabelAnchor { get; set; }
		ViewZoomAnchor CycleGroupIndicatorAnchor { get; set; }

		bool ShowThumbnailOverlays { get; set; }
		bool ShowThumbnailFrames { get; set; }
		bool ShowSystemNameOnThumbnail { get; set; }

		bool LockThumbnailLocation { get; set; }
		bool ThumbnailSnapToGrid { get; set; }
		int ThumbnailSnapToGridSizeX { get; set; }
		int ThumbnailSnapToGridSizeY { get; set; }
		bool ThumbnailSnapToEdges { get; set; }

		bool EnableActiveClientHighlight { get; set; }
		Color ActiveClientHighlightColor { get; set; }
		Color PreventPreviewColor { get; set; }
		Color OverlayLabelColor { get; set; }
		OverlayFont OverlayLabelFont { get; set; }

		string IconName { get; set; }

		/// <summary>"Dark", "Light", or empty (follow Windows) - see ThemeManager.</summary>
		string UiTheme { get; set; }

		/// <summary>The settings window's own (restored) size in DIPs; empty keeps the default.</summary>
		Size SettingsWindowSize { get; set; }
		bool SettingsWindowTopmost { get; set; }
		bool MaintainThumbnailAspectRatio { get; set; }

		GlobalShortcutSettings GetGlobalShortcutSettings();
		void SetGlobalShortcutSettings(GlobalShortcutSettings settings);
		void ConfigureShortcutHotkeyRecording(Action suspendGlobalHotkeys, Action resumeGlobalHotkeys);
		void SetSettingsSyncConfiguration(IThumbnailConfiguration configuration, Action persistConfiguration = null);

		/// <summary>Gives the view direct access to the cycle group membership dictionaries for the Cycle Groups tab UI.</summary>
		void SetCycleGroupsConfiguration(IThumbnailConfiguration configuration, Action persistConfiguration = null);

		/// <summary>Gives the view direct access to config-profile operations (list/switch/save-as/import) for the General tab UI.</summary>
		void SetConfigurationStorage(IConfigurationStorage configurationStorage);

		/// <summary>Raised after the view has switched the active config profile; presenter should reload settings into the UI and re-register hotkeys.</summary>
		Action ConfigProfileChanged { get; set; }

		void SetDocumentationUrl(string url);
		void SetVersionInfo(string version);
		void BeginLoadSettings();
		void EndLoadSettings();
		void SetThumbnailSizeLimitations(Size minimumSize, Size maximumSize);

		void Minimize();

		void AddThumbnails(IList<IThumbnailDescription> thumbnails);
		void RemoveThumbnails(IList<IThumbnailDescription> thumbnails);
		void RefreshZoomSettings();

		Action ApplicationExitRequested { get; set; }
		Action FormActivated { get; set; }
		Action FormMinimized { get; set; }
		Action<ViewCloseRequest> FormCloseRequested { get; set; }
		Action ApplicationSettingsChanged { get; set; }
		Action GlobalShortcutSettingsChanged { get; set; }
		Action ThumbnailsSizeChanged { get; set; }
		Action<string> ThumbnailStateChanged { get; set; }
		Action<string> DocumentationLinkActivated { get; set; }
		Action CloseAllEveClientsRequested { get; set; }
		Action RefreshPortraitsRequested { get; set; }

		void SetRefreshPortraitsEnabled(bool enabled);
	}
}