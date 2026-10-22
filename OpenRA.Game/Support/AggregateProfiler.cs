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

using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace OpenRA.Support
{
	/// <summary>
	/// Bench.Profile=true: accumulates simulation time per trait type, activity type, actor type, effect type and
	/// idle-notification trait, and writes the totals to &lt;Bench.Out&gt;/&lt;name&gt;-profile.csv every FlushEvery world
	/// ticks. Off, it costs a static bool test per sample point.
	/// </summary>
	public static class AggregateProfiler
	{
		public static bool Enabled;

		// Only every SampleEvery-th world tick is attributed, so the profiler's own cost (a dictionary update per
		// trait call) barely disturbs the plain per-tick timing of the other ticks; FlushEvery world ticks therefore
		// contain FlushEvery / SampleEvery sampled ticks
		public const int SampleEvery = 10;
		public const int FlushEvery = 250;
		public static int SampledTicksPerFlush => FlushEvery / SampleEvery;
		static bool sampling;

		static readonly Dictionary<string, long> Ticks = new();
		static readonly Dictionary<string, int> Counts = new();
		static bool channelOpen;
		static string prefix = "";

		public static void Start(string filePrefix)
		{
			prefix = filePrefix;
			Enabled = true;
		}

		public static long Now() => sampling ? Stopwatch.GetTimestamp() : 0;

		/// <summary>Called at the start of each world tick; decides whether this tick is attributed.</summary>
		public static void BeginTick(int worldTick)
		{
			sampling = Enabled && worldTick % SampleEvery == 0;
		}

		/// <summary>Records the first frame at which a replay diverged from the recorded game.</summary>
		public static void NoteDesync(int frame)
		{
			if (!Enabled)
				return;

			Log.AddChannel("desync", $"{prefix}desync.txt");
			Log.Write("desync", $"Replay out of sync at net frame {frame}");
		}

		public static void Add(string key, long start)
		{
			if (!Enabled || start == 0)
				return;

			var elapsed = Stopwatch.GetTimestamp() - start;
			Ticks[key] = Ticks.TryGetValue(key, out var t) ? t + elapsed : elapsed;
			Counts[key] = Counts.TryGetValue(key, out var c) ? c + 1 : 1;
		}

		public static void Flush(int worldTick, int actorCount, int effectCount)
		{
			if (!Enabled || worldTick % FlushEvery != 0)
				return;

			if (!channelOpen)
			{
				Log.AddChannel("profile", $"{prefix}profile.csv");
				Log.Write("profile", $"tick,actors,effects,key,ms,count  (each block sums {SampledTicksPerFlush} sampled ticks, every {SampleEvery}th)");
				channelOpen = true;
			}

			var toMs = 1000.0 / Stopwatch.Frequency;
			foreach (var kv in Ticks.OrderByDescending(kv => kv.Value))
				Log.Write("profile", string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4:F3},{5}", worldTick, actorCount, effectCount, kv.Key, kv.Value * toMs, Counts[kv.Key]));

			Ticks.Clear();
			Counts.Clear();
		}
	}
}
