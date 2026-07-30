# Manual test script — recent features (~5–10 min)

Prereqs: EVE clients running (or ready to log in), `C:\Eve\EVE-F-Preview.exe` deployed, Overlay **Show thumbnails overlays** on for label checks.

---

## 1. Start minimized + tray (30s)

- [x] **General** → enable **Start minimized** (and **Minimize to System Tray** if you use tray)
- [x] Save / let it persist, fully quit the app, relaunch from `C:\Eve\EVE-F-Preview.exe`
- [x] **Check:** main window does not stay in front; tray icon present if tray enabled; double-click tray restores the window
- [x] **Edge:** turn **Start minimized** off, relaunch → window should open normally

---

## 2. Toggle thumbnails hotkey (45s)

- [x] **Shortcuts** → **Toggle thumbnails visibility** → bind something unused (e.g. `Ctrl+Shift+H`)
- [x] With previews visible, press the hotkey
- [x] **Check:** all thumbnails hide
- [x] Press again → they return
- [x] **Edge:** leave hotkey empty, restart app → no crash; accidental key shouldn’t toggle

---

## 3. Click-through while held (45s)

This row is now a **dropdown**, not a recorder — modifier-only combos can't be captured by the hotkey recorder.

- [x] **Shortcuts** → **Click-through while held** → pick e.g. `Ctrl+Shift` from the dropdown
- [x] Hover a thumbnail; click without holding → should activate / interact as usual
- [x] Hold the modifiers, click “through” the thumbnail onto whatever is behind (desktop / another window)
- [x] **Check:** clicks pass through only while held; release → normal again (state is polled every 50ms, so it should feel instant)
- [x] **Edge:** set it back to `(none)` → no click-through
- [x] **Edge:** an old value like `Ctrl+Shift+C` in the config is read as `Ctrl+Shift`

---

## 4. New preview spawn + auto-tile — SKIPPED

UI hidden for now (code + config keys still present). Skip this section.

---

## 5. System name from Local chat (1–2 min)

Read from `Documents\EVE\logs\Chatlogs\Local_*.txt` (`EVE System > Channel changed to Local : X`), so **chat logging must be on** in the EVE client. Any system change writes that line, including clone jumps and death clones.

- [x] **Overlay** → enable **Show system name (logs)** (top right of the panel)
- [ ] **Check:** overlay shows a second line like `[Jita]` for every logged-in client
- [ ] Jump a gate on one client
- [ ] **Check:** that thumbnail’s system updates within a refresh or two, without hovering it
- [ ] **Edge — clone / death clone:** jump clone (or get podded)
- [ ] **Check:** the line switches to the destination system, not the old one
- [ ] **Edge:** a character with no chat log yet shows `[unknown]`

---

## 6. Config profiles + import (1–2 min)

- [ ] **General** → **Config profile** combo → note current file
- [ ] **Save As…** → e.g. `EVE-F-Preview-test.json` next to the exe (`C:\Eve\`)
- [ ] Change something visible (e.g. thumbnail opacity), **Load** the original profile
- [ ] **Check:** opacity (and layout) restore; hotkeys still work after load
- [ ] **Import…** → pick a vanilla `EVE-O-Preview.json` if you have one (or the repo sample). Save as a new name, switch to it when prompted
- [ ] **Check:** import succeeds; sizes/hotkeys look sane; app doesn’t crash
- [ ] Switch back to your real profile when done

*(Skip EVE-X import unless you have an* `EVE-X-Preview.json` *handy — best-effort mapper.)*

---

## 7. Settings sync identity scrub (1–2 min) — optional if you use sync

Only if you already use **Settings Sync** and have a spare alt that can receive a copy.

- [ ] On the **source** character, open Contracts and set **Owner** filter to the source name (or type the source name into Give ISK / address book search so it lands in autocomplete history)
- [ ] Run a **manual sync** (copy) from source → that alt (same profile as usual)
- [ ] **Check:** on destination alt, Contracts owner filter is **not** stuck on the source name
- [ ] **Check:** Give ISK / address book / contract autocomplete does **not** suggest the source name from scrubbed history (other history can remain)
- [ ] **Edge:** sync report mentions cleared/`editHistory` fields when scrub ran

---

## Quick pass / fail summary

- [ ] 1 — Start minimized
- [ ] 2 — Toggle thumbnails hotkey
- [ ] 3 — Click-through while held
- [x] 4 — Spawn + auto-tile (UI hidden; skipped)
- [ ] 5 — System name (jump + clone clear)
- [ ] 6 — Profiles + import
- [ ] 7 — Sync identity scrub (optional)

**Notes / bugs:**

- 

- 