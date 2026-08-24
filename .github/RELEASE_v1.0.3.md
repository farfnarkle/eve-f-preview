## EVE-F-Preview 1.0.3

Per-destination chat channel selection for Settings Sync, plus a click-to-activate fix.

### New

- **Settings Sync: per-character channel selection** — "Channels to keep on copy" is now set per destination character instead of one shared list for everyone. Keep your general channels (corp, alliance, comms) on every alt while only keeping something like a fleet intel channel on the characters that actually need it.
- **Destination characters is now a persistent checklist** on the Settings Sync tab instead of a popup shown only when you click Sync — check who's included, click any character (checked or not) to preview/edit its channel selection.
- **Source character is now a dropdown** instead of a list.
- **Manual account ID override** — right-click a character in Settings Sync to set its account ID by hand when auto-detection can't read it off the running client's command line (e.g. some quick-login shortcuts don't expose it).

### Fixes

- **Clicking thumbnails could stop switching the active client entirely** — a regression where the internal hotkey used to steal window focus could register itself on a background thread under certain timing, silently breaking activation for every click and hotkey for the rest of the session until restart.
- **Settings Sync backup collisions** when multiple characters share the same EVE account — syncing all of them at once could fail with "file already exists" on the account's auto-backup.

### Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)
- EVE clients in Fixed Window or Window Mode (Fullscreen not supported)

### Install

Extract the zip somewhere writable (not Program Files), run `EVE-F-Preview.exe`. Existing `EVE-F-Preview.json` is unchanged.
