@echo off
rem bench-suite.cmd <label> [run-number]
rem Runs the benchmark suite (see BENCHMARKS.md in the workbench): three replays, each once at throughput pace (the
rem whole replay, simulation as fast as it goes, five frames a second at most) and once paced as a live game over its
rem peak two minutes; the Winterland window also as an 85%% computer (Bench.LogicMs=47) and at a forced 20 fps, and in a
rem Turbo install the 85%% computer with the floor at 15 and with no frame skipping. Results go to
rem results\replay\<label>-<run>-<A|B|C>-<thru|paced|slow|fps20|slow15|slownoskip>.*.
rem The replays go in bench-replays\ next to this file, named eight-chambers.orarep, winterland.orarep, eden.orarep
rem (the workbench has them). Run it alternately in the stock-bench and the Turbo install, three times each, with the
rem machine otherwise idle and a laptop on mains:  bench-suite.cmd stock 1, bench-suite.cmd turbo 1, stock 2, ...
setlocal
if "%~1"=="" ( echo usage: bench-suite.cmd ^<label^> [run-number] & exit /b 2 )
set "LABEL=%~1"
set "RUN=%~2"
if "%RUN%"=="" set "RUN=1"
set "R=%~dp0bench-replays"
for %%f in (eight-chambers winterland eden) do if not exist "%R%\%%f.orarep" ( echo missing %R%\%%f.orarep & exit /b 1 )
cd /d "%~dp0"
echo == throughput: the whole replay, simulation as fast as it goes
call bench-replay.cmd "%R%\eight-chambers.orarep" %LABEL%-%RUN%-A-thru Bench.Timestep=5 Debug.ForceFramerate=5
call bench-replay.cmd "%R%\winterland.orarep"     %LABEL%-%RUN%-B-thru Bench.Timestep=5 Debug.ForceFramerate=5
call bench-replay.cmd "%R%\eden.orarep"           %LABEL%-%RUN%-C-thru Bench.Timestep=5 Debug.ForceFramerate=5
echo == paced: the peak two minutes of each, played as a live game (stock: a frame after every tick; Turbo: its frame skipping at the install's floor)
call bench-replay.cmd "%R%\eight-chambers.orarep" %LABEL%-%RUN%-A-paced Bench.Duration=3000t
call bench-replay.cmd "%R%\winterland.orarep"     %LABEL%-%RUN%-B-paced Bench.WarpTo=11190t Bench.Duration=3000t
call bench-replay.cmd "%R%\eden.orarep"           %LABEL%-%RUN%-C-paced Bench.WarpTo=9055t Bench.Duration=3000t
echo == the Winterland window as an 85%% computer would play it (every logic tick padded to 47 ms against 40 ms ticks), and at a forced 20 frames a second
call bench-replay.cmd "%R%\winterland.orarep"     %LABEL%-%RUN%-B-slow  Bench.WarpTo=11190t Bench.Duration=3000t Bench.LogicMs=47
call bench-replay.cmd "%R%\winterland.orarep"     %LABEL%-%RUN%-B-fps20 Bench.WarpTo=11190t Bench.Duration=3000t Debug.ForceFramerate=20
if not exist TURBO.md goto done
echo == Turbo only: the 85%% computer with the frame-rate floor at 15, and with no frame skipping at all (stock's drawing)
call bench-replay.cmd "%R%\winterland.orarep"     %LABEL%-%RUN%-B-slow15     Bench.WarpTo=11190t Bench.Duration=3000t Bench.LogicMs=47 Graphics.MinFramerate=15
call bench-replay.cmd "%R%\winterland.orarep"     %LABEL%-%RUN%-B-slownoskip Bench.WarpTo=11190t Bench.Duration=3000t Bench.LogicMs=47 Bench.NoFrameSkip=true
:done
echo == done: results\replay\%LABEL%-%RUN%-*
