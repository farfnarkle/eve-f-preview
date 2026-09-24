# v1.0.12

## Fixed

- **General tab: the "Config profile" row (dropdown + Load / Save As… / Import…) was silently collapsing to nothing**, leaving only its orphaned help icon visible and those controls unreachable. The panel hosting them relied on a layout pattern (a plain `Panel` with `AutoSize` wrapping a docked child) that doesn't reliably report a size in WinForms - rebuilt it as a `TableLayoutPanel`, which does.
- **About tab used fixed pixel positions for every label** instead of the same auto-flowing layout the other tabs use, which caused the new "Update available" link to overlap the app name/version row. Converted it to the shared layout system so it can't happen again.

## Changed

- **Tightened spacing across every settings tab.** Row-to-row gaps, button margins, and panel padding were all reduced roughly in half from where a previous UI pass had left them, so the settings window reads less spaced-out without changing anything functionally.
