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
using OpenRA.Primitives;

namespace OpenRA.Graphics
{
	public class AnimationWithOffset
	{
		public readonly Animation Animation;
		public readonly Func<WVec> OffsetFunc;
		public readonly Func<bool> DisableFunc;
		public readonly Func<WPos, int> ZOffset;

		/// <summary>
		/// PERF: The owner promises to call <see cref="OpenRA.Mods.Common.Traits.Render.RenderSprites.AnimationChanged"/>
		/// (or the equivalent of its renderer) whenever DisableFunc's answer changes, so that the animation need not be
		/// asked every tick while it is static.
		/// </summary>
		public bool DisableChangeAnnounced { get; init; }

		public AnimationWithOffset(Animation a, Func<WVec> offset, Func<bool> disable)
			: this(a, offset, disable, null) { }

		public AnimationWithOffset(Animation a, Func<WVec> offset, Func<bool> disable, int zOffset)
			: this(a, offset, disable, _ => zOffset) { }

		public AnimationWithOffset(Animation a, Func<WVec> offset, Func<bool> disable, Func<WPos, int> zOffset)
		{
			Animation = a;
			OffsetFunc = offset;
			DisableFunc = disable;
			ZOffset = zOffset;
		}

		public IRenderable[] Render(Actor self, PaletteReference pal)
		{
			var center = self.CenterPosition;
			var offset = OffsetFunc?.Invoke() ?? WVec.Zero;

			var z = ZOffset?.Invoke(center + offset) ?? 0;
			return Animation.Render(center, offset, z, pal);
		}

		public Rectangle ScreenBounds(Actor self, WorldRenderer wr)
		{
			var center = self.CenterPosition;
			var offset = OffsetFunc?.Invoke() ?? WVec.Zero;

			return Animation.ScreenBounds(wr, center, offset);
		}

		public static implicit operator AnimationWithOffset(Animation a)
		{
			return new AnimationWithOffset(a, null, null, null);
		}
	}
}
