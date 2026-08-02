// One element's geometry: every segment kind SVG can draw, turned into the quadratics the
// glyph baker takes, with the element's transform folded in on the way out.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    // One quadratic in document units. Straight runs get a control point at the midpoint, which
    // is the same shape with a parameterization the solver is happy with, and the convention
    // GlyphBake already uses for a TrueType line.
    internal struct SvgQuad {
        internal Vector2 P1;
        internal Vector2 P2;
        internal Vector2 P3;
    }

    // A path under construction. Points go in in the element's own coordinates and come out in
    // document coordinates: the transform is applied per control point, which is exact for every
    // segment kind here since an affine map takes a Bezier's control points to the mapped
    // curve's. Curve subdivision runs before the transform, so the tolerance is divided by how
    // far the transform can stretch a unit vector and still means what it says at the end.
    internal sealed class SvgOutline {
        internal SvgOutline(in SvgMatrix m, float tol) {
            _m = m;
            _tol = MathF.Max(tol / MathF.Max(m.MaxScale, 1e-9f), 1e-9f);
        }

        private readonly SvgMatrix _m;
        private readonly float _tol;

        internal readonly List<SvgQuad> Quads = new();
        // One entry per subpath: where its quads start, how many, and whether a Z closed it.
        internal readonly List<int> Starts = new();
        internal readonly List<int> Counts = new();
        internal readonly List<bool> Closed = new();

        // The geometry's box in the element's own coordinates, which is what an
        // objectBoundingBox gradient is laid out against.
        internal Vector2 LocalMin = new(float.MaxValue, float.MaxValue);
        internal Vector2 LocalMax = new(float.MinValue, float.MinValue);
        internal bool HasBox => LocalMin.X <= LocalMax.X;

        internal Vector2 Current;
        internal Vector2 SubStart;
        // The control point the next smooth segment reflects, and which kind it came from.
        internal Vector2 LastCubic;
        internal Vector2 LastQuad;
        internal bool AfterCubic;
        internal bool AfterQuad;

        private int _open = -1;

        internal int SubpathCount => Starts.Count;

        internal void MoveTo(Vector2 p) {
            EndSubpath();
            Current = p;
            SubStart = p;
            AfterCubic = false;
            AfterQuad = false;
            _open = Quads.Count;
            Starts.Add(_open);
            Counts.Add(0);
            Closed.Add(false);
            Grow(p);
        }

        internal void LineTo(Vector2 p) {
            if (_open < 0) MoveTo(Current);
            AfterCubic = false;
            AfterQuad = false;
            if (p == Current) return;
            Emit(Current, (Current + p) * 0.5f, p);
            Current = p;
        }

        internal void QuadTo(Vector2 c, Vector2 p) {
            if (_open < 0) MoveTo(Current);
            LastQuad = c;
            AfterQuad = true;
            AfterCubic = false;
            if (p == Current && c == Current) return;
            Emit(Current, c, p);
            Current = p;
        }

        internal void CubicTo(Vector2 c1, Vector2 c2, Vector2 p) {
            if (_open < 0) MoveTo(Current);
            LastCubic = c2;
            AfterCubic = true;
            AfterQuad = false;
            if (p == Current && c1 == Current && c2 == Current) return;
            Cubic(Current, c1, c2, p);
            Current = p;
        }

        // Elliptical arc in the endpoint parameterization SVG writes it in. Everything about it
        // is the spec's F.6.5, including the out of range radii the spec says to scale up rather
        // than reject.
        internal void ArcTo(float rx, float ry, float rotation, bool largeArc, bool sweep, Vector2 p) {
            if (_open < 0) MoveTo(Current);
            AfterCubic = false;
            AfterQuad = false;
            Vector2 p0 = Current;
            if (p == p0) return;
            rx = MathF.Abs(rx);
            ry = MathF.Abs(ry);
            if (rx < 1e-9f || ry < 1e-9f) {
                LineTo(p);
                return;
            }

            float phi = rotation * (MathF.PI / 180f);
            float cosPhi = MathF.Cos(phi);
            float sinPhi = MathF.Sin(phi);
            Vector2 d = (p0 - p) * 0.5f;
            float x1 = cosPhi * d.X + sinPhi * d.Y;
            float y1 = -sinPhi * d.X + cosPhi * d.Y;

            float lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
            bool grown = lambda > 1f;
            if (grown) {
                float grow = MathF.Sqrt(lambda);
                rx *= grow;
                ry *= grow;
            }

            float rxs = rx * rx;
            float rys = ry * ry;
            float denom = rxs * y1 * y1 + rys * x1 * x1;
            float num = MathF.Max(rxs * rys - denom, 0f);
            // Radii scaled up to reach the endpoints put the center exactly on the chord's
            // midpoint. Reading that out of the difference of two products instead would take the
            // square root of the last few bits of a cancellation and move the whole arc.
            float coef = grown || denom <= 0f ? 0f : MathF.Sqrt(num / denom);
            if (largeArc == sweep) coef = -coef;
            float cx1 = coef * rx * y1 / ry;
            float cy1 = -coef * ry * x1 / rx;
            var center = new Vector2(
                cosPhi * cx1 - sinPhi * cy1 + (p0.X + p.X) * 0.5f,
                sinPhi * cx1 + cosPhi * cy1 + (p0.Y + p.Y) * 0.5f);

            var u = new Vector2((x1 - cx1) / rx, (y1 - cy1) / ry);
            var w = new Vector2((-x1 - cx1) / rx, (-y1 - cy1) / ry);
            float theta = MathF.Atan2(u.Y, u.X);
            float sweepAngle = MathF.Atan2(w.Y, w.X) - theta;
            if (!sweep && sweepAngle > 0f) sweepAngle -= MathF.Tau;
            if (sweep && sweepAngle < 0f) sweepAngle += MathF.Tau;
            if (sweepAngle == 0f) {
                LineTo(p);
                return;
            }

            // A quadratic through a sub arc's ends with its control point where the two tangents
            // meet sits (1 - cos h)^2 / (2 cos h) radii proud of the arc at its midpoint, for a
            // half angle h, and the ellipse's longer radius is what that scales by once the
            // circle frame is mapped out. The measured worst case runs about 15% over that, so
            // the budget it has to fit inside is the tolerance with room left.
            float allow = _tol / MathF.Max(rx, ry) * 0.6f;
            int pieces = (int)MathF.Ceiling(MathF.Abs(sweepAngle) / (MathF.PI * 0.5f));
            if (pieces < 1) pieces = 1;
            while (pieces < 256 && ArcError(MathF.Abs(sweepAngle) / pieces * 0.5f) > allow) {
                pieces++;
            }

            float step = sweepAngle / pieces;
            float half = step * 0.5f;
            float scale = 1f / MathF.Cos(half);
            Vector2 from = p0;
            for (int i = 0; i < pieces; i++) {
                float a0 = theta + step * i;
                float a1 = a0 + step;
                float mid = a0 + half;
                Vector2 c = OnEllipse(center, rx, ry, cosPhi, sinPhi,
                                      MathF.Cos(mid) * scale, MathF.Sin(mid) * scale);
                Vector2 to = i == pieces - 1
                    ? p
                    : OnEllipse(center, rx, ry, cosPhi, sinPhi, MathF.Cos(a1), MathF.Sin(a1));
                Emit(from, c, to);
                from = to;
            }
            Current = p;
        }

        private static float ArcError(float half) {
            float c = MathF.Cos(half);
            if (c <= 1e-6f) return float.MaxValue;
            float k = 1f - c;
            return k * k / (2f * c);
        }

        private static Vector2 OnEllipse(Vector2 center, float rx, float ry, float cosPhi, float sinPhi, float cx, float cy) {
            float x = rx * cx;
            float y = ry * cy;
            return new Vector2(center.X + cosPhi * x - sinPhi * y, center.Y + sinPhi * x + cosPhi * y);
        }

        internal void Close() {
            if (_open < 0) return;
            if (Current != SubStart) {
                Emit(Current, (Current + SubStart) * 0.5f, SubStart);
            }
            Counts[Counts.Count - 1] = Quads.Count - _open;
            Closed[Closed.Count - 1] = true;
            Current = SubStart;
            AfterCubic = false;
            AfterQuad = false;
            _open = -1;
        }

        internal void Finish() {
            EndSubpath();
            // A moveto with nothing after it draws nothing and strokes nothing.
            for (int i = Starts.Count - 1; i >= 0; i--) {
                if (Counts[i] != 0) continue;
                Starts.RemoveAt(i);
                Counts.RemoveAt(i);
                Closed.RemoveAt(i);
            }
        }

        private void EndSubpath() {
            if (_open < 0) return;
            Counts[Counts.Count - 1] = Quads.Count - _open;
            _open = -1;
        }

        // A cubic as a run of quadratics. Splitting a cubic into k equal pieces divides its third
        // difference by k^3, and a piece's midpoint quadratic sits sqrt(3) / 18 of that third
        // difference off the cubic, so the piece count comes straight out of the tolerance with
        // nothing left to iterate.
        private void Cubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3) {
            Vector2 third = p3 - p2 * 3f + p1 * 3f - p0;
            float err = 0.09622504f * third.Length();
            int k = 1;
            if (err > _tol) {
                k = (int)MathF.Ceiling(MathF.Cbrt(err / _tol));
                k = Math.Clamp(k, 1, 64);
            }
            float d = 1f / k;
            for (int i = 0; i < k; i++) {
                float t0 = i * d;
                float t1 = i == k - 1 ? 1f : (i + 1) * d;
                Vector2 q0 = i == 0 ? p0 : At(p0, p1, p2, p3, t0);
                Vector2 q3 = i == k - 1 ? p3 : At(p0, p1, p2, p3, t1);
                Vector2 q1 = q0 + Slope(p0, p1, p2, p3, t0) * (d / 3f);
                Vector2 q2 = q3 - Slope(p0, p1, p2, p3, t1) * (d / 3f);
                Emit(q0, (q1 * 3f - q0 + q2 * 3f - q3) * 0.25f, q3);
            }
        }

        private static Vector2 At(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t) {
            float u = 1f - t;
            return p0 * (u * u * u) + p1 * (3f * u * u * t) + p2 * (3f * u * t * t) + p3 * (t * t * t);
        }
        private static Vector2 Slope(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t) {
            float u = 1f - t;
            return (p1 - p0) * (3f * u * u) + (p2 - p1) * (6f * u * t) + (p3 - p2) * (3f * t * t);
        }

        private void Emit(Vector2 p1, Vector2 p2, Vector2 p3) {
            Grow(p1, p2, p3);
            Quads.Add(new SvgQuad { P1 = _m.Apply(p1), P2 = _m.Apply(p2), P3 = _m.Apply(p3) });
        }

        private void Grow(Vector2 p) {
            LocalMin = Vector2.Min(LocalMin, p);
            LocalMax = Vector2.Max(LocalMax, p);
        }

        // The curve's own box rather than its control hull: a quadratic turns at most once per
        // axis, and where it turns is one divide.
        private void Grow(Vector2 p1, Vector2 p2, Vector2 p3) {
            Grow(p1);
            Grow(p3);
            GrowAxis(p1.X, p2.X, p3.X, ref LocalMin.X, ref LocalMax.X);
            GrowAxis(p1.Y, p2.Y, p3.Y, ref LocalMin.Y, ref LocalMax.Y);
        }

        private static void GrowAxis(float a, float b, float c, ref float lo, ref float hi) {
            float den = a - 2f * b + c;
            if (den == 0f) return;
            float t = (a - b) / den;
            if (!(t > 0f) || !(t < 1f)) return;
            float u = 1f - t;
            float v = u * u * a + 2f * u * t * b + t * t * c;
            if (v < lo) lo = v;
            if (v > hi) hi = v;
        }

        // Whether every control point is a number. A transform can carry a coordinate past what a
        // float holds, and the differences taken after that are not numbers at all.
        internal bool Finite {
            get {
                foreach (SvgQuad q in Quads) {
                    if (!float.IsFinite(q.P1.X) || !float.IsFinite(q.P1.Y)
                        || !float.IsFinite(q.P2.X) || !float.IsFinite(q.P2.Y)
                        || !float.IsFinite(q.P3.X) || !float.IsFinite(q.P3.Y)) {
                        return false;
                    }
                }
                return true;
            }
        }

        // The geometry's box in document units, curve by curve rather than control point by
        // control point.
        internal void DocBox(out Vector2 min, out Vector2 max) {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            foreach (SvgQuad q in Quads) {
                min = Vector2.Min(min, Vector2.Min(q.P1, q.P3));
                max = Vector2.Max(max, Vector2.Max(q.P1, q.P3));
                GrowAxis(q.P1.X, q.P2.X, q.P3.X, ref min.X, ref max.X);
                GrowAxis(q.P1.Y, q.P2.Y, q.P3.Y, ref min.Y, ref max.Y);
            }
            if (min.X > max.X) {
                min = Vector2.Zero;
                max = Vector2.Zero;
            }
        }

        // The subpath as a polyline, close enough to it that no point of the curve is further
        // than tol away. A quadratic split into k pieces divides its second difference by k^2,
        // and half that difference is how far a quadratic strays from its own chord.
        internal void Flatten(int subpath, List<Vector2> into, float tol) {
            into.Clear();
            int start = Starts[subpath];
            int count = Counts[subpath];
            if (count == 0) return;
            into.Add(Quads[start].P1);
            for (int i = 0; i < count; i++) {
                SvgQuad q = Quads[start + i];
                float bow = ((q.P1 + q.P3) * 0.5f - q.P2).Length() * 0.5f;
                int k = 1;
                if (bow > tol) {
                    k = (int)MathF.Ceiling(MathF.Sqrt(bow / tol));
                    k = Math.Clamp(k, 1, 256);
                }
                for (int j = 1; j <= k; j++) {
                    float t = j / (float)k;
                    float u = 1f - t;
                    into.Add(q.P1 * (u * u) + q.P2 * (2f * u * t) + q.P3 * (t * t));
                }
            }
        }
    }
}
