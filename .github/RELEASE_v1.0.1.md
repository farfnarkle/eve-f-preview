## EVE-F-Preview 1.0.1

Bugfixes and UI polish for Settings Sync and the main settings tabs.

### Fixes

- **Ship module layout** — sync no longer overwrites each alt’s HUD module arrangement (`ui/slotOrder`) or per-module auto-repeat / auto-reload
- **Sync crash** — fixed a regression that aborted every character copy with `expected sequence for channel data, got Dictionary`

### UI

- General, Overlay, and Settings Sync tabs cleaned up (spacing, control order, larger source/channel lists)
- About tab rewritten with clearer fork history and links to this repo, upstream EVE-O Preview, and the original forum thread

### Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)
- EVE clients in Fixed Window or Window Mode (Fullscreen not supported)

### Install

Extract the zip somewhere writable (not Program Files), run `EVE-F-Preview.exe`. Existing `EVE-F-Preview.json` is unchanged.
