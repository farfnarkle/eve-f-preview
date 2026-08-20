## EVE-F-Preview 1.0.2

New Cycle Groups tab, mouse-button hotkeys, and a round of settings-UI and sync fixes.

### New

- **Cycle Groups tab** — assign clients to a cycle group and set their cycle order from a grid instead of hand-editing the config JSON. Add running clients from a dropdown or type an exact window title for offline characters; delete a row with the trash icon
- **Mouse buttons as hotkeys** — side buttons (and middle click) can now be recorded and used for any cycle/shortcut hotkey, not just keyboard keys
- **"Excluded from cycle group" now persists** — Ctrl+click a preview to exclude it from cycling; that choice survives a restart instead of resetting every launch

### Fixes

- **Mouse-button hotkeys now actually switch the active client** — they previously cycled the highlight but didn't bring the EVE window to the foreground, a Windows permission quirk around who's allowed to steal focus
- **Cycle-group dropdown crash on mixed-DPI monitors** (e.g. 4K primary + 2K secondary)
- **Settings sync** now also preserves drone groups/favorites and fleet formation prefs on the destination instead of overwriting them from the source

### UI

- Settings tabs spaced out with hover help icons on jargon-heavy options (General, Thumbnail, Overlay, Shortcuts, Settings Sync)
- Main window widened slightly to fit the new Cycle Groups grid
- Dynamic cycle group hotkeys stay in sync when the setting is toggled

### Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)
- EVE clients in Fixed Window or Window Mode (Fullscreen not supported)

### Install

Extract the zip somewhere writable (not Program Files), run `EVE-F-Preview.exe`. Existing `EVE-F-Preview.json` is unchanged.
