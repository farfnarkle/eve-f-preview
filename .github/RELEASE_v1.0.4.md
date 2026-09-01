## EVE-F-Preview 1.0.4

Tighter, more reliable window activation when cycling and clicking quickly.

### Fixes

- **Thumbnail highlight briefly jumping to the wrong client, then reverting** — could happen when cycling with a hotkey quickly while also clicking directly on an EVE window. The internal mechanism used to steal window focus tracked a single shared "pending" target; a hotkey press and a click landing close together could overwrite each other's target before the previous one was actually applied, silently dropping an activation. Activation requests are now served one at a time from a real queue instead.
- **Failed activations now self-correct immediately** instead of leaving the wrong thumbnail highlighted for up to ~750ms — the app confirms against the real foreground window right away and reconciles if it doesn't match.
- Removed an unnecessary background-thread hop on the click-to-activate path that was only adding an ordering hazard.

### Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)
- EVE clients in Fixed Window or Window Mode (Fullscreen not supported)

### Install

Extract the zip somewhere writable (not Program Files), run `EVE-F-Preview.exe`. Existing `EVE-F-Preview.json` is unchanged.
