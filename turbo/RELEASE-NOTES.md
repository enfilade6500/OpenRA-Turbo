# OpenRA Turbo 1.0

A faster client for OpenRA release-20250330. It simulates the same game, so it plays on the
usual servers with the usual clients; the main menu shows `release-20250330 (Turbo 1.0)`.

## Downloads

Each one is OpenRA's own release-20250330 download with Turbo's files in it, nothing else
changed; it sits next to a standard install and shares its settings, maps, replays and content.

- **Windows:** `OpenRA-Turbo-1.0-release-20250330-x64-winportable.zip` — extract, run `RedAlert.exe`.
- **Linux:** `OpenRA-Turbo-1.0-Red-Alert-x86_64.AppImage` — make executable, run.
- **Patch for an existing install, any OS:** `OpenRA-Turbo-1.0-release-20250330-patch.zip` — the
  changed files only; see the README. macOS has no whole-game download (Apple's signing rules);
  TURBO.md has a Terminal recipe for patching an installed copy.
- `SHA256SUMS.txt`.

Measured on an 8-player, 57-minute Red Alert game replayed on one machine, simulation time per
tick against the stock client: 20.4 → 15.8 ms over the whole game and 36.7 → 26.0 ms in the
last ten minutes, then a further 11% and 12% from the later scan work (measured on a slower day,
relative to the build before it). The replay stayed in sync with the stock clients' recorded
sync hashes for all 86,000 ticks in every run.

## Simulation (same results, less work)

- Idle units no longer walk through every neighbour looking for a target when nobody hostile
  is near; when they do walk, they walk only the actors that could be hostile, with an
  actor's target types cached between changes and the cheap checks made first.
- Aircraft find the aircraft they keep their distance from without walking every unit under
  the flight path.
- Things that are only drawn do less per tick: still animations, hidden animations, rank
  decorations, GPS dots, production bars.
- A hole found on the way, in the first version of the scan skip: the tick after a capture,
  the captured building was still filed under its old owner. Fixed before any release.

## Drawing

- Selecting hundreds of units no longer doubles the frame: brackets and bars are drawn with
  less work per unit, and a held drag box costs what "always show bars" costs instead of
  growing with the square of the units.
- "Show when damaged" no longer costs a pass over every undamaged unit.
- The mouse pointer over a large selection is chosen from one rifleman (and one rocket
  soldier) instead of each of them; everything else is still asked unit by unit.
- Frames are skipped while the simulation is behind, down to a floor (default 20 FPS, in
  Settings > Turbo), so a slow computer plays at full speed instead of falling behind.
- Scrolling keeps its speed at any frame rate (stock slowed it below 40 FPS).

## Settings

A *Turbo* page in the settings: the frame-skip floor, a forced low frame rate for testing, the
status-bars setting, and three options to draw fewer bars in huge games (selected units'
health bar only when damaged; no bars inside a drag box; no automatic bars on infantry).

## Also in the box

A benchmark that records real games (`bench.cmd`, see TURBO.md), and a profiler
(`Bench.Profile=true`) that splits the simulation's time by trait, activity and actor type.

## Known limits

- Red Alert only. Tiberian Dawn and Dune 2000 are in the Windows download because OpenRA's is
  one zip for all three, but they are untested and have no Linux download. An "out of sync" that
  only the Turbo player gets is the signal; please report it with the replay.
- Hidden looping animations resume where they stopped rather than where they would have been.
- The settings labels are English only.
- The next OpenRA release will need the work ported again.
