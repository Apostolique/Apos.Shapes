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
    // Band block, 2 * Bands * (1 + MaxCurves) texels, .rg used:
    //   [0, Bands)              horizontal band headers, one per band row.
    //   [Bands, 2 * Bands)      vertical band headers, one per band column.
    //   the rest                one curve list per header, in header order, each exactly
    //                           MaxCurves texels long.
    // A header is (curve count, curve list offset). The offset is a linear texel index into
    // the band texture and lands absolute only once the glyph seats; here it is relative to
    // the block. A list entry holds a relative linear index into the glyph's curve block in
    // .r, which seating resolves into the 2D texel coordinate the shader fetches.
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
        // texel. Must match MAX_BAND_CURVES in the shader.
        internal const int MaxCurves = 16;

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
            // Narrower bands can only drop curves from a list, never add them, so doubling
            // until the longest list fits is what guarantees the shader's fixed loop covers
            // every band. Band membership does not depend on the order the curves are in, so
            // the trial runs before the sorts do.
            while (Longest(curves, bands, x1, y1, sizeX, sizeY) > maxCurves && bands < MaxBands) {
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

            int heads = bands * 2;
            var bandTexels = new float[heads * (1 + maxCurves) * 4];

            // Horizontal bands cut the glyph's Y extent into strips and the shader solves for
            // where each curve crosses the sample's scanline, so the lists are sorted by
            // descending max x: the curves that can still cover the sample come first, and a
            // list that has to lose its tail loses the curves furthest to the left, which are
            // the ones whose coverage and weight terms clamp to zero anyway.
            curves.Sort(ByMaxX);
            int at = heads;
            for (int b = 0; b < bands; b++) {
                float lo = y1 + b * dimY;
                float hi = lo + dimY;
                at = Fill(g, bandTexels, b, at, curves, lo, hi, horizontal: true, maxCurves: maxCurves);
            }
            // Vertical bands cut the X extent and the shader swaps the axes to reuse the same
            // solver, so the same argument runs on Y.
            curves.Sort(ByMaxY);
            for (int b = 0; b < bands; b++) {
                float lo = x1 + b * dimX;
                float hi = lo + dimX;
                at = Fill(g, bandTexels, bands + b, at, curves, lo, hi, horizontal: false, maxCurves: maxCurves);
            }
            g.BandTexels = bandTexels;
            return g;
        }

        // Writes one band's header and its padded curve list, and returns where the next list
        // starts. Offsets are relative to the block; seating makes them absolute.
        private static int Fill(
            BakedGlyph g, float[] texels, int band, int at, List<GlyphCurve> curves,
            float lo, float hi, bool horizontal, int maxCurves) {

            int count = 0;
            int over = 0;
            foreach (GlyphCurve c in curves) {
                if (!Crosses(c, lo, hi, horizontal)) continue;
                if (count == maxCurves) {
                    over++;
                    continue;
                }
                Write(texels, at + count, new Vector2(c.Texel, 0f), Vector2.Zero);
                count++;
            }
            if (over > 0) g.Clamped++;
            for (int i = count; i < maxCurves; i++) {
                Write(texels, at + i, Vector2.Zero, Vector2.Zero);
            }
            Write(texels, band, new Vector2(count, at), Vector2.Zero);
            return at + maxCurves;
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

        // The longest curve list any of the 2 * bands bands would hold.
        private static int Longest(List<GlyphCurve> curves, int bands, int x1, int y1, int sizeX, int sizeY) {
            int dimX = (sizeX + bands - 1) / bands;
            int dimY = (sizeY + bands - 1) / bands;
            int longest = 0;
            for (int b = 0; b < bands; b++) {
                float lo = y1 + b * dimY;
                int n = 0;
                foreach (GlyphCurve c in curves) {
                    if (Crosses(c, lo, lo + dimY, horizontal: true)) n++;
                }
                if (n > longest) longest = n;
            }
            for (int b = 0; b < bands; b++) {
                float lo = x1 + b * dimX;
                int n = 0;
                foreach (GlyphCurve c in curves) {
                    if (Crosses(c, lo, lo + dimX, horizontal: false)) n++;
                }
                if (n > longest) longest = n;
            }
            return longest;
        }

        private static readonly Comparison<GlyphCurve> ByMaxX =
            (a, b) => MathF.Max(MathF.Max(b.P1.X, b.P2.X), b.P3.X)
                .CompareTo(MathF.Max(MathF.Max(a.P1.X, a.P2.X), a.P3.X));
        private static readonly Comparison<GlyphCurve> ByMaxY =
            (a, b) => MathF.Max(MathF.Max(b.P1.Y, b.P2.Y), b.P3.Y)
                .CompareTo(MathF.Max(MathF.Max(a.P1.Y, a.P2.Y), a.P3.Y));

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
