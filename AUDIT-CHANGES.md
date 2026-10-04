# Audit changes (October 2026)

Everything changed by the security / performance / cleanup audit, so that if something breaks it can be traced to one change instead of chased.

- **Branch:** `claude/audit-fixes` (local only, **not pushed**). `main` is untouched and still matches GitHub.
- **One commit per change.** Each change below names its commit. To undo just one: `git revert <commit>` then run `deploy.bat`.
- **Deployed:** this branch's build is running from `C:\Eve` (deployed 2026-10-03 21:27). Your settings from just before it are in `C:\Eve\EVE-F-Preview.json.bak_before_audit_fixes`.
- **Logs to check if something seems off:** `C:\Eve\EVE-F-Preview.log` (crashes, settings-save problems), `C:\Eve\thumbs\portrait-fetch.log`, `C:\Eve\settings-sync.log`.

## Results

Measured on this PC with 6 EVE clients running, same settings, 30-second samples:

| | Before | After |
|---|---|---|
| EVE-F-Preview CPU use | **1.70%** of one core | **0.41%** of one core |
| EVE-F-Preview GPU use | 0% | 0% |
| Measured UI-thread work per refresh (every 500 ms): process scan + chat logs + indicator | ~5.7 ms | under 0.1 ms |
| UI freezes | 42 ms at first refresh, ~0.1 s each time a client starts (~0.6 s at app start), 15-25 ms of a pegged core per window switch | none of these left |

The thumbnails themselves were already cheap: Windows' compositor spends about 0.4% GPU on them, against roughly 4.4% for each EVE client.

## If something breaks: where to look first

| Symptom | Most likely | Commit |
|---|---|---|
| A client's thumbnail doesn't appear, or a closed client's stays | P1 | `92aac30` |
| System name on a thumbnail stuck or `[unknown]` | P2 | `82c75ba` |
| Cycling sometimes doesn't actually swap to the client | **S3 first**, then P3 | `13eb082`, `c6d4ad6` |
| Mouse-button hotkeys stop working, or misfire | P4 | `1b68c0b` |
| Account-grouped positions or portraits wrong for a newly started client | P5, then C1 | `fe6aaba`, `dabd1cd` |
| Click-through modifier doesn't work | P6 | `96fda50` |
| Character indicator doesn't update | P6 | `96fda50` |
| A pop-up says settings can't be saved | C2 is reporting a real problem; see `EVE-F-Preview.log` | `0b11318` |
| Imported config shows no portraits at first | S1 (they re-download at startup) | `2093052` |
| Settings Sync backups look different | C3 | `b261031` |
| "Close all EVE clients" behaves differently | C4 | `f758362` |
| `deploy.bat` fails where it used to succeed | L6 | `2745896` |

---

## Feature work from earlier the same day (not part of the audit)

**`764b715`: Other apps; group hotkeys alongside dynamic cycling; settings moved**

- **Other apps page:** add running programs to cycling. They're tracked by program name, with a per-app dynamic cycling switch.
- **Group hotkeys with dynamic cycling:** dynamic cycling no longer disables the numbered group hotkeys. If a key is set for both, dynamic cycling takes it.
- **Live group membership:** group hotkeys pick up edits on the Cycle groups page without a restart.
- **Moved settings:** Close all EVE clients is now on the General page, and the Dynamic cycle group switch is at the top of Cycle groups.

---

## Performance

### P1: Finding EVE windows (`92aac30`)

- **Problem.** Every 500 ms the app listed every process on the PC (294 here), then searched all windows again for each EVE client. That cost **4.5 ms per refresh on the UI thread**, about 70% of the app's CPU.
- **Fix.** One pass over the open windows per refresh. It uses the same rule as before for a program's main window: the first visible window with no owner. Process names are looked up once per new process.
- **Verified.** Old and new code compared side by side: identical windows, titles and app flags, including Discord set up as an "Other app". Now **0.05 ms per refresh**.
- **If it breaks.** A client's thumbnail missing, or one lingering after its client closed.

### P2: Chat-log system names (`82c75ba`)

- **Problem.** The system-name reader opened the newest Local log of **every character that had ever logged** (33 here, only 6 running) on every refresh, on the UI thread. That cost 0.9 ms per refresh, plus a 3.3 ms folder scan every 5 s and a **~40 ms freeze on the first refresh**.
- **Fix.** It now reads only the running characters' logs, matching each log to a character by ID or by the "Listener:" name in the log's header, read once per file. It runs on a background thread, one pass at a time. When it sees a jump it updates the thumbnails straight away, so system names appear as fast as before.
- **Verified.** Identical systems for all 6 clients compared with the old code. The background pass takes 0.20 ms; the UI thread now spends about 1 µs per refresh.
- **If it breaks.** A system name not updating after a jump, or showing `[unknown]`.

### P3: Waiting for a window switch (`c6d4ad6`)

