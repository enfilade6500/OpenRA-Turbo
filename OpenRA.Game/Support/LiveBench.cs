#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using OpenRA.Graphics;
using OpenRA.Network;

namespace OpenRA.Support
{
	/// <summary>
	/// Plays a replay the way a live game runs - one logic tick per timestep, a frame after every tick, the player's own
	/// display settings - and records what every tick cost on the main thread, to measure what this computer keeps up with
	/// and what the display options and loop changes do about it. The settings file is never written while this is active.
	///
	/// Launch arguments:
	///   Bench.Out=dir          results directory (turns the bench on)
	///   Bench.Name=name        file name for this run (default: bench)
	///   Bench.WarpTo=m:ss      game time to skip at full speed, without frames, before measuring (default 0)
	///   Bench.Duration=s       game seconds to measure, then exit (default: to the end of the replay)
	///   Bench.LogicMs=ms       pad every logic tick to at least this long, to behave like a slower computer (default 0)
	///   Bench.Ballast=ms       add this to every logic tick (default 0)
	///   Bench.NoFrameSkip=true draw a frame after every tick whatever the time, as the game did before frames were
	///                          skipped while the simulation is behind (Graphics.MinFramerate), for comparison
	///   Bench.RenderRepeat=n   draw the world n times per frame, to behave like a weaker graphics card (default 1)
	///   Bench.Zoom=z           viewport zoom while measuring: a number (screen px per world px), or "map" to fit the whole map
	///                          on the screen (default: the zoom the Battlefield Camera setting gives; Graphics.ViewportDistance=Far
	///                          selects another setting for the run)
	///   Bench.Timestep=ms      play at this many ms per tick instead of the replay's game speed (30 = "faster", 40 = normal)
	///   Bench.Select=all       (with Bench.Live, on a test map) every second select all of the viewed player's units,
	///                          keep the camera centred on them and apply Bench.Zoom, to measure selection drawing;
	///                          Bench.Select=camera does the camera and zoom without selecting, for the comparison;
	///                          Bench.Select=drag holds a drag box over them instead (the rollover, every tick)
	///   Bench.Live=true        record real games instead: play normally, and every multiplayer or skirmish game played in this
	///                          session is written to Bench.Out as live-&lt;time&gt;-&lt;map&gt;.csv/.json with a line in summary.txt.
	///                          No warp, no padding, no camera; the other Bench.* options are ignored. Per tick this adds the
	///                          client's own network state: ticks it wanted but had no orders for (stalls), the net frame, the
	///                          frames it holds but has not run (queue, as reported to the server in pings) and the tick scale
	///                          the server asked for.
	/// Display settings are given the usual way: Graphics.VSync=False Graphics.Mode=Windowed Graphics.CapFramerate=True ...
	/// </summary>
	public static class LiveBench
	{
		public static bool Active { get; private set; }
		public static bool Live { get; private set; }
		public static bool Measuring { get; private set; }
		public static bool NoFrameSkip { get; private set; }
		public static int RenderRepeat { get; private set; } = 1;

		static string outDir, name;
		static int warpToTick, durationTicks, timestepOverride, measureTimestep = 40;
		static float logicFloorMs, ballastMs, zoom, zoomUsed;
		static bool zoomToMap;
		static bool selectAll, dragOverUnits, cameraOnUnits;
		static bool selectZoomApplied;
		static WPos mapCentre;

		static readonly Stopwatch Clock = Stopwatch.StartNew();
		static double Now => Clock.Elapsed.TotalMilliseconds;

		sealed class TickRecord
		{
			public int Tick;
			public double Start, Late, Logic, Pad;
			public int Frames, Skipped;
			public double Render, Begin, Flip;
			public double AnnotGen, AnnotDraw, Flush;
			public int Actors, InView, Selected;

			/// <summary>A frame was drawn in this tick: add its times, and the selection drawing's share of them.</summary>
			public void AddFrame(double totalMs, double beginMs, double flipMs)
			{
				Frames++;
				Render += totalMs;
				Begin += beginMs;
				Flip += flipMs;
				AnnotGen += PerfHistory.Items["render_annot_gen"].LastValue;
				AnnotDraw += PerfHistory.Items["render_annot_draw"].LastValue;
				Flush += PerfHistory.Items["render_flush"].LastValue;
			}

