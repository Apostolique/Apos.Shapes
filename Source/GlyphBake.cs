// Turns a glyph outline into the two texel blocks the Slug pixel loops read.
// Derived from Forme (MIT, Christopher Whitley), itself derived from the Slug reference
// shaders (Eric Lengyel, MIT/Apache-2.0, patent dedicated to the public domain 2026-03-17).
// See THIRD_PARTY_NOTICES.md.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    // One quadratic Bezier of an outline, in the font's design units. TrueType draws a contour
    // from on curve points with implied off curve midpoints, and a straight run is a quadratic
    // whose control point sits at the midpoint of its ends, so every segment of every contour
    // reaches the shader as the same primitive and the solver only ever sees one degree.
    internal struct GlyphCurve {
        internal Vector2 P1;
        internal Vector2 P2;
        internal Vector2 P3;
        // Where this curve's first texel sits in the glyph's own curve block, assigned in
        // outline order before the band sorts shuffle the list.
        internal int Texel;
    }

    // A glyph baked into the two texel blocks the shader addresses, plus everything a quad and
    // a line of text need to place it. Pure CPU data: nothing here touches a graphics device,
    // and a GlyphTable is what turns it into texels at a fixed place in the two textures.
    //
    // Band block, 4 * Bands * (1 + MaxCurves) texels, .rg used:
    //   [0, 2 * Bands)          horizontal band headers, two per band row.
    //   [2 * Bands, 4 * Bands)  vertical band headers, two per band column.
    //   the rest                one curve list per header, in header order, each exactly
    //                           MaxCurves texels long.
    // Each band is split in two at a line of the other axis's band grid. The first of its two
    // headers is for the samples before the split, which cast their ray backwards, the second
    // for the ones after it, which cast it forwards. A header is (curve count, curve list
    // offset), and the first header's count also carries the split, as the band index it falls
    // before, times 32. The offset is a linear texel index into the band texture and lands
    // absolute only once the glyph seats; here it is relative to the block. A list entry holds
    // a relative linear index into the glyph's curve block in .r, which seating resolves into
    // the 2D texel coordinate the shader fetches.
    //
    // Curve block, 2 * (1 + curve count) texels:
    //   texels 0 and 1          the pad curve every short list is filled out with.
    //   texels 2i + 2, 2i + 3   curve i as (p1.xy, p2.xy) then (p3.xy, 0, 0).
    // Control points are in em units. The shader's second fetch reads the texel right after
    // the first, so a curve's two texels have to share a row; every block is an even number of
    // texels and every arena is an even number of texels wide, which puts every curve on an
    // even column and makes that free.
    internal sealed class BakedGlyph {
        internal BakedGlyph(int glyph) {
            Glyph = glyph;
        }

        // The font's own glyph index, which is what kerning and the registry key on.
        internal readonly int Glyph;

        // Horizontal advance and left side bearing in design units, straight off the font.
        internal int Advance;
        internal int Bearing;
        // The outline's bounding box in design units, and the same box in em units, which is
        // what the quad's corners carry as their sample coordinates.
        internal int X1;
        internal int Y1;
        internal int X2;
        internal int Y2;
        internal Vector2 Min;
        internal Vector2 Max;
        // Set on a side the quad should end exactly at, with no anti aliasing margin, because
        // another quad starts there. A drawing's tiles meet on these edges.
        internal bool SeamMinX;
        internal bool SeamMaxX;
        internal bool SeamMinY;
        internal bool SeamMaxY;

        // Bands per axis. Zero means the glyph has no outline to draw: a space, a code point
        // the font has no glyph for, or an outline this baker cannot express.
        internal int Bands;
        // Em coordinate to band index space: bandPos = em * xy + zw, floored and clamped to
        // [0, Bands - 1] by the shader.
        internal Vector4 Transform;

        internal float[] BandTexels = Array.Empty<float>();
        internal float[] CurveTexels = Array.Empty<float>();
        // Bands whose curve list ran past MaxCurves and lost its tail. The sort is what makes
        // that survivable, so this is a quality statistic rather than an error.
        internal int Clamped;
        // The list length every band is padded out to, which has to match the shader's
        // MAX_BAND_CURVES for the padding to mean anything.
        internal int MaxCurves;

        internal bool HasOutline => Bands > 0;
        internal int HeaderCount => Bands * 4;
        // Baked with the long lists, which only the shader versions with dense text read.
        internal bool Dense => MaxCurves > GlyphBake.MaxCurves;
        internal int BandTexelCount => BandTexels.Length >> 2;
        internal int CurveTexelCount => CurveTexels.Length >> 2;

        // The entry this glyph occupies in the given table right now, stamped as about to pack.
        // A glyph holds nothing of its own about where it sits, so one font can back any number
        // of batches at once without the two of them fighting over one answer.
        internal int Seat(GlyphTable table) => table.Resolve(this);

        // Whether the table can seat this glyph without evicting one an undrawn quad still
        // needs. Seating is the check, the same way a ramp's is.
        internal bool TryPin(GlyphTable table) => Seat(table) >= 0;
    }

    internal static class GlyphBake {
        // The shader fetches every lane of a fixed length loop and masks by the header's count,
        // so every list is padded out to exactly this and the dead lanes still land on a real
        // texel. Must match MAX_BAND_CURVES in the shader. Both list lengths stay under 32,
        // which is what the split is packed above in a header's count.
        internal const int MaxCurves = 8;
        // The lists a glyph too intricate for MaxCurves bakes with instead, which some symbol and
        // CJK glyphs and dense SVG drawings are. Must match DENSE_BAND_CURVES in the shader.
        internal const int DenseCurves = 16;

        // Bands per axis never goes past this. The band block grows as 2 * bands * (1 +
        // MaxCurves) texels, so the ceiling is what keeps one difficult glyph from taking a
        // texture row to itself.
        private const int MaxBands = 64;

        // The curve every short list is padded out with. All three control points sit an em and
        // a half below and left of the em square, so the sign test in RootEligibility zeroes both
        // roots on top of the count mask, and the pad is a real parabola rather than a point:
        // a control point off the line between the ends keeps the second difference away from
        // zero, so the solver's reciprocals stay small and no dead lane can produce an
        // infinity for a zero mask to fail to kill.
        //
        // 1.5 ems out rather than a thousand because the KNI repack stores control points as
        // fixed point over [-2, 2] and a pad outside that would clamp. It only has to sit past
        // every sample the quad can reach, and a quad covers the outline's box plus an anti
        // aliasing margin, so a fifth of an em past the deepest descender is already generous.
        private static readonly Vector2 PadP1 = new Vector2(-1.5f, -1.5f);
        private static readonly Vector2 PadP2 = new Vector2(-1.52f, -1.52f);
        private static readonly Vector2 PadP3 = new Vector2(-1.5f, -1.5f);

        // Bakes an outline, given in design units in outline order, against the box and metrics
        // the font reported for it. The curve list is sorted in place.
        internal static BakedGlyph Bake(
            List<GlyphCurve> curves, int glyph, int advance, int bearing,
            int x1, int y1, int x2, int y2, int unitsPerEm, int maxCurves) {

            var g = new BakedGlyph(glyph) {
                Advance = advance,
                Bearing = bearing,
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                MaxCurves = maxCurves,
            };
            float em = unitsPerEm;
            g.Min = new Vector2(x1 / em, y1 / em);
            g.Max = new Vector2(x2 / em, y2 / em);
            if (curves.Count == 0) return g;

            // A control point sitting on either end makes the quadratic a straight line
            // through a doubled point, which is a second difference of zero on both axes and a
            // solver that has to fall back to its linear form. Pulling it to the midpoint
            // leaves the same curve with a well conditioned parameterization.
            for (int i = 0; i < curves.Count; i++) {
                GlyphCurve c = curves[i];
                if (c.P2 == c.P1 || c.P2 == c.P3) {
                    c.P2 = (c.P1 + c.P3) * 0.5f;
                    curves[i] = c;
                }
            }

            // Texel 0 of the block is the pad curve, so the outline starts at texel 2.
            for (int i = 0; i < curves.Count; i++) {
                GlyphCurve c = curves[i];
                c.Texel = (i + 1) * 2;
                curves[i] = c;
            }

            var curveTexels = new float[(curves.Count + 1) * 2 * 4];
            Write(curveTexels, 0, PadP1, PadP2);
            Write(curveTexels, 1, PadP3, Vector2.Zero);
            for (int i = 0; i < curves.Count; i++) {
                GlyphCurve c = curves[i];
                Write(curveTexels, c.Texel, Em(c.P1, em), Em(c.P2, em));
                Write(curveTexels, c.Texel + 1, Em(c.P3, em), Vector2.Zero);
            }
            g.CurveTexels = curveTexels;

            // The box is measured inclusive of both edges, which is what makes a glyph one
            // design unit tall still get a band of nonzero height.
            int sizeX = x2 - x1 + 1;
            int sizeY = y2 - y1 + 1;

            // Forme's heuristic: half the shorter side in design units, capped at 16. For any
            // real font that is 16 for everything but a glyph a few dozen units across.
            int bands = Math.Max(1, Math.Min(16, Math.Min(sizeX, sizeY) / 2));
            int margin = Margin(unitsPerEm);
            // Narrower bands can only drop curves from a list, never add them, so doubling
            // until the longest list fits is what guarantees the shader's fixed loop covers
            // every band. Band membership does not depend on the order the curves are in, so
            // the trial runs before the sorts do.
            while (Longest(curves, bands, x1, y1, sizeX, sizeY, margin) > maxCurves && bands < MaxBands) {
                bands = Math.Min(MaxBands, bands * 2);
            }

            int dimX = (sizeX + bands - 1) / bands;
            int dimY = (sizeY + bands - 1) / bands;
            g.Bands = bands;
            // Forme builds the transform out of one reciprocal per axis, which is the number
            // the em scale multiplies through: bandPos = (design - origin) / dim.
            float sx = 1f / MathF.Max(1f, dimX);
            float sy = 1f / MathF.Max(1f, dimY);
            g.Transform = new Vector4(em * sx, em * sy, -x1 * sx, -y1 * sy);

            int heads = bands * 4;
            var bandTexels = new float[heads * (1 + maxCurves) * 4];

            // Horizontal bands cut the glyph's Y extent into strips and the shader solves for
            // where each curve crosses the sample's scanline. Each is split along X at a line of
            // the vertical band grid, which is where the shader can tell the two halves apart.
            // Vertical bands cut the X extent and the shader swaps the axes to reuse the same
            // solver, so the same runs on Y.
            int at = heads;
            var buckets = new List<GlyphCurve>[bands];
            var mins = new float[curves.Count];
            var maxs = new float[curves.Count];
            Bucket(curves, true, y1, dimY, bands, buckets);
            for (int b = 0; b < bands; b++) {
                at = Fill(g, bandTexels, 2 * b, at, buckets[b], mins, maxs, horizontal: true, x1, dimX, bands, margin, maxCurves);
            }
            Bucket(curves, false, x1, dimX, bands, buckets);
            for (int b = 0; b < bands; b++) {
                at = Fill(g, bandTexels, 2 * (bands + b), at, buckets[b], mins, maxs, horizontal: false, y1, dimY, bands, margin, maxCurves);
            }
            g.BandTexels = bandTexels;
            return g;
        }

        // How far past its split a half's list reaches. A sample's antialiasing reads the curves
        // within half a pixel of it, so a sample next to the split needs a few from the other
        // side. Half a pixel at 8 pixels per em covers that for any text worth reading; below
        // that, a sample within a fraction of a pixel of the split can miss a curve.
        private static int Margin(int unitsPerEm) => Math.Max(1, unitsPerEm / 16);

        // Where a band splits, as the index of the band of the other axis it falls before, and
        // how many curves each half then holds. The split that leaves the longer half shortest
        // wins, nearest the middle on a tie. A split at either end leaves one half no sample
        // reads, so that half doesn't count.
        // The candidates go by in order, so each half's count comes off the sorted extents with a
        // pointer that only moves forward. mins and maxs are scratch, at least members long.
        private static (int Split, int Before, int After) Split(List<GlyphCurve> members, bool horizontal, float start, int dim, int bands, int margin, float[] mins, float[] maxs) {
            int n = members.Count;
            for (int i = 0; i < n; i++) {
                mins[i] = Min(members[i], horizontal);
                maxs[i] = Max(members[i], horizontal);
            }
            Array.Sort(mins, 0, n);
            Array.Sort(maxs, 0, n);
            int bestSplit = 0, bestBefore = 0, bestAfter = n, bestCost = int.MaxValue;
            int before = 0, behind = 0;
            for (int k = 0; k <= bands; k++) {
                float at = start + k * dim;
                while (before < n && mins[before] <= at + margin) before++;
                while (behind < n && maxs[behind] < at - margin) behind++;
                int after = n - behind;
                int cost = Math.Max(k > 0 ? before : 0, k < bands ? after : 0);
                if (cost < bestCost || (cost == bestCost && Math.Abs(2 * k - bands) < Math.Abs(2 * bestSplit - bands))) {
                    bestSplit = k;
                    bestBefore = before;
                    bestAfter = after;
                    bestCost = cost;
                }
            }
            return (bestSplit, bestBefore, bestAfter);
        }

        // The extent of a curve along the axis its band's rays run, which is x for a horizontal
        // band.
        private static float Min(in GlyphCurve c, bool horizontal) {
            return horizontal ? MathF.Min(MathF.Min(c.P1.X, c.P2.X), c.P3.X) : MathF.Min(MathF.Min(c.P1.Y, c.P2.Y), c.P3.Y);
        }
        private static float Max(in GlyphCurve c, bool horizontal) {
            return horizontal ? MathF.Max(MathF.Max(c.P1.X, c.P2.X), c.P3.X) : MathF.Max(MathF.Max(c.P1.Y, c.P2.Y), c.P3.Y);
        }

        // Writes one band's two headers and their padded curve lists, and returns where the next
        // list starts. members are the band's curves, which it sorts. Offsets are relative to the
        // block; seating makes them absolute.
        private static int Fill(
            BakedGlyph g, float[] texels, int header, int at, List<GlyphCurve> members, float[] mins, float[] maxs,
            bool horizontal, float splitStart, int splitDim, int bands, int margin, int maxCurves) {

            var (split, _, _) = Split(members, horizontal, splitStart, splitDim, bands, margin, mins, maxs);
            float splitAt = splitStart + split * splitDim;
            bool over = false;

            // Before the split, rays run backwards, so the list keeps the curves whose near end
            // comes first: one that has to lose its tail loses the ones furthest along, which
            // only the samples right at the split reach.
            members.Sort((a, b) => Min(a, horizontal).CompareTo(Min(b, horizontal)));
            int count = 0;
            foreach (GlyphCurve c in members) {
                if (Min(c, horizontal) > splitAt + margin) continue;
                if (count == maxCurves) {
                    over |= split > 0;
                    continue;
                }
                Write(texels, at + count, new Vector2(c.Texel, 0f), Vector2.Zero);
                count++;
            }
            for (int i = count; i < maxCurves; i++) Write(texels, at + i, Vector2.Zero, Vector2.Zero);
            Write(texels, header, new Vector2(count + 32 * split, at), Vector2.Zero);
            at += maxCurves;

            // After it they run forwards, so the curves whose far end comes last go first, and
            // a list that has to lose its tail loses the ones furthest back, whose coverage and
            // weight terms clamp to zero anyway.
            members.Sort((a, b) => Max(b, horizontal).CompareTo(Max(a, horizontal)));
            count = 0;
            foreach (GlyphCurve c in members) {
                if (Max(c, horizontal) < splitAt - margin) continue;
                if (count == maxCurves) {
                    over |= split < bands;
                    continue;
                }
                Write(texels, at + count, new Vector2(c.Texel, 0f), Vector2.Zero);
                count++;
            }
            for (int i = count; i < maxCurves; i++) Write(texels, at + i, Vector2.Zero, Vector2.Zero);
            Write(texels, header + 1, new Vector2(count, at), Vector2.Zero);
            at += maxCurves;

            if (over) g.Clamped++;
            return at;
        }

        // Whether a curve belongs in a band. A curve that runs flat along the band's own axis
        // can never cross the sample's line, so it is dropped rather than counted. The overlap
        // test takes both boundaries, which is what puts a curve that ends exactly on one into
        // both of the bands it touches.
        private static bool Crosses(in GlyphCurve c, float lo, float hi, bool horizontal) {
            float a = horizontal ? c.P1.Y : c.P1.X;
            float b = horizontal ? c.P2.Y : c.P2.X;
            float d = horizontal ? c.P3.Y : c.P3.X;
            if (a == b && b == d) return false;
            return MathF.Min(MathF.Min(a, b), d) <= hi && MathF.Max(MathF.Max(a, b), d) >= lo;
        }

        // The longest curve list any half of the 2 * bands bands would hold, each split the
        // way Fill splits it.
        private static int Longest(List<GlyphCurve> curves, int bands, int x1, int y1, int sizeX, int sizeY, int margin) {
            int dimX = (sizeX + bands - 1) / bands;
            int dimY = (sizeY + bands - 1) / bands;
            int longest = 0;
            var buckets = new List<GlyphCurve>[bands];
            var mins = new float[curves.Count];
            var maxs = new float[curves.Count];
            for (int axis = 0; axis < 2; axis++) {
                bool horizontal = axis == 0;
                Bucket(curves, horizontal, horizontal ? y1 : x1, horizontal ? dimY : dimX, bands, buckets);
                for (int b = 0; b < bands; b++) {
                    var (split, before, after) = Split(buckets[b], horizontal, horizontal ? x1 : y1, horizontal ? dimX : dimY, bands, margin, mins, maxs);
                    longest = Math.Max(longest, Math.Max(split > 0 ? before : 0, split < bands ? after : 0));
                }
            }
            return longest;
        }

        // Each band's curves, which are the ones Crosses puts in it. A curve can only cross the
        // bands its extent spans, so it is only tried against those.
        private static void Bucket(List<GlyphCurve> curves, bool horizontal, float start, int dim, int bands, List<GlyphCurve>[] buckets) {
            for (int b = 0; b < bands; b++) {
                if (buckets[b] == null) buckets[b] = new List<GlyphCurve>();
                else buckets[b].Clear();
            }
            foreach (GlyphCurve c in curves) {
                float lo = horizontal ? MathF.Min(MathF.Min(c.P1.Y, c.P2.Y), c.P3.Y) : MathF.Min(MathF.Min(c.P1.X, c.P2.X), c.P3.X);
                float hi = horizontal ? MathF.Max(MathF.Max(c.P1.Y, c.P2.Y), c.P3.Y) : MathF.Max(MathF.Max(c.P1.X, c.P2.X), c.P3.X);
                int first = Math.Max(0, (int)MathF.Floor((lo - start) / dim) - 1);
                int last = Math.Min(bands - 1, (int)MathF.Floor((hi - start) / dim) + 1);
                for (int b = first; b <= last; b++) {
                    float bandLo = start + b * dim;
                    if (Crosses(c, bandLo, bandLo + dim, horizontal)) buckets[b].Add(c);
                }
            }
        }

        // Design units to em units, a division per component rather than the multiply by a
        // reciprocal a Vector2 divide would do: the reciprocal of a units per em is not exact,
        // and the whole point of one shared unit is that two ways of reaching it agree.
        private static Vector2 Em(Vector2 p, float unitsPerEm) {
            return new Vector2(p.X / unitsPerEm, p.Y / unitsPerEm);
        }

        private static void Write(float[] texels, int texel, Vector2 rg, Vector2 ba) {
            int i = texel * 4;
            texels[i] = rg.X;
            texels[i + 1] = rg.Y;
            texels[i + 2] = ba.X;
            texels[i + 3] = ba.Y;
        }
    }
}
