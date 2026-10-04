# v2.1.0

Cycle to other programs and games, use numbered group hotkeys alongside dynamic cycling, and a much lighter app: about a quarter of the CPU it used before, and no more UI or mouse stalls.

## Added

- **Other apps.** A new settings page for adding other programs or games to your cycle rotation. Pick a running program, then choose whether dynamic cycling includes it, or add it to a numbered group on the Cycle groups page. Apps get a thumbnail like an EVE client and are tracked by program name, so it doesn't matter what their window title says.
- **Group hotkeys alongside dynamic cycling.** Turning on dynamic cycling no longer disables the numbered cycle group hotkeys. If a key is set for both, dynamic cycling takes it.
- **Settings Sync keeps a permanent original backup** (`_sync_original`) of each settings file, from before automatic syncs first changed it. Before, it was gone after 5 launches. "Delete sync backups" still removes it.
- **You're told if your settings can't be saved.** Before, a failed save was silently ignored. Now it's logged to `EVE-F-Preview.log` and retried, and if it keeps failing you get a message.

## Changed

- **Close all EVE clients** has moved to the General page.
- **The Dynamic cycle group switch** has moved to the top of the Cycle groups page.
- **Edits on the Cycle groups page** take effect straight away, without a restart.
- **The app no longer sends an Alt key press** when Windows refuses a window switch. That key press could reach the game. The README now explains exactly what input the app uses to switch windows.

## Performance

- About a quarter of the CPU it used before: 0.4% of one core instead of 1.7%, measured with 6 clients running.
- No more freezes when a client starts, on the first refresh, or during every window switch.
- Mouse-button hotkeys run on their own thread, so a busy app can never stall the mouse cursor.
- System names in the overlay only read the chat logs of characters that are running, in the background.

## Fixed

- **A future crash:** character and account IDs are now stored as 64-bit numbers. New EVE characters are close to the 32-bit limit, and crossing it would have crashed the app when a client started.
- **Leftover "Example Toon" settings** from EVE-O's default config are cleaned up. They made the app look up two made-up characters at every start.
- **Deleted characters** are no longer looked up at every start. A renamed character still gets its portrait.
- **Imported configs** can no longer point the app at other folders for portraits.
- **Portraits** only download from CCP's image server.

## Removed

- **Linux build:** the unbuilt Linux (wmctrl) code paths. The Windows build is unchanged, and the Wine compatibility option is untouched.
