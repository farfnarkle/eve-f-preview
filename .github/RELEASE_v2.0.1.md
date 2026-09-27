# v2.0.1

Mostly fixes: settings that weren't being saved now are, Settings Sync can no longer lose or corrupt EVE settings files, and several crashes are gone.

## Added

- **The update popup shows the release notes** for the new version, so you can see what changed before updating.

## Fixed

- **Settings that weren't being saved now are**: thumbnail sizes after resizing by dragging the frame, the character indicator's position after dragging it, edge-snapped thumbnail positions, tracked client window layouts, and learned account ids. Settings save automatically as they change - there's nothing to save by hand.
- **The config file is saved atomically**, so a crash or power loss mid-save can't leave it empty. The previous version is kept as `EVE-F-Preview.json.bak` and used automatically if the main file is ever unreadable.
- **Switching config profiles could carry settings over from the previous profile.** Each profile now loads cleanly, and a broken profile shows an error and leaves you on the one you were using.
- **Settings Sync could damage or lose EVE settings files:**
  - Some text in EVE settings files was corrupted when synced; it's now copied through exactly.
  - A file is only overwritten after its backup succeeded.
  - Two backups made in the same second no longer overwrite each other.
  - The sync report only counts files that were actually synced.
- **Automatic sync at startup no longer freezes the window** while it runs.
- **The character indicator was stretched wide with only a few clients running**, leaving a lone square in the middle of an oversized box. It now fits its squares.
- **Character portraits**: clients started from the EVE launcher use the exact character (not a name search), a portrait is never taken from an unrelated search result, and refreshing portraits while clients are running no longer risks a crash.
- **A cycle hotkey could crash the app** with several clients sitting at the login screen.
- **The thumbnail refresh rate setting was ignored**; it's now honoured.
- **New thumbnails start at their per-client size** instead of the default size.
- **Account lookups no longer scan every running process** on each check, which could cause brief stutters.
- **The current-system overlay can no longer be spoofed** by another player typing a fake system name in chat.
- **Crash logs** are written next to the exe and appended with a timestamp, instead of overwriting the previous one.
