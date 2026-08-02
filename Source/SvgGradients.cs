// The gradient elements out of defs, the href chains between them, and what one of them turns
// into once an element paints with it.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    // A linearGradient or radialGradient as it was written. Attributes stay unresolved because
    // href inheritance is per attribute: a gradient takes a value from the one it points at only
    // where it did not set that value itself, so "absent" and "set to the default" are different
    // things here.
    internal sealed class SvgGradientDef {
        internal SvgGradientDef(string id, bool radial) {
            Id = id;
            Radial = radial;
        }

        internal readonly string Id;
        internal readonly bool Radial;
        internal string? Href;
        internal readonly Dictionary<string, string> Attrs = new(StringComparer.Ordinal);
        // Null when the element had no stop children, which is what makes it inherit them.
        internal List<SvgStop>? Stops;
    }

    internal readonly struct SvgStop {
        internal SvgStop(float offset, Color color) {
            Offset = offset;
            Color = color;
        }
        internal readonly float Offset;
        internal readonly Color Color;
    }

    // Every gradient in one document, with the lookups that follow href chains. A chain that
    // loops back on itself stops at the first repeat rather than spinning.
    internal sealed class SvgGradients {
        private readonly Dictionary<string, SvgGradientDef> _byId = new(StringComparer.Ordinal);

        internal void Add(SvgGradientDef def) {
            if (def.Id.Length == 0) return;
            _byId[def.Id] = def;
        }

        internal bool Has(string id) => _byId.ContainsKey(id);

        private string? Attr(SvgGradientDef def, string name) {
            SvgGradientDef? at = def;
            for (int i = 0; at != null && i < 16; i++) {
                if (at.Attrs.TryGetValue(name, out string? v)) return v;
                at = Next(at);
            }
            return null;
        }

        private List<SvgStop>? Stops(SvgGradientDef def) {
            SvgGradientDef? at = def;
            for (int i = 0; at != null && i < 16; i++) {
                if (at.Stops != null) return at.Stops;
                at = Next(at);
            }
            return null;
        }

        private SvgGradientDef? Next(SvgGradientDef def) {
            if (def.Href == null) return null;
            if (!_byId.TryGetValue(def.Href, out SvgGradientDef? to)) return null;
            return ReferenceEquals(to, def) ? null : to;
        }

        // The gradient an element paints with, in the em frame. Returns false when the reference
        // misses or the gradient has no stops, which is what leaves the fallback paint in place.
        //
        // box is the element's geometry box in its own coordinates, which is what an
        // objectBoundingBox gradient is laid out over; ctm takes that coordinate system to the
        // document, and toEm takes the document to the em frame the rest of the load works in.
        internal bool TryResolve(
            string id, Vector2 boxMin, Vector2 boxMax, in SvgMatrix ctm,
            Func<Vector2, Vector2> toEm, float viewW, float viewH, float alpha,
            out Gradient gradient, ref int skipped) {

            gradient = default;
            if (!_byId.TryGetValue(id, out SvgGradientDef? def)) return false;
            List<SvgStop>? stops = Stops(def);
            if (stops == null || stops.Count == 0) return false;

            bool userSpace = string.Equals(Attr(def, "gradientUnits"), "userSpaceOnUse", StringComparison.Ordinal);
            SvgMatrix local = SvgMatrix.Parse(Attr(def, "gradientTransform"), ref skipped);
            if (!userSpace) {
                float bw = boxMax.X - boxMin.X;
                float bh = boxMax.Y - boxMin.Y;
                var box = new SvgMatrix(bw, 0f, 0f, bh, boxMin.X, boxMin.Y);
                local = SvgMatrix.Mul(box, local);
            }
            SvgMatrix full = SvgMatrix.Mul(ctm, local);

            // A percentage is against the viewport in user space and against the unit square in
            // bounding box space, where a bare number already is a fraction.
            float px = userSpace ? viewW : 1f;
            float py = userSpace ? viewH : 1f;
            float pd = userSpace ? MathF.Sqrt((viewW * viewW + viewH * viewH) * 0.5f) : 1f;

            Vector2 a, b;
            Gradient.Shape shape;
            if (def.Radial) {
                float cx = Num(Attr(def, "cx"), 0.5f, px);
                float cy = Num(Attr(def, "cy"), 0.5f, py);
                float r = Num(Attr(def, "r"), 0.5f, pd);
                // A focal point off the center makes the sweep eccentric, which the radial
                // gradient here can't do, so it resolves to the center and counts as dropped.
                if (SvgColor.TryLength(Attr(def, "fx"), px, out float fx) && fx != cx) skipped++;
                if (SvgColor.TryLength(Attr(def, "fy"), py, out float fy) && fy != cy) skipped++;
                if (full.Anisotropic) skipped++;
                a = toEm(full.Apply(new Vector2(cx, cy)));
                b = toEm(full.Apply(new Vector2(cx + r, cy)));
                shape = Gradient.Shape.Radial;
                if (a == b) return false;
            } else {
                float x1 = Num(Attr(def, "x1"), 0f, px);
                float y1 = Num(Attr(def, "y1"), 0f, py);
                float x2 = Num(Attr(def, "x2"), 1f, px);
                float y2 = Num(Attr(def, "y2"), 0f, py);
                a = toEm(full.Apply(new Vector2(x1, y1)));
                b = toEm(full.Apply(new Vector2(x2, y2)));
                shape = Gradient.Shape.Linear;
                // A gradient with no length is its last stop's color everywhere.
                if (a == b) {
                    gradient = Fade(stops[stops.Count - 1].Color, alpha);
                    return true;
                }
            }

            Gradient.RepeatStyle repeat = Attr(def, "spreadMethod") switch {
                "repeat" => Gradient.RepeatStyle.Sawtooth,
                "reflect" => Gradient.RepeatStyle.Triangle,
                _ => Gradient.RepeatStyle.None,
            };

            if (stops.Count == 1) {
                gradient = Fade(stops[0].Color, alpha);
                return true;
            }
            if (stops.Count == 2 && stops[0].Offset <= 0f && stops[1].Offset >= 1f) {
                gradient = new Gradient(a, Fade(stops[0].Color, alpha), b, Fade(stops[1].Color, alpha), shape, repeat);
                return true;
            }

            var pairs = new (float, Color)[stops.Count];
            for (int i = 0; i < stops.Count; i++) {
                pairs[i] = (stops[i].Offset, Fade(stops[i].Color, alpha));
            }
            gradient = new Gradient(a, b, new ColorRamp(pairs), shape, repeat);
            return true;
        }

        // A gradient coordinate. The defaults are all percentages, so an absent one is the
        // fraction times whatever a percent is worth in the units in force.
        private static float Num(string? v, float fraction, float percentOf) {
            return SvgColor.TryLength(v, percentOf, out float n) ? n : fraction * percentOf;
        }

        internal static Color Fade(Color c, float alpha) {
            if (alpha >= 1f) return c;
            return new Color(c.R, c.G, c.B, (byte)Math.Clamp((int)MathF.Round(c.A * alpha), 0, 255));
        }
    }
}
