# v1.0.6

## Fixed

- **Keyboard cycle hotkeys occasionally not activating the real EVE client.** When you cycled quickly, the thumbnail highlight could advance without the actual client window coming to the foreground.

  The keyboard cycle hotkeys are handled inside a genuine `WM_HOTKEY` message, which is exactly the moment Windows grants a process permission to change the foreground window. The previous activation path ignored that grant and instead always kicked off a synthetic key-press round-trip through the message queue before calling `SetForegroundWindow` — a race that could occasionally lose or delay the activation under fast cycling.

  Activation now tries a direct `SetForegroundWindow` first and verifies it against the real foreground window. When called from inside a real hotkey handler (all keyboard cycling), this succeeds immediately with no race. The synthetic-press fallback is unchanged and still handles the mouse-button hotkey path, which never gets the automatic grant.
