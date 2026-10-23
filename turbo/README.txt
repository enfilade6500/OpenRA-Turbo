OpenRA Turbo 1.0  -  a faster client for OpenRA release-20250330
=================================================================

What it is
----------
Turbo is the standard OpenRA release-20250330 client with changes that make big games run
faster on your own computer: the simulation does less work per tick (idle target scans,
aircraft, animations), drawing does less work per frame (selections, status bars, the mouse
pointer), and when your computer still cannot keep up, frames are skipped so the game runs at
full speed instead of falling behind. Scrolling keeps its speed at any frame rate.

The game it simulates is the same one. Turbo plays on the usual servers with the usual
clients, in the same games; replays made with either play on both. Nothing is sent to anyone
about it. The main menu shows "release-20250330 (Turbo 1.0)" so you can tell it is installed.

Install (Windows)
-----------------
1. Close the game. Find your OpenRA install folder (the one with RedAlert.exe or OpenRA.exe;
   usually C:\Program Files (x86)\OpenRA\ or %LOCALAPPDATA%\Programs\OpenRA\). It must be
   release-20250330.
2. Keep a copy of that folder, or at least of these files, to go back:
     OpenRA.Game.dll, OpenRA.Mods.Common.dll, OpenRA.Mods.Cnc.dll,
     mods\common\chrome\settings.yaml, settings-display.yaml, settings-advanced.yaml,
     mods\ra\mod.yaml, mods\cnc\mod.yaml, mods\cnc\chrome\settings.yaml,
     mods\d2k\mod.yaml, mods\ts\mod.yaml
3. Copy the contents of this zip's "OpenRA" folder over the install folder, replacing files.
4. Start the game. The main menu should say "release-20250330 (Turbo 1.0)".

To go back, put the saved files back (or reinstall release-20250330).

Settings
--------
Settings > Turbo holds the options Turbo adds. You do not need to touch any of them: big games
are faster without changing anything. The options are for when frames are what is short:
  - Catching up: skip frames down to N FPS (default 20). When the game is behind, frames are
    skipped to let it catch up, but never below this rate. 25 means never skip.
  - Force a low frame rate: testing only; draws at most this many frames per second.
  - Status bars: the usual setting, plus three ways to draw fewer bars when thousands of units
    are on the screen (selected units' health bar only when damaged; no bars for units inside
    a selection box being dragged; no automatic bars on infantry).

If something goes wrong
-----------------------
If a game drops you with "out of sync" and nobody else, that is the one thing this client could
do that the standard one would not; please report it with the replay (Replays folder) and the
map. Anything else (a crash, a layout problem in the settings) please report too. Going back to
the standard files is always possible, see above.

Linux and macOS
---------------
The DLLs are the same on every platform, but the packaged installs there cannot be patched in
place; build from source instead (branch "turbo", `make`), see TURBO.md in the source.

Source and notes: https://github.com/enfilade6500/OpenRA-ZeroLag (branch turbo), TURBO.md.
