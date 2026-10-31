@echo off
rem run-all.cmd: the suite in stock\ and turbo\ taking turns, three times each (see BENCHMARKS.md). Run from C:\bench.
cd /d "%~dp0"
for %%i in (1 2 3) do (
  call stock\bench-suite.cmd stock %%i
  call turbo\bench-suite.cmd turbo %%i
)
rem The reference point, once per build: the whole 57-minute Pit game at throughput pace, the replay behind the
rem 1.0 figures (long, map Lua; 85,644 ticks, so twenty minutes to an hour depending on the machine)
call stock\bench-replay.cmd bench-replays\pit.orarep stock-pit-thru Bench.Timestep=5 Debug.ForceFramerate=5
call turbo\bench-replay.cmd bench-replays\pit.orarep turbo-pit-thru Bench.Timestep=5 Debug.ForceFramerate=5
echo == all done: stock\results\replay and turbo\results\replay
