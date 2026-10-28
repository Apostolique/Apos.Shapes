// The boxes an SVG drawing is cut to: its viewBox, the box of a nested svg or a symbol, and a
// clipPath that is one rectangle or circle.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    // A turned rectangle with round corners, the shape a ShapeBatch clip takes. Center and Half
    // are in the em frame. Angle turns the rectangle in the document's own y down frame, which
    // is the way the world turns too, and Rounding is a corner radius in ems.
    internal readonly struct SvgClip : IEquatable<SvgClip> {
        internal SvgClip(Vector2 center, Vector2 half, float angle, float rounding) {
            Center = center;
            Half = half;
            Angle = angle;
            Rounding = rounding;
        }

        internal readonly Vector2 Center;
        internal readonly Vector2 Half;
        internal readonly float Angle;
        internal readonly float Rounding;

        public bool Equals(SvgClip o) => Center == o.Center && Half == o.Half && Angle == o.Angle && Rounding == o.Rounding;
        public override bool Equals(object? obj) => obj is SvgClip c && Equals(c);
        public override int GetHashCode() => HashCode.Combine(Center, Half, Angle, Rounding);

        // An em point in the clip's own frame, where the rectangle is [-Half, Half].
        internal Vector2 Local(Vector2 em) {
            (float sin, float cos) = MathF.SinCos(Angle);
            float dx = em.X - Center.X;
            float dy = Center.Y - em.Y;
            return new Vector2(dx * cos + dy * sin, dy * cos - dx * sin);
        }

        // The way back from the clip's own frame to the em frame.
        internal Vector2 Em(Vector2 local) {
            (float sin, float cos) = MathF.SinCos(Angle);
            return new Vector2(Center.X + local.X * cos - local.Y * sin, Center.Y - local.X * sin - local.Y * cos);
        }

        private bool Contains(Vector2 em) {
            Vector2 p = Local(em);
            float r = MathF.Min(Rounding, MathF.Min(Half.X, Half.Y));
            float qx = MathF.Abs(p.X) - Half.X + r;
            float qy = MathF.Abs(p.Y) - Half.Y + r;
            float outside = new Vector2(MathF.Max(qx, 0f), MathF.Max(qy, 0f)).Length();
            return outside + MathF.Min(MathF.Max(qx, qy), 0f) - r <= 0f;
        }

        // Whether an em box is wholly inside, which leaves the clip nothing to do. The clip is
        // convex, so the four corners being in is enough.
        internal bool Covers(Vector2 min, Vector2 max) {
            return Contains(min) && Contains(max) && Contains(new Vector2(min.X, max.Y)) && Contains(new Vector2(max.X, min.Y));
        }

        // Whether an em box is wholly outside the rectangle, corners ignored, by looking for a
        // gap along the box's two axes and then the clip's.
        internal bool Misses(Vector2 min, Vector2 max) {
            (float sin, float cos) = MathF.SinCos(Angle);
            // The clip's corners in the em frame, where y points up and the turn goes the other way.
            var ux = new Vector2(cos, -sin) * Half.X;
            var uy = new Vector2(sin, cos) * -Half.Y;
            Vector2 lo = new(float.MaxValue);
            Vector2 hi = new(float.MinValue);
            foreach (Vector2 c in new[] { Center + ux + uy, Center + ux - uy, Center - ux + uy, Center - ux - uy }) {
                lo = Vector2.Min(lo, c);
                hi = Vector2.Max(hi, c);
            }
            if (hi.X < min.X || lo.X > max.X || hi.Y < min.Y || lo.Y > max.Y) return true;

            lo = new Vector2(float.MaxValue);
            hi = new Vector2(float.MinValue);
            foreach (Vector2 c in new[] { min, max, new Vector2(min.X, max.Y), new Vector2(max.X, min.Y) }) {
                Vector2 p = Local(c);
                lo = Vector2.Min(lo, p);
                hi = Vector2.Max(hi, p);
            }
            return hi.X < -Half.X || lo.X > Half.X || hi.Y < -Half.Y || lo.Y > Half.Y;
        }
    }

    // A clip in the document's own units, before the em frame is known, linked to the clips
    // around it. Every element under the same clips shares the same link.
    internal sealed class SvgClipChain {
        internal SvgClipChain(Vector2 center, Vector2 half, float angle, float rounding, SvgClipChain? outer) {
            Center = center;
            Half = half;
            Angle = angle;
            Rounding = rounding;
            Outer = outer;
        }

        internal readonly Vector2 Center;
        internal readonly Vector2 Half;
        internal readonly float Angle;
        internal readonly float Rounding;
        internal readonly SvgClipChain? Outer;

        // A rectangle with round corners through a transform, which only stays one when the
        // transform keeps right angles, and keeps the corners round only when it scales both
        // axes the same. Null when it doesn't.
        internal static SvgClipChain? From(in SvgMatrix m, float x, float y, float w, float h, float r, SvgClipChain? outer) {
            if (!(w > 0f) || !(h > 0f)) return null;
            Vector2 p0 = m.Apply(new Vector2(x, y));
            Vector2 u = m.Apply(new Vector2(x + w, y)) - p0;
            Vector2 v = m.Apply(new Vector2(x, y + h)) - p0;
            float lu = u.Length();
            float lv = v.Length();
            if (!(lu > 0f) || !(lv > 0f) || !float.IsFinite(lu) || !float.IsFinite(lv)) return null;
            if (MathF.Abs(Vector2.Dot(u, v)) > 1e-4f * lu * lv) return null;
            float rounding = 0f;
            if (r > 0f) {
                float su = lu / w;
                float sv = lv / h;
                if (MathF.Abs(su - sv) > 1e-3f * MathF.Max(su, sv)) return null;
                rounding = MathF.Min(r * su, MathF.Min(lu, lv) * 0.5f);
            }
            return new SvgClipChain(p0 + (u + v) * 0.5f, new Vector2(lu, lv) * 0.5f, MathF.Atan2(u.Y, u.X), rounding, outer);
        }
    }
}
