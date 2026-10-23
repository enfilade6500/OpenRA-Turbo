@echo off
rem Starts the game from this checkout with the live bench recording (see TURBO.md, "Measuring real games").
rem Play as usual; every game of the session is written to results\live as live-<time>-<map>.csv/.json plus a line
rem in results\live\summary.txt. Extra arguments are passed on, e.g.  bench.cmd Bench.NoFrameSkip=true
cd /d "%~dp0"
if not exist results\live mkdir results\live
call launch-game.cmd Game.Mod=ra Bench.Live=true "Bench.Out=%~dp0results\live" %*
