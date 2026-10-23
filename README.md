# OpenRA Turbo

OpenRA Turbo is a minimal modification of the standard OpenRA release client consisting of a few small tweaks to let it run faster with many units on the map. It is a fully compatible drop-in replacement. As a rough estimate, with several hundreds or thousands of units in a game, it can save 40% of the time per tick, give or take depending on conditions.

## Features

Under the hood:

- **Big games run faster.** Idle units no longer re-check all their neighbours for targets every few ticks, aircraft keep their distance from each other without looking at every unit below them, and things that only get drawn (standing animations, rank icons, GPS dots) stop doing work for the simulation.
- **Big selections draw faster.** Selecting thousands of units, or holding a drag box over them, used to double the time a frame took; now it adds little.
- **Scrolling keeps its speed** at any frame rate; the standard client slows it down below 40 FPS.
- **"Show when damaged" is free** on undamaged units, and the mouse pointer over a huge selection is chosen without asking every rifleman.

Defaults and options, in *Settings > Turbo*:

- **Frame skipping.** When the simulation falls behind, frames are skipped until it has caught up, so the game keeps its speed instead of slowing down for you and everyone waiting on you. By default it never goes below 20 frames per second; the slider sets that floor (25 means never skip).
- A forced low frame rate option, to help the player test and calibrate the frame rate minimum.
- Three ways to draw fewer status bars when thousands of units are on the screen: health bars on selected units only while damaged, no bars inside a drag box, no automatic bars on infantry.

## Install (Windows)

1. Close the game. Find the OpenRA install folder (with `RedAlert.exe` or `OpenRA.exe`; usually `C:\Program Files (x86)\OpenRA\` or `%LOCALAPPDATA%\Programs\OpenRA\`). It must be release-20250330.
2. Keep a copy of the folder, or of the files the zip replaces, to go back.
3. Copy the contents of the zip's `OpenRA` folder over the install folder, replacing files.
4. Start the game. The main menu should say `release-20250330 (Turbo 1.0)`.

To go back, put the saved files back, or reinstall release-20250330. Linux and macOS installs cannot be patched in place; build from source (`make`), see TURBO.md.

## For developers

Every simulation change is checked by replaying a game recorded from stock clients and matching its sync hashes tick for tick, so the results are identical by test, not by argument. Measurements, the proof runs and the tooling are in [TURBO.md](TURBO.md).

Simulation:

- **Idle target scans** ask the actor map first. Each bin keeps, per owner, how many actors it holds and which could be disguised; a unit that attacks only enemies skips its scan when no bin in range holds anyone who could appear hostile, and when it does scan it walks only those actors, in the original order. Owner changes are covered across the one-tick window before the bins are updated.
- **Scan candidates** are rejected by the cheap tests (relationship, target types) before the shroud lookups, with an actor's enabled target types cached between condition changes and no allocation per candidate.
- **Aircraft repulsion** walks a per-bin list of aircraft rather than every actor under the flight path.
- **Draw-only traits** do less per tick: still animations and hidden loops are not ticked (animations with a completion callback always are), rank decorations skip while still or hidden, GPS dots resolve visibility for the viewing player only, production bars compute on draw.

Drawing:

- **Selection decorations** write their quads straight into the batch (brackets as eight lines, bars through a many-lines call), with the decoration bounds computed once per frame per actor instead of three times.
- **Drag box** rollover is a set, not a list, and its contents are recomputed only when the mouse, modifiers or world tick change.
- **Bars renderables** are made only for actors that have something to draw.
- **The mouse pointer** over a selection: order lists built without LINQ and reused, the answer cached across frames of the same tick, and the basic infantry types (`e1`, `e3`, a setting) answered once per type.
- **Frame skipping** in the game loop while the simulation is behind, down to `Graphics.MinFramerate`; `Debug.ForceFramerate` caps frames for testing.
- **Scrolling** credits a frame with up to 100 ms of movement instead of 25, so it holds its speed at low frame rates; joystick scrolling becomes time-based for slow frames.

Tooling, all off unless asked for: a live bench that records real games tick by tick (`bench.cmd`), a replay bench with padded or ballasted ticks, a profiler that splits simulation time by trait, activity and actor type (`Bench.Profile=true`), and a headless settings-page check with screenshots (`Bench.Settings=<page>`).

## Problems and limits

- The one thing this client can do that the standard one cannot is drop *you* with "out of sync" while everyone else plays on. It has not happened in testing; if it does, please report it with the replay and the map.
- Tested on Red Alert. Tiberian Dawn, Dune 2000 and Tiberian Sun run the same engine code but have not had a long game checked against stock clients.
- The next OpenRA release will need the work ported again.

Source: this branch (`turbo`) of [enfilade6500/OpenRA-ZeroLag](https://github.com/enfilade6500/OpenRA-ZeroLag). OpenRA's own README is [README-OpenRA.md](README-OpenRA.md); the license is OpenRA's (GPLv3).
