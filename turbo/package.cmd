@echo off
rem Assembles the Turbo release zip from this checkout after "make.cmd all": the three DLLs and the changed
rem mod files, laid out as they go into an install, plus the README and TURBO.md.  Run from the checkout root.
setlocal EnableExtensions
set "VER=1.0"
set "NAME=OpenRA-Turbo-%VER%-release-20250330"
set "DIST=turbo-dist\%NAME%\OpenRA"
if not exist bin\OpenRA.Game.dll ( echo Build first: make.cmd all & exit /b 1 )
if exist turbo-dist rmdir /s /q turbo-dist
mkdir "%DIST%\mods\common\chrome" "%DIST%\mods\common\fluent" "%DIST%\mods\ra" "%DIST%\mods\cnc\chrome" "%DIST%\mods\d2k" "%DIST%\mods\ts"
copy /y bin\OpenRA.Game.dll "%DIST%\" >nul
copy /y bin\OpenRA.Mods.Common.dll "%DIST%\" >nul
copy /y bin\OpenRA.Mods.Cnc.dll "%DIST%\" >nul
for %%f in (settings.yaml settings-display.yaml settings-advanced.yaml settings-turbo.yaml) do copy /y "mods\common\chrome\%%f" "%DIST%\mods\common\chrome\" >nul
copy /y mods\common\fluent\turbo.ftl "%DIST%\mods\common\fluent\" >nul
copy /y mods\ra\mod.yaml "%DIST%\mods\ra\" >nul
copy /y mods\cnc\mod.yaml "%DIST%\mods\cnc\" >nul
copy /y mods\cnc\chrome\settings.yaml "%DIST%\mods\cnc\chrome\" >nul
copy /y mods\d2k\mod.yaml "%DIST%\mods\d2k\" >nul
copy /y mods\ts\mod.yaml "%DIST%\mods\ts\" >nul
copy /y README.md "turbo-dist\%NAME%\" >nul
copy /y TURBO.md "turbo-dist\%NAME%\" >nul
powershell -NoProfile -Command "Compress-Archive -Force -Path 'turbo-dist\%NAME%\*' -DestinationPath 'turbo-dist\%NAME%.zip'"
powershell -NoProfile -Command "Get-FileHash -Algorithm SHA256 'turbo-dist\%NAME%\OpenRA\*.dll' | ForEach-Object { $_.Hash.ToLower() + '  ' + (Split-Path $_.Path -Leaf) }" > "turbo-dist\%NAME%.sha256.txt"
type "turbo-dist\%NAME%.sha256.txt"
echo Wrote turbo-dist\%NAME%.zip