			// Live games only
			public float Zoom, Scale;
			public int Stalls, Paused, NetFrame, Queue;
		}

		/// <summary>Ticks after the switch to measuring that are written to the CSV but left out of the summary (the first frames
		/// at the new viewport upload sprites and buffers, and the loop is still catching up from the warp).</summary>
		const int SettleTicks = 50;
		const int StartTimeoutMs = 180_000;

		static readonly List<TickRecord> AllRecords = new();
		static double tickStart, pendingLate, measureStart, measureEnd;
		static int firstMeasuredTick = -1, resets, lastWorldTick;
		static string finishReason, replayPath, mapTitle;
		static int desyncFrame = -1;
		static bool finished;
		static StreamWriter csv;

		public static void Configure(Arguments args)
		{
			outDir = args.GetValue("Bench.Out", null);
			if (string.IsNullOrEmpty(outDir))
				return;

			Active = true;
			Live = args.GetValue("Bench.Live", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
			name = args.GetValue("Bench.Name", "bench");
			warpToTick = ParseGameTime(args.GetValue("Bench.WarpTo", "0"));
			durationTicks = ParseGameTime(args.GetValue("Bench.Duration", "0"));
			logicFloorMs = Parse(args.GetValue("Bench.LogicMs", "0"));
			ballastMs = Parse(args.GetValue("Bench.Ballast", "0"));
			NoFrameSkip = args.GetValue("Bench.NoFrameSkip", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
			RenderRepeat = Math.Max(1, (int)Parse(args.GetValue("Bench.RenderRepeat", "1")));
			var zoomArg = args.GetValue("Bench.Zoom", "0");
			zoomToMap = zoomArg.Equals("map", StringComparison.OrdinalIgnoreCase);
			zoom = zoomToMap ? 0 : Parse(zoomArg);
			timestepOverride = (int)Parse(args.GetValue("Bench.Timestep", "0"));
			replayPath = args.GetValue("Launch.Replay", "");
			var selectArg = args.GetValue("Bench.Select", "");
			selectAll = selectArg.Equals("all", StringComparison.OrdinalIgnoreCase);
			dragOverUnits = selectArg.Equals("drag", StringComparison.OrdinalIgnoreCase);
			cameraOnUnits = selectAll || dragOverUnits || selectArg.Equals("camera", StringComparison.OrdinalIgnoreCase);

			if (Live)
			{
				// Nothing may change how the game plays: this mode only watches (Bench.NoFrameSkip stays, to record real
				// games with a frame after every tick for comparison; it changes what is drawn, not what is simulated)
				warpToTick = durationTicks = timestepOverride = 0;
				logicFloorMs = ballastMs = 0;
				if (!cameraOnUnits)
					zoom = 0;
				RenderRepeat = 1;
				zoomToMap = false;
			}

			Directory.CreateDirectory(outDir);
			Console.WriteLine($"LiveBench: {(Live ? "recording live games" : name)} -> {outDir}");
		}

		static float Parse(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0;

		/// <summary>"90", "1:30" or "1:30.5" in game seconds, as a tick count at 40 ms per tick.</summary>
		static int ParseGameTime(string s)
		{
			var parts = s.Split(':');
			var seconds = 0f;
			foreach (var p in parts)
				seconds = seconds * 60 + Parse(p);
			return (int)Math.Round(seconds * 25);
		}

		/// <summary>The replay has loaded: skip ahead at full speed if asked, otherwise start measuring at once.</summary>
		public static void OnWorldLoaded(World world)
		{
			if (!Active || Live || world == null || !world.IsReplay)
				return;

			mapTitle = world.Map.Title;
			lastWorldTick = world.WorldTick;
			if (warpToTick > world.WorldTick)
			{
				world.ReplayTimestep = 1;
				Console.WriteLine($"LiveBench: warping to tick {warpToTick}");
			}
		}

		/// <summary>The loop is about to run a logic tick; <paramref name="lateMs"/> is how far past its due time it starts.</summary>
		public static void BeforeLogic(double lateMs)
		{
			if (!Active)
				return;

			tickStart = Now;
			pendingLate = lateMs;
		}

		/// <summary>The game is shutting down: a live game still being recorded is written out.</summary>
		public static void OnExit()
		{
			if (Live)
				LiveGame.Finish("client closed");
		}

		/// <summary>The logic tick is done: pad it if asked, record it, and decide whether measuring starts or ends here.</summary>
		public static void AfterLogic(World world, WorldRenderer wr)
		{
			if (!Active || finished)
				return;

			if (Live)
			{
				LiveGame.AfterLogic(world, wr);
				return;
			}

			// A missing map or replay leaves a dialog on the screen instead of a game; do not wait forever for it
			if (world == null || !world.IsReplay)
			{
				if (Clock.ElapsedMilliseconds > StartTimeoutMs)
					Finish("the replay did not start within three minutes (map or replay missing, or an incompatible version?)");
				return;
			}

			var ticked = world.WorldTick != lastWorldTick;
			lastWorldTick = world.WorldTick;
			if (!ticked)
				return;

			if (!Measuring)
			{
				if (world.WorldTick < warpToTick)
					return;

				StartMeasuring(world, wr);
			}

			var logic = Now - tickStart;
			var target = Math.Max(logic, logicFloorMs) + ballastMs;
			if (target > logic)
			{
				var until = tickStart + target;
				while (Now < until)
					Thread.SpinWait(50);
			}

			var current = new TickRecord
			{
				Tick = world.WorldTick,
				Start = tickStart,
				Late = pendingLate,
				Logic = logic,
				Pad = Math.Max(0, target - logic),
				Actors = world.Actors.Count(),
				Selected = world.Selection.Actors.Count
			};

			// Keep the camera where it was put, whatever the mouse does, and count what the frame has to draw: the actors the
			// renderer picks for the viewport
			if (wr != null)
			{
				wr.Viewport.Center(mapCentre);
				current.InView = world.ScreenMap.RenderableActorsInBox(wr.Viewport.TopLeft, wr.Viewport.BottomRight).Count();
			}
			AllRecords.Add(current);
			if (AllRecords.Count > 1)
				WriteCsv(AllRecords[^2], current.Start);

			if (durationTicks > 0 && world.WorldTick - firstMeasuredTick >= durationTicks)
				Finish("done");
			else if (Game.OrderManager?.Connection is ReplayConnection rc && Game.OrderManager.NetFrameNumber >= rc.TickCount)
				Finish("replay ended");
		}

		static void StartMeasuring(World world, WorldRenderer wr)
		{
			Measuring = true;
			firstMeasuredTick = world.WorldTick;
			measureStart = tickStart;
			measureTimestep = timestepOverride > 0 ? timestepOverride : world.Timestep;
			world.ReplayTimestep = measureTimestep;

			if (wr != null)
			{
				var tl = world.Map.ProjectedTopLeft;
				var br = world.Map.ProjectedBottomRight;
				mapCentre = new WPos((tl.X + br.X) / 2, (tl.Y + br.Y) / 2, 0);
				wr.Viewport.Center(mapCentre);

				var target = zoom;
				if (zoomToMap)
				{
					// The zoom at which the whole map fits on the screen
					var size = wr.ScreenPxPosition(br) - wr.ScreenPxPosition(tl);
					var res = Game.Renderer.NativeResolution;
					target = Math.Min((float)res.Width / size.X, (float)res.Height / size.Y);
				}

				if (target > 0)
				{
					// Zooming out past the camera setting is normally reserved for spectators (by a factor of two);
					// allow whatever the run asks for
					if (target < wr.Viewport.MinZoom)
						wr.Viewport.UnlockMinimumZoom(0.999f * target / wr.Viewport.MinZoom);
					wr.Viewport.AdjustZoom((float)Math.Log(target / wr.Viewport.Zoom));
				}

				zoomUsed = wr.Viewport.Zoom;
			}

			csv = new StreamWriter(Path.Combine(outDir, name + ".csv"), false, Encoding.ASCII);
			csv.WriteLine("tick,t_ms,late_ms,logic_ms,pad_ms,interval_ms,frames,skipped,render_ms,begin_ms,flip_ms,actors,in_view," +
				"annot_gen_ms,annot_draw_ms,flush_ms,selected");
			var clock = TimeSpan.FromMilliseconds((double)firstMeasuredTick * world.Timestep);
			Console.WriteLine($"LiveBench: measuring from tick {firstMeasuredTick} (game clock {clock:m\\:ss}) at zoom {zoomUsed:F3}");
		}

		static void WriteCsv(TickRecord r, double nextStart)
		{
			var c = CultureInfo.InvariantCulture;
			csv?.WriteLine(string.Join(",",
				r.Tick.ToString(c), (r.Start - measureStart).ToString("F1", c), r.Late.ToString("F1", c), r.Logic.ToString("F2", c),
				r.Pad.ToString("F2", c), (nextStart - r.Start).ToString("F2", c), r.Frames.ToString(c), r.Skipped.ToString(c),
				r.Render.ToString("F2", c), r.Begin.ToString("F2", c), r.Flip.ToString("F2", c), r.Actors.ToString(c), r.InView.ToString(c),
				r.AnnotGen.ToString("F2", c), r.AnnotDraw.ToString("F2", c), r.Flush.ToString("F2", c), r.Selected.ToString(c)));
		}

		/// <summary>A frame was drawn; the times are what the main thread spent in it, in starting the world pass, and in the hand-off.</summary>
		public static void AfterRender(double totalMs, double beginMs, double flipMs)
		{
			if (Live)
			{
				LiveGame.AfterRender(totalMs, beginMs, flipMs);
				return;
			}

			if (!Measuring || finished)
				return;

			var r = AllRecords.Count > 0 ? AllRecords[^1] : null;
			r?.AddFrame(totalMs, beginMs, flipMs);
		}

		public static void NoteSkippedFrame()
		{
			if (Live)
				LiveGame.NoteSkippedFrame();
			else if (Measuring && AllRecords.Count > 0)
				AllRecords[^1].Skipped++;
		}

		public static void NoteLogicReset()
		{
			if (Live)
				LiveGame.NoteReset();
			else if (Measuring)
				resets++;
		}

		public static void NoteDesync(int frame)
		{
			if (!Active)
				return;

			if (Live)
			{
				LiveGame.NoteDesync(frame);
				return;
			}

			desyncFrame = frame;
			Finish($"desync at frame {frame}");
		}

		public static void Finish(string reason)
		{
			if (!Active || finished)
				return;

			finished = true;
			finishReason = reason;
			measureEnd = Now;
			Console.WriteLine($"LiveBench: finished ({reason})");

			try
			{
				WriteResults();
			}
			catch (Exception e)
			{
				Console.WriteLine($"LiveBench: could not write results: {e}");
			}

			Game.Exit();
		}

		static double Percentile(List<double> sorted, double p) =>
			sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(p * (sorted.Count - 1)))];

		static void WriteResults()
		{
			if (AllRecords.Count > 0)
				WriteCsv(AllRecords[^1], measureEnd);
			csv?.Dispose();
			csv = null;

			var c = CultureInfo.InvariantCulture;
			var g = Game.Settings.Graphics;
			var Records = AllRecords.Where(r => r.Tick >= firstMeasuredTick + SettleTicks).ToList();
			var n = Records.Count;
			var wall = n > 0 ? measureEnd - Records[0].Start : 0;
			var intervals = new List<double>();
			for (var i = 1; i < n; i++)
				intervals.Add(Records[i].Start - Records[i - 1].Start);
			intervals.Sort();

			var capacity = wall > 0 ? 100.0 * n * measureTimestep / wall : 0;
			var frames = Records.Sum(r => r.Frames);
			var skipped = Records.Sum(r => r.Skipped);
			var lateTicks = Records.Count(r => r.Late >= 5);
			double Mean(Func<TickRecord, double> f) => n > 0 ? Records.Average(f) : 0;

			var summary = new Dictionary<string, object>
			{
				["name"] = name,
				["reason"] = finishReason,
				["replay"] = Path.GetFileName(replayPath),
				["map"] = mapTitle,
				["machine"] = $"{Environment.OSVersion} {Environment.ProcessorCount} cores {Platform.RuntimeVersion}",
				["gl"] = SafeGl(),
				["mode"] = g.Mode.ToString(),
				["resolution"] = $"{Game.Renderer.Resolution.Width}x{Game.Renderer.Resolution.Height}",
				["vsync"] = g.VSync,
				["cap_framerate"] = g.CapFramerate,
				["max_framerate"] = g.MaxFramerate,
				["cap_to_tick"] = g.CapFramerateToGameFps,
				["warp_to_tick"] = warpToTick,
				["timestep_ms"] = measureTimestep,
				["logic_floor_ms"] = logicFloorMs,
				["ballast_ms"] = ballastMs,
				["no_frame_skip"] = NoFrameSkip,
				["render_repeat"] = RenderRepeat,
				["zoom"] = zoomUsed,
				["zoom_arg"] = zoomToMap ? "map" : zoom > 0 ? zoom.ToString("F3", c) : "",
				["viewport_distance"] = g.ViewportDistance.ToString(),
				["actors_mean"] = Mean(r => r.Actors),
				["in_view_mean"] = Mean(r => r.InView),
				["ticks"] = n,
				["first_tick"] = firstMeasuredTick + SettleTicks,
				["wall_s"] = wall / 1000,
				["capacity_pct"] = capacity,
				["interval_mean_ms"] = intervals.Count > 0 ? intervals.Average() : 0,
				["interval_p50_ms"] = Percentile(intervals, 0.5),
				["interval_p95_ms"] = Percentile(intervals, 0.95),
				["interval_max_ms"] = intervals.Count > 0 ? intervals[^1] : 0,
				["logic_mean_ms"] = Mean(r => r.Logic),
				["logic_p95_ms"] = Percentile(Records.Select(r => r.Logic).OrderBy(x => x).ToList(), 0.95),
				["pad_mean_ms"] = Mean(r => r.Pad),
				["render_mean_ms"] = Mean(r => r.Render),
				["begin_mean_ms"] = Mean(r => r.Begin),
				["flip_mean_ms"] = Mean(r => r.Flip),
				["frames"] = frames,
				["fps"] = wall > 0 ? 1000.0 * frames / wall : 0,
				["skipped_frames"] = skipped,
				["late_ticks_pct"] = n > 0 ? 100.0 * lateTicks / n : 0,
				["late_mean_ms"] = Mean(r => r.Late),
				["logic_resets"] = resets,
				["desync_frame"] = desyncFrame,
			};

			var json = new StringBuilder("{\n");
			foreach (var kv in summary)
			{
				var v = kv.Value switch
				{
					null => "null",
					bool b => b ? "true" : "false",
					string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
					double d => d.ToString("F3", c),
					float f => f.ToString("F3", c),
					_ => Convert.ToString(kv.Value, c)
				};
				json.Append($"  \"{kv.Key}\": {v},\n");
			}

			json.Length -= 2;
			json.Append("\n}\n");
			File.WriteAllText(Path.Combine(outDir, name + ".json"), json.ToString());

			var line = string.Format(c,
				"{0,-26} {21,2}ms {1,-16} {2,-5} {3,5} {4,5} {5,4} {6,3} | ticks {7,5} capacity {8,6:F1}% interval {9,5:F1} p95 {10,5:F1} | " +
				"logic {11,5:F1} pad {12,4:F1} render {13,5:F1} begin {14,4:F1} flip {15,5:F1} | fps {16,5:F1} skipped {17,4} late {18,4:F0}% resets {19} | " +
				"zoom {22,5:F2} view {23,4:F0}/{24,4:F0} | {20}",
				name, g.Mode, g.VSync ? "vsync" : "-", g.CapFramerateToGameFps ? "tick" : g.CapFramerate ? g.MaxFramerate.ToString(c) : "-",
				logicFloorMs > 0 ? "L" + logicFloorMs.ToString("F0", c) : ballastMs > 0 ? "B" + ballastMs.ToString("F0", c) : "-",
				NoFrameSkip ? "noskip" : "-", RenderRepeat > 1 ? "x" + RenderRepeat : "",
				n, capacity, summary["interval_mean_ms"], summary["interval_p95_ms"],
				summary["logic_mean_ms"], summary["pad_mean_ms"], summary["render_mean_ms"], summary["begin_mean_ms"], summary["flip_mean_ms"],
				summary["fps"], skipped, summary["late_ticks_pct"], resets, finishReason, measureTimestep,
				zoomUsed, summary["in_view_mean"], summary["actors_mean"]);
			File.AppendAllText(Path.Combine(outDir, "summary.txt"), line + "\n");
			Console.WriteLine(line);
		}

		/// <summary>
		/// Records real games as they are played: one CSV per game with the same per-tick columns as the replay bench plus
		/// the client's network state, a JSON summary, and a line in summary.txt. A game starts being recorded when a regular
		/// (non-shellmap, non-replay) world first ticks and ends when the world goes away or the client closes.
		/// </summary>
		static class LiveGame
		{
			static World world, finishedWorld;
			static StreamWriter csv;
			static string name, mapTitle, connection;
			static int timestep, players;
			static double start, end;
			static int lastTick = -1, firstTick, stalls, paused, resets, desyncFrame = -1, lastActors, lastInView;
			static readonly List<TickRecord> Records = new();

			public static void AfterLogic(World w, WorldRenderer wr)
			{
				if (w == null || w.Type != WorldType.Regular || w.IsReplay)
				{
					if (world != null)
						Finish("game ended");
					return;
				}

				// A world whose recording has ended (a desync) is not recorded again
				if (w == finishedWorld)
					return;

				if (w != world)
				{
					if (world != null)
						Finish("game ended");
					Start(w);
				}

				// The loop ran a logic step but the world did not advance: waiting for orders over the network, or paused
				if (w.WorldTick == lastTick)
				{
					if (w.Paused)
						paused++;
					else
						stalls++;
					return;
				}

				lastTick = w.WorldTick;
				var om = Game.OrderManager;

				if (dragOverUnits && wr != null)
				{
					var viewed = w.RenderPlayer ?? w.LocalPlayer;
					w.Selection.SetRollover(w.Actors.Where(a => a.IsInWorld && !a.IsDead && a.Owner == viewed && a.Info.HasTraitInfo<Traits.ISelectableInfo>()));
				}

				// Bench.Select=all: keep every unit of the viewed player selected and in view (=camera: in view only)
				if (cameraOnUnits && wr != null && w.WorldTick % 25 == 0)
				{
					var viewed = w.RenderPlayer ?? w.LocalPlayer;
					var units = w.Actors.Where(a => a.IsInWorld && !a.IsDead && a.Owner == viewed && a.Info.HasTraitInfo<Traits.ISelectableInfo>()).ToList();
					if (selectAll)
						w.Selection.Combine(w, units, false, false);

					if (units.Count > 0)
						wr.Viewport.Center(units);

					if (zoom > 0 && !selectZoomApplied)
					{
						selectZoomApplied = true;
						if (zoom < wr.Viewport.MinZoom)
							wr.Viewport.UnlockMinimumZoom(0.999f * (float)zoom / wr.Viewport.MinZoom);
						wr.Viewport.AdjustZoom((float)Math.Log(zoom / wr.Viewport.Zoom));
					}
				}

				// What the frame has to draw, sampled every fifth tick to keep the recorder's own cost negligible
				if (wr != null && w.WorldTick % 5 == 0)
				{
					lastActors = w.Actors.Count();
					lastInView = w.ScreenMap.RenderableActorsInBox(wr.Viewport.TopLeft, wr.Viewport.BottomRight).Count();
				}

				var r = new TickRecord
				{
					Tick = w.WorldTick,
					Start = tickStart,
					Late = pendingLate,
					Logic = Now - tickStart,
					Actors = lastActors,
					InView = lastInView,
					Selected = w.Selection.Actors.Count,
					Zoom = wr?.Viewport.Zoom ?? 0,
					Stalls = stalls,
					Paused = paused,
					NetFrame = om.NetFrameNumber,
					Queue = om.OrderQueueLength,
					Scale = om.TickScale
				};
				stalls = 0;
				paused = 0;
				Records.Add(r);
				if (Records.Count > 1)
					Write(Records[^2], r.Start);

				// The file is complete to the second even if the client never gets to finish it (a crash, a killed process)
				if (w.WorldTick % 25 == 0)
					csv?.Flush();
			}

			public static void AfterRender(double totalMs, double beginMs, double flipMs)
			{
				if (world == null || Records.Count == 0)
					return;

				Records[^1].AddFrame(totalMs, beginMs, flipMs);
			}

			public static void NoteSkippedFrame()
			{
				if (world != null && Records.Count > 0)
					Records[^1].Skipped++;
			}

			public static void NoteReset()
			{
				if (world != null)
					resets++;
			}

			public static void NoteDesync(int frame)
			{
				if (world == null)
					return;

				desyncFrame = frame;
				Finish($"desync at frame {frame}");
			}

			static void Start(World w)
			{
				world = w;
				mapTitle = w.Map.Title;
				timestep = w.Timestep;
				connection = Game.OrderManager.Connection.GetType().Name.Replace("Connection", "").ToLowerInvariant();
				players = w.Players.Count(p => p.Playable);
				lastTick = -1;
				firstTick = w.WorldTick;
				stalls = paused = resets = 0;
				desyncFrame = -1;
				lastActors = lastInView = 0;
				Records.Clear();
				start = Now;

				var safeMap = new string(mapTitle.Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_').ToArray());
				name = $"live-{DateTime.Now:yyyyMMdd-HHmmss}-{safeMap}";
				csv = new StreamWriter(Path.Combine(outDir, name + ".csv"), false, Encoding.ASCII);
				csv.WriteLine("tick,t_ms,late_ms,logic_ms,pad_ms,interval_ms,frames,skipped,render_ms,begin_ms,flip_ms,actors,in_view," +
					"annot_gen_ms,annot_draw_ms,flush_ms,selected,zoom,stalls,paused,netframe,queue,scale");
				Console.WriteLine($"LiveBench: recording {name} ({connection}, {players} players, {timestep} ms ticks)");
			}

			static void Write(TickRecord r, double nextStart)
			{
				var c = CultureInfo.InvariantCulture;
				csv?.WriteLine(string.Join(",",
					r.Tick.ToString(c), (r.Start - start).ToString("F1", c), r.Late.ToString("F1", c), r.Logic.ToString("F2", c),
					"0", (nextStart - r.Start).ToString("F2", c), r.Frames.ToString(c), r.Skipped.ToString(c),
					r.Render.ToString("F2", c), r.Begin.ToString("F2", c), r.Flip.ToString("F2", c), r.Actors.ToString(c), r.InView.ToString(c),
					r.AnnotGen.ToString("F2", c), r.AnnotDraw.ToString("F2", c), r.Flush.ToString("F2", c), r.Selected.ToString(c),
					r.Zoom.ToString("F3", c), r.Stalls.ToString(c), r.Paused.ToString(c), r.NetFrame.ToString(c), r.Queue.ToString(c), r.Scale.ToString("F3", c)));
			}

			public static void Finish(string reason)
			{
				if (world == null)
					return;

				end = Now;
				try
				{
					if (Records.Count > 0)
						Write(Records[^1], end);
					csv?.Dispose();
					WriteSummary(reason);
				}
				catch (Exception e)
				{
					Console.WriteLine($"LiveBench: could not write {name}: {e}");
				}

				csv = null;
				finishedWorld = world;
				world = null;
				Records.Clear();
			}

			static void WriteSummary(string reason)
			{
				var c = CultureInfo.InvariantCulture;
				var g = Game.Settings.Graphics;
				var n = Records.Count;
				var wall = n > 0 ? end - Records[0].Start : 0;
				var frames = Records.Sum(r => r.Frames);
				var render = Records.Sum(r => r.Render);
				var begin = Records.Sum(r => r.Begin);
				var stallTicks = Records.Sum(r => r.Stalls);
				var pausedTicks = Records.Sum(r => r.Paused);
				var logic = Records.Select(r => r.Logic).OrderBy(x => x).ToList();
				var late = Records.Select(r => r.Late).OrderBy(x => x).ToList();
				double Mean(Func<TickRecord, double> f) => n > 0 ? Records.Average(f) : 0;

				var summary = new Dictionary<string, object>
				{
					["name"] = name,
					["live"] = true,
					["reason"] = reason,
					["map"] = mapTitle,
					["connection"] = connection,
					["players"] = players,
					["machine"] = $"{Environment.OSVersion} {Environment.ProcessorCount} cores {Platform.RuntimeVersion}",
					["gl"] = SafeGl(),
					["mode"] = g.Mode.ToString(),
					["resolution"] = $"{Game.Renderer.Resolution.Width}x{Game.Renderer.Resolution.Height}",
					["vsync"] = g.VSync,
					["cap_framerate"] = g.CapFramerate,
					["max_framerate"] = g.MaxFramerate,
					["cap_to_tick"] = g.CapFramerateToGameFps,
					["viewport_distance"] = g.ViewportDistance.ToString(),
					["timestep_ms"] = timestep,
					["first_tick"] = firstTick,
					["ticks"] = n,
					["wall_s"] = wall / 1000,
					["realised_speed_pct"] = wall > 0 ? 100.0 * n * timestep / wall : 0,
					["stall_ticks"] = stallTicks,
					["stall_pct"] = n + stallTicks > 0 ? 100.0 * stallTicks / (n + stallTicks) : 0,
					["paused_ticks"] = pausedTicks,
					["logic_mean_ms"] = Mean(r => r.Logic),
					["logic_p50_ms"] = Percentile(logic, 0.5),
					["logic_p95_ms"] = Percentile(logic, 0.95),
					["logic_max_ms"] = logic.Count > 0 ? logic[^1] : 0,
					["render_mean_ms"] = Mean(r => r.Render),
					["render_per_frame_ms"] = frames > 0 ? (render - begin) / frames : 0,
					["begin_mean_ms"] = Mean(r => r.Begin),
					["flip_mean_ms"] = Mean(r => r.Flip),
					["annot_gen_mean_ms"] = Mean(r => r.AnnotGen),
					["annot_draw_mean_ms"] = Mean(r => r.AnnotDraw),
					["flush_mean_ms"] = Mean(r => r.Flush),
					["selected_mean"] = Mean(r => r.Selected),
					["frames"] = frames,
					["fps"] = wall > 0 ? 1000.0 * frames / wall : 0,
					["late_ticks_pct"] = n > 0 ? 100.0 * Records.Count(r => r.Late >= 5) / n : 0,
					["late_mean_ms"] = Mean(r => r.Late),
					["late_p95_ms"] = Percentile(late, 0.95),
					["logic_resets"] = resets,
					["queue_mean"] = Mean(r => r.Queue),
					["queue_max"] = n > 0 ? Records.Max(r => r.Queue) : 0,
					["scale_mean"] = Mean(r => r.Scale),
					["slowed_pct"] = n > 0 ? 100.0 * Records.Count(r => r.Scale > 1.005f) / n : 0,
					["catching_up_pct"] = n > 0 ? 100.0 * Records.Count(r => r.Scale < 0.995f) / n : 0,
					["zoom_mean"] = Mean(r => r.Zoom),
					["in_view_mean"] = Mean(r => r.InView),
					["actors_mean"] = Mean(r => r.Actors),
					["desync_frame"] = desyncFrame,
				};

				var json = new StringBuilder("{\n");
				foreach (var kv in summary)
				{
					var v = kv.Value switch
					{
						null => "null",
						bool b => b ? "true" : "false",
						string str => "\"" + str.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
						double d => d.ToString("F3", c),
						float f => f.ToString("F3", c),
						_ => Convert.ToString(kv.Value, c)
					};
					json.Append($"  \"{kv.Key}\": {v},\n");
				}

				json.Length -= 2;
				json.Append("\n}\n");
				File.WriteAllText(Path.Combine(outDir, name + ".json"), json.ToString());

				var line = string.Format(c,
					"{0,-40} {1,2}ms {2,-16} {3,-5} {4,4} {5,2}p | {6,6:F1} min speed {7,5:F1}% stalls {8,4:F1}% paused {9,4} | logic {10,5:F1} p95 {11,5:F1} max {12,5:F0} " +
					"| frame {13,4:F1} begin {14,4:F1} flip {15,4:F1} fps {16,4:F0} late {17,3:F0}% resets {18} " +
					"| slowed {19,4:F1}% catchup {20,4:F1}% queue {21,4:F1} | zoom {22,4:F2} view {23,4:F0}/{24,4:F0} | {25}",
					name, timestep, g.Mode, g.VSync ? "vsync" : "-", g.CapFramerateToGameFps ? "tick" : g.CapFramerate ? g.MaxFramerate.ToString(c) : "-", players,
					wall / 60000, summary["realised_speed_pct"], summary["stall_pct"], pausedTicks, summary["logic_p50_ms"], summary["logic_p95_ms"], summary["logic_max_ms"],
					summary["render_per_frame_ms"], summary["begin_mean_ms"], summary["flip_mean_ms"], summary["fps"], summary["late_ticks_pct"], resets,
					summary["slowed_pct"], summary["catching_up_pct"], summary["queue_mean"], summary["zoom_mean"], summary["in_view_mean"], summary["actors_mean"], reason);
				File.AppendAllText(Path.Combine(outDir, "summary.txt"), line + "\n");
				Console.WriteLine(line);
			}
		}

		static string SafeGl()
		{
			try
			{
				return Game.Renderer?.GLVersion ?? "";
			}
			catch
			{
				return "";
			}
		}
	}
}
