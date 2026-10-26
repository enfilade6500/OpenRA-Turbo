# OpenRA Turbo 1.1

A faster client for OpenRA release-20250330. It simulates the same game, so it plays on the
usual servers with the usual clients; the main menu shows `release-20250330 (Turbo 1.1)`.

## Downloads

Each one is OpenRA's own release-20250330 download with Turbo's files in it, nothing else
changed; it sits next to a standard install and shares its settings, maps, replays and content.

- **Windows:** `OpenRA-Turbo-1.1-release-20250330-x64-winportable.zip` — extract, run `RedAlert.exe`.
- **Linux:** `OpenRA-Turbo-1.1-Red-Alert-x86_64.AppImage` — make executable, run.
- **Patch for an existing install, any OS:** `OpenRA-Turbo-1.1-release-20250330-patch.zip` — the
  changed files only; see the README. macOS has no whole-game download (Apple's signing rules);
  TURBO.md has a Terminal recipe for patching an installed copy.
- `SHA256SUMS.txt`.

## New in 1.1: maps full of walls

A map's walls, trees, mines and buildings were visited several times every tick, like units in
combat, although nothing happens to them: their own tick, their sprites, their health bar's
smoothing, each player's frozen-under-fog copy of them. On an 8-player interior map with 6,600
wall pieces (Eight Chambers) that was 25 ms a tick from the first second. Now:

- an idle actor whose idle hooks have nothing to do is skipped by the world loop;
- a trait can stop being ticked until something it knows about happens: the health bar once it has
  settled, a wall until a neighbour changes, an actor's sprites once every animation is still;
- each player's frozen-actor bookkeeping ticks only the copies with something to do and keeps its
  synced hashes as running sums, bit-identical to the old walk over every copy;
- craters waiting under the fog are re-checked only when the fog or the craters changed.

Eight Chambers on the test machine: the first 236 ticks of a replay, 20.6 ms a tick with 1.0, 6.7
with 1.1; nine to twelve minutes in, armies fighting, 34.2 against 15.9. Whole replays of that game
and of a 57-minute Pit game play back in sync with the recorded hashes. Maps without thousands of
static actors gain less; nothing is lost on any map.

Also: the bench splits the logic tick into its phases (`actors_ms`, `traits_ms`, `effects_ms`,
`synchash_ms`, `tickrender_ms`), notes garbage-collection pauses (`gc_gen`, `gc_pause_ms`) and can
carry on past a desync (`Bench.IgnoreDesync=true`).

## From 1.0

Simulation (same results, less work): idle units skip the target scan when nobody hostile is
near, and walk only the possibly hostile when they do; aircraft keep their distance without walking
every unit below; still animations, hidden animations, rank decorations, GPS dots and production
bars do less per tick. Drawing: selecting hundreds of units no longer doubles the frame; "show
when damaged" is free on undamaged units; the mouse pointer over a large selection is chosen from
one rifleman. Frames are skipped while the simulation is behind (floor 20 FPS, *Settings > Turbo*)
so a slow computer plays at full speed instead of falling behind; scrolling keeps its speed at any
frame rate. A *Turbo* settings page with the frame-skip floor, a forced low frame rate for testing,
the status-bars setting and three options to draw fewer bars.

## Known limits

- Red Alert only. Tiberian Dawn and Dune 2000 are in the Windows download because OpenRA's is
  one zip for all three, but they are untested and have no Linux download. An "out of sync" that
  only the Turbo player gets is the signal; please report it with the replay.
- Hidden looping animations resume where they stopped rather than where they would have been.
- A trait that stopped being ticked and missed its wake-up would show a stale health bar or a
  wall drawn with an old connection, never anything in the game; none seen so far.
- The settings labels are English only.
- The next OpenRA release will need the work ported again.
