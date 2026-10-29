@echo off
rem setup-bench.cmd: the benchmark suite's two checkouts, built and ready, from a scratch folder. Nothing private
rem is needed except the three replays. From a console:
rem   mkdir C:\bench
rem   cd /d C:\bench
rem   git clone --depth 1 https://github.com/enfilade6500/OpenRA-Turbo turbo
rem   (put eight-chambers.orarep, winterland.orarep and eden.orarep into C:\bench\bench-replays\)
rem   turbo\setup-bench.cmd
rem   run-all.cmd
rem Needs git and the .NET 6 SDK on the PATH (make.cmd builds with it). Clones the stock-bench branch next to this
rem checkout as stock\, builds both, fetches the replays' maps from the resource centre into the game's own map folder,
rem copies the replays into each checkout and run-all.cmd next to them, and plays 500 ticks in stock\ as a smoke test.
rem Safe to run again later: it pulls and rebuilds.
setlocal
for %%i in ("%~dp0..") do set "BENCH=%%~fi"
for %%i in ("%~dp0.") do set "ME=%%~nxi"
for %%p in (git dotnet curl) do where %%p >nul 2>&1 || (echo %%p is needed on the PATH & exit /b 1)
if /i not "%ME%"=="turbo" echo note: this checkout is "%ME%"; run-all.cmd expects it to be called turbo

if not exist "%BENCH%\bench-replays\eight-chambers.orarep" (
  echo Put the suite's replays - eight-chambers.orarep, winterland.orarep, eden.orarep - into %BENCH%\bench-replays\ first.
  exit /b 1
)

cd /d "%BENCH%"
if not exist stock\.git git clone --depth 1 -b stock-bench https://github.com/enfilade6500/OpenRA-Turbo stock || exit /b 1

for %%d in (%ME% stock) do (
  echo == %%d: update and build
  pushd "%BENCH%\%%d"
  git pull -q --ff-only
  call make.cmd all
  if not exist bin\OpenRA.Game.dll ( echo build failed in %%d & popd & exit /b 1 )
  if not exist bench-replays mkdir bench-replays
  copy /y "%BENCH%\bench-replays\*.orarep" bench-replays\ >nul
  popd
)

rem The maps the replays need (Eight Chambers, Winterland 6v6, Eden 7v7), by uid from the resource centre, into the
rem game's map folder - shared with the normal install, so the game content is already there
set "SUP=%APPDATA%\OpenRA"
if not exist "%SUP%" if exist "%USERPROFILE%\Documents\OpenRA" set "SUP=%USERPROFILE%\Documents\OpenRA"
set "MAPS=%SUP%\maps\ra\release-20250330"
if not exist "%MAPS%" mkdir "%MAPS%"
for %%u in (7ea1a9ba559c9a02af9ead008386f0b08b0897ea 8c6685cd6792a808ecbab14aa0848d478a13583d b77d1fbd33d5690d6db32b4d987144efd33d0a0e) do (
  if not exist "%MAPS%\%%u.oramap" curl -sSL -o "%MAPS%\%%u.oramap" "https://resource.openra.net/map/%%u" || echo could not fetch map %%u
)
echo == maps in %MAPS%

copy /y "%BENCH%\%ME%\run-all.cmd" "%BENCH%\" >nul

echo == smoke test: 500 ticks of Eight Chambers in the stock checkout
pushd "%BENCH%\stock"
call bench-replay.cmd bench-replays\eight-chambers.orarep smoke Bench.Duration=500t Bench.Timestep=5 Debug.ForceFramerate=5
popd
echo == ready: run-all.cmd runs the suite in both checkouts, three times each, taking turns
