using System;
using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    /// <summary>
    /// Colors a <see cref="Gradient"/> from (position, color) stops instead of two colors.
    /// Stops are (position, color) pairs in [0, 1] that blend between them the way two stops
    /// do, in whatever <see cref="ColorSpace"/> the batch draws with. Two stops on the same
    /// position make a hard edge, antialiased like a shape edge. Positions snap to a 256 step
    /// grid when the stops bake, so a stop lands within 1/512 of where it was asked for.
    /// Each color space actually drawn bakes two rows of the batch's 256 row ramp table, so
    /// build color ramps once and reuse them where possible, the same as ramps. Colors
    /// quantize to 8 bits per channel in the space's own frame; the batch's dither covers the
    /// difference the same way it covers the display's.
    /// </summary>
    public sealed class ColorRamp {
        /// <param name="stops">
        /// (position, color) pairs in [0, 1], in any order. Two stops on the same position make a
        /// hard edge, and the order they are given in decides which side is which.
        /// </param>
        /// <exception cref="ArgumentException">No stops were given.</exception>
        public ColorRamp(params (float Position, Color Color)[] stops) {
            if (stops == null || stops.Length == 0) {
                throw new ArgumentException("A color ramp needs at least one stop.", nameof(stops));
            }

            int n = stops.Length;
            _pos = new float[n];
            _colors = new Color[n];
            for (int i = 0; i < n; i++) {
                _pos[i] = MathF.Round(Math.Clamp(stops[i].Position, 0f, 1f) * Ramp.Width) / Ramp.Width;
                _colors[i] = stops[i].Color;
            }
            // Insertion sort, stable: stops sharing a position keep their given order, which is
            // what decides the two sides of a hard edge.
            for (int i = 1; i < n; i++) {
                float p = _pos[i];
                Color c = _colors[i];
                int j = i - 1;
                while (j >= 0 && _pos[j] > p) {
                    _pos[j + 1] = _pos[j];
                    _colors[j + 1] = _colors[j];
                    j--;
                }
                _pos[j + 1] = p;
                _colors[j + 1] = c;
            }
        }

        private readonly float[] _pos;
        private readonly Color[] _colors;
        // One slot per color space, baked the first time that space draws these stops: the
        // edge color row, its running integral row, and where each last resolved.
        private readonly Slot?[] _slots = new Slot?[3];
        private sealed class Slot {
            internal Slot(byte[] colors, byte[] ints) {
                Colors = colors;
                Ints = ints;
                ColorHash = RampTable.Hash(colors);
                IntHash = RampTable.Hash(ints);
            }
            internal readonly byte[] Colors;
            internal readonly byte[] Ints;
            internal readonly ulong ColorHash;
            internal readonly ulong IntHash;
            // Where each row last resolved, swapped whole; see RampSlot.
            internal RampSlot? ColorAt;
            internal RampSlot? IntAt;
        }

        // The two rows these stops occupy right now in the given table, resolved and stamped
        // like a Ramp's. The rows are independent entries, so each recycles and re-seats on
        // its own; an index is always current for its own content.
        internal (int ColorRow, int IntRow) Rows(ColorSpace space, RampTable table) {
            Slot slot = GetSlot(space);
            return (table.Resolve(slot.Colors, slot.ColorHash, ref slot.ColorAt),
                    table.Resolve(slot.Ints, slot.IntHash, ref slot.IntAt));
        }

        // Whether the table can seat both rows without evicting one an undrawn shape still
        // needs; resolving is the check, same as a Ramp's.
        internal bool TryPin(RampTable table, ColorSpace space) {
            var (c, i) = Rows(space, table);
            return c >= 0 && i >= 0;
        }

        private Slot GetSlot(ColorSpace space) {
            int s = (int)space;
            Slot? slot = _slots[s];
            if (slot == null) {
                Slot baked = Bake(space);
                lock (_slots) {
                    // A racing bake produced identical bytes, and identical bytes collapse to
                    // the same rows in a table, so first-in wins with nothing leaked.
                    slot = _slots[s] ??= baked;
                }
            }
            return slot;
        }

        // Each color texel takes two RGBA8 slots holding the color at the texel's two edges in
        // the space's remapped frame, with one-sided limits so a jump sits exactly between two
        // texels and stays a jump. The companion row keeps each channel's running integral at
        // the texel's start as unorm16 pairs, accumulated from the quantized edge bytes so the
        // shader's own trapezoids land on exactly the same numbers. The integral is what lets
        // the shader box filter the row over an AA band wider than a texel in two reads.
        private Slot Bake(ColorSpace space) {
            int n = _pos.Length;
            var val = new Vector4[n];
            for (int i = 0; i < n; i++) {
                val[i] = Palette.ToFrame(_colors[i], space);
            }
            if (space == ColorSpace.Oklch) {
                Palette.UnwrapHues(_pos, val);
            }

            var colors = new byte[Ramp.Width * 8];
            var ints = new byte[Ramp.Width * 8];
            var acc = new double[4];
            for (int i = 0; i < Ramp.Width; i++) {
                Vector4 c0 = Eval(val, i / (float)Ramp.Width, fromLeft: false);
                Vector4 c1 = Eval(val, (i + 1) / (float)Ramp.Width, fromLeft: true);
                if (space == ColorSpace.Oklch) {
                    // Hue is periodic, so each texel recenters its pair on its own turn: a
                    // crossing of the wheel bakes as a jump from 1 to 0 between two texels,
                    // which is the same angle and costs nothing.
                    float k = MathF.Floor((c0.Z + c1.Z) * 0.5f);
                    c0.Z -= k;
                    c1.Z -= k;
                }
                for (int ch = 0; ch < 4; ch++) {
                    int q0 = Q8(Get(c0, ch));
                    int q1 = Q8(Get(c1, ch));
                    colors[i * 8 + ch] = (byte)q0;
                    colors[i * 8 + 4 + ch] = (byte)q1;
                    int f = (int)Math.Round(acc[ch] / Ramp.Width * 65535.0);
                    ints[i * 8 + ch * 2] = (byte)(f & 255);
                    ints[i * 8 + ch * 2 + 1] = (byte)(f >> 8);
                    acc[ch] += (q0 + q1) / 2.0 / 255.0;
                }
            }
            return new Slot(colors, ints);
        }

        // The curve at x, approaching from one side, blended the way the shader blends two
        // stops: alpha straight, the color channels weighted by each side's alpha so a
        // transparent stop's hidden color can't tint the run. In Oklch the unwrap already
        // picked each hue's branch, so the plain lerp is the short way around the wheel.
        private Vector4 Eval(Vector4[] val, float x, bool fromLeft) {
            float[] pos = _pos;
            int n = pos.Length;
            if (fromLeft) {
                int i = 0;
                while (i < n && pos[i] < x) i++;
                if (i == n) return val[n - 1];
                if (pos[i] == x || i == 0) return val[i];
                return Blend(val[i - 1], val[i], (x - pos[i - 1]) / (pos[i] - pos[i - 1]));
            } else {
                int i = n - 1;
                while (i >= 0 && pos[i] > x) i--;
                if (i < 0) return val[0];
                if (pos[i] == x || i == n - 1) return val[i];
                return Blend(val[i], val[i + 1], (x - pos[i]) / (pos[i + 1] - pos[i]));
            }
        }
        private static Vector4 Blend(Vector4 a, Vector4 b, float t) {
            float oa = a.W + (b.W - a.W) * t;
            float tc = oa > 0f ? t * b.W / oa : t;
            return new Vector4(
                a.X + (b.X - a.X) * tc,
                a.Y + (b.Y - a.Y) * tc,
                a.Z + (b.Z - a.Z) * tc,
                oa);
        }

        private static int Q8(float v) {
            return (int)MathF.Round(Math.Clamp(v, 0f, 1f) * 255f);
        }
        private static float Get(Vector4 v, int ch) {
            return ch == 0 ? v.X : ch == 1 ? v.Y : ch == 2 ? v.Z : v.W;
        }
    }
}
