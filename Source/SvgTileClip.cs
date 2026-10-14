// Cuts an outline down to the part inside a box without changing which points inside the box
// are filled.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    internal static class SvgTileClip {
        // The curves of every closed contour, clipped to the box and flattened into one list.
        // A contour that never enters the box vanishes. One that leaves and comes back has the
        // stretch outside replaced by a straight chord between where it left and where it came
        // back. The chord and the stretch it replaces make a closed loop that lies entirely on
        // the far side of the box's edge, and a closed loop adds nothing to the winding number
        // of a point outside it, so every point inside the box is filled exactly as before.
        // Pass an infinite bound to leave a side alone.
        internal static List<GlyphCurve> Clip(
            List<List<GlyphCurve>> contours, float minX, float maxX, float minY, float maxY) {

            var result = new List<GlyphCurve>();
            foreach (List<GlyphCurve> contour in contours) {
                List<GlyphCurve>? c = contour;
                c = Side(c, axis: 0, minX, keepGreater: true);
                c = Side(c, axis: 0, maxX, keepGreater: false);
                c = Side(c, axis: 1, minY, keepGreater: true);
                c = Side(c, axis: 1, maxY, keepGreater: false);
                if (c != null) result.AddRange(c);
            }
            return result;
        }

        // One edge of the box. Returns the loop untouched when it is wholly inside and null
        // when none of it is.
        private static List<GlyphCurve>? Side(List<GlyphCurve>? loop, int axis, float limit, bool keepGreater) {
            if (loop == null || loop.Count == 0) return null;
            if (float.IsInfinity(limit)) return loop;

            var pieces = new List<GlyphCurve>();
            foreach (GlyphCurve c in loop) {
                Split(c, axis, limit, pieces);
            }

            int n = pieces.Count;
            var inside = new bool[n];
            int insideCount = 0;
            for (int i = 0; i < n; i++) {
                GlyphCurve p = pieces[i];
                float mid = 0.25f * Get(p.P1, axis) + 0.5f * Get(p.P2, axis) + 0.25f * Get(p.P3, axis);
                inside[i] = keepGreater ? mid >= limit : mid <= limit;
                if (inside[i]) insideCount++;
            }
            if (insideCount == n) return loop;
            if (insideCount == 0) return null;

            // Start where the loop comes back in, so every stretch outside is bracketed by a
            // piece inside on both ends, wrapping around.
            int start = 0;
            for (int i = 0; i < n; i++) {
                if (inside[i] && !inside[(i + n - 1) % n]) {
                    start = i;
                    break;
                }
            }

            var kept = new List<GlyphCurve>();
            Vector2 lastEnd = default;
            bool pendingChord = false;
            for (int k = 0; k < n; k++) {
                int i = (start + k) % n;
                if (!inside[i]) {
                    pendingChord = true;
                    continue;
                }
                GlyphCurve p = pieces[i];
                if (pendingChord && k > 0) {
                    AddChord(kept, lastEnd, p.P1);
                }
                pendingChord = false;
                kept.Add(p);
                lastEnd = p.P3;
            }
            if (pendingChord) {
                AddChord(kept, lastEnd, pieces[start].P1);
            }
            return kept;
        }

        private static void AddChord(List<GlyphCurve> into, Vector2 from, Vector2 to) {
            if (from == to) return;
            into.Add(new GlyphCurve { P1 = from, P2 = (from + to) * 0.5f, P3 = to });
        }

        // Splits a curve wherever it crosses the line, so every piece lies wholly on one side.
        // The points on the line are set to it exactly, so neighbors still share their ends.
        private static void Split(GlyphCurve c, int axis, float limit, List<GlyphCurve> into) {
            float a = Get(c.P1, axis);
            float b = Get(c.P2, axis);
            float d = Get(c.P3, axis);

            // x(t) = (1 - t)^2 a + 2 t (1 - t) b + t^2 d, set equal to the limit.
            float qa = a - 2f * b + d;
            float qb = 2f * (b - a);
            float qc = a - limit;

            Span<float> roots = stackalloc float[2];
            int count = 0;
            // A straight run has a control point at the midpoint, which leaves qa at rounding
            // noise. Dividing by that loses the root, so a curve that is nearly straight takes
            // the linear form.
            float scale = MathF.Max(MathF.Max(MathF.Abs(a), MathF.Abs(b)), MathF.Max(MathF.Abs(d), 1f));
            if (MathF.Abs(qa) <= 1e-5f * scale) {
                if (MathF.Abs(qb) > 1e-9f) {
                    float t = -qc / qb;
                    if (t > 0f && t < 1f) roots[count++] = t;
                }
            } else {
                float disc = qb * qb - 4f * qa * qc;
                if (disc >= 0f) {
                    // The cancellation free form: one root from the large term, the other from
                    // the product of the roots.
                    float s = MathF.Sqrt(disc);
                    float q = -0.5f * (qb + (qb >= 0f ? s : -s));
                    float t1 = q / qa;
                    float t2 = q != 0f ? qc / q : t1;
                    if (t1 > t2) (t1, t2) = (t2, t1);
                    if (t1 > 0f && t1 < 1f) roots[count++] = t1;
                    if (t2 > 0f && t2 < 1f && (count == 0 || t2 > roots[0])) roots[count++] = t2;
                }
            }

            if (count == 0) {
                into.Add(c);
                return;
            }

            GlyphCurve rest = c;
            float done = 0f;
            for (int i = 0; i < count; i++) {
                float t = (roots[i] - done) / (1f - done);
                Divide(rest, t, axis, limit, out GlyphCurve left, out GlyphCurve right);
                into.Add(left);
                rest = right;
                done = roots[i];
            }
            into.Add(rest);
        }

        private static void Divide(GlyphCurve c, float t, int axis, float limit, out GlyphCurve left, out GlyphCurve right) {
            Vector2 q0 = Vector2.Lerp(c.P1, c.P2, t);
            Vector2 q1 = Vector2.Lerp(c.P2, c.P3, t);
            Vector2 m = Vector2.Lerp(q0, q1, t);
            m = With(m, axis, limit);
            left = new GlyphCurve { P1 = c.P1, P2 = q0, P3 = m };
            right = new GlyphCurve { P1 = m, P2 = q1, P3 = c.P3 };
        }

        private static float Get(Vector2 v, int axis) => axis == 0 ? v.X : v.Y;

        private static Vector2 With(Vector2 v, int axis, float value) =>
            axis == 0 ? new Vector2(value, v.Y) : new Vector2(v.X, value);
    }
}
