# EVE-F-Preview

**EVE-F-Preview** is a community fork of [EVE-O Preview](https://github.com/Proopai/eve-o-preview). It shows live thumbnails of your EVE Online clients so you can watch and switch between them quickly — with the mouse or hotkeys.

Executable: `EVE-F-Preview.exe`  
Settings: `EVE-F-Preview.json` next to the exe (an existing `EVE-O-Preview.json` is still loaded on first run). Settings save automatically whenever anything changes.

Discord: https://discord.gg/xYt8R9AFXB

---

## What this app does

While running, EVE-F-Preview shows a live preview window for each running EVE client. Click a preview (or press a hotkey) to bring that client to the front. It is a task switcher only — it does **not** modify the game or its interface, and it never sends game input.

Works with native EVE clients, Steam EVE, or a mix.

**It will never:**

- modify the EVE Online interface
- display a modified EVE Online interface
- broadcast keyboard or mouse events into the game
- interact with EVE beyond bringing a window to the foreground, moving/resizing it, or minimizing/closing it when you ask

**One technical note, for transparency:** Windows only lets a program bring another window to the front right after the user pressed one of its hotkeys. For switches that don't start from such a hotkey (mouse-button hotkeys, for example), EVE-F-Preview presses an *unassigned* key code (0xE8) that it has registered as its own hotkey, so Windows hands that press to EVE-F-Preview itself. The press is only sent when Windows is sure to deliver it to EVE-F-Preview; only the matching key release — for a key that does nothing — can reach the window in front. No other input is ever synthesized.

**Do not use EVE-F-Preview for anything that would break the EVE Online EULA or ToS.** If a feature combination might, treat it as a bug and report it.

CCP has previously stated that unchanged, view-only client overlays (as EVE-O Preview works) are allowed. This fork is not endorsed by or affiliated with CCP.

---

## Install & use

### Requirements

- Windows 10 or 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows x64)
- EVE clients in **Fixed Window** or **Window Mode** — **Fullscreen is not supported**

### Install

1. Download a release zip and extract it somewhere you can write to (e.g. Desktop or `C:\Eve`).
   - **Do not** install under `Program Files` — the app stores `EVE-F-Preview.json` beside the exe and needs write access.
2. Run `EVE-F-Preview.exe` and your EVE clients (order does not matter).
3. Tune options in the settings window (pages described below).

Coming from EVE-O or EVE-X: use **General → Import…**, or drop a legacy `EVE-O-Preview.json` next to the exe for a one-time auto-load.

### Build from source

```bat
deploy.bat
```

Builds the app, publishes a single-file exe under `bin\net8.0-windows8.0\win-x64\publish\`, and copies it to the install folder set at the top of the script (`C:\Eve` by default; your settings file there is never touched).

---

## Settings pages

| Page | What's there |
| --- | --- |
| **General** | Start minimized / minimize to tray, update checks, keep the settings window on top, configuration profiles (Load, Save as, Import EVE-O / EVE-X), **Close all EVE clients** (asks each client to close, force-closes any still open 5 seconds later). |
| **Thumbnails** | Size (with *Maintain aspect ratio*), opacity, frames, always on top; hide the active client's thumbnail, hide while EVE isn't focused, **show character portraits instead of live previews** (lower GPU/CPU use; portraits from ESI cached in `thumbs\`); lock positions, snap to edges/grid, remember positions per EVE account, separate layout per client; the **character indicator** (a small grid of squares mirroring your thumbnail layout, gold for the active client). |
| **Overlay** | Character name label (font, colour, position), the **current solar system** (needs chat logging in EVE), active-client highlight, cycle-group badge position. |
| **Zoom & overwatch** | Enlarge a thumbnail on hover; **overwatch**: pin one large preview of a client (Ctrl+click its thumbnail). |
| **Hotkeys** | Only-while-EVE-is-focused, mouse double-click protection, dynamic cycle hotkeys, numbered cycle group hotkeys, minimize all clients, toggle thumbnails, click-through modifier. Keyboard keys, mouse buttons 4/5 and middle click can all be used. |
| **Cycle groups** | **Dynamic cycle group** switch (cycle in on-screen thumbnail order), and which clients each numbered group cycles through, in what order. With dynamic cycling on, the numbered group hotkeys keep working; a key set for both cycles dynamically. |
| **Other apps** | Add other programs or games to your cycle rotation: pick a running program, then choose whether dynamic cycling includes it, or add it to a numbered group on the Cycle groups page. Apps get a thumbnail like EVE clients and are tracked by program name. |
| **Clients** | Running clients (switch one off to hide its thumbnail; remembered per character), track client window positions, hide the title bar, minimize inactive clients, minimize/restore animation. |
| **Settings sync** | Copy EVE window layouts and chat settings from one character to others (EVE must be closed), with channel keep-lists, profile picker, backups and optional sync on startup. Every settings file also keeps a permanent `_sync_original` backup from before automatic syncs first changed it. |

### Mouse gestures (on a thumbnail)

| Action | Gesture |
| --- | --- |
| Activate client | Click |
| Pin / unpin overwatch preview | Ctrl+click (with Overwatch mode on) |
| Exclude from / include in cycling | Shift+click |
| Minimize client | Ctrl+Alt+click |
| Switch to the last non-EVE window | Ctrl+Shift+click |
| Move thumbnail | Right-drag |
| Resize thumbnail | Hold both buttons and drag |

---

## Advanced (file-only) settings

Per-client activation hotkeys live in `EVE-F-Preview.json` under `ClientHotkey` (edit only while the app is closed):

```json
"ClientHotkey": {
  "EVE - Character Name": "F1",
  "EVE - Other Character": "Control+Shift+F4"
}
```

Other file-only options (highlight thickness, refresh period, priority clients, per-client size/colour/zoom, …) are documented in older EVE-O Preview notes and still apply — back up the JSON before hand-editing.

**System names** need EVE **chat logging** enabled so `Documents\EVE\logs\Chatlogs\Local_*.txt` is written. Without logs, overlays show `[unknown]`.

**Logs:** crashes and settings-save problems are written to `EVE-F-Preview.log` next to the exe; portrait downloads to `thumbs\portrait-fetch.log`; settings sync runs to `settings-sync.log`.

---

## Credits

**Upstream / community maintainers:** Devilen, Dal Shooth, Izakbar, and earlier maintainers (Aura Asuna, Phrynohyas Tig-Rah, Makari Aeron, StinkRay). Contributions from CCP FoxFour on legitimacy discussion.

- Upstream: https://github.com/Proopai/eve-o-preview
- Forum: https://forums.eveonline.com/t/eve-o-preview-v8-0-2-0/463600
- Earlier history: https://bitbucket.org/ulph/eve-o-preview-git

Wormhole static data: see `src/Eve-F-Preview/Data/WormholeStatics.README.md` (Pathfinder, MIT).

---

## CCP copyright notice

EVE Online, the EVE logo, EVE and all associated logos and designs are the intellectual property of CCP hf. All artwork, screenshots, characters, vehicles, storylines, world facts or other recognizable features of the intellectual property relating to these trademarks are likewise the intellectual property of CCP hf. EVE Online and the EVE logo are the registered trademarks of CCP hf. All rights are reserved worldwide. All other trademarks are the property of their respective owners. CCP hf. has granted permission to EVE-O Preview to use EVE Online and all associated logos and designs for promotional and information purposes. This fork (EVE-F-Preview) is not endorsed by or affiliated with CCP hf.
