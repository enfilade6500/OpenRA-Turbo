# stock-bench

OpenRA `release-20250330` exactly as released, plus the client bench and nothing else: the
control for every Turbo measurement. A number from this build and a number from a Turbo build
come from the same recorder, the same columns, the same replay, the same machine; the difference
is Turbo.

What this branch adds to the release, all inert unless the bench is on (`Bench.Out=…` on the
command line):

- `OpenRA.Game/Support/LiveBench.cs` and `AggregateProfiler.cs`, as on Turbo's `main`;
- the hooks they need: timing calls around the logic tick and the frame in `Game.cs`,
  `PerfSample`s around the tick's phases in `World.cs`, the per-trait and per-activity
  attribution for `Bench.Profile`, `OrderManager.OutOfSync` asking the bench before pausing a
  desynced replay, `ReplayConnection.AllOrdersDelivered`, `Activity.LeafActivity`;
- a replay that the bench is measuring is drawn as a live game is (a frame after every tick),
  which is what stock does in a live game and what the comparison is about;
- `Debug.ForceFramerate` (default 0, off), so a run can be made to draw at most n frames a second
  on a machine whose rendering would otherwise dominate the time;
- the game does not save its settings or register itself as an installation while benchmarking.

It does **not** have Turbo's frame skipping, its settings page or any of its simulation and
rendering changes. The simulation is the release's, byte for byte in behaviour: it plays the
same replays back in sync.

## Using it

Windows: take a copy of the official `OpenRA-release-20250330-x64-winportable.zip`, unzip it
somewhere of its own, and drop this build's `OpenRA.Game.dll` over the one in it (the two mod
assemblies are unchanged; this zip carries them anyway, built from the same source). Then put
`bench-replay.cmd` and `bench-suite.cmd` next to `RedAlert.exe`, the replays in `bench-replays\`,
and run

    bench-suite.cmd stock 1

which writes `results\replay\stock-1-*.csv/.json` and a line per run in `results\replay\summary.txt`.
The same two scripts in a Turbo install (`bench-suite.cmd turbo 1`) give the other column.
`bench.cmd` records live games instead (`Bench.Live=true`), the same as in Turbo.

Linux: the AppImage of the release, extracted (`--appimage-extract`), with the DLL copied into
`squashfs-root/usr/lib/openra/`, run through `AppRun` with the same arguments.

The bench's arguments are documented at the top of `LiveBench.cs`; the methodology, the suite and
its windows are in the workbench's `BENCHMARKS.md`.

## Keeping it current

The bench file is copied from Turbo's `main` whenever it changes there (`git checkout main --
OpenRA.Game/Support/LiveBench.cs OpenRA.Game/Support/AggregateProfiler.cs` on this branch, then
a build); the hooks only change when the bench grows a new need. The branch is never merged
anywhere.
