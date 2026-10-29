@echo off
rem bench-suite.cmd <label> [run-number]
rem Runs the benchmark suite (see BENCHMARKS.md in the workbench): three replays, each once at throughput pace (the
rem whole replay, simulation as fast as it goes, five frames a second at most) and once paced as a live game over its
rem peak two minutes; in a Turbo install also the two fights with the frame-rate floor at 15 and with no frame
rem skipping. Results go to results\replay\<label>-<run>-<A|B|C>-<thru|paced|paced15|noskip>.*.
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
if not exist TURBO.md goto done
echo == Turbo only: the same windows with the frame-rate floor at 15, and with no frame skipping at all (stock's drawing)
call bench-replay.cmd "%R%\winterland.orarep"     %LABEL%-%RUN%-B-paced15 Bench.WarpTo=11190t Bench.Duration=3000t Graphics.MinFramerate=15
call bench-replay.cmd "%R%\eden.orarep"           %LABEL%-%RUN%-C-paced15 Bench.WarpTo=9055t Bench.Duration=3000t Graphics.MinFramerate=15
call bench-replay.cmd "%R%\winterland.orarep"     %LABEL%-%RUN%-B-noskip Bench.WarpTo=11190t Bench.Duration=3000t Bench.NoFrameSkip=true
call bench-replay.cmd "%R%\eden.orarep"           %LABEL%-%RUN%-C-noskip Bench.WarpTo=9055t Bench.Duration=3000t Bench.NoFrameSkip=true
:done
echo == done: results\replay\%LABEL%-%RUN%-*