- **Problem.** After each switch, the app confirms with Windows that the switch landed. This wait is part of the earlier fix for clients not actually swapping. It re-checked in a busy loop, keeping a core at 100% on the UI thread for the 15–25 ms a switch takes (up to 150 ms per attempt).
- **Fix.** Same checks and same limits, but it sleeps 1 ms between checks. The timer resolution is raised to 1 ms only during the wait, so confirmation is as quick as before.
- **Verified.** On a switch that Windows refuses, it reports back after 21.2 ms instead of 20.1 ms, using **0.8 ms of CPU instead of 19.5 ms**.
- **If it breaks.** Cycling feeling slower, or the highlight briefly on the wrong client.

### P4: Mouse-button hotkey hook (`1b68c0b`)

- **Problem.** Mouse-button hotkeys (buttons 4/5, middle click) use a system-wide mouse hook that ran on the UI thread. Windows waits for that hook before moving the cursor, so any UI stall froze the mouse everywhere. Windows also silently removes hooks that are too slow. This only applies while a mouse button is bound; your current hotkeys don't use one.
- **Fix.** The hook now has its own high-priority thread. That thread only decides whether a click is a hotkey (and swallows it, as before); the actual work runs on the UI thread.
- **Verified.** The hook starts on its own thread, handles a matching side-button press, lets other buttons and mouse movement through, stops when the last mouse hotkey is removed, and can start again.
- **If it breaks.** Mouse-button hotkeys stop working or fire twice, or recording a mouse button on the Hotkeys page doesn't pick it up.

### P5: Reading a new client's launch details (`fe6aaba`)

- **Problem.** The account and character ID come from the client's command line, read through a Windows management (WMI) query. That took **93–148 ms per client**, on the UI thread: a freeze whenever a client started, and about 0.6 s at app start with 6 clients.
- **Fix.** The first read for each window happens in the background and is applied when it arrives. Repeat reads come from a cache, as before.
- **Verified.** Background reads gave the same account and character IDs as direct reads for all 6 running clients.
- **If it breaks.** Per-account thumbnail positions or launch-ID portraits wrong for a client that just started.

### P6: Skipping work that has nothing to do (`96fda50`)

- **Click-through polling:** the click-through modifier was checked 20 times a second even with none configured. It now runs only while one is set.
- **Character indicator:** its grid was rebuilt on every refresh. It's now skipped when nothing changed: **0.27 ms → 0.014 ms**. It still resizes correctly with 1, 3, 8 or 1 clients.
- **If it breaks.** Click-through not working after you set a modifier, or the indicator not updating.

---

## Correctness

### C1: 64-bit character and account IDs (`dabd1cd`)

- **Problem.** IDs were stored as 32-bit ints in about 30 places. Your newest character, 2,124,739,002, is only 22.7 million below the int limit. Once new EVE IDs pass that limit, reading a new client's launch details would throw an uncaught error and crash the app.
- **Fix.** All character and account IDs are now 64-bit and parsed safely. The config file format is unchanged.
- **Verified.** Your real config round-trips unchanged, including all 35 account mappings. IDs above the old limit parse correctly, and absurd values are rejected without crashing.

### C2: Reporting failed settings saves (`0b11318`)

- **Problem.** A failed save (file locked, folder not writable) was silently ignored.
- **Fix.** Each failure is logged to `EVE-F-Preview.log` and retried 2 s later, up to 3 quick retries; ordinary saves keep trying after that. If the retry also fails, a message tells you, once per session. Recovery is logged too.
- **Verified.** With the file deliberately locked: both failures logged, the recovery logged, and the file written once unlocked. The popup and the timed retry need the running app, so they weren't exercised.

### C3: Permanent original backup for Settings Sync (`b261031`)

- **Problem.** Auto-sync runs at every launch and keeps only 5 automatic backups, so after 5 launches nothing from before the first sync was left. Pruning also sorted by modification time, which `File.Copy` copies from the source file. That reflects when the settings changed, not when the backup was made.
- **Fix.**
  - The first automatic backup of a file also creates `<file>_sync_original.dat`, which is never pruned. "Delete sync backups" still removes it.
  - Files that already had automatic backups get their oldest one as the original, the earliest state still available.
  - Pruning now orders by the timestamp in the file name.
- **Verified.** Tested with fake settings files:
  - An existing set of backups (with deliberately reversed modification times) got the right original, and the right 5 were kept.
  - A new file got a pristine original that survived 7 more syncs.

### C4: Close all EVE clients asks first (`f758362`)

