// The d attribute's grammar, and the basic shapes that are shorthand for one.

using System;
using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    internal static class SvgPathData {
        // Walks a path's data into an outline. Malformed data stops the path where it broke and
        // keeps everything that parsed, which is what a browser draws for the same string.
        // Returns false when something was dropped.
        internal static bool Parse(string? d, SvgOutline into) {
            bool ok = true;
            if (!string.IsNullOrEmpty(d)) {
                var s = new SvgScan(d);
                char pending = '\0';
                // A path that does not start with a moveto is in error and draws nothing at all,
                // rather than starting from the origin.
                if (!s.End && s.Peek != 'M' && s.Peek != 'm') {
                    into.Finish();
                    return false;
                }
                while (!s.End) {
                    char c = s.Peek;
                    if (char.IsLetter(c)) {
                        s.Skip();
                        pending = c;
                    } else if (pending == '\0') {
                        // Numbers before any command.
                        ok = false;
                        break;
                    } else if (pending == 'M') {
                        // A moveto's extra coordinate pairs are linetos.
                        pending = 'L';
                    } else if (pending == 'm') {
                        pending = 'l';
                    } else if (pending == 'Z' || pending == 'z') {
                        ok = false;
                        break;
                    }
                    if (!Run(s, into, pending)) {
                        ok = false;
                        break;
                    }
                }
            }
            into.Finish();
            return ok;
        }

        private static bool Run(SvgScan s, SvgOutline o, char cmd) {
            bool rel = char.IsLower(cmd);
            Vector2 at = o.Current;
            switch (char.ToUpperInvariant(cmd)) {
                case 'M': {
                    if (!Point(s, at, rel, out Vector2 p)) return false;
                    o.MoveTo(p);
                    return true;
                }
                case 'L': {
                    if (!Point(s, at, rel, out Vector2 p)) return false;
                    o.LineTo(p);
                    return true;
                }
                case 'H': {
                    if (!s.TryNumber(out float x)) return false;
                    o.LineTo(new Vector2(rel ? at.X + x : x, at.Y));
                    return true;
                }
                case 'V': {
                    if (!s.TryNumber(out float y)) return false;
                    o.LineTo(new Vector2(at.X, rel ? at.Y + y : y));
                    return true;
                }
                case 'C': {
                    if (!Point(s, at, rel, out Vector2 c1)) return false;
                    if (!Point(s, at, rel, out Vector2 c2)) return false;
                    if (!Point(s, at, rel, out Vector2 p)) return false;
                    o.CubicTo(c1, c2, p);
                    return true;
                }
                case 'S': {
                    // The first control point mirrors the last cubic's second one, or sits on the
                    // current point when the segment before was not a cubic.
                    Vector2 c1 = o.AfterCubic ? at * 2f - o.LastCubic : at;
                    if (!Point(s, at, rel, out Vector2 c2)) return false;
                    if (!Point(s, at, rel, out Vector2 p)) return false;
                    o.CubicTo(c1, c2, p);
                    return true;
                }
                case 'Q': {
                    if (!Point(s, at, rel, out Vector2 c)) return false;
                    if (!Point(s, at, rel, out Vector2 p)) return false;
                    o.QuadTo(c, p);
                    return true;
                }
                case 'T': {
                    Vector2 c = o.AfterQuad ? at * 2f - o.LastQuad : at;
                    if (!Point(s, at, rel, out Vector2 p)) return false;
                    o.QuadTo(c, p);
                    return true;
                }
                case 'A': {
                    if (!s.TryNumber(out float rx)) return false;
                    if (!s.TryNumber(out float ry)) return false;
                    if (!s.TryNumber(out float rot)) return false;
                    if (!s.TryFlag(out bool large)) return false;
                    if (!s.TryFlag(out bool sweep)) return false;
                    if (!Point(s, at, rel, out Vector2 p)) return false;
                    o.ArcTo(rx, ry, rot, large, sweep, p);
                    return true;
                }
                case 'Z':
                    o.Close();
                    return true;
                default:
                    return false;
            }
        }

        private static bool Point(SvgScan s, Vector2 at, bool rel, out Vector2 p) {
            p = default;
            if (!s.TryNumber(out float x)) return false;
            if (!s.TryNumber(out float y)) return false;
            p = rel ? new Vector2(at.X + x, at.Y + y) : new Vector2(x, y);
            return true;
        }

        internal static void Rect(SvgOutline o, float x, float y, float w, float h, float rx, float ry) {
            if (!(w > 0f) || !(h > 0f)) return;
            rx = MathF.Min(MathF.Max(rx, 0f), w * 0.5f);
            ry = MathF.Min(MathF.Max(ry, 0f), h * 0.5f);
            if (rx <= 0f || ry <= 0f) {
                o.MoveTo(new Vector2(x, y));
                o.LineTo(new Vector2(x + w, y));
                o.LineTo(new Vector2(x + w, y + h));
                o.LineTo(new Vector2(x, y + h));
                o.Close();
                return;
            }
            o.MoveTo(new Vector2(x + rx, y));
            o.LineTo(new Vector2(x + w - rx, y));
            o.ArcTo(rx, ry, 0f, false, true, new Vector2(x + w, y + ry));
            o.LineTo(new Vector2(x + w, y + h - ry));
            o.ArcTo(rx, ry, 0f, false, true, new Vector2(x + w - rx, y + h));
            o.LineTo(new Vector2(x + rx, y + h));
            o.ArcTo(rx, ry, 0f, false, true, new Vector2(x, y + h - ry));
            o.LineTo(new Vector2(x, y + ry));
            o.ArcTo(rx, ry, 0f, false, true, new Vector2(x + rx, y));
            o.Close();
        }

        internal static void Ellipse(SvgOutline o, float cx, float cy, float rx, float ry) {
            if (!(rx > 0f) || !(ry > 0f)) return;
            o.MoveTo(new Vector2(cx + rx, cy));
            o.ArcTo(rx, ry, 0f, false, true, new Vector2(cx - rx, cy));
            o.ArcTo(rx, ry, 0f, false, true, new Vector2(cx + rx, cy));
            o.Close();
        }

        internal static void Line(SvgOutline o, float x1, float y1, float x2, float y2) {
            o.MoveTo(new Vector2(x1, y1));
            o.LineTo(new Vector2(x2, y2));
            o.Finish();
        }

        // points is the same number soup everywhere else is. An odd trailing number is dropped,
        // which is what the spec says to do with it.
        internal static bool Points(string? value, SvgOutline o, bool close) {
            var s = new SvgScan(value);
            bool first = true;
            bool ok = true;
            while (!s.End) {
                if (!s.TryNumber(out float x) || !s.TryNumber(out float y)) {
                    ok = false;
                    break;
                }
                var p = new Vector2(x, y);
                if (first) {
                    o.MoveTo(p);
                    first = false;
                } else {
                    o.LineTo(p);
                }
            }
            if (close) o.Close();
            o.Finish();
            return ok;
        }
    }
}
