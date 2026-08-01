using System;
using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    /// <summary>
    /// Procedural gradient colors from a cosine per channel: bias + amplitude * cos(tau * (frequency * t + phase)).
    /// The idea comes from https://iquilezles.org/articles/palettes/. Channels follow the batch's
    /// <see cref="ColorSpace"/>, so in Oklab the three cosines drive lightness and the two color axes
    /// rather than red, green and blue. With whole number frequencies the palette tiles with no seam,
    /// which is what makes it pair well with <see cref="Gradient.RepeatStyle.Sawtooth"/>.
    /// </summary>
    public struct Palette {
        public Palette(Vector3 bias, Vector3 amplitude, Vector3 frequency, Vector3 phase, float alpha = 1f) {
            Bias = bias;
            Amplitude = amplitude;
            Frequency = frequency;
            Phase = phase;
            Alpha = alpha;
        }

        /// <summary>Center of each channel's oscillation, in [0, 1].</summary>
        public Vector3 Bias;
        /// <summary>How far each channel swings around its bias, in [0, 1]. The result is clamped to [0, 1].</summary>
        public Vector3 Amplitude;
        /// <summary>Cycles per gradient length. Snapped to whole numbers in [0, 15] when the shape is drawn.</summary>
        public Vector3 Frequency;
        /// <summary>Where in its cycle each channel starts, as a fraction of one cycle.</summary>
        public Vector3 Phase;
        /// <summary>Opacity of the whole palette, in [0, 1].</summary>
        public float Alpha;

        /// <summary>
        /// Fits a palette through color stops. Each channel picks the whole number frequency and
        /// the cosine that pass nearest the stops, weighted so the stops themselves count most.
        /// The fit is an approximation: one cosine per channel can hit three stops exactly and
        /// runs close past more, but it can't hold flat or make a hard edge, and a palette always
        /// ends where it started, so stops whose two ends differ can't both land. For those,
        /// mirrored fits the stops into the front half of the palette and their
        /// reflection into the back half: aim the gradient across twice the distance you want and
        /// the shape runs through the stops once. The space matters because the cosines run in the
        /// batch's <see cref="ColorSpace"/>: fit in the space you draw with.
        /// </summary>
        public static Palette FromStops(ColorSpace colorSpace, params (float Position, Color Color)[] stops) {
            return FromStops(colorSpace, false, stops);
        }
        /// <inheritdoc cref="FromStops(ColorSpace, ValueTuple{float, Color}[])"/>
        public static Palette FromStops(ColorSpace colorSpace, bool mirrored, params (float Position, Color Color)[] stops) {
            if (stops == null || stops.Length == 0) {
                throw new ArgumentException("A palette fit needs at least one stop.", nameof(stops));
            }

            int n = stops.Length;
            var pos = new float[n];
            var val = new Vector4[n];
            for (int i = 0; i < n; i++) {
                pos[i] = Math.Clamp(stops[i].Position, 0f, 1f);
                val[i] = ToFrame(stops[i].Color, colorSpace);
            }
            // Stable insertion sort, same as Ramp: stops sharing a position keep their order.
            for (int i = 1; i < n; i++) {
                float p = pos[i];
                Vector4 v = val[i];
                int j = i - 1;
                while (j >= 0 && pos[j] > p) {
                    pos[j + 1] = pos[j];
                    val[j + 1] = val[j];
                    j--;
                }
                pos[j + 1] = p;
                val[j + 1] = v;
            }
            if (colorSpace == ColorSpace.Oklch) {
                UnwrapHues(pos, val);
            }

            // The fit runs on the curve between the stops too, sampled densely, so the shape of
            // the transitions counts and not just the stops. The stops ride along as heavy
            // samples, which is what pulls the cosine through the colors that were asked for.
            const int Samples = 256;
            const float StopWeight = 64f;
            int extra = mirrored ? n * 2 : n;
            var t = new float[Samples + extra];
            var w = new float[Samples + extra];
            var y = new Vector3[Samples + extra];
            float alpha = 0f;
            for (int k = 0; k < Samples; k++) {
                t[k] = (k + 0.5f) / Samples;
                w[k] = 1f;
                float u = mirrored ? 1f - MathF.Abs(1f - 2f * t[k]) : t[k];
                Vector4 s = EvalStops(pos, val, u);
                y[k] = new Vector3(s.X, s.Y, s.Z);
                alpha += s.W / Samples;
            }
            for (int i = 0; i < n; i++) {
                var c = new Vector3(val[i].X, val[i].Y, val[i].Z);
                if (mirrored) {
                    (t[Samples + i * 2], w[Samples + i * 2], y[Samples + i * 2]) = (pos[i] * 0.5f, StopWeight, c);
                    (t[Samples + i * 2 + 1], w[Samples + i * 2 + 1], y[Samples + i * 2 + 1]) = (1f - pos[i] * 0.5f, StopWeight, c);
                } else {
                    (t[Samples + i], w[Samples + i], y[Samples + i]) = (pos[i], StopWeight, c);
                }
            }

            Vector3 bias = default, amp = default, freq = default, phase = default;
            for (int ch = 0; ch < 3; ch++) {
                var (b, a, f, p) = FitChannel(t, w, y, ch);
                switch (ch) {
                    case 0: (bias.X, amp.X, freq.X, phase.X) = (b, a, f, p); break;
                    case 1: (bias.Y, amp.Y, freq.Y, phase.Y) = (b, a, f, p); break;
                    default: (bias.Z, amp.Z, freq.Z, phase.Z) = (b, a, f, p); break;
                }
            }
            return new Palette(bias, amp, freq, phase, alpha);
        }

        // One channel: for each whole number frequency, the least squares cosine through the
        // weighted samples via bias + P cos + Q sin, which is linear; the frequency with the
        // smallest residual wins and (P, Q) fold back into amplitude and phase.
        private static (float Bias, float Amp, float Freq, float Phase) FitChannel(float[] t, float[] w, Vector3[] y, int ch) {
            int m = t.Length;
            var yv = new float[m];
            for (int k = 0; k < m; k++) {
                yv[k] = ch == 0 ? y[k].X : ch == 1 ? y[k].Y : y[k].Z;
            }

            float sw = 0f, sy = 0f;
            for (int k = 0; k < m; k++) {
                sw += w[k];
                sy += w[k] * yv[k];
            }
            float mean = sy / sw;
            float best = 0f;
            for (int k = 0; k < m; k++) {
                float e = yv[k] - mean;
                best += w[k] * e * e;
            }
            (float Bias, float Amp, float Freq, float Phase) result = (mean, 0f, 0f, 0f);

            for (int f = 1; f <= 15; f++) {
                float s00 = 0f, s01 = 0f, s02 = 0f, s11 = 0f, s12 = 0f, s22 = 0f;
                float b0 = 0f, b1 = 0f, b2 = 0f;
                for (int k = 0; k < m; k++) {
                    float ang = MathF.Tau * f * t[k];
                    float c = MathF.Cos(ang);
                    float s = MathF.Sin(ang);
                    float wk = w[k];
                    s00 += wk;
                    s01 += wk * c;
                    s02 += wk * s;
                    s11 += wk * c * c;
                    s12 += wk * c * s;
                    s22 += wk * s * s;
                    b0 += wk * yv[k];
                    b1 += wk * yv[k] * c;
                    b2 += wk * yv[k] * s;
                }
                float det = s00 * (s11 * s22 - s12 * s12) - s01 * (s01 * s22 - s12 * s02) + s02 * (s01 * s12 - s11 * s02);
                if (MathF.Abs(det) < 1e-9f) continue;
                float bb = (b0 * (s11 * s22 - s12 * s12) - s01 * (b1 * s22 - s12 * b2) + s02 * (b1 * s12 - s11 * b2)) / det;
                float pp = (s00 * (b1 * s22 - b2 * s12) - b0 * (s01 * s22 - s12 * s02) + s02 * (s01 * b2 - b1 * s02)) / det;
                float qq = (s00 * (s11 * b2 - s12 * b1) - s01 * (s01 * b2 - b1 * s02) + b0 * (s01 * s12 - s11 * s02)) / det;

                float res = 0f;
                for (int k = 0; k < m; k++) {
                    float ang = MathF.Tau * f * t[k];
                    float e = yv[k] - bb - pp * MathF.Cos(ang) - qq * MathF.Sin(ang);
                    res += w[k] * e * e;
                }
                if (res < best) {
                    best = res;
                    float a = MathF.Sqrt(pp * pp + qq * qq);
                    float ph = a > 0f ? MathF.Atan2(-qq, pp) / MathF.Tau : 0f;
                    result = (bb, a, f, ph - MathF.Floor(ph));
                }
            }
            return result;
        }

        // The stop colors in the remapped [0, 1] frame the shader works in, the same
        // conversions the packed stop pairs go through. Shared with ColorRamp's bake.
        internal static Vector4 ToFrame(Color c, ColorSpace colorSpace) {
            if (colorSpace == ColorSpace.Rgb) {
                return new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
            }
            Vector3 lab = VertexShape.ToOklab(c);
            if (colorSpace == ColorSpace.Oklab) {
                return new Vector4(lab.X, lab.Y * 1.25f + 0.5f, lab.Z * 1.25f + 0.5f, c.A / 255f);
            }
            float chroma = MathF.Sqrt(lab.Y * lab.Y + lab.Z * lab.Z);
            float hue = MathF.Atan2(lab.Z, lab.Y);
            return new Vector4(lab.X, chroma * 2.5f, hue / MathF.Tau + 0.5f, c.A / 255f);
        }

        // Grays have no hue of their own, so they borrow the nearest chromatic stop's, and each
        // hue after the first takes the branch within half a turn of the one before it so the
        // fit follows the short way around the wheel. An unwrapped hue can leave [0, 1]; the
        // channel clamps when it evaluates, so a track that winds past a full turn frays there.
        // ColorRamp's bake shares this and recenters per texel instead, so it doesn't fray.
        internal static void UnwrapHues(float[] pos, Vector4[] val) {
            const float achromatic = 1e-4f * 2.5f;
            int first = -1;
            for (int i = 0; i < val.Length; i++) {
                if (val[i].Y >= achromatic) { first = i; break; }
            }
            if (first < 0) {
                for (int i = 0; i < val.Length; i++) val[i].Z = 0.5f;
                return;
            }
            for (int i = 0; i < val.Length; i++) {
                if (val[i].Y < achromatic) {
                    val[i].Z = val[i > first ? i - 1 : first].Z;
                } else if (i > 0) {
                    float prev = val[i - 1].Z;
                    val[i].Z -= MathF.Round(val[i].Z - prev);
                }
            }
        }

        private static Vector4 EvalStops(float[] pos, Vector4[] val, float u) {
            int n = pos.Length;
            if (u <= pos[0]) return val[0];
            if (u >= pos[n - 1]) return val[n - 1];
            int i = 1;
            while (pos[i] < u) i++;
            float span = pos[i] - pos[i - 1];
            return span > 0f ? Vector4.Lerp(val[i - 1], val[i], (u - pos[i - 1]) / span) : val[i];
        }
    }
}
