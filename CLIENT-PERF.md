# Client performance branch

A set of client-side changes to OpenRA release-20250330 that make big games run faster on the
player's own computer. None of them changes what the game simulates: a player running this
client plays in the same games, on the same servers, as players running the stock release, and
the stock release's multiplayer version check passes. This branch is based directly on the
release and contains nothing of the ZeroLag server work.

## What it does

**Idle target scans skip the walk when nobody hostile is near.** A unit standing idle rescans its
weapon range every 3 to 8 ticks for something to shoot at. Stock enumerates every actor in range
and rejects the non-hostile ones one by one, so an army standing around spends most of its tick
looking at itself, in proportion to how tightly it is massed. The actor map now records, per
spatial bin, which owners have actors in it (and which have actors that could be disguised as
someone else's); a unit that can only attack enemies asks that first and returns at once when the
answer is nobody. When the walk does run, it runs unchanged and in the same order, so the chosen
target is the same.

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
reused then; a computer that draws several frames per tick is spared all but the first. And above
100 selected units, one unit of each *type* is asked rather than every unit: units of a type answer
alike unless one is in an unusual state (out of ammunition, a trait disabled), and a selection that
size is thousands of riflemen, not a handful of aircraft; a unit with no order at all (an ally's, a
dead one) passes the question to the next of its type. The orders issued on a click are still
worked out per unit, so only the icon could ever differ, and only in that unusual case. Headless,
1,000 riflemen selected: 2.2 ms a frame to 0.3. Below 100 selected nothing changes.

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

**Three status-bar options**, in *Settings > Display* under the status bars dropdown, for players who
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
*Settings > Display*: "Catching up: skip frames down to N FPS" (`Graphics.MinFramerate`,
default 15; 25 never skips at normal game speed). *Settings > Advanced*, developer section:
"Force a low frame rate" (`Debug.ForceFramerate`, 0 = off) draws at most that many frames per
second whatever the load, to try out what a low frame rate feels like.

## Measurements

Headless playback of a server-side replay of an 8-player game on Big Vanilla Pit (57 game
minutes, 3,300 to 3,600 actors late in the game), on one machine, stock versus this branch, mean
simulation time per tick:

| | whole game | minutes 10-30 | 30-45 | 45-55 | p95, 45-55 |
|---|---|---|---|---|---|
| stock | 20.4 ms | 16.6 | 24.0 | 36.7 | 51.4 |
| this branch | 15.8 ms | 13.7 | 18.6 | 26.0 | 36.9 |

The playback stayed in sync with the replay's recorded sync hashes for all 86,000 ticks; the
replay was recorded from stock clients, which is the test that the simulation is unchanged.

Synthetic map, 2,000 idle riflemen massed in one blob: 34.7 ms per tick stock, 9.2 ms here.

Frame skipping, on a synthetic 4,000-unit map at 1080p where a tick plus a frame takes 60 ms
against a 40 ms budget: stock ran the game at 15 ticks per second, this branch at 20, bound by
the 15 FPS floor (a floor of 10 reaches the full 25).

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
in-game performance graph shows too. In the `Bench.Select` modes the mouse is put over the middle
of the screen, so the cursor is worked out over the units as it would be in play. A real machine,
4,000 infantry selected at 1755x987: 46 ms a frame, of which 12.6 producing the annotations and
6.7 drawing them; the rest is the world pass (20 ms with nothing selected) and the cursor. The other `Bench.*` options
(see the file's header) replay a recorded game as if it were live, with a padded or ballasted
tick, a repeated world draw, a fixed camera or a whole-map zoom, for controlled experiments.

## Building and installing

Build as usual for OpenRA (`make` on Linux and macOS, `make.cmd all` on Windows; see
INSTALL.md). The tree is stamped `release-20250330` (the `VERSION` file and the mods'
`Version:` lines, as the release packaging does), so the build identifies itself as the release
and plays on release servers with release clients; a `{DEV_VERSION}` build would be turned away.
Then copy, from the build output into an installed release-20250330:

    OpenRA.Game.dll
    OpenRA.Mods.Common.dll
    OpenRA.Mods.Cnc.dll
    mods/common/chrome/settings-display.yaml
    mods/common/chrome/settings-advanced.yaml

over the files of the same name (keep copies of the originals to go back). The whole build can be
run in place as well, like any OpenRA checkout.

## Caveats

- A real desync would show as the usual "out of sync" drop, for the player running this client.
  The replay evidence is strong; a small game before a big one is still sensible.
- Hidden looping animations resume from where they stopped rather than from where they would
  have been. Nobody can tell, but it is a visible difference from stock in principle.
- The settings labels are plain English, not translation keys.
- "No automatic status bars on infantry" goes by the `Infantry` target type, which the official
  mods all use; a mod that names its infantry differently gets no effect from it.
