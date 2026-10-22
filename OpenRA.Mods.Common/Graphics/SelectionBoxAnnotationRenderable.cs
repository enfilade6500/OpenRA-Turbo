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
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Graphics
{
	public class SelectionBoxAnnotationRenderable : IRenderable, IFinalizedRenderable
	{
		readonly Rectangle decorationBounds;
		readonly Color color;

		public SelectionBoxAnnotationRenderable(Actor actor, Rectangle decorationBounds, Color color)
			: this(actor.CenterPosition, decorationBounds, color) { }

		public SelectionBoxAnnotationRenderable(WPos pos, Rectangle decorationBounds, Color color)
		{
			Pos = pos;
			this.decorationBounds = decorationBounds;
			this.color = color;
		}

		public WPos Pos { get; }

		public int ZOffset => 0;
		public bool IsDecoration => true;

		public IRenderable WithZOffset(int newOffset) { return this; }
		public IRenderable OffsetBy(in WVec vec) { return new SelectionBoxAnnotationRenderable(Pos + vec, decorationBounds, color); }
		public IRenderable AsDecoration() { return this; }

		public IFinalizedRenderable PrepareRender(WorldRenderer wr) { return this; }
		public void Render(WorldRenderer wr)
		{
			var tl = wr.Viewport.WorldToViewPx(new float2(decorationBounds.Left, decorationBounds.Top)).ToFloat2();
			var br = wr.Viewport.WorldToViewPx(new float2(decorationBounds.Right, decorationBounds.Bottom)).ToFloat2();
			var tr = new float2(br.X, tl.Y);
			var bl = new float2(tl.X, br.Y);
			var u = new float2(4, 0);
			var v = new float2(0, 4);

			// PERF: Each corner bracket is two straight one-pixel lines meeting at the corner; drawing them as such
			// costs eight quads and no allocation, where a joined polyline per corner cost an array and the
			// mitre arithmetic for every selected unit on every frame.
			Span<float3> lines = stackalloc float3[16]
			{
				tl + u, tl, tl, tl + v,
				tr - u, tr, tr, tr + v,
				br - u, br, br, br - v,
				bl + u, bl, bl, bl - v,
			};
			Game.Renderer.RgbaColorRenderer.DrawLines(lines, 1, color);
		}

		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { return Rectangle.Empty; }
	}
}
