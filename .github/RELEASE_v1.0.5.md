## EVE-F-Preview 1.0.5

Wormhole class and statics on the thumbnail overlay.

### New

- **Wormhole system info on the overlay** — with "Show system name" enabled (Overlay tab), a client sitting in a J-space system now gets a third overlay line showing that system's own class and its static wormhole destination(s), e.g.:

  ```
  Farfnarkle
  [J154516]
  C2 : HS/C4
  ```

  Backed by a bundled, offline-compiled dataset covering 2,585 wormhole systems — CCP's Static Data Export and ESI don't carry static wormhole data at all, so there's nothing to query live. K-space systems are unaffected; only recognized J-space systems get the extra line.

### Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)
- EVE clients in Fixed Window or Window Mode (Fullscreen not supported)

### Install

Extract the zip somewhere writable (not Program Files), run `EVE-F-Preview.exe`. Existing `EVE-F-Preview.json` is unchanged.
