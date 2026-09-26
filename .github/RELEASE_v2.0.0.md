# v2.0.0

The settings window is rewritten from Windows Forms to WPF, with a dark/light theme and a redesigned, Windows 11 Settings-style layout: left-hand navigation instead of tabs, cards and toggle switches instead of dense option grids. Config files, hotkeys, and thumbnail behavior are unchanged - this is a UI rewrite, not a behavior change.

## Added

- **Dark and light theme**, with a toggle at the bottom of the navigation. By default it follows your Windows theme; switching it explicitly remembers your choice, including a matching title bar.
- **The settings window remembers its size** between restarts, and has a "Keep this window on top" option (General tab) if you'd rather it not always float above EVE. Its default size is now 750x900.
- **"Maintain aspect ratio"** next to the thumbnail size fields (Thumbnails tab): turn it on and changing the width adjusts the height to match, and the other way round. It also applies when resizing a thumbnail by dragging its frame.

## Fixed

- **The settings window wasn't actually remembering its size or "keep on top" setting.** Root cause: the icon-name field triggered a real settings save in the middle of every startup load, before the window's own size/position had been applied yet - that premature save overwrote the just-loaded values with the window's not-yet-initialized defaults. Loading now can't trigger a save of its own.
- **Changing the overlay label's font, colour, or position didn't apply to thumbnails until something else forced a refresh.** It now updates immediately.
- **Turning off "Track client locations" wiped every saved window position**, even though nothing reads them while tracking is off. Re-enabling it now picks up where you left off instead of starting from scratch.
- **Unchecking "Hide the title bar" was forcing a title bar back onto clients that never had one**, including EVE's own borderless "Fixed Window" mode - this could leave the client's rendering stuck sized for the wrong area until a full relaunch. Unchecking it now just leaves EVE's own window styling alone.
- **Disabling a client's thumbnail on the Clients tab turned its square red in the character indicator**, which looked like an error state. It now shows as an ordinary square in its usual spot.
- **Fixed a rare startup crash** that could happen while checking for updates.
