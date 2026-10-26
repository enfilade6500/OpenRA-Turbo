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
using System.Linq;
using OpenRA.Primitives;
using OpenRA.Support;

namespace OpenRA.Graphics
{
	public class Animation
	{
		public ISpriteSequence CurrentSequence { get; private set; }
		public string Name { get; private set; }
		public bool IsDecoration { get; set; }

		readonly Map map;
		readonly SequenceSet sequences;
		readonly Func<WAngle> facingFunc;
		readonly Func<bool> paused;

		int frame;
		bool backwards;
		bool tickAlways;
		int timeUntilNextFrame;
		Action tickFunc;

		/// <summary>
		/// The animation is a plain loop (PlayRepeating): its frame follows the tick count and nothing is called
		/// back, so ticking it while it is not drawn changes nothing anyone can observe.
		/// </summary>
		public bool IsLoop { get; private set; }

		/// <summary>A loop of a single frame: a still image that no tick ever changes.</summary>
		public bool IsStill => IsLoop && CurrentSequence != null && CurrentSequence.Length <= 1;

		/// <summary>
		/// Nothing a tick could change: a still, a held frame, or a finished PlayThen. Only a Play call changes it from
		/// here, and <see cref="Changed"/> is raised when one does, so the owner may stop ticking it until then.
		/// </summary>
		public bool IsStatic => !tickAlways && (tickFunc == null || IsStill);

		/// <summary>Raised when a Play call, ReplaceAnim or ChangeImage changes what the animation shows or does.</summary>
		public event Action Changed;

		public Animation(World world, string name)
			: this(world, name, () => WAngle.Zero) { }

		public Animation(World world, string name, Func<WAngle> facingFunc)
			: this(world, name, facingFunc, null) { }

		public Animation(World world, string name, Func<bool> paused)
			: this(world, name, () => WAngle.Zero, paused) { }

		public Animation(World world, string name, Func<WAngle> facingFunc, Func<bool> paused)
		{
			map = world.Map;
			sequences = world.Map.Sequences;
			Name = name.ToLowerInvariant();
			this.facingFunc = facingFunc;
			this.paused = paused;
		}

		public int CurrentFrame => backwards ? CurrentSequence.Length - frame - 1 : frame;

		public Sprite Image => CurrentSequence.GetSprite(CurrentFrame, facingFunc());

		public IRenderable[] Render(WPos pos, in WVec offset, int zOffset, PaletteReference palette)
		{
			var tintModifiers = CurrentSequence.IgnoreWorldTint ? TintModifiers.IgnoreWorldTint : TintModifiers.None;
			var alpha = CurrentSequence.GetAlpha(CurrentFrame);
			var (image, rotation) = CurrentSequence.GetSpriteWithRotation(CurrentFrame, facingFunc());
			var imageRenderable = new SpriteRenderable(
				image, pos, offset, CurrentSequence.ZOffset + zOffset, palette,
				CurrentSequence.Scale, alpha, float3.Ones, tintModifiers, IsDecoration, rotation);

			var shadow = CurrentSequence.GetShadow(CurrentFrame, facingFunc());
			if (shadow != null)
			{
				var height = map.DistanceAboveTerrain(pos).Length;

				var shadowRenderable = new SpriteRenderable(
					shadow, pos, offset - new WVec(0, 0, height), CurrentSequence.ShadowZOffset + zOffset + height, palette,
					CurrentSequence.Scale, 1f, float3.Ones, tintModifiers,
					true, rotation);
				return new IRenderable[] { shadowRenderable, imageRenderable };
			}

			return new IRenderable[] { imageRenderable };
		}

		public IRenderable[] RenderUI(WorldRenderer wr, int2 pos, in WVec offset, int zOffset, PaletteReference palette, float scale = 1f, float rotation = 0f)
		{
			scale *= CurrentSequence.Scale;
			var screenOffset = (scale * wr.ScreenVectorComponents(offset)).XY.ToInt2();
			var imagePos = pos + screenOffset - new int2((int)(scale * Image.Size.X / 2), (int)(scale * Image.Size.Y / 2));
			var alpha = CurrentSequence.GetAlpha(CurrentFrame);
			var imageRenderable = new UISpriteRenderable(Image, WPos.Zero + offset, imagePos, CurrentSequence.ZOffset + zOffset, palette, scale, alpha, rotation);

			var shadow = CurrentSequence.GetShadow(CurrentFrame, facingFunc());
			if (shadow != null)
			{
				var shadowPos = pos - new int2((int)(scale * shadow.Size.X / 2), (int)(scale * shadow.Size.Y / 2));
				var shadowRenderable = new UISpriteRenderable(shadow, WPos.Zero + offset, shadowPos, CurrentSequence.ShadowZOffset + zOffset, palette, scale, 1f, rotation);
				return new IRenderable[] { shadowRenderable, imageRenderable };
			}

			return new IRenderable[] { imageRenderable };
		}

