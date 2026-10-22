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
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Orders;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Orders
{
	public class UnitOrderGenerator : IOrderGenerator
	{
		readonly string worldSelectCursor = ChromeMetrics.Get<string>("WorldSelectCursor");
		readonly string worldDefaultCursor = ChromeMetrics.Get<string>("WorldDefaultCursor");

		// The order list OrderForUnit works in, kept between calls (see there)
		static List<(IIssueOrder Trait, IOrderTargeter Order)> orderBuffer;

		// A type with more than this many units selected answers the cursor question once, for all of them (see GetCursor)
		const int CursorSampleTypeAbove = 100;
		readonly Dictionary<ActorInfo, int> cursorTypeCounts = new();
		readonly HashSet<ActorInfo> cursorSampledTypes = new();

		// What the last cursor was worked out for, and what it was (see GetCursor)
		int cursorTick = -1, cursorSelectionHash;
		CPos cursorCell;
		TargetType cursorTargetType;
		Actor cursorTargetActor;
		FrozenActor cursorTargetFrozen;
		WPos cursorTargetPos;
		Modifiers cursorModifiers;
		MouseButton cursorButton;
		string cursor;

		protected static Target TargetForInput(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			var actor = world.ScreenMap.ActorsAtMouse(mi)
				.Where(a => !a.Actor.IsDead && a.Actor.Info.HasTraitInfo<ITargetableInfo>() && !world.FogObscures(a.Actor))
				.WithHighestSelectionPriority(worldPixel, mi.Modifiers);

			if (actor != null)
				return Target.FromActor(actor);

			var frozen = world.ScreenMap.FrozenActorsAtMouse(world.RenderPlayer, mi)
				.Where(a => a.Info.HasTraitInfo<ITargetableInfo>() && a.Visible && a.HasRenderables)
				.WithHighestSelectionPriority(worldPixel, mi.Modifiers);

			if (frozen != null)
				return Target.FromFrozenActor(frozen);

			return Target.FromCell(world, cell);
		}

		public virtual IEnumerable<Order> Order(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			var target = TargetForInput(world, cell, worldPixel, mi);
			var orders = world.Selection.Actors
				.Select(a => OrderForUnit(a, target, cell, mi))
				.Where(o => o != null)
				.ToList();

			var actorsInvolved = orders.Select(o => o.Actor).Distinct().ToArray();
			if (actorsInvolved.Length == 0)
				yield break;

			// HACK: This is required by the hacky player actions-per-minute calculation
			// TODO: Reimplement APM properly and then remove this
			yield return new Order("CreateGroup", actorsInvolved[0].Owner.PlayerActor, false, actorsInvolved);

			foreach (var o in orders)
				yield return CheckSameOrder(o.Order, o.Trait.IssueOrder(o.Actor, o.Order, o.Target, mi.Modifiers.HasModifier(Modifiers.Shift)));
		}

		public virtual void Tick(World world) { }
		public virtual IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		public virtual IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world) { yield break; }
		public virtual IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world) { yield break; }

		public virtual string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			var target = TargetForInput(world, cell, worldPixel, mi);

			bool useSelect;
			if (Game.Settings.Game.UseClassicMouseStyle && !InputOverridesSelection(world, worldPixel, mi))
				useSelect = target.Type == TargetType.Actor && target.Actor.Info.HasTraitInfo<ISelectableInfo>();
			else
			{
				// PERF: The cursor is chosen from the orders of every selected unit, on every frame. Between frames of
				// the same world tick nothing that goes into it changes unless the mouse reached another cell or target,
				// a modifier or the selection changed, so the last answer is reused then (a computer that draws several
				// frames per tick is spared all but the first).
				var targetPos = target.Type == TargetType.Terrain ? target.CenterPosition : WPos.Zero;
				if (world.WorldTick == cursorTick && cell == cursorCell && mi.Modifiers == cursorModifiers && mi.Button == cursorButton
					&& world.Selection.Hash == cursorSelectionHash && target.Type == cursorTargetType && target.Actor == cursorTargetActor
					&& target.FrozenActor == cursorTargetFrozen && targetPos == cursorTargetPos)
					return cursor;

				cursorTick = world.WorldTick;
				cursorCell = cell;
				cursorModifiers = mi.Modifiers;
				cursorButton = mi.Button;
				cursorSelectionHash = world.Selection.Hash;
				cursorTargetType = target.Type;
				cursorTargetActor = target.Actor;
				cursorTargetFrozen = target.FrozenActor;
				cursorTargetPos = targetPos;

				// The first unit offering the highest-priority order decides. PERF: A type with more than
				// CursorSampleTypeAbove units selected is asked once, through its first unit, rather than once per unit:
				// units of a type answer alike unless one is in an unusual state (out of ammunition, a trait disabled),
				// and that many of one type is a blob of riflemen, not a flight of aircraft; the types with fewer units in
				// the same selection are still asked unit by unit. A unit with no order at all (an ally's, a dead one)
				// passes the question on to the next of its type. The orders issued on a click are worked out per unit.
				UnitOrderResult cursorOrder = null;
				var selection = world.Selection.Actors;
				cursorTypeCounts.Clear();
				cursorSampledTypes.Clear();
				if (selection.Count > CursorSampleTypeAbove)
				{
					foreach (var a in selection)
					{
						cursorTypeCounts.TryGetValue(a.Info, out var n);
						cursorTypeCounts[a.Info] = n + 1;
					}
				}

				foreach (var a in selection)
				{
					var sampled = cursorTypeCounts.Count > 0 && cursorTypeCounts[a.Info] > CursorSampleTypeAbove;
					if (sampled && !cursorSampledTypes.Add(a.Info))
						continue;

					var o = OrderForUnit(a, target, cell, mi);
					if (o == null)
					{
						if (sampled)
							cursorSampledTypes.Remove(a.Info);

						continue;
					}

					if (o.Cursor != null && (cursorOrder == null || o.Order.OrderPriority > cursorOrder.Order.OrderPriority))
						cursorOrder = o;
				}

				if (cursorOrder != null)
					return cursor = cursorOrder.Cursor;

				useSelect = target.Type == TargetType.Actor && target.Actor.Info.HasTraitInfo<ISelectableInfo>() &&
					(mi.Modifiers.HasModifier(Modifiers.Shift) || world.Selection.Actors.Count == 0);
				return cursor = useSelect ? worldSelectCursor : worldDefaultCursor;
			}

			return useSelect ? worldSelectCursor : worldDefaultCursor;
		}

		public void Deactivate() { }

		bool IOrderGenerator.HandleKeyPress(KeyInput e) { return false; }

		// Used for classic mouse orders, determines whether or not action at xy is move or select
		public virtual bool InputOverridesSelection(World world, int2 xy, MouseInput mi)
		{
			var actor = world.ScreenMap.ActorsAtMouse(xy)
				.Where(a =>
					!a.Actor.IsDead &&
					a.Actor.Info.HasTraitInfo<ISelectableInfo>() &&
					(a.Actor.Owner.IsAlliedWith(world.RenderPlayer) || !world.FogObscures(a.Actor)))
				.WithHighestSelectionPriority(xy, mi.Modifiers);

			if (actor == null)
				return true;

			var target = Target.FromActor(actor);
			var cell = world.Map.CellContaining(target.CenterPosition);
			var actorsAt = world.ActorMap.GetActorsAt(cell).ToList();

			var modifiers = TargetModifiers.None;
			if (mi.Modifiers.HasModifier(Modifiers.Ctrl))
				modifiers |= TargetModifiers.ForceAttack;
			if (mi.Modifiers.HasModifier(Modifiers.Shift))
				modifiers |= TargetModifiers.ForceQueue;
			if (mi.Modifiers.HasModifier(Modifiers.Alt))
				modifiers |= TargetModifiers.ForceMove;

			foreach (var a in world.Selection.Actors)
			{
				var o = OrderForUnit(a, target, cell, mi);
				if (o != null && o.Order.TargetOverridesSelection(a, target, actorsAt, cell, modifiers))
					return true;
			}

			return false;
		}

		public virtual void SelectionChanged(World world, IEnumerable<Actor> selected) { }

		/// <summary>
		/// Returns the most appropriate order for a given actor and target.
		/// First priority is given to orders that interact with the given actors.
		/// Second priority is given to actors in the given cell.
		/// </summary>
		protected static UnitOrderResult OrderForUnit(Actor self, Target target, CPos xy, MouseInput mi)
		{
			if (mi.Button != Game.Settings.Game.MouseButtonPreference.Action)
				return null;

			if (self.Owner != self.World.LocalPlayer)
				return null;

			if (self.World.IsGameOver)
				return null;

			if (self.Disposed || !target.IsValidFor(self))
				return null;

			var modifiers = TargetModifiers.None;
			if (mi.Modifiers.HasModifier(Modifiers.Ctrl))
				modifiers |= TargetModifiers.ForceAttack;
			if (mi.Modifiers.HasModifier(Modifiers.Shift))
				modifiers |= TargetModifiers.ForceQueue;
			if (mi.Modifiers.HasModifier(Modifiers.Alt))
				modifiers |= TargetModifiers.ForceMove;

			// PERF: This runs for every selected actor on every frame, to choose the mouse cursor. The orders are
			// collected into a reused list and sorted in place (a stable insertion sort, as the lists are short), where
			// LINQ allocated an object per order, an iterator per trait and the sort's buffers each time.
			var orders = orderBuffer ?? new List<(IIssueOrder Trait, IOrderTargeter Order)>();
			orderBuffer = null;
			try
			{
				foreach (var trait in self.TraitsImplementing<IIssueOrder>())
					foreach (var order in trait.Orders)
						orders.Add((trait, order));

				for (var i = 1; i < orders.Count; i++)
				{
					var item = orders[i];
					var j = i - 1;
					while (j >= 0 && orders[j].Order.OrderPriority < item.Order.OrderPriority)
					{
						orders[j + 1] = orders[j];
						j--;
					}

					orders[j + 1] = item;
				}

				for (var i = 0; i < 2; i++)
				{
					foreach (var o in orders)
					{
						var localModifiers = modifiers;
						string cursor = null;
						if (o.Order.CanTarget(self, target, ref localModifiers, ref cursor))
							return new UnitOrderResult(self, o.Order, o.Trait, cursor, target);
					}

					// No valid orders, so check for orders against the cell
					target = Target.FromCell(self.World, xy);
				}

				return null;
			}
			finally
			{
				orders.Clear();
				orderBuffer = orders;
			}
		}

		static Order CheckSameOrder(IOrderTargeter iot, Order order)
		{
			if (order == null && iot.OrderID != null)
				TextNotificationsManager.Debug("BUG: in order targeter - decided on {0} but then didn't order", iot.OrderID);
			else if (order != null && iot.OrderID != order.OrderString)
				TextNotificationsManager.Debug("BUG: in order targeter - decided on {0} but ordered {1}", iot.OrderID, order.OrderString);
			return order;
		}

		protected sealed class UnitOrderResult
		{
			public readonly Actor Actor;
			public readonly IOrderTargeter Order;
			public readonly IIssueOrder Trait;
			public readonly string Cursor;
			public ref readonly Target Target => ref target;

			readonly Target target;

			public UnitOrderResult(Actor actor, IOrderTargeter order, IIssueOrder trait, string cursor, in Target target)
			{
				Actor = actor;
				Order = order;
				Trait = trait;
				Cursor = cursor;
				this.target = target;
			}
		}

		public virtual bool ClearSelectionOnLeftClick => true;
	}
}