- **Problem.** It killed every client outright, so EVE never shut down normally.
- **Fix.** Each client is asked to close normally. Any still open after 5 seconds (stuck, or showing EVE's own quit prompt) is force-closed as before. It only ever touches EVE clients, and the UI doesn't freeze while it waits.
- **Verified.** On stand-in windows: the one that closes itself exited after 0.1 s, and the one that refuses was force-closed at 5.3 s. It wasn't tried on real EVE clients, since that would have closed your game.

---

## Security

The risk was low overall. Already fine: the update check and link opening (web links only, from the right GitHub repo), Settings Sync file paths (built from real folders and numeric IDs), atomic config saves, HTTPS everywhere, and no unsafe deserialization.

### S1: Imported configs (`2093052`)

- **Problem.** Import applied a foreign config wholesale. That included the portrait folder (where portraits and the fetch log get written) and per-character portrait file paths (loaded as images). A shared config could therefore make the app write into, or read from, folders of the sender's choosing.
- **Fix.** On import, the portrait-folder setting is dropped. Each portrait entry keeps only its `<character id>.png` file name and is pointed at the app's own `thumbs` folder; missing files re-download at startup.
- **Verified.** A crafted config: outside paths were removed, the character ID was kept, and ordinary settings still imported. Importing your real config changed nothing else, and all 38 portrait entries kept their file names.

### S2: Portrait downloads (`fb9715a`)

- **Problem.** Portrait links from EVE's API were downloaded wherever they pointed.
- **Fix.** Downloads now require HTTPS on `evetech.net`, which is where EVE's API points (`images.evetech.net`). Anything else is logged and skipped.
- **Verified.** The real link passes; plain http, other hosts, look-alike hosts and `file:` links are rejected.

### S3: Keys reaching the game during a switch (`13eb082`)

- **Problem.** The README says the app never sends keyboard input to the game, but two things in the window-switch code could:
  - **The Alt tap:** a last-resort tap of Alt, used when Windows refused a switch, reached the window in front.
  - **The unused-key press:** the press of key code 0xE8, used to get Windows' permission to switch windows, would also reach the game if the app hadn't registered the modifier combination being held at that moment.
- **Fix.**
  - The Alt tap is removed. A switch Windows still refuses is now reported as failed, and the highlight moves back to the real foreground client.
  - The unused-key press is only sent when the held modifiers form a combination the app registered, so Windows always delivers it to EVE-F-Preview.
  - The README now explains this plainly. Only the release of that unused key can reach the window in front, and it does nothing.
- **Watch.** This is the change most likely to matter for the earlier swap problem. **If cycling occasionally stops swapping, revert this commit first.**

---

## Cleanup (no intended behaviour change)

- **L1 (`fe29d2a`): Linux/Wine code removed.** The `#if LINUX` branches only compiled with a build flag nothing sets. Also removed: the two Lutris install scripts (upstream EVE-O's), and a logger only that code used. The Windows build keeps every Windows branch. The config-only `WineCompatibilityMode` option is untouched.
- **L2 (`6da91eb`): dead code.** Three interface methods nobody called, a counter that was written but never read, two unused `using` lines, and four TODOs with no plan behind them.
- **L3 (`6fe16a5`): one copy of repeated logic.**
  - **`EveClient`** holds the client process name, the login-screen title and the "EVE - " title parsing. That replaces five copies of the parsing and four copies of `"exefile"`, checked against the old copies on ten sample titles: same results.
  - **EVE settings folder path:** now built in one place (it was built 6 times).
  - **Physical key check:** one function instead of three copies plus a duplicate Windows declaration. The UI-thread key check (`WindowNativeMethods.IsKeyDown`) is a different thing and stays.
- **L4 (`1510414`): `Eve-F-Mock` trimmed.** This is the fake EVE window used for testing. Removed its leftovers from the old .NET Framework project: an empty `App.config`, a `packages.config`, unused settings and resource files, and two package references. `deploy.bat` now builds only the app.
- **L5 (`8dbeca0`): README rewritten** for the current settings pages. It also corrects the minimize gesture (it's Ctrl+Alt+click; Ctrl+click is overwatch), lists where the logs are, and includes the plain note from S3. `TESTING-NEW-FEATURES.md`, a finished checklist for the old UI, is deleted.
- **L6 (`2745896`): `deploy.bat` hardened.**
  - **Windows tools by full path:** in Git Bash, a bare `find` is Git's, which broke the "is the app still running?" check.
  - **Always a fresh exe:** the old published exe is deleted first, and the build is a full rebuild that publish only packages.
  - **Byte-for-byte check:** after copying, the deployed exe is compared with the new build, and any mismatch fails the deploy instead of printing "Deploy complete".
  - **Why:** found while deploying this branch. When a deploy's output was sent to a log file, the app it started kept that log open, so the next deploy couldn't open it and silently did nothing; the log being read was the previous run's.

## Changed outside git (can't be undone with `git revert`)

All deleted with your approval:

- **`release-staging\`:** every old build and zip except v2.0.1, including the odd `v8.0.x` ones (about 65 MB). The GitHub releases still have them.
- **`artifacts\`:** the old EVE-O build.
- **`C:\Eve`:** six old hand-made config backups. Kept: `EVE-F-Preview.json.bak` (automatic), `.bak_before_group1_order`, and the new `.bak_before_audit_fixes`.

## Deliberately not done

- **Splitting `ThumbnailManager.cs`** (2,375 lines): skipped, as agreed. It's the biggest break risk and gains no speed.
- **Label clipping on the Hotkeys page:** at the default 750 px window width the row labels get cut off. Your window is 892 px wide, so you don't see it.
- **The config-only Wine compatibility option** (static thumbnails): left alone, since it isn't part of the Linux build code.
