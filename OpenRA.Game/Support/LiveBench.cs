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
	///   Bench.Zoom=z           viewport zoom while measuring (default: leave the player's)
	/// Display settings are given the usual way: Graphics.VSync=False Graphics.Mode=Windowed Graphics.CapFramerate=True ...
	/// </summary>
	public static class LiveBench
	{
		public static bool Active { get; private set; }
		public static bool Measuring { get; private set; }
		public static bool NoFrameSkip { get; private set; }
		public static int RenderRepeat { get; private set; } = 1;

		static string outDir, name;
		static int warpToTick, durationTicks;
		static float logicFloorMs, ballastMs, zoom;

		static readonly Stopwatch Clock = Stopwatch.StartNew();
		static double Now => Clock.Elapsed.TotalMilliseconds;

		sealed class TickRecord
		{
			public int Tick;
			public double Start, Late, Logic, Pad;
			public int Frames, Skipped;
			public double Render, Begin, Flip;
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
			name = args.GetValue("Bench.Name", "bench");
			warpToTick = ParseGameTime(args.GetValue("Bench.WarpTo", "0"));
			durationTicks = ParseGameTime(args.GetValue("Bench.Duration", "0"));
			logicFloorMs = Parse(args.GetValue("Bench.LogicMs", "0"));
			ballastMs = Parse(args.GetValue("Bench.Ballast", "0"));
			NoFrameSkip = args.GetValue("Bench.NoFrameSkip", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
			RenderRepeat = Math.Max(1, (int)Parse(args.GetValue("Bench.RenderRepeat", "1")));
			zoom = Parse(args.GetValue("Bench.Zoom", "0"));
			replayPath = args.GetValue("Launch.Replay", "");

			Directory.CreateDirectory(outDir);
			Console.WriteLine($"LiveBench: {name} -> {outDir}");
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
			if (!Active || world == null || !world.IsReplay)
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

		/// <summary>The logic tick is done: pad it if asked, record it, and decide whether measuring starts or ends here.</summary>
		public static void AfterLogic(World world, WorldRenderer wr)
		{
			if (!Active || finished)
				return;

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
				Pad = Math.Max(0, target - logic)
			};
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
			world.ReplayTimestep = world.Timestep;

			if (wr != null)
			{
				var tl = world.Map.ProjectedTopLeft;
				var br = world.Map.ProjectedBottomRight;
				wr.Viewport.Center(new WPos((tl.X + br.X) / 2, (tl.Y + br.Y) / 2, 0));
				if (zoom > 0)
					wr.Viewport.AdjustZoom((float)Math.Log(zoom / wr.Viewport.Zoom));
			}

			csv = new StreamWriter(Path.Combine(outDir, name + ".csv"), false, Encoding.ASCII);
			csv.WriteLine("tick,t_ms,late_ms,logic_ms,pad_ms,interval_ms,frames,skipped,render_ms,begin_ms,flip_ms");
			Console.WriteLine($"LiveBench: measuring from tick {firstMeasuredTick}");
		}

		static void WriteCsv(TickRecord r, double nextStart)
		{
			var c = CultureInfo.InvariantCulture;
			csv?.WriteLine(string.Join(",",
				r.Tick.ToString(c), (r.Start - measureStart).ToString("F1", c), r.Late.ToString("F1", c), r.Logic.ToString("F2", c),
				r.Pad.ToString("F2", c), (nextStart - r.Start).ToString("F2", c), r.Frames.ToString(c), r.Skipped.ToString(c),
				r.Render.ToString("F2", c), r.Begin.ToString("F2", c), r.Flip.ToString("F2", c)));
		}

		/// <summary>A frame was drawn; the times are what the main thread spent in it, in starting the world pass, and in the hand-off.</summary>
		public static void AfterRender(double totalMs, double beginMs, double flipMs)
		{
			if (!Measuring || finished)
				return;

			var r = AllRecords.Count > 0 ? AllRecords[^1] : null;
			if (r == null)
				return;

			r.Frames++;
			r.Render += totalMs;
			r.Begin += beginMs;
			r.Flip += flipMs;
		}

		public static void NoteSkippedFrame()
		{
			if (Measuring && AllRecords.Count > 0)
				AllRecords[^1].Skipped++;
		}

		public static void NoteLogicReset()
		{
			if (Measuring)
				resets++;
		}

		public static void NoteDesync(int frame)
		{
			if (!Active)
				return;

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

			var timestep = Game.OrderManager?.World?.Timestep ?? 40;
			var capacity = wall > 0 ? 100.0 * n * timestep / wall : 0;
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
				["logic_floor_ms"] = logicFloorMs,
				["ballast_ms"] = ballastMs,
				["no_frame_skip"] = NoFrameSkip,
				["render_repeat"] = RenderRepeat,
				["zoom"] = zoom,
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
				"{0,-22} {1,-16} {2,-5} {3,5} {4,5} {5,4} {6,3} | ticks {7,5} capacity {8,6:F1}% interval {9,5:F1} p95 {10,5:F1} | " +
				"logic {11,5:F1} pad {12,4:F1} render {13,5:F1} begin {14,4:F1} flip {15,5:F1} | fps {16,5:F1} skipped {17,4} late {18,4:F0}% resets {19} | {20}",
				name, g.Mode, g.VSync ? "vsync" : "-", g.CapFramerateToGameFps ? "tick" : g.CapFramerate ? g.MaxFramerate.ToString(c) : "-",
				logicFloorMs > 0 ? "L" + logicFloorMs.ToString("F0", c) : ballastMs > 0 ? "B" + ballastMs.ToString("F0", c) : "-",
				NoFrameSkip ? "noskip" : "-", RenderRepeat > 1 ? "x" + RenderRepeat : "",
				n, capacity, summary["interval_mean_ms"], summary["interval_p95_ms"],
				summary["logic_mean_ms"], summary["pad_mean_ms"], summary["render_mean_ms"], summary["begin_mean_ms"], summary["flip_mean_ms"],
				summary["fps"], skipped, summary["late_ticks_pct"], resets, finishReason);
			File.AppendAllText(Path.Combine(outDir, "summary.txt"), line + "\n");
			Console.WriteLine(line);
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
