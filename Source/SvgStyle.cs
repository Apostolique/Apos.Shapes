// The presentation properties a drawable inherits, and where their values are read from.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Xml;

namespace Apos.Shapes {
    // One element's attributes, with the inline style attribute layered over them. A declaration
    // in style="" wins over the presentation attribute of the same name, which is what CSS says
    // and what decides which of two conflicting fills an element gets.
    internal sealed class SvgAttrs {
        internal SvgAttrs(XmlReader reader) {
            if (!reader.HasAttributes) return;
            _own = new Dictionary<string, string>(StringComparer.Ordinal);
            reader.MoveToFirstAttribute();
            do {
                // Namespaced attributes are stored under both spellings so xlink:href and href
                // answer the same lookup.
                _own[reader.Name] = reader.Value;
                _own[reader.LocalName] = reader.Value;
            } while (reader.MoveToNextAttribute());
            reader.MoveToElement();

            if (!_own.TryGetValue("style", out string? style) || style.Length == 0) return;
            _style = new Dictionary<string, string>(StringComparer.Ordinal);
            int at = 0;
            while (at < style.Length) {
                int end = style.IndexOf(';', at);
                if (end < 0) end = style.Length;
                int colon = style.IndexOf(':', at);
                if (colon > at && colon < end) {
                    string name = style.Substring(at, colon - at).Trim();
                    string value = style.Substring(colon + 1, end - colon - 1).Trim();
                    if (name.Length > 0) _style[name] = value;
                }
                at = end + 1;
            }
        }

        private readonly Dictionary<string, string>? _own;
        private readonly Dictionary<string, string>? _style;

        internal string? Get(string name) {
            if (_style != null && _style.TryGetValue(name, out string? v)) return v;
            if (_own != null && _own.TryGetValue(name, out string? a)) return a;
            return null;
        }

        // Attributes that are never CSS properties, so style="" has no say over them.
        internal string? Raw(string name) {
            return _own != null && _own.TryGetValue(name, out string? v) ? v : null;
        }

        internal bool Has(string name) => _own != null && _own.ContainsKey(name);
    }

    // What a drawable paints with, and what a group hands down to its children. Everything here
    // inherits except opacity, which is a group's own and gets folded into its children instead
    // of composited, since there's no offscreen pass to composite in.
    internal struct SvgStyle {
        internal SvgPaint Fill;
        internal SvgPaint Stroke;
        internal float FillOpacity;
        internal float StrokeOpacity;
        internal float Opacity;
        internal bool EvenOdd;
        internal float StrokeWidth;
        internal PathCap Cap;
        internal PathJoin Join;
        internal float MiterLimit;
        internal float[]? Dash;
        internal float DashOffset;
        // What currentColor resolves to. Black until a color property says otherwise, which is
        // the initial value CSS gives it.
        internal Color Current;

        internal static SvgStyle Root() {
            return new SvgStyle {
                Fill = SvgPaint.Flat(Color.Black),
                Stroke = SvgPaint.None,
                FillOpacity = 1f,
                StrokeOpacity = 1f,
                Opacity = 1f,
                EvenOdd = false,
                StrokeWidth = 1f,
                Cap = PathCap.Butt,
                Join = PathJoin.Miter,
                MiterLimit = 4f,
                Dash = null,
                DashOffset = 0f,
                Current = Color.Black,
            };
        }

        // This element's own values on top of the inherited ones.
        internal SvgStyle With(SvgAttrs a, ref int skipped) {
            SvgStyle s = this;

            if (SvgColor.TryColor(a.Get("color"), out Color current)) s.Current = current;

            string? v = a.Get("fill");
            if (v != null && SvgColor.TryPaint(v, s.Current, out SvgPaint fill)) s.Fill = fill;
            v = a.Get("stroke");
            if (v != null && SvgColor.TryPaint(v, s.Current, out SvgPaint stroke)) s.Stroke = stroke;

            if (TryAlpha(a.Get("fill-opacity"), out float fo)) s.FillOpacity = fo;
            if (TryAlpha(a.Get("stroke-opacity"), out float so)) s.StrokeOpacity = so;
            if (TryAlpha(a.Get("opacity"), out float op)) s.Opacity *= op;

            v = a.Get("fill-rule");
            if (v != null) {
                v = v.Trim();
                if (string.Equals(v, "evenodd", StringComparison.OrdinalIgnoreCase)) s.EvenOdd = true;
                else if (string.Equals(v, "nonzero", StringComparison.OrdinalIgnoreCase)) s.EvenOdd = false;
            }

            if (SvgColor.TryLength(a.Get("stroke-width"), 1f, out float w) && w >= 0f) s.StrokeWidth = w;

            v = a.Get("stroke-linecap");
            if (v != null) {
                switch (v.Trim().ToLowerInvariant()) {
                    case "butt": s.Cap = PathCap.Butt; break;
                    case "round": s.Cap = PathCap.Round; break;
                    case "square": s.Cap = PathCap.Square; break;
                }
            }

            v = a.Get("stroke-linejoin");
            if (v != null) {
                switch (v.Trim().ToLowerInvariant()) {
                    case "miter": s.Join = PathJoin.Miter; break;
                    case "round": s.Join = PathJoin.Round; break;
                    case "bevel": s.Join = PathJoin.Bevel; break;
                    // miter-clip and arcs have no equivalent in the stroke renderer.
                    case "miter-clip": s.Join = PathJoin.Miter; skipped++; break;
                    case "arcs": s.Join = PathJoin.Round; skipped++; break;
                }
            }

            v = a.Get("stroke-miterlimit");
            if (v != null && float.TryParse(v.Trim(), System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out float ml)
                && ml >= 1f) {
                s.MiterLimit = ml;
            }

            v = a.Get("stroke-dasharray");
            if (v != null) {
                string t = v.Trim();
                if (t.Length == 0 || string.Equals(t, "none", StringComparison.OrdinalIgnoreCase)) {
                    s.Dash = null;
                } else {
                    var scan = new SvgScan(t);
                    var list = new List<float>();
                    bool bad = false;
                    while (!scan.End) {
                        if (!scan.TryNumber(out float d) || d < 0f) {
                            bad = true;
                            break;
                        }
                        // A percentage is against the viewport diagonal, which nothing here
                        // knows about yet; the number in front of it is the honest reading.
                        if (scan.Peek == '%') scan.Skip();
                        list.Add(d);
                    }
                    // A malformed or all zero list draws solid, per the spec.
                    float sum = 0f;
                    foreach (float d in list) sum += d;
                    s.Dash = bad || list.Count == 0 || sum <= 0f ? null : list.ToArray();
                }
            }

            if (SvgColor.TryLength(a.Get("stroke-dashoffset"), 1f, out float off)) s.DashOffset = off;

            return s;
        }

        private static bool TryAlpha(string? v, out float value) {
            value = 1f;
            if (v == null) return false;
            string t = v.Trim();
            bool percent = t.EndsWith("%", StringComparison.Ordinal);
            if (percent) t = t.Substring(0, t.Length - 1);
            if (!float.TryParse(t, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float f)) {
                return false;
            }
            if (percent) f *= 0.01f;
            value = Math.Clamp(f, 0f, 1f);
            return true;
        }
    }
}
