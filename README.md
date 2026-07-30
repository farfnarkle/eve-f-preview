# EVE-F-Preview

**EVE-F-Preview** is a community fork of [EVE-O Preview](https://github.com/Proopai/eve-o-preview). It shows live thumbnails of your EVE Online clients so you can watch and switch between them quickly — mouse or hotkeys.

Executable: `EVE-F-Preview.exe`  
Settings: `EVE-F-Preview.json` next to the exe (an existing `EVE-O-Preview.json` is still loaded on first run)

Discord: https://discord.gg/xYt8R9AFXB

---

## What this app does

While running, EVE-F-Preview shows a live preview window for each active EVE client. Click a preview (or use a hotkey) to bring that client to the front. It is a task switcher only — it does **not** inject input into EVE or modify the game UI.

Works with native EVE clients, Steam EVE, or a mix.

**It will never:**

- modify the EVE Online interface
- display a modified EVE Online interface
- broadcast keyboard or mouse events into the game
- interact with EVE beyond bringing a window to the foreground or resizing/minimizing it

**Do not use EVE-F-Preview for anything that would break the EVE Online EULA or ToS.** If a feature combination might, treat it as a bug and report it.

CCP has previously stated that unchanged, view-only client overlays (as EVE-O Preview works) are allowed. This fork is not endorsed by or affiliated with CCP.

---

## What's different in this fork

Compared with upstream [Proopai/eve-o-preview](https://github.com/Proopai/eve-o-preview), this fork adds:

| Area | Changes |
| --- | --- |
| **Rebrand** | Exe, mutex, logs, and default config use **EVE-F-Preview**; legacy `EVE-O-Preview.json` still loads if the new file is missing. |
| **Portrait thumbnails** | With **Do not display previews**, thumbnails show ESI character portraits (cached under `thumbs/`) instead of live DWM captures — lower GPU/CPU use. |
| **Account-based layout** | Optional thumbnail positions keyed by EVE **account ID**, so swapping characters on an account keeps the same slot. |
| **Overwatch mode** | Ctrl+click a preview to pin an enlarged focused thumbnail. |
| **Dynamic cycle** | Cycle clients in on-screen thumbnail order instead of a fixed list. |
| **Shortcuts UI** | Edit cycle-group and global hotkeys in the app (no JSON required for those). |
| **Settings Sync** | Copy `core_char` / `core_user` between characters, with channel keep-list, profile picker, backups, and optional auto-sync on startup. Identity scrubbing clears source-character names from filters / edit history on copy. |
| **Config profiles** | Load / Save As / Import profiles from the General tab. Import scaffolds a new config from vanilla **EVE-O** or **EVE-X** JSON. |
| **Toggle thumbnails** | Optional hotkey to hide/show all previews at once. |
| **Click-through** | Optional modifier (dropdown on Shortcuts) — hold it to click through thumbnails. |
| **System name overlay** | Optional second overlay line from Local chat logs (`Channel changed to Local : SYSTEM`). Requires EVE chat logging. Shows `[SYSTEM]` or `[unknown]`. |
| **Start minimized** | Separate from minimize-to-tray; start the main window minimized. |
| **Stability** | Cycle hotkeys on the UI thread, cleaner refresh (less flicker/hitching), portrait-mode highlight fixes. |

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
3. Tune options on the tabs described below.

Coming from EVE-O or EVE-X: use **General → Import…**, or drop a legacy `EVE-O-Preview.json` next to the exe for a one-time auto-load.

### Build from source

```bat
deploy.bat
```

That publishes a single-file exe under `bin\net8.0-windows8.0\win-x64\publish\` (and can copy it to your install folder if you use the included deploy script).

---

## Quick start — useful options

### General

| Option | Notes |
| --- | --- |
| Minimize to System Tray / Start minimized | Tray behaviour vs start minimized on launch |
| Track client locations | Restore EVE window positions |
| Hide preview of active client / Minimize inactive clients | Reduce clutter and GPU load |
| Previews always on top / Hide when EVE not active | Visibility rules |
| Only register cycle hotkeys when EVE is active | Avoid stealing hotkeys outside EVE |
| Dynamic cycle group / Account based positioning | Cycle order and per-account slots |
| Config profile | Load, Save As, Import (EVE-O / EVE-X) |
| Auto settings sync | Optional sync on startup when EVE is closed |

### Thumbnail

| Option | Notes |
| --- | --- |
| Size / opacity / snap / lock | Layout |
| Do not display previews | Portrait mode instead of live capture |
| Refresh portraits | Re-fetch ESI portraits |
| Overwatch mode | Enlarged focused preview (Ctrl+click) |

### Overlay

| Option | Notes |
| --- | --- |
| Show overlay / frames / highlight | Labels and borders |
| Label font, color, position | Character name styling |
| Show system name (logs) | `[SYSTEM]` from Local chat; needs chat logging in EVE |

### Shortcuts

| Option | Notes |
| --- | --- |
| Cycle groups 1–5 / Dynamic cycle | Forward & backward |
| Minimize all clients | Global hotkey |
| Toggle thumbnails visibility | Hide/show all previews |
| Click-through while held | Modifier dropdown (e.g. Ctrl+Shift) |

### Settings Sync

Close **all** EVE clients before syncing. Pick source/destinations, settings profile, and channels to keep. The tool backs up targets before overwrite and scrubs source identity from copied user settings where possible.

### Active Clients

Uncheck a client to hide its thumbnail for the current session (not persisted).

### Mouse gestures (on a thumbnail)

| Action | Gesture |
| --- | --- |
| Activate client | Click |
| Minimize client | Ctrl+click |
| Toggle cycle-group membership | Shift+click |
| Switch to last non-EVE app | Ctrl+Shift+click |
| Move thumbnail | Right-drag |
| Resize | Left+right drag |

---

## Hotkeys & advanced config

Most cycle/global shortcuts are on the **Shortcuts** tab. Per-client activation hotkeys still live in JSON under `ClientHotkey` (edit only while the app is closed):

```json
"ClientHotkey": {
  "EVE - Character Name": "F1",
  "EVE - Other Character": "Control+Shift+F4"
}
```

Cycle group membership/order can be edited on Shortcuts or via `CycleGroup*ForwardHotkeys`, `CycleGroup*BackwardHotkeys`, and `CycleGroup*ClientsOrder` in `EVE-F-Preview.json`.

Other file-only options (highlight thickness, refresh period, priority clients, per-client size/color/zoom, etc.) are documented in older EVE-O Preview notes and still apply — backup the JSON before hand-editing.

**System names** need EVE **chat logging** enabled so `Documents\EVE\logs\Chatlogs\Local_*.txt` is written. Without logs, overlays show `[unknown]`.

---

## Credits

**Upstream / community maintainers:** Devilen, Dal Shooth, Izakbar, and earlier maintainers (Aura Asuna, Phrynohyas Tig-Rah, Makari Aeron, StinkRay). Contributions from CCP FoxFour on legitimacy discussion.

- Upstream: https://github.com/Proopai/eve-o-preview
- Forum: https://forums.eveonline.com/t/eve-o-preview-v8-0-2-0/463600
- Earlier history: https://bitbucket.org/ulph/eve-o-preview-git

---

## CCP copyright notice

EVE Online, the EVE logo, EVE and all associated logos and designs are the intellectual property of CCP hf. All artwork, screenshots, characters, vehicles, storylines, world facts or other recognizable features of the intellectual property relating to these trademarks are likewise the intellectual property of CCP hf. EVE Online and the EVE logo are the registered trademarks of CCP hf. All rights are reserved worldwide. All other trademarks are the property of their respective owners. CCP hf. has granted permission to EVE-O Preview to use EVE Online and all associated logos and designs for promotional and information purposes. This fork (EVE-F-Preview) is not endorsed by or affiliated with CCP hf.
