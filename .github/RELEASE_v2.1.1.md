# v2.1.1

Thumbnails stay on top of fullscreen EVE clients, and the update check runs only at startup.

## Fixed

- **Thumbnails hidden behind fullscreen EVE.** A client in Fullscreen mode put itself above the thumbnails when it came to the front. The thumbnails now go back on top whenever an EVE client is activated, as they did in EVE-O. Swapping between fullscreen clients is slower than in Fixed Window mode, which looks the same and swaps instantly.

## Changed

- **Update check** runs once when the app starts, instead of every 12 hours.
