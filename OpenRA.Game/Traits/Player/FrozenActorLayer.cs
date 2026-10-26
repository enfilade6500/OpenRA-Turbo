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
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Traits
{
	public interface ICreatesFrozenActors
	{
		void OnVisibilityChanged(FrozenActor frozen);

		/// <summary>One of the trait's frozen copies has set <see cref="FrozenActor.NeedRenderables"/>.</summary>
		void OnRenderablesRequested(FrozenActor frozen);
	}

	[TraitLocation(SystemActors.Player)]
	[Desc("Required for FrozenUnderFog to work. Attach this to the player actor.")]
	public class FrozenActorLayerInfo : TraitInfo, Requires<ShroudInfo>
	{
		[Desc("Size of partition bins (cells)")]
		public readonly int BinSize = 10;

		public override object Create(ActorInitializer init) { return new FrozenActorLayer(init.Self, this); }
	}

	public class FrozenActor
	{
		public readonly PPos[] Footprint;
		public readonly WPos CenterPosition;
		readonly Actor actor;
		readonly ICreatesFrozenActors frozenTrait;
		readonly Shroud shroud;
		readonly List<WPos> targetablePositions = new();

		public Player Viewer { get; }
		public Player Owner { get; private set; }
		public BitSet<TargetableType> TargetTypes { get; private set; }
		public IEnumerable<WPos> TargetablePositions => targetablePositions;

		public ITooltipInfo TooltipInfo { get; private set; }
		public Player TooltipOwner { get; private set; }
		readonly ITooltip[] tooltips;

		public int HP { get; private set; }
		public DamageState DamageState { get; private set; }
		readonly IHealth health;

		readonly IVisibilityModifier[] visibilityModifiers;

		// The Visible flag is tied directly to the actor visibility under the fog.
		// If Visible is true, the actor is made invisible (via FrozenUnderFog/IDefaultVisibility)
		// and this FrozenActor is rendered instead.
		// The Hidden flag covers the edge case that occurs when the backing actor was last "seen"
		// but not actually visible because a visibility modifier hid the actor. Setting Visible to
		// true when the actor is hidden under the fog would leak the actors position via the
		// tooltips and AutoTargetability, and keeping Visible as false would cause the actor to be
		// rendered under the fog.
		public bool Visible { get; private set; } = true;
		public bool Hidden { get; private set; } = false;

		public bool Shrouded { get; private set; }

		// PERF: The layer that holds this copy, told of everything that gives its Tick something to do (a flash, a visibility
		// update, the actor's death) and of every change of Visible, so that it ticks only the copies concerned and keeps its
		// synced hashes as running sums instead of walking every copy every tick
		public FrozenActorLayer Layer { get; internal set; }

		bool needRenderables;
		public bool NeedRenderables
		{
			get => needRenderables;
			set
			{
				needRenderables = value;
				if (value)
					frozenTrait.OnRenderablesRequested(this);
			}
		}

		bool updateVisibilityNextTick;
		public bool UpdateVisibilityNextTick
		{
			get => updateVisibilityNextTick;
			set
			{
				updateVisibilityNextTick = value;
				if (value)
					Layer?.NeedsTick(this);
			}
		}

		public IRenderable[] Renderables = NoRenderables;
		public Rectangle[] ScreenBounds = NoBounds;

		public Polygon MouseBounds = Polygon.Empty;

		static readonly IRenderable[] NoRenderables = Array.Empty<IRenderable>();
		static readonly Rectangle[] NoBounds = Array.Empty<Rectangle>();

		int flashTicks;
		TintModifiers flashModifiers;
		float3 flashTint;
		float? flashAlpha;

		public FrozenActor(Actor actor, ICreatesFrozenActors frozenTrait, PPos[] footprint, Player viewer, bool startsRevealed)
		{
			this.actor = actor;
			this.frozenTrait = frozenTrait;
			Viewer = viewer;
			shroud = viewer.Shroud;
			NeedRenderables = startsRevealed;

			// Consider all cells inside the map area (ignoring the current map bounds)
			Footprint = footprint
				.Where(m => shroud.Contains(m))
				.ToArray();

			if (Footprint.Length == 0)
				throw new ArgumentException("This frozen actor has no footprint.\n" +
					$"Actor Name: {actor.Info.Name}\n" +
					$"Actor Location: {actor.Location}\n" +
					$"Input footprint: [{footprint.Select(p => p.ToString()).JoinWith("|")}]\n" +
					$"Input footprint (after shroud.Contains): [{footprint.Select(p => shroud.Contains(p).ToString()).JoinWith("|")}]");

			CenterPosition = actor.CenterPosition;

			tooltips = actor.TraitsImplementing<ITooltip>().ToArray();
			health = actor.TraitOrDefault<IHealth>();
			visibilityModifiers = actor.TraitsImplementing<IVisibilityModifier>().ToArray();

			UpdateVisibility();
		}

		public uint ID => actor.ActorID;
		public bool IsValid => Owner != null;
		public ActorInfo Info => actor.Info;
		public Actor Actor => !actor.IsDead ? actor : null;

		public void RefreshState()
		{
			Owner = actor.Owner;
			TargetTypes = actor.GetEnabledTargetTypes();
			targetablePositions.Clear();
			targetablePositions.AddRange(actor.GetTargetablePositions());

			if (health != null)
			{
				HP = health.HP;
				DamageState = health.DamageState;
			}

			var tooltip = tooltips.FirstEnabledTraitOrDefault();
			if (tooltip != null)
			{
				TooltipInfo = tooltip.TooltipInfo;
				TooltipOwner = tooltip.Owner;
			}
		}

		public void RefreshHidden()
		{
			Hidden = false;
			foreach (var visibilityModifier in visibilityModifiers)
			{
				if (!visibilityModifier.IsVisible(actor, Viewer))
				{
					Hidden = true;
					break;
				}
			}
		}

		public void Tick()
		{
			if (flashTicks > 0)
				flashTicks--;

			if (UpdateVisibilityNextTick)
				UpdateVisibility();
		}

		/// <summary>Whether the next Tick has something left to do (after which the layer asks again).</summary>
		public bool TickPending => flashTicks > 0 || updateVisibilityNextTick;

		/// <summary>The actor has died or is being disposed: the layer must look at this copy again.</summary>
		public void ActorGone()
		{
			Layer?.NeedsTick(this);
		}

		void UpdateVisibility()
		{
			UpdateVisibilityNextTick = false;

			var wasVisible = Visible;
			Shrouded = true;
			Visible = true;

			// PERF: Avoid LINQ.
			foreach (var puv in Footprint)
			{
				var cv = shroud.GetVisibility(puv);
				if (cv.HasFlag(Shroud.CellVisibility.Visible))
				{
					Visible = false;
					Shrouded = false;
					break;
				}

				if (Shrouded && cv.HasFlag(Shroud.CellVisibility.Explored))
					Shrouded = false;
			}

			// Force the backing trait to update so other actors can't
			// query inconsistent state (both hidden or both visible)
			if (Visible != wasVisible)
			{
				frozenTrait.OnVisibilityChanged(this);
				Layer?.VisibleChanged(this);
			}

			NeedRenderables |= Visible && !wasVisible;
		}

		public void Invalidate()
		{
			Owner = null;
		}

		public void Flash(Color color, float alpha)
		{
			flashTicks = 5;
			Layer?.NeedsTick(this);
			flashModifiers = TintModifiers.ReplaceColor;
			flashTint = new float3(color.R, color.G, color.B) / 255f;
			flashAlpha = alpha;
		}

		public void Flash(float3 tint)
		{
			flashTicks = 5;
			Layer?.NeedsTick(this);
			flashModifiers = TintModifiers.None;
			flashTint = tint;
			flashAlpha = null;
		}

		public IEnumerable<IRenderable> Render()
		{
			if (Shrouded)
				return NoRenderables;

			if (flashTicks > 0 && flashTicks % 2 == 0)
			{
				return Renderables.Concat(Renderables.Where(r => !r.IsDecoration && r is IModifyableRenderable)
					.Select(r =>
					{
						var mr = (IModifyableRenderable)r;
						mr = mr.WithTint(flashTint, mr.TintModifiers | flashModifiers);
						if (flashAlpha.HasValue)
							mr = mr.WithAlpha(flashAlpha.Value);

						return mr;
					}));
			}

			return Renderables;
		}

		public bool HasRenderables => !Shrouded && Renderables.Length > 0;

		public override string ToString()
		{
			return $"{Info.Name} {ID}{(IsValid ? "" : " (invalid)")}";
		}
	}

	public class FrozenActorLayer : IRender, ITick, ISync
	{
		[Sync]
		public int VisibilityHash;

		[Sync]
		public int FrozenHash;

		readonly int binSize;
		readonly World world;
		readonly Player owner;
		readonly Dictionary<uint, FrozenActor> frozenActorsById;
		readonly SpatiallyPartitioned<FrozenActor> partitionedFrozenActors;

		// PERF: The synced hashes as running sums (every copy's id, and the ids of the visible ones), and the copies whose
		// Tick has something to do, so that a tick costs what changed rather than a walk over every copy on the map. The
		// hashes are published at tick time exactly as the walk computed them: FrozenHash counts the copies present
		// during the tick (including those it removes), VisibilityHash the ones visible after their Tick.
		int frozenHashSum, visibilityHashSum;
		readonly HashSet<FrozenActor> pending = new();
		readonly List<FrozenActor> ticking = new();

		public FrozenActorLayer(Actor self, FrozenActorLayerInfo info)
		{
			binSize = info.BinSize;
			world = self.World;
			owner = self.Owner;
			frozenActorsById = new Dictionary<uint, FrozenActor>();

			partitionedFrozenActors = new SpatiallyPartitioned<FrozenActor>(
				world.Map.MapSize.X, world.Map.MapSize.Y, binSize);

			self.Trait<Shroud>().OnShroudChanged += uv =>
			{
				foreach (var fa in partitionedFrozenActors.At(new int2(uv.U, uv.V)))
					fa.UpdateVisibilityNextTick = true;
			};
		}

		public void Add(FrozenActor fa)
		{
			frozenActorsById.Add(fa.ID, fa);
			world.ScreenMap.AddOrUpdate(owner, fa);
			partitionedFrozenActors.Add(fa, FootprintBounds(fa));

			fa.Layer = this;
			frozenHashSum += (int)fa.ID;
			if (fa.Visible)
				visibilityHashSum += (int)fa.ID;
			if (fa.TickPending || fa.Actor == null)
				pending.Add(fa);
		}

		public void Remove(FrozenActor fa)
		{
			partitionedFrozenActors.Remove(fa);
			world.ScreenMap.Remove(owner, fa);
			if (!frozenActorsById.Remove(fa.ID))
				return;

			fa.Layer = null;
			frozenHashSum -= (int)fa.ID;
			if (fa.Visible)
				visibilityHashSum -= (int)fa.ID;
			pending.Remove(fa);
		}

		/// <summary>A copy's Tick has something to do (a flash, a visibility update, or its actor is gone).</summary>
		public void NeedsTick(FrozenActor fa)
		{
			pending.Add(fa);
		}

		/// <summary>A copy's Visible has just flipped (only ever during its Tick, or before it is added).</summary>
		public void VisibleChanged(FrozenActor fa)
		{
			visibilityHashSum += fa.Visible ? (int)fa.ID : -(int)fa.ID;
		}

		static Rectangle FootprintBounds(FrozenActor fa)
		{
			var p1 = fa.Footprint[0];
			var minU = p1.U;
			var maxU = p1.U;
			var minV = p1.V;
			var maxV = p1.V;
			foreach (var p in fa.Footprint)
			{
				if (minU > p.U)
					minU = p.U;
				else if (maxU < p.U)
					maxU = p.U;

				if (minV > p.V)
					minV = p.V;
				else if (maxV < p.V)
					maxV = p.V;
			}

			return Rectangle.FromLTRB(minU, minV, maxU + 1, maxV + 1);
		}

		void ITick.Tick(Actor self)
		{
			// Every copy present now counts, including those this tick removes
			FrozenHash = frozenHashSum;

			List<FrozenActor> frozenActorsToRemove = null;
			if (pending.Count > 0)
			{
				ticking.Clear();
				ticking.AddRange(pending);
				pending.Clear();

				foreach (var frozenActor in ticking)
				{
					frozenActor.Tick();

					// A copy of an actor that is gone is dropped once the player can see that (the copy is not shown)
					if (!frozenActor.Visible && frozenActor.Actor == null)
					{
						frozenActorsToRemove ??= new List<FrozenActor>();
						frozenActorsToRemove.Add(frozenActor);
					}
					else if (frozenActor.TickPending)
						pending.Add(frozenActor);
				}
			}

			VisibilityHash = visibilityHashSum;

			if (frozenActorsToRemove != null)
				foreach (var fa in frozenActorsToRemove)
					Remove(fa);
		}

		public virtual IEnumerable<IRenderable> Render(Actor self, WorldRenderer wr)
		{
			return world.ScreenMap.RenderableFrozenActorsInBox(owner, wr.Viewport.TopLeft, wr.Viewport.BottomRight)
				.Where(f => f.Visible)
				.SelectMany(ff => ff.Render());
		}

		public IEnumerable<Rectangle> ScreenBounds(Actor self, WorldRenderer wr)
		{
			// Player-actor render traits don't require screen bounds
			yield break;
		}

		public FrozenActor FromID(uint id)
		{
			if (!frozenActorsById.TryGetValue(id, out var fa))
				return null;

			return fa;
		}

		public IEnumerable<FrozenActor> FrozenActorsInRegion(CellRegion region, bool onlyVisible = true)
		{
			var tl = region.TopLeft;
			var br = region.BottomRight;
			return partitionedFrozenActors.InBox(Rectangle.FromLTRB(tl.X, tl.Y, br.X, br.Y))
				.Where(fa => fa.IsValid && (!onlyVisible || fa.Visible));
		}

		public IEnumerable<FrozenActor> FrozenActorsInCircle(World world, WPos origin, WDist r, bool onlyVisible = true)
		{
			var centerCell = world.Map.CellContaining(origin);
			var cellRange = (r.Length + 1023) / 1024;
			var tl = centerCell - new CVec(cellRange, cellRange);
			var br = centerCell + new CVec(cellRange, cellRange);

			// Target ranges are calculated in 2D, so ignore height differences
			return partitionedFrozenActors.InBox(Rectangle.FromLTRB(tl.X, tl.Y, br.X, br.Y))
				.Where(fa => fa.IsValid &&
					(!onlyVisible || fa.Visible) &&
					(fa.CenterPosition - origin).HorizontalLengthSquared <= r.LengthSquared);
		}
	}
}
