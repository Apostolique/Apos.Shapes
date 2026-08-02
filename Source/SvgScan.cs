// Scanning the number soup SVG attributes are written in, and the transform lists built out of it.

using System;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    // A cursor over a string of SVG numbers. The grammar is looser than any BCL parser takes:
    // ".5.5" is two numbers, "-.5-.5" is two more, and an arc's flags run straight into the
    // number after them, so every token is scanned by hand and handed to float.TryParse whole.
    internal sealed class SvgScan {
        internal SvgScan(string? s) {
            _s = s ?? string.Empty;
        }

        private readonly string _s;
        private int _at;

        internal int At {
            get => _at;
            set => _at = value;
        }
        internal int Length => _s.Length;

        internal bool End {
            get {
                SkipWs();
                return _at >= _s.Length;
            }
        }

        internal char Peek => _at < _s.Length ? _s[_at] : '\0';
        internal void Skip() => _at++;

        // wsp per the spec, plus the vertical tab no grammar mentions and every renderer takes.
        internal static bool IsWs(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\v';
        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        internal void SkipWs() {
            while (_at < _s.Length && IsWs(_s[_at])) _at++;
        }

        // comma-wsp: whitespace around at most one comma. Two commas in a row is malformed, and
        // leaving the second one in place is what makes the next token fail rather than silently
        // shifting the argument list by one.
        internal void SkipSep() {
            SkipWs();
            if (_at < _s.Length && _s[_at] == ',') {
                _at++;
                SkipWs();
            }
        }

        internal bool TryNumber(out float v) {
            v = 0f;
            SkipSep();
            int start = _at;
            if (_at < _s.Length && (_s[_at] == '+' || _s[_at] == '-')) _at++;
            int digits = 0;
            while (_at < _s.Length && IsDigit(_s[_at])) {
                _at++;
                digits++;
            }
            bool trailingDot = false;
            if (_at < _s.Length && _s[_at] == '.') {
                _at++;
                int before = _at;
                while (_at < _s.Length && IsDigit(_s[_at])) {
                    _at++;
                    digits++;
                }
                trailingDot = _at == before;
            }
            if (digits == 0) {
                _at = start;
                return false;
            }
            int mantissa = _at;
            if (_at < _s.Length && (_s[_at] == 'e' || _s[_at] == 'E')) {
                _at++;
                if (_at < _s.Length && (_s[_at] == '+' || _s[_at] == '-')) _at++;
                int exp = 0;
                while (_at < _s.Length && IsDigit(_s[_at])) {
                    _at++;
                    exp++;
                }
                // "1e" is the number 1 followed by a letter, which in path data is a command.
                if (exp == 0) _at = mantissa;
            }
            // A trailing '.' with no fraction after it is a legal SVG number that the BCL parser
            // takes either way; dropping it costs nothing and removes the question.
            int len = _at - start - (trailingDot && _at == mantissa ? 1 : 0);
            if (!float.TryParse(_s.AsSpan(start, len), NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                || !float.IsFinite(v)) {
                _at = start;
                v = 0f;
                return false;
            }
            return true;
        }

        // An arc flag is exactly one character, which is what lets "a1 1 0 011 1" mean two flags
        // and an x of 1.
        internal bool TryFlag(out bool flag) {
            flag = false;
            SkipSep();
            if (_at >= _s.Length) return false;
            char c = _s[_at];
            if (c != '0' && c != '1') return false;
            _at++;
            flag = c == '1';
            return true;
        }

        // The name of a function call like translate( or rgb(, with the paren consumed.
        internal string? TryName() {
            SkipSep();
            int start = _at;
            while (_at < _s.Length && (char.IsLetter(_s[_at]) || _s[_at] == '-')) _at++;
            if (_at == start) return null;
            string name = _s.Substring(start, _at - start);
            SkipWs();
            if (_at >= _s.Length || _s[_at] != '(') {
                _at = start;
                return null;
            }
            _at++;
            return name;
        }

        internal bool TryClose() {
            SkipSep();
            if (_at < _s.Length && _s[_at] == ')') {
                _at++;
                return true;
            }
            return false;
        }
    }

    // An SVG transform, which is the top two rows of a 3x3: (x, y) goes to (A x + C y + E, B x + D y + F).
    internal readonly struct SvgMatrix {
        internal SvgMatrix(float a, float b, float c, float d, float e, float f) {
            A = a;
            B = b;
            C = c;
            D = d;
            E = e;
            F = f;
        }

        internal readonly float A, B, C, D, E, F;

        internal static readonly SvgMatrix Identity = new(1f, 0f, 0f, 1f, 0f, 0f);

        internal Vector2 Apply(Vector2 p) {
            return new Vector2(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);
        }

        // The outer transform applied on top of the inner one, which is what nesting a g inside
        // another g composes to.
        internal static SvgMatrix Mul(in SvgMatrix outer, in SvgMatrix inner) {
            return new SvgMatrix(
                outer.A * inner.A + outer.C * inner.B,
                outer.B * inner.A + outer.D * inner.B,
                outer.A * inner.C + outer.C * inner.D,
                outer.B * inner.C + outer.D * inner.D,
                outer.A * inner.E + outer.C * inner.F + outer.E,
                outer.B * inner.E + outer.D * inner.F + outer.F);
        }

        internal float Det => A * D - B * C;

        // The largest a unit vector can grow through this, which is what turns a tolerance in
        // document units into one that means the same thing before the transform runs.
        internal float MaxScale {
            get {
                float e = (A * A + B * B + C * C + D * D) * 0.5f;
                float f = (A * A + B * B - C * C - D * D) * 0.5f;
                float g = A * C + B * D;
                return MathF.Sqrt(MathF.Max(e + MathF.Sqrt(f * f + g * g), 0f));
            }
        }

        // How much a stroke width grows through this. A transform that scales the two axes by
        // different amounts turns a round pen into an elliptical one, which the stroke renderer
        // has no way to draw, so the area preserving mean is what it gets instead.
        internal float PenScale => MathF.Sqrt(MathF.Abs(Det));

        // Whether the two axes come out of this far enough apart that a stroke or a radial
        // gradient is visibly the wrong shape.
        internal bool Anisotropic {
            get {
                float sx = MathF.Sqrt(A * A + B * B);
                float sy = MathF.Sqrt(C * C + D * D);
                float shear = MathF.Abs(A * C + B * D);
                float big = MathF.Max(sx, sy);
                if (big <= 0f) return false;
                return MathF.Abs(sx - sy) > big * 0.01f || shear > big * big * 0.01f;
            }
        }

        // A transform attribute: any number of matrix/translate/scale/rotate/skewX/skewY applied
        // left to right. An unreadable list stops where it broke and keeps what composed, which
        // is what every renderer does with one.
        internal static SvgMatrix Parse(string? value, ref int skipped) {
            SvgMatrix m = Identity;
            if (string.IsNullOrEmpty(value)) return m;
            var s = new SvgScan(value);
            Span<float> args = stackalloc float[6];
            while (!s.End) {
                string? name = s.TryName();
                if (name == null) break;
                int n = 0;
                while (n < 6 && s.TryNumber(out float v)) args[n++] = v;
                if (!s.TryClose()) break;
                SvgMatrix step;
                switch (name) {
                    case "matrix":
                        if (n != 6) return m;
                        step = new SvgMatrix(args[0], args[1], args[2], args[3], args[4], args[5]);
                        break;
                    case "translate":
                        if (n < 1) return m;
                        step = new SvgMatrix(1f, 0f, 0f, 1f, args[0], n > 1 ? args[1] : 0f);
                        break;
                    case "scale":
                        if (n < 1) return m;
                        step = new SvgMatrix(args[0], 0f, 0f, n > 1 ? args[1] : args[0], 0f, 0f);
                        break;
                    case "rotate": {
                        if (n < 1) return m;
                        float r = args[0] * (MathF.PI / 180f);
                        float sin = MathF.Sin(r);
                        float cos = MathF.Cos(r);
                        var rot = new SvgMatrix(cos, sin, -sin, cos, 0f, 0f);
                        if (n >= 3) {
                            var to = new SvgMatrix(1f, 0f, 0f, 1f, args[1], args[2]);
                            var back = new SvgMatrix(1f, 0f, 0f, 1f, -args[1], -args[2]);
                            rot = Mul(to, Mul(rot, back));
                        }
                        step = rot;
                        break;
                    }
                    case "skewX":
                        if (n < 1) return m;
                        step = new SvgMatrix(1f, 0f, MathF.Tan(args[0] * (MathF.PI / 180f)), 1f, 0f, 0f);
                        break;
                    case "skewY":
                        if (n < 1) return m;
                        step = new SvgMatrix(1f, MathF.Tan(args[0] * (MathF.PI / 180f)), 0f, 1f, 0f, 0f);
                        break;
                    default:
                        skipped++;
                        continue;
                }
                m = Mul(m, step);
            }
            return m;
        }
    }
}
