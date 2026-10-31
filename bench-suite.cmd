@echo off
rem bench-suite.cmd <label> [run-number] [research]
rem Runs the benchmark suite (see BENCHMARKS.md in the workbench): three replays, each once at throughput pace (the
rem whole replay, simulation as fast as it goes, five frames a second at most) and once paced as a live game over its
rem peak two minutes, and the two fights drawn at a fixed 20 and 15 frames a second. That is the report set. With a
rem third argument "research" it adds the Winterland window as an 85%% computer (Bench.LogicMs=47), and in a Turbo
rem install that computer with the floor at 15 and with no frame skipping: lab runs for understanding the frame
rem skipping on a machine that is not naturally behind.
rem Results go to results\replay\<label>-<run>-<A|B|C>-<thru|paced|fps20|fps15|slow|slow15|slownoskip>.*.
rem The replays go in bench-replays\ next to this file, named eight-chambers.orarep, winterland.orarep, eden.orarep
rem (the workbench has them). Run it alternately in the stock-bench and the Turbo install, three times each, with the
rem machine otherwise idle and a laptop on mains:  bench-suite.cmd stock 1, bench-suite.cmd turbo 1, stock 2, ...
setlocal
if "%~1"=="" ( echo usage: bench-suite.cmd ^<label^> [run-number] [research] & exit /b 2 )
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
rem Drawn runs: no vsync (the wait for the screen blocks the loop and would count as render time) and at most 60 frames a second
set "DISPLAY=Graphics.VSync=False Graphics.CapFramerate=True Graphics.MaxFramerate=60"
echo == paced: the peak two minutes of each, played as a live game (stock: a frame after every tick; Turbo: its frame skipping at the install's floor)
call bench-replay.cmd "%R%\eight-chambers.orarep" %LABEL%-%RUN%-A-paced Bench.Duration=3000t %DISPLAY%
call bench-replay.cmd "%R%\winterland.orarep"     %LABEL%-%RUN%-B-paced Bench.WarpTo=11190t Bench.Duration=3000t %DISPLAY%
call bench-replay.cmd "%R%\eden.orarep"           %LABEL%-%RUN%-C-paced Bench.WarpTo=9055t Bench.Duration=3000t %DISPLAY%
echo == fixed cadence: the Winterland and Eden windows drawn at exactly 20 and 15 frames a second
call bench-replay.cmd "%R%\winterland.orarep"     %LABEL%-%RUN%-B-fps20 Bench.WarpTo=11190t Bench.Duration=3000t Debug.ForceFramerate=20 %DISPLAY%
call bench-replay.cmd "%R%\winterland.orarep"     %LABEL%-%RUN%-B-fps15 Bench.WarpTo=11190t Bench.Duration=3000t Debug.ForceFramerate=15 %DISPLAY%
call bench-replay.cmd "%R%\eden.orarep"           %LABEL%-%RUN%-C-fps20 Bench.WarpTo=9055t Bench.Duration=3000t Debug.ForceFramerate=20 %DISPLAY%
call bench-replay.cmd "%R%\eden.orarep"           %LABEL%-%RUN%-C-fps15 Bench.WarpTo=9055t Bench.Duration=3000t Debug.ForceFramerate=15 %DISPLAY%
if /i not "%~3"=="research" goto done
echo == research runs: the Winterland window as an 85%% computer would play it (every logic tick padded to 47 ms against 40 ms ticks)
call bench-replay.cmd "%R%\winterland.orarep"     %LABEL%-%RUN%-B-slow  Bench.WarpTo=11190t Bench.Duration=3000t Bench.LogicMs=47 %DISPLAY%
if not exist TURBO.md goto done
echo == Turbo only: the 85%% computer with the frame-rate floor at 15, and with no frame skipping at all (stock's drawing)
call bench-replay.cmd "%R%\winterland.orarep"     %LABEL%-%RUN%-B-slow15     Bench.WarpTo=11190t Bench.Duration=3000t Bench.LogicMs=47 Graphics.MinFramerate=15 %DISPLAY%
call bench-replay.cmd "%R%\winterland.orarep"     %LABEL%-%RUN%-B-slownoskip Bench.WarpTo=11190t Bench.Duration=3000t Bench.LogicMs=47 Bench.NoFrameSkip=true %DISPLAY%
:done
echo == done: results\replay\%LABEL%-%RUN%-*
