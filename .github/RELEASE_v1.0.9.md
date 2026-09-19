# v1.0.9

## Fixed

- **Client swaps that occasionally didn't land** when cycling quickly. Right after asking Windows to change the foreground window, the OS briefly reports no foreground window at all while the switch completes; the app read that gap as a failed swap and needlessly escalated. Confirmation now waits for the switch to settle before judging it.
- If Windows genuinely refuses a swap, activation now escalates through stronger fallbacks (sharing input with the current foreground thread, then an Alt tap) before giving up, instead of trying a plain `SetForegroundWindow` twice.

In testing, 176 consecutive swaps all landed on the first, direct attempt (median ~18 ms, max ~51 ms).
