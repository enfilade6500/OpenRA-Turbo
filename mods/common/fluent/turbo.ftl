## OpenRA Turbo (settings-turbo.yaml, TurboSettingsLogic)
label-turbo-title = OpenRA Turbo { $version }

label-turbo-desc-a = A faster client for this release. The game it simulates is the same one, so it plays on the usual
label-turbo-desc-b = servers with the usual clients. Everything on this page affects this computer only. Big games are
label-turbo-desc-c = faster without touching anything here; the settings below are for when frames are what is short.

label-turbo-frames-section = Frames
label-turbo-min-framerate = Catching up: skip frames down to { $fps } FPS
label-turbo-min-framerate-never = Catching up: never skip frames
label-turbo-min-framerate-desc-a = When your computer cannot keep up with the game, frames
label-turbo-min-framerate-desc-b = are skipped so the game runs at full speed, never below this
label-turbo-min-framerate-desc-c = rate (scrolling keeps its speed whatever the frame rate)
label-turbo-force-framerate = Force a low frame rate: { $fps } FPS
label-turbo-force-framerate-off = Force a low frame rate: off
label-turbo-force-framerate-desc-a = Draws at most this many frames per second whatever the
label-turbo-force-framerate-desc-b = load, to try out what a low frame rate feels like (testing)

label-turbo-bars-section = Status bars
label-turbo-status-bars = Status bars
label-turbo-status-bars-desc-a = The same setting as on the Display page. Each bar drawn
label-turbo-status-bars-desc-b = is a little work per unit per frame; with thousands of
label-turbo-status-bars-desc-c = units in view the options below keep most of it away
checkbox-turbo-selected-bar-damaged = Selected: health bar only when damaged
checkbox-turbo-drag-box-bars = Bars for units inside a selection box
checkbox-turbo-no-infantry-bars = No automatic status bars on infantry
