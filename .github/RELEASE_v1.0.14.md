# v1.0.14

## Fixed

- **The settings window didn't actually remember its size (introduced in v1.0.13).** The size was only saved 800ms after you stopped resizing, so it doesn't write to disk on every pixel while dragging - but closing or minimizing right after resizing (the normal way to do it) beat that timer, and the new size was silently dropped. Closing or minimizing now saves the size immediately instead of waiting.
- **Fixed a rare startup crash** that could happen while checking for updates, introduced by the same v1.0.13 rewrite.
