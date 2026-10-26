# OpenRA Turbo

A faster client for OpenRA release-20250330: a set of client-side changes that make big games run
faster on the player's own computer. None of them changes what the game simulates: a player
running Turbo plays in the same games, on the same servers, as players running the stock
release, and the stock release's multiplayer version check passes. The main menu shows
`release-20250330 (Turbo 1.0)`; the mod version servers see is the release's. The source is based
directly on the release; it shares nothing with the author's ZeroLag server fork beyond stock
OpenRA. The knobs it adds are on a *Turbo* page in the settings.

## What it does

**Idle target scans skip the walk when nobody hostile is near.** A unit standing idle rescans its
weapon range every 3 to 8 ticks for something to shoot at. Stock enumerates every actor in range
and rejects the non-hostile ones one by one, so an army standing around spends most of its tick
looking at itself, in proportion to how tightly it is massed. The actor map now records, per
spatial bin, which owners have actors in it (and which have actors that could be disguised as
someone else's); a unit that can only attack enemies asks that first and returns at once when the
answer is nobody. When the walk does run, it runs in the same order, so the chosen target is the
same. The walk itself, for the scans that do run (the front line, and medics, who look for allies),
is cheaper three ways. A unit that attacks only enemies walks only the actors that could appear
hostile to it (owned by an enemy, or disguisable and not an ally's), which the actor map yields in
the same order as everything, instead of every neighbour; the stock hostility test still runs on
those, so the candidates are the same. An actor's enabled target types, asked for every candidate,
are kept between changes of its conditions rather than recomputed each time (`Targetable` says when
they change). And the cheap priority filter (relationship, target types) comes before the costly
visibility checks rather than after — what a medic needed, since an undamaged ally fails by target
type (the `Heal` type exists only while damaged) and stock paid the shroud lookups first for every
one of them. Late game on the Pit, profiled: a walking scan 48 µs down to 18, the 95 of them per
tick 4.5 ms down to 1.7, medics 3.1 ms down to 1.7. A unit or building that changes owner is taken
out of the world and put back, which lands in the bins a tick later; over that tick the bin also
carries the owner it changed to, so a scan that stock would have made against a just-captured
building is still made.

**Aircraft repulsion walks only aircraft.** Every airborne aircraft pushes away from the others
around it every tick, and found them by walking every unit under its flight path; the actor map
keeps the aircraft of each bin in a side list, in the same order, and the walk uses that.

**Things that are only drawn do less per tick.** Rank decorations do not tick while hidden or
when they are a single still image; GPS dots compute their visibility for the player whose view
is drawn rather than for every player; production bars compute their value when drawn; still
animations (most standing units, every tree and wall) are not ticked, nor are looping animations
that are currently hidden. Animations with a completion callback keep ticking exactly as before.

**A large selection is drawn more cheaply.** Selecting a few hundred units was found to double the
frame on a real machine. Each selected unit's corner brackets are now eight straight lines rather
than four joined polylines, boxes and bars go through a many-lines call that prepares the colour
once and writes each quad straight into the batch, and a unit's decoration bounds are computed
once per frame instead of once for the box, once for the bars and once for the screen bounds.
Measured headless with 1,000 infantry selected: the per-frame cost of the selection down by about a
quarter. On a real machine with 4,000 infantry selected it was less: 48 ms a tick down to 45, over
a 20 ms baseline, so most of what a selection costs there is still there. The live bench's
annotation columns (below) say where it goes on a given machine; the per-unit bookkeeping (trait
lookups, the renderable objects) is the next thing to look at if it still matters.

**"Show when damaged" no longer costs a bars pass over every undamaged unit.** With that setting,
stock gave every unit on the screen a bars renderable on every frame, for the extra bars (cargo,
ammunition, capture progress) that the setting also shows; a unit with no such bars got one that
drew nothing, which on a screen of 4,000 undamaged infantry was 5 ms a frame on a real machine for
no pixels. An actor with no extra bars now gets the renderable only when its health is to be shown.

**The mouse cursor over a large selection costs less.** The cursor is chosen on every frame from the
orders of every selected unit (whichever offers the highest-priority order for what is under the
mouse wins), which with 4,000 selected was as much work as drawing their decorations. Each unit's
order list was built with LINQ and sorted afresh — an object per order, an iterator per trait and
the sort's buffers, every unit, every frame; it is now collected into a reused list and sorted in
place, with the same order. Measured headless, 1,000 infantry selected and the mouse over them:
3.6 ms a frame down to 2.3. And between frames of the same world tick the answer cannot change
unless the mouse reached another cell or target, a modifier changed or the selection did, so it is
reused then; a computer that draws several frames per tick is spared all but the first. And the
basic infantry — `Game.CursorSampleActorTypes`, by default `e1` and `e3`, the rifleman and the
rocket soldier in Red Alert and Tiberian Dawn — are asked once per type, through their first
selected unit, rather than once per unit: they make up most of any big selection and answer alike
whatever their state, having no ammunition to run out of and nothing to deploy. Every other type
is asked unit by unit as before, so aircraft that need rearming keep their exact icon however many
are selected. A unit with no order at all (an ally's, a dead one) passes the question to the next
of its type, and the unit under the mouse, if selected, is always asked itself. Headless, 1,000
riflemen selected: 2.2 ms a frame to 0.3. Other mods name their infantry differently; the list
is a setting.

**A held drag box is no longer heavier than "always show".** While a selection box is held open,
every unit inside shows its bars. The set of units in the box was kept as a list and searched once
per actor on the screen per frame, a box of 500 over a screen of 1,000 being half a million
comparisons a frame; it is a set now. And the box's contents, with their selection-priority sort,
were recomputed on every frame; they are recomputed when the mouse, the modifiers or the world
tick change, which on a computer that draws several frames per tick is less than half as often.
Measured headless with a box held over 1,000 units: the per-frame annotation work from 3.1 ms back
to the 1.2 ms it costs with no box at all. On a real machine, a box held over 4,000 infantry: 80 ms a
tick before, 28 after, against 20 with no box; the 8 ms left is the bars themselves. The search was
quadratic, so the bigger the game, the more it mattered.

**Three status-bar options**, in *Settings > Turbo* under the status bars dropdown, for players who
would rather not pay for bars that tell them nothing (each is a few microseconds per unit per
frame, which is nothing for twenty units and a frame-doubling for four thousand):
"Selected: health bar only when damaged" (`Game.SelectedHealthBarOnlyWhenDamaged`) leaves the
brackets and the extra bars (ammunition, cargo, capture progress) as they are but shows a selected
unit's health bar only while it has lost health; "Bars for units inside a selection box"
(`Game.DragBoxStatusBars`, on by default) turned off means a box being dragged highlights nothing
until it is released, and the box's contents are not even worked out until then; "No automatic
status bars on infantry" (`Game.NoAutomaticStatusBarsOnInfantry`) exempts infantry (actors with the
`Infantry` target type) from "Show when damaged" and "Always show". All three are display settings
of this client only.

**Frames are skipped while the simulation is behind.** Stock draws at least one frame after every
simulation tick, late or not; on a slow computer that frame is time taken from the game, and the
player falls behind. Now, when a tick finishes after the next one was already due, the frame is
skipped and the simulation goes straight on until it has caught up, never falling below a minimum
frame rate so the screen keeps moving. Nothing changes while the computer keeps up.
*Settings > Turbo*: "Catching up: skip frames down to N FPS" (`Graphics.MinFramerate`, default
20; 25 never skips at normal game speed) and, for testing, "Force a low frame rate"
(`Debug.ForceFramerate`, 0 = off), which draws at most that many frames per second whatever the
load, to try out what a low frame rate feels like.

**Scrolling keeps its speed at any frame rate.** Edge and keyboard scrolling move the view once
per frame by an amount meant to follow the time since the last frame, but stock credited a frame
with at most 25 ms, so below 40 frames per second scrolling slowed down (at 15 it crawled at a
third of its speed) — and that is exactly where frame skipping puts a slow computer. The credit is
now capped at 100 ms, enough for any frame rate the skipping allows, and the clock runs between
scrolls so the first frame of a scroll is credited with a frame, not with the cap. Joystick
scrolling (the mouse-scroll mode) was not time-based at all; it now credits frames longer than one
sixtieth of a second with their length, and is otherwise what it was.

**Actors with nothing to do are not ticked (1.1).** Every actor on the map was visited several times
a tick whether anything happened to it or not: its own tick (an idle activity to run, idle
notifiers to call), its `RenderSprites` (animations to advance), its `Health` (the health bar's
display value to smooth), and for a wall its `WithWallSpriteBody` (neighbours to re-check). None
of that does anything for a wall, a tree, a mine or a building standing there, and each visit is
a cache miss into a scattered object, so a map with thousands of such actors paid several
milliseconds a tick for them. Three things now stop that. An idle actor whose idle notifiers all
say they want no tick (`IIdleTickOptional`; `ScriptTriggers` says so until a Lua `OnIdle` handler
is registered) is skipped by the world's actor loop (`Actor.NeedsTick`). An `ITick` trait can ask
not to be ticked until something it knows about happens (`World.SetTickDormant`; the trait
dictionary keeps the flag beside the trait, so the tick loop skips it without touching it): `Health`
once its display value has settled, until the next change of HP; `WithWallSpriteBody` until a
neighbour changes; `RenderSprites` once every animation is static — a still, a held frame
(`Animation.PlayFrame`, which the wall body now uses instead of fetching its frame every tick), a
finished `PlayThen` — and has no offset or visibility function that could change its answer
(`WithSpriteBody` announces its visibility changes instead of being asked). `Animation.Changed`
wakes the sprites when a Play call changes them. Only traits whose tick touches nothing the
simulation reads do this, so a missed wake-up could show on the screen, never in the game.

**The frozen-actor bookkeeping is event-driven (1.1).** Each player's `FrozenActorLayer` walked
every frozen copy on the map every tick (11 players × 6,600 walls on Eight Chambers: 73,000
visits a tick) to tick it and to recompute two synced hashes, and `FrozenUnderFog` asked each of
its copies every frame whether it wanted fresh renderables. The layer now keeps the hashes as
running sums (adjusted on add, remove, and each change of `Visible`, which only ever happens in
the copy's own tick) and ticks only the copies with something to do: a flash, a visibility update
(the shroud callback already names them), or an actor that has died or been disposed
(`FrozenUnderFog` tells the copies). The sums are published at tick time exactly as the walk
computed them — the copies present during the tick, those visible after it — so the hashes are
bit-identical, which the replay check confirms. `FrozenUnderFog` keeps one "someone is waiting"
flag, set by the copies themselves, and drops out of the render tick while it is clear. Upstream's
open pull request #22420 is after the same cost with a different approach (see "Relation to
upstream OpenRA").

**Smudges under the fog are re-checked only when something changed (1.1).** `SmudgeLayer` looked at
every crater waiting under the viewer's fog every frame to see whether it had come into view —
thousands of them late in a game, forever. It now does so only when a smudge was added or removed,
the viewer's shroud changed anywhere, or the viewer changed.

## Measurements

Eight Chambers (1.1): an 8-player interior map with 6,576 wall actors, which played at 25 ms a
tick from the first second on a fast computer. The first 236 ticks of a replay, headless, on this
machine: 21.8 to 24.2 ms a tick with 1.0, 7.7 to 8.4 with 1.1. What was 3.1 ms of actor ticks is
1.3; 7.9 ms of trait ticks is 0.3; 4.3 ms of render ticks is 0.1. What remains is mostly the sync
hash (2.4 ms a tick on average: every actor's synced state every net frame, which is the protocol)
and the engine's fixed per-tick work.

Headless playback of a server-side replay of an 8-player game on Big Vanilla Pit (57 game
minutes, 3,300 to 3,600 actors late in the game), on one machine, stock versus this branch, mean
simulation time per tick:

| | whole game | minutes 10-30 | 30-45 | 45-55 | p95, 45-55 |
|---|---|---|---|---|---|
| stock | 20.4 ms | 16.6 | 24.0 | 36.7 | 51.4 |
| this branch | 15.8 ms | 13.7 | 18.6 | 26.0 | 36.9 |

The playback stayed in sync with the replay's recorded sync hashes for all 86,000 ticks; the
replay was recorded from stock clients, which is the test that the simulation is unchanged.
The later scan work (walking only the hostile, the cached target types, the aircraft list, the
cheaper rejection) was measured on a slower day on the same machine, so its numbers are relative:
against the build of the table, the whole game another 11% less, minutes 45-55 12% less
(35.2 to 30.9 ms there that day, p95 48 to 41), again in sync for every tick.

Synthetic map, 2,000 idle riflemen massed in one blob: 34.7 ms per tick stock, 9.2 ms here.

Frame skipping, on a synthetic 4,000-unit map at 1080p where a tick plus a frame takes 60 ms
against a 40 ms budget: stock ran the game at 15 ticks per second, this branch at 20, bound by
the 15 FPS floor (a floor of 10 reaches the full 25).

The bench now also splits the logic tick (`actors_ms`, `traits_ms`, `effects_ms`, `synchash_ms`,
`tickrender_ms`, from the engine's own timers, so unlike the profiler's per-call sampling they do
not inflate cheap frequent calls), and `Bench.IgnoreDesync=true` keeps a run going past a desync
for workload measurements on placeholder content.

## Measuring real games: the live bench

The branch also carries the client bench (`OpenRA.Game/Support/LiveBench.cs`). It does nothing
unless asked for on the command line. `bench.cmd` in the checkout starts the game with it on,
recording to `results\live` (anything else on its command line is passed on, so
`bench.cmd Bench.NoFrameSkip=true` is the stock-drawing comparison run); by hand it is

    launch-game.cmd Game.Mod=ra Bench.Live=true Bench.Out=C:\bench

from a checkout, or the same arguments to `OpenRA.exe` / `RedAlert.exe` in an install the DLLs
were copied into. The game plays exactly as normal and writes, for
every multiplayer or skirmish game of the session, `live-<time>-<map>.csv` with one row per tick
— logic ms, how late the tick started, frames drawn and frames skipped, render ms, actors alive
and in view, the server's tick scale, stalls — plus a `.json` summary and a line in
`summary.txt`. Those files are how to see what this build does on a given computer in a given
game, and what a slow computer is actually short of. `Bench.NoFrameSkip=true` records a game
with a frame after every tick, as stock draws, for comparison. On a test map, `Bench.Select=all`
keeps every unit selected and the camera on them (`=camera` for the camera alone, `=drag` for a
held drag box over them). Every row also carries `selected` (units selected that tick) and the
frame's selection work split from the rest: `annot_gen_ms` (producing the boxes, bars and other
annotations), `annot_draw_ms` (turning them into vertices), `flush_ms` (handing the batches to
the GL driver) and `cursor_ms` (choosing the mouse cursor from the selection's orders), from the
perf items `render_annot_gen`, `render_annot_draw`, `render_flush` and `render_cursor`, which the
in-game performance graph shows too, and the frame's three stages, `prepare_ms` (deciding what to
draw: the actors in view, their sprites, sorting), `world_ms` (drawing the terrain and the sprites)
and `widgets_ms` (the user interface, radar included, plus the annotations and the cursor). In the `Bench.Select` modes the mouse is put over the middle
of the screen, so the cursor is worked out over the units as it would be in play. A real machine,
4,000 infantry selected at 1755x987: 46 ms a frame, of which 12.6 producing the annotations and
6.7 drawing them; the rest is the world pass (20 ms with nothing selected) and the cursor. The other `Bench.*` options
(see the file's header) replay a recorded game as if it were live, with a padded or ballasted
tick, a repeated world draw, a fixed camera or a whole-map zoom, for controlled experiments;
`Bench.Timestep=1` runs a replay flat out. `Bench.Profile=true` adds `<name>-profile.csv`, the
simulation's time split by trait, activity, actor type, effect, idle-notification trait and scan
outcome, sampled one tick in ten and summed every 250 ticks; it is what the measurements in this
file came from. Off, it costs a static bool test per sample point.

## Building and installing

Build as usual for OpenRA (`make` on Linux and macOS, `make.cmd all` on Windows; see
INSTALL.md). The tree is stamped `release-20250330` (the `VERSION` file and the mods'
`Version:` lines, as the release packaging does), so the build identifies itself as the release
and plays on release servers with release clients; a `{DEV_VERSION}` build would be turned away.
Then copy, from the build output into an installed release-20250330:

    OpenRA.Game.dll
    OpenRA.Mods.Common.dll
    OpenRA.Mods.Cnc.dll
    mods/common/chrome/settings.yaml
    mods/common/chrome/settings-display.yaml
    mods/common/chrome/settings-advanced.yaml
    mods/common/chrome/settings-turbo.yaml
    mods/common/fluent/turbo.ftl
    mods/<mod>/mod.yaml            (lists the new settings page and its strings; ra, cnc, d2k and ts each)
    mods/cnc/chrome/settings.yaml  (Tiberian Dawn has its own)

over the files of the same name (keep copies of the originals to go back). The whole build can be
run in place as well, like any OpenRA checkout. (`settings-display.yaml` and `settings-advanced.yaml`
are back to stock since the Turbo page took their rows; copying them is harmless.)

### The downloads

`turbo/package.sh` (Linux) makes the release downloads from a build of the three assemblies and
OpenRA's own release-20250330 downloads, which it fetches once into `turbo-dist/upstream` and
checks against their SHA-256 as published on OpenRA's release page (pinned in the script):

- `OpenRA-Turbo-<ver>-release-20250330-patch.zip` — the files above, laid out as they go into
  an install, for patching a stock installation on any OS. `turbo/package.cmd` makes the same
  zip on Windows from `make.cmd all`.
- `OpenRA-Turbo-<ver>-release-20250330-x64-winportable.zip` — OpenRA's official Windows
  portable zip with those files laid over it, nothing else touched. Unzip anywhere and run.
- `OpenRA-Turbo-<ver>-Red-Alert-x86_64.AppImage` — OpenRA's official AppImage, unpacked,
  overlaid and repacked with the official image's own runtime (the `.desktop` name gains "Turbo"
  so a menu entry can be told from stock). Only Red Alert is built, being the only mod tested;
  the script builds the Tiberian Dawn and Dune 2000 ones the same way if asked.
- `SHA256SUMS.txt`.

The released files are not built on anyone's own machine. The GitHub Actions workflow
`.github/workflows/turbo.yml` builds the engine, runs the mod checks and runs `package.sh` for
every push, so each commit has a public build log with the downloads attached to it; pushing a tag
`turbo-<version>` (which must equal `Turbo.Version`) additionally records a signed provenance
attestation for each download and opens a draft release holding them, which a person reviews and
publishes. Anyone can check that a download came out of that workflow, from that commit, with
`gh attestation verify <file> --owner enfilade6500`. OpenRA's own workflows (its CI, release
packaging, itch.io and documentation uploads) are not carried, since they are its release
machinery and not Turbo's.

The whole-game downloads coexist with a stock install: the support directory (`%APPDATA%\OpenRA`
on Windows, `~/.config/openra` on Linux) holds settings, maps, replays and game content, and
every release-20250330 on the machine shares it. macOS gets no whole-game download: OpenRA's
app bundles are signed and notarized, any change to a file inside breaks the signature, and
re-signing needs an Apple developer account and a Mac. Patching an installed bundle by hand
should work for the terminal-literate: copy the patch zip's files into
`OpenRA - Red Alert.app/Contents/Resources/`, then clear the quarantine flag and re-sign ad hoc,
`xattr -dr com.apple.quarantine "<the .app>" && codesign --force --deep --sign - "<the .app>"`.
Untested: nobody on this project has a Mac.

## Relation to upstream OpenRA

Checked on 2026-10-24 against upstream's `bleed` branch (at dbe6747333, 2026-10-15; about 600
commits since release-20250330) and the heads of its ~680 most recent pull requests (#21700 and
up, merged or not), file by file for everything Turbo touches and by title for the rest; and
against the issue tracker: 79 of the 83 open issues and pull requests matching "performance",
the 24 best matches of the 97 matching health bar, status bar, scrolling or cursor, and the
discussions of the pull requests named below.

One of Turbo's changes was made upstream first and is theirs: **the reusable list of valid
target priorities in `AutoTarget.ChooseTarget`**, in place of a LINQ `ToList` per candidate —
Gustas, [bb0a39ba6c](https://github.com/OpenRA/OpenRA/commit/bb0a39ba6c) "Don't allocate
priorities" (April 2025), with the clear moved to the top of the loop in
[ef8077b589](https://github.com/OpenRA/OpenRA/commit/ef8077b589) "Fix autotarget ignoring
target validity" (May 2026). Turbo's version was written without knowing it and matches the
fixed form; the rest of that change (the filter before the visibility checks, the hostile-only
walk, the cached target types, the armament test without enumerators) is not upstream.
RoosterDragon's [06e4a2e010](https://github.com/OpenRA/OpenRA/commit/06e4a2e010) "Reduce
allocations in main game loop" (December 2025) also addresses the armament-range lambda in the
same loop, by containing it in a local function; Turbo replaces the enumerators instead. A pull
request by Vapre, [#21948](https://github.com/OpenRA/OpenRA/pull/21948) "Less allocations in
AutoTarget" (June 2025), rewrote the same loop without LINQ, with the same aim as those parts of
Turbo and no spatial precheck or caching; it was closed as outdated once 06e4a2e010 had gone in.

Frame skipping is Turbo's own code, but not its own idea: issue
[#18491](https://github.com/OpenRA/OpenRA/issues/18491) "performance, remove the forced renders"
(anvilvapre, August 2020, still open under Idea/Wishlist) asks for exactly that: "let the game
skip render frames - rather than forcing them", a loop that notices when logic and rendering
cannot keep up, and the option of running at 10–20 FPS that earlier releases had. Turbo's
`MinFramerate` floor is that option. The same issue also asks for a split of network and
rendering delay in the ping statistics, which Turbo does not do.

Adjacent, not overlapping: [#21218](https://github.com/OpenRA/OpenRA/issues/21218) "Performance
issues with big amount of units" (November 2023, open) is a report of lag at 400–600 units that
RoosterDragon traced to many AI units pathfinding long distances on a large map, a cost Turbo
does not touch; [#19419](https://github.com/OpenRA/OpenRA/issues/19419) (2021) reports frame
rates collapsing in long games with many units and proposes lazy asset loading;
[#9367](https://github.com/OpenRA/OpenRA/issues/9367) (2015) proposes spreading large
calculations over several ticks; [#14011](https://github.com/OpenRA/OpenRA/issues/14011)
"Option to disable cosmetic renderings" (2017) asks for a detail-level setting that draws less on
weak machines, the family Turbo's status-bar options belong to, though it names different things
(idle overlays, contrails), and the nearest status-bar request,
[#14159](https://github.com/OpenRA/OpenRA/issues/14159) (2017), is for automatic transport-load
bars, not fewer bars. For scrolling, open pull request
[#22522](https://github.com/OpenRA/OpenRA/pull/22522) (Tweety3D, July 2026) adds a mouse-panning
speed slider and widens the edge-scrolling one; the 25 ms per-frame credit that slows scrolling
at low frame rates is not changed there or anywhere upstream. No issue or pull request was found
on the pointer cost of a large selection, the drag box, the selection drawing, aircraft
repulsion, or the idle-unit scans.

Nothing else in Turbo was found upstream, merged or proposed: not the actor-map owner masks and
the scan skip, the hostile-only walk, the per-actor target-type cache, the aircraft list, the
still-animation and decoration skips, the selection drawing, the drag box set, the bars
renderable skip, the pointer choice, or the scrolling cap (upstream's "smooth scrolling" is
sub-pixel camera movement, a different thing). Upstream has done plenty of performance work of
its own since the release that Turbo does not carry and a port to the next release would
inherit: among others, cached renderable enumerables and a stable sort without per-frame
allocation (RoosterDragon), faster trigonometry, deduplication, PNG parsing and radar ticking
(Gustas), MersenneTwister allocation (Gustas), and open proposals for zero-allocation trait
enumerators (Vapre, #22501), `RenderUtil` (Vapre, #22499), cloak detection through the actor map
(darkademic, #22291) and source-generated sync hashing (#21234).

Turbo 1.1's frozen-actor work and upstream's open pull request
[#22420](https://github.com/OpenRA/OpenRA/pull/22420) "Improve frozen actor performance"
(PunkPun, March 2026) go after the same cost — `FrozenActorLayer` and `FrozenUnderFog` with
thousands of actors — and theirs was first. The approaches differ: that pull request describes
immutable structures, cheaper hashing and fewer dictionary walks; Turbo keeps the synced hashes as
running sums and ticks only the copies that have something to do. Turbo's was written without
reading theirs, from the profile of one map, but the problem was theirs to name. The rest of 1.1
(dormant traits, idle actors skipped, the smudge re-check) was not found upstream.

## Caveats

- A real desync would show as the usual "out of sync" drop, for the player running this client.
  The replay evidence is strong; a small game before a big one is still sensible.
- A trait that stops being ticked (1.1) and misses its wake-up would show a stale health bar, a
  wall drawn with an old connection, or a still animation one change behind; none of it reaches
  the simulation. Nothing of the kind has been seen; if it is, the actor type and what was done
  to it are the report.
- Hidden looping animations resume from where they stopped rather than from where they would
  have been. Nobody can tell, but it is a visible difference from stock in principle.
- The settings labels are English only (keys in `mods/common/fluent/turbo.ftl`, one language).
- `bench.cmd Bench.Settings=true` opens and closes the settings dialog headlessly at the menu, which
  builds every settings page; it is the check that a layout change has not broken one.
  `Bench.Settings=TURBO_PANEL` (any page id) opens that page instead and saves a screenshot of it
  to the usual Screenshots folder before exiting, which is the check that it also looks right.
- "No automatic status bars on infantry" goes by the `Infantry` target type, which the official
  mods all use; a mod that names its infantry differently gets no effect from it.
