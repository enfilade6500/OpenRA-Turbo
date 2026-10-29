@echo off
rem bench-replay.cmd <replay.orarep> <name> [arguments]
rem Plays a replay with the bench recording every tick, into results\replay\<name>.csv / .json and a line in
rem results\replay\summary.txt. Works in an install (next to RedAlert.exe) and in a checkout (next to launch-game.cmd).
rem Arguments after the name go to the game, e.g.  Bench.WarpTo=11190t Bench.Duration=3000t  (the peak window of a
rem replay, in ticks), Bench.Timestep=5 (throughput: run the simulation as fast as it goes), Debug.ForceFramerate=5
rem (draw at most five frames a second), Bench.Zoom=map (the whole map in view). See TURBO.md / STOCK-BENCH.md.
setlocal
if "%~2"=="" (
  echo usage: bench-replay.cmd ^<replay.orarep^> ^<name^> [Bench.WarpTo=11190t Bench.Duration=3000t Bench.Timestep=5 ...]
  exit /b 2
)
set "REPLAY=%~f1"
set "NAME=%~2"
set "REST="
:collect
if "%~3"=="" goto run
set "REST=%REST% %3"
shift /3
goto collect
:run
cd /d "%~dp0"
if not exist results\replay mkdir results\replay
if exist RedAlert.exe (
  RedAlert.exe "Launch.Replay=%REPLAY%" "Bench.Out=%~dp0results\replay" "Bench.Name=%NAME%" %REST%
) else (
  call launch-game.cmd Game.Mod=ra "Launch.Replay=%REPLAY%" "Bench.Out=%~dp0results\replay" "Bench.Name=%NAME%" %REST%
)
findstr /b /c:"%NAME% " results\replay\summary.txt
