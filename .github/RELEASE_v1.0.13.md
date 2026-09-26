# v1.0.13

## Changed

- **Rewritten from Windows Forms to WPF**, with a redesigned settings window: a Windows 11 Settings-style layout with left-hand navigation instead of tabs, and cards/toggle switches instead of dense option grids. Every page (General, Thumbnails, Overlay, Zoom & overwatch, Hotkeys, Cycle groups, Clients, Settings sync, About) has been reorganized for readability. Config files, hotkeys, and thumbnail behavior are unchanged - this only replaces how the settings window looks and is built.
- **Added a dark/light theme**, with a toggle at the bottom of the navigation. By default it follows your Windows theme; switching it explicitly remembers your choice. The window's title bar matches too.
- **The settings window now remembers its size** between restarts, and has a new "Keep this window on top" option (General tab) if you'd rather it not always float above EVE.
- **Added "Maintain aspect ratio"** next to the thumbnail size fields (Thumbnails tab): turn it on and changing the width adjusts the height to match, and the other way round. It also applies when resizing a thumbnail by dragging its frame.

## Fixed

- **Changing the overlay label's font, colour, or position didn't apply to thumbnails until something else forced a refresh** (like moving one). It now updates immediately.
- **Turning off "Track client locations" wiped every saved window position**, even though nothing reads them while tracking is off. Re-enabling it now picks up where you left off instead of starting from scratch.
- **Unchecking "Hide the title bar" was forcing a title bar back onto clients that never had one**, including EVE's own borderless "Fixed Window" mode - this could leave the client's rendering stuck sized for the wrong area until a full relaunch. Unchecking it now just leaves EVE's own window styling alone.
- **Disabling a client's thumbnail on the Clients tab turned its square red in the character indicator**, which looked like an error state. It now shows as an ordinary square in its usual spot, the same as before it had a thumbnail toggle.
