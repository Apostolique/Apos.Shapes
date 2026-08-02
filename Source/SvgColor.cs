// Colors and paint values, the way CSS writes them inside an SVG.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    // What a fill or a stroke was set to. An absent property is not one of these: it inherits,
    // so the walk keeps the parent's value instead of building one.
    internal enum SvgPaintKind {
        None = 0,
        Flat = 1,
        // url(#id) into a gradient in defs. Flat carries the fallback color for when it misses.
        Ref = 2,
    }

    internal readonly struct SvgPaint {
        internal SvgPaint(SvgPaintKind kind, Color color, string? id = null, bool current = false) {
            Kind = kind;
            Color = color;
            Id = id;
            Current = current;
        }

        internal readonly SvgPaintKind Kind;
        internal readonly Color Color;
        internal readonly string? Id;
        // Set when the value was currentColor, so a later phase can recolor what asked for it.
        internal readonly bool Current;

        internal static readonly SvgPaint None = new(SvgPaintKind.None, Color.Transparent);
        internal static SvgPaint Flat(Color c) => new(SvgPaintKind.Flat, c);
    }

    internal static class SvgColor {
        // fill and stroke: none, currentColor, a color, or a url() into defs with an optional
        // color after it for when the reference misses. Returns false when the value is not one
        // of those, which leaves the inherited value in place.
        internal static bool TryPaint(string? value, Color current, out SvgPaint paint) {
            paint = SvgPaint.None;
            if (string.IsNullOrEmpty(value)) return false;
            string v = value.Trim();
            if (v.Length == 0) return false;
            if (Eq(v, "none")) {
                paint = SvgPaint.None;
                return true;
            }
            if (Eq(v, "currentColor")) {
                paint = new SvgPaint(SvgPaintKind.Flat, current, null, true);
                return true;
            }
            if (v.Length > 5 && (v[0] == 'u' || v[0] == 'U') && v.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) {
                int close = v.IndexOf(')');
                if (close < 0) return false;
                string target = v.Substring(4, close - 4).Trim().Trim('\'', '"');
                if (target.StartsWith("#", StringComparison.Ordinal)) target = target.Substring(1);
                if (target.Length == 0) return false;
                // The rest is the fallback paint, which is what gets used when nothing in defs
                // answers to that id.
                string rest = v.Substring(close + 1).Trim();
                Color fallback = Color.Transparent;
                SvgPaintKind kind = SvgPaintKind.Ref;
                if (rest.Length > 0 && !Eq(rest, "none") && TryColor(rest, out Color c)) fallback = c;
                paint = new SvgPaint(kind, fallback, target);
                return true;
            }
            if (TryColor(v, out Color flat)) {
                paint = SvgPaint.Flat(flat);
                return true;
            }
            return false;
        }

        internal static bool TryColor(string? value, out Color color) {
            color = Color.Black;
            if (string.IsNullOrEmpty(value)) return false;
            string v = value.Trim();
            if (v.Length == 0) return false;

            if (v[0] == '#') return TryHex(v, out color);

            if (v.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)) {
                var s = new SvgScan(v);
                string? name = s.TryName();
                if (name == null) return false;
                bool alpha = Eq(name, "rgba");
                if (!alpha && !Eq(name, "rgb")) return false;
                Span<float> parts = stackalloc float[4];
                parts[3] = 1f;
                int n = 0;
                while (n < 4) {
                    if (!s.TryNumber(out float f)) break;
                    // A percentage is over the channel's whole range; a bare number is 0 to 255
                    // for a color channel and 0 to 1 for alpha.
                    if (s.Peek == '%') {
                        s.Skip();
                        f = f * (n == 3 ? 0.01f : 2.55f);
                    }
                    parts[n++] = f;
                }
                if (n < 3) return false;
                color = new Color(Byte(parts[0]), Byte(parts[1]), Byte(parts[2]), Byte(parts[3] * 255f));
                return true;
            }

            if (Eq(v, "transparent")) {
                color = Color.Transparent;
                return true;
            }
            if (Named.TryGetValue(v.ToLowerInvariant(), out uint rgb)) {
                color = new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)255);
                return true;
            }
            return false;
        }

        private static bool TryHex(string v, out Color color) {
            color = Color.Black;
            int n = v.Length - 1;
            if (n != 3 && n != 4 && n != 6 && n != 8) return false;
            Span<int> d = stackalloc int[8];
            for (int i = 0; i < n; i++) {
                int h = Hex(v[i + 1]);
                if (h < 0) return false;
                d[i] = h;
            }
            if (n <= 4) {
                color = new Color(
                    (byte)(d[0] * 17), (byte)(d[1] * 17), (byte)(d[2] * 17),
                    (byte)(n == 4 ? d[3] * 17 : 255));
            } else {
                color = new Color(
                    (byte)(d[0] * 16 + d[1]), (byte)(d[2] * 16 + d[3]), (byte)(d[4] * 16 + d[5]),
                    (byte)(n == 8 ? d[6] * 16 + d[7] : 255));
            }
            return true;
        }

        private static int Hex(char c) {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private static byte Byte(float v) {
            return (byte)Math.Clamp((int)MathF.Round(v), 0, 255);
        }

        private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // The CSS color keywords, which SVG takes as color values everywhere. Read only, so one
        // table serves every document at once.
        private static readonly Dictionary<string, uint> Named = new(StringComparer.Ordinal) {
            { "aliceblue", 0xF0F8FF }, { "antiquewhite", 0xFAEBD7 }, { "aqua", 0x00FFFF },
            { "aquamarine", 0x7FFFD4 }, { "azure", 0xF0FFFF }, { "beige", 0xF5F5DC },
            { "bisque", 0xFFE4C4 }, { "black", 0x000000 }, { "blanchedalmond", 0xFFEBCD },
            { "blue", 0x0000FF }, { "blueviolet", 0x8A2BE2 }, { "brown", 0xA52A2A },
            { "burlywood", 0xDEB887 }, { "cadetblue", 0x5F9EA0 }, { "chartreuse", 0x7FFF00 },
            { "chocolate", 0xD2691E }, { "coral", 0xFF7F50 }, { "cornflowerblue", 0x6495ED },
            { "cornsilk", 0xFFF8DC }, { "crimson", 0xDC143C }, { "cyan", 0x00FFFF },
            { "darkblue", 0x00008B }, { "darkcyan", 0x008B8B }, { "darkgoldenrod", 0xB8860B },
            { "darkgray", 0xA9A9A9 }, { "darkgreen", 0x006400 }, { "darkgrey", 0xA9A9A9 },
            { "darkkhaki", 0xBDB76B }, { "darkmagenta", 0x8B008B }, { "darkolivegreen", 0x556B2F },
            { "darkorange", 0xFF8C00 }, { "darkorchid", 0x9932CC }, { "darkred", 0x8B0000 },
            { "darksalmon", 0xE9967A }, { "darkseagreen", 0x8FBC8F }, { "darkslateblue", 0x483D8B },
            { "darkslategray", 0x2F4F4F }, { "darkslategrey", 0x2F4F4F }, { "darkturquoise", 0x00CED1 },
            { "darkviolet", 0x9400D3 }, { "deeppink", 0xFF1493 }, { "deepskyblue", 0x00BFFF },
            { "dimgray", 0x696969 }, { "dimgrey", 0x696969 }, { "dodgerblue", 0x1E90FF },
            { "firebrick", 0xB22222 }, { "floralwhite", 0xFFFAF0 }, { "forestgreen", 0x228B22 },
            { "fuchsia", 0xFF00FF }, { "gainsboro", 0xDCDCDC }, { "ghostwhite", 0xF8F8FF },
            { "gold", 0xFFD700 }, { "goldenrod", 0xDAA520 }, { "gray", 0x808080 },
            { "grey", 0x808080 }, { "green", 0x008000 }, { "greenyellow", 0xADFF2F },
            { "honeydew", 0xF0FFF0 }, { "hotpink", 0xFF69B4 }, { "indianred", 0xCD5C5C },
            { "indigo", 0x4B0082 }, { "ivory", 0xFFFFF0 }, { "khaki", 0xF0E68C },
            { "lavender", 0xE6E6FA }, { "lavenderblush", 0xFFF0F5 }, { "lawngreen", 0x7CFC00 },
            { "lemonchiffon", 0xFFFACD }, { "lightblue", 0xADD8E6 }, { "lightcoral", 0xF08080 },
            { "lightcyan", 0xE0FFFF }, { "lightgoldenrodyellow", 0xFAFAD2 }, { "lightgray", 0xD3D3D3 },
            { "lightgreen", 0x90EE90 }, { "lightgrey", 0xD3D3D3 }, { "lightpink", 0xFFB6C1 },
            { "lightsalmon", 0xFFA07A }, { "lightseagreen", 0x20B2AA }, { "lightskyblue", 0x87CEFA },
            { "lightslategray", 0x778899 }, { "lightslategrey", 0x778899 }, { "lightsteelblue", 0xB0C4DE },
            { "lightyellow", 0xFFFFE0 }, { "lime", 0x00FF00 }, { "limegreen", 0x32CD32 },
            { "linen", 0xFAF0E6 }, { "magenta", 0xFF00FF }, { "maroon", 0x800000 },
            { "mediumaquamarine", 0x66CDAA }, { "mediumblue", 0x0000CD }, { "mediumorchid", 0xBA55D3 },
            { "mediumpurple", 0x9370DB }, { "mediumseagreen", 0x3CB371 }, { "mediumslateblue", 0x7B68EE },
            { "mediumspringgreen", 0x00FA9A }, { "mediumturquoise", 0x48D1CC }, { "mediumvioletred", 0xC71585 },
            { "midnightblue", 0x191970 }, { "mintcream", 0xF5FFFA }, { "mistyrose", 0xFFE4E1 },
            { "moccasin", 0xFFE4B5 }, { "navajowhite", 0xFFDEAD }, { "navy", 0x000080 },
            { "oldlace", 0xFDF5E6 }, { "olive", 0x808000 }, { "olivedrab", 0x6B8E23 },
            { "orange", 0xFFA500 }, { "orangered", 0xFF4500 }, { "orchid", 0xDA70D6 },
            { "palegoldenrod", 0xEEE8AA }, { "palegreen", 0x98FB98 }, { "paleturquoise", 0xAFEEEE },
            { "palevioletred", 0xDB7093 }, { "papayawhip", 0xFFEFD5 }, { "peachpuff", 0xFFDAB9 },
            { "peru", 0xCD853F }, { "pink", 0xFFC0CB }, { "plum", 0xDDA0DD },
            { "powderblue", 0xB0E0E6 }, { "purple", 0x800080 }, { "rebeccapurple", 0x663399 },
            { "red", 0xFF0000 }, { "rosybrown", 0xBC8F8F }, { "royalblue", 0x4169E1 },
            { "saddlebrown", 0x8B4513 }, { "salmon", 0xFA8072 }, { "sandybrown", 0xF4A460 },
            { "seagreen", 0x2E8B57 }, { "seashell", 0xFFF5EE }, { "sienna", 0xA0522D },
            { "silver", 0xC0C0C0 }, { "skyblue", 0x87CEEB }, { "slateblue", 0x6A5ACD },
            { "slategray", 0x708090 }, { "slategrey", 0x708090 }, { "snow", 0xFFFAFA },
            { "springgreen", 0x00FF7F }, { "steelblue", 0x4682B4 }, { "tan", 0xD2B48C },
            { "teal", 0x008080 }, { "thistle", 0xD8BFD8 }, { "tomato", 0xFF6347 },
            { "turquoise", 0x40E0D0 }, { "violet", 0xEE82EE }, { "wheat", 0xF5DEB3 },
            { "white", 0xFFFFFF }, { "whitesmoke", 0xF5F5F5 }, { "yellow", 0xFFFF00 },
            { "yellowgreen", 0x9ACD32 },
        };

        // A length or a plain number. Only the units that mean something without a font or a
        // viewport are taken; the rest fall back to the number in front of them.
        internal static bool TryLength(string? value, float percentOf, out float length) {
            length = 0f;
            if (string.IsNullOrEmpty(value)) return false;
            var s = new SvgScan(value);
            if (!s.TryNumber(out float v)) return false;
            s.SkipWs();
            char c = s.Peek;
            if (c == '%') {
                length = v * 0.01f * percentOf;
                return true;
            }
            if (c == 'p' || c == 'P') {
                // px is the user unit; pt, pc and the physical units are fixed multiples of it.
                string rest = value.Substring(s.At).Trim().ToLowerInvariant();
                length = rest switch {
                    "pt" => v * 96f / 72f,
                    "pc" => v * 16f,
                    _ => v,
                };
                return true;
            }
            if (c == 'i' || c == 'I') {
                length = v * 96f;
                return true;
            }
            if (c == 'c' || c == 'C') {
                length = v * 96f / 2.54f;
                return true;
            }
            if (c == 'm' || c == 'M') {
                length = v * 96f / 25.4f;
                return true;
            }
            length = v;
            return true;
        }
    }
}