		public Rectangle ScreenBounds(WorldRenderer wr, WPos pos, in WVec offset)
		{
			var scale = CurrentSequence.Scale;
			var xy = wr.ScreenPxPosition(pos) + wr.ScreenPxOffset(offset);
			var cb = CurrentSequence.Bounds;
			return Rectangle.FromLTRB(
				xy.X + (int)(cb.Left * scale),
				xy.Y + (int)(cb.Top * scale),
				xy.X + (int)(cb.Right * scale),
				xy.Y + (int)(cb.Bottom * scale));
		}

		public IRenderable[] Render(WPos pos, PaletteReference palette)
		{
			return Render(pos, WVec.Zero, 0, palette);
		}

		public void Play(string sequenceName)
		{
			PlayThen(sequenceName, null);
		}

		int CurrentSequenceTickOrDefault()
		{
			const int DefaultTick = 40; // 25 fps == 40 ms
			return CurrentSequence?.Tick ?? DefaultTick;
		}

		void PlaySequence(string sequenceName)
		{
			CurrentSequence = GetSequence(sequenceName);
			timeUntilNextFrame = CurrentSequenceTickOrDefault();
			Changed?.Invoke();
		}

		/// <summary>One frame of a sequence, held until the next Play call: a still that costs no ticks.</summary>
		public void PlayFrame(string sequenceName, int frameIndex)
		{
			backwards = false;
			tickAlways = false;
			IsLoop = false;
			tickFunc = null;
			PlaySequence(sequenceName);
			frame = frameIndex;
		}

		public void PlayRepeating(string sequenceName)
		{
			backwards = false;
			tickAlways = false;
			IsLoop = true;
			PlaySequence(sequenceName);

			frame = 0;
			tickFunc = () =>
			{
				++frame;
				if (frame >= CurrentSequence.Length)
					frame = 0;
			};
		}

		public bool ReplaceAnim(string sequenceName)
		{
			if (!HasSequence(sequenceName))
				return false;

			CurrentSequence = GetSequence(sequenceName);
			timeUntilNextFrame = Math.Min(CurrentSequenceTickOrDefault(), timeUntilNextFrame);
			frame %= CurrentSequence.Length;
			Changed?.Invoke();
			return true;
		}

		public void PlayThen(string sequenceName, Action after)
		{
			backwards = false;
			tickAlways = false;
			IsLoop = false;
			PlaySequence(sequenceName);

			frame = 0;
			tickFunc = () =>
			{
				++frame;
				if (frame >= CurrentSequence.Length)
				{
					frame = CurrentSequence.Length - 1;
					tickFunc = null;
					after?.Invoke();
				}
			};
		}

		public void PlayBackwardsThen(string sequenceName, Action after)
		{
			PlayThen(sequenceName, after);
			backwards = true;
		}

		public void PlayFetchIndex(string sequenceName, Func<int> func)
		{
			backwards = false;
			tickAlways = true;
			IsLoop = false;
			PlaySequence(sequenceName);

			frame = func();
			tickFunc = () => frame = func();
		}

		public void PlayFetchDirection(string sequenceName, Func<int> direction)
		{
			tickAlways = false;
			IsLoop = false;
			PlaySequence(sequenceName);

			frame = 0;
			tickFunc = () =>
			{
				var d = direction();
				if (d > 0 && ++frame >= CurrentSequence.Length)
					frame = 0;

				if (d < 0 && --frame < 0)
					frame = CurrentSequence.Length - 1;
			};
		}

		public void Tick()
		{
			if (paused == null || !paused())
				Tick(40); // tick one frame
		}

		public void Tick(int t)
		{
			if (tickAlways)
				tickFunc?.Invoke();
			else
			{
				// PERF: A still image (most units standing, every tree and wall) or a finished animation has nothing to advance
				if (tickFunc == null || IsStill)
					return;

				timeUntilNextFrame -= t;
				while (timeUntilNextFrame <= 0)
				{
					tickFunc?.Invoke();
					timeUntilNextFrame += CurrentSequenceTickOrDefault();
				}
			}
		}

		public void ChangeImage(string newImage, string newAnimIfMissing)
		{
			newImage = newImage.ToLowerInvariant();

			if (Name != newImage)
			{
				Name = newImage;
				if (!ReplaceAnim(CurrentSequence.Name))
					ReplaceAnim(newAnimIfMissing);
			}
		}

		public bool HasSequence(string seq) { return sequences.HasSequence(Name, seq); }

		public ISpriteSequence GetSequence(string sequenceName)
		{
			return sequences.GetSequence(Name, sequenceName);
		}

		public string GetRandomExistingSequence(string[] sequences, MersenneTwister random)
		{
			return sequences.Where(s => HasSequence(s)).RandomOrDefault(random);
		}
	}
}
