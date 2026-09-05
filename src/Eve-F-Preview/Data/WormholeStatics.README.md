# Wormhole system statics

`WormholeStatics.json` maps each J-space system name to its own class (C1-C6,
or C13-C18 for shattered systems) and the destination(s) its static wormhole(s)
always lead to (C1-C6, HS, LS, or NS). This is community-compiled data that
CCP's Static Data Export and ESI do not carry.

## Source & license

- **Statics (system -> wormhole type ids):** [exodus4d/Pathfinder](https://github.com/exodus4d/pathfinder),
  `export/sql/eve_universe.sql.zip` (`system` and `system_static` tables).
- **Wormhole type -> destination class table:** hand-extracted from Pathfinder's
  `js/app/conf/signature_type.js`.
- **License:** MIT — Copyright (c) 2017 Mark Friedrich. Redistributed with
  attribution per https://github.com/exodus4d/pathfinder.
- **Type id -> hole code:** [Fuzzwork](https://www.fuzzwork.co.uk) SDE CSV export (`invTypes.csv`).
- **Snapshot pulled:** 2026-09-05 (2,585 of 2,604 systems resolved; a handful of
  rare Thera/Pochven-adjacent hole codes aren't in the base signature_type.js
  table and are skipped rather than mislabeled).

## Regenerating

This is a manual, offline step - not run by the app or the release build, so a
stale or unreachable upstream source can never break a build. To refresh:

1. Pull the latest `export/sql/eve_universe.sql.zip` from Pathfinder and the
   latest `invTypes.csv` from Fuzzwork.
2. Re-run the join (system -> statics -> hole code -> destination class) and
   regenerate `WormholeStatics.json`.
3. Review the diff (system/static counts) before committing.
