// The RGBA8 form of the two glyph textures, for a backend that cannot take a float one.

using System;

namespace Apos.Shapes {
    // WebGL1 through KNI cannot upload an RGBA32F texture at all: nkast.wasm.canvas 8.0.11 wraps
    // every heap array in a Uint8Array before calling texImage2D, and WebGL rejects that view
    // type for a FLOAT upload with INVALID_OPERATION. OES_texture_float being present makes no
    // difference, the upload never reaches the driver. So on that family both glyph textures
    // become SurfaceFormat.Color and carry the same numbers as fixed point instead.
    //
    // Nothing about the layout moves: the block structure, the padding to 16 lanes, the linear
    // index addressing and the seating rewrite are the ones the float path proved. Only what one
    // texel holds changes, and the shader's two fetch helpers change with it.
    internal static class GlyphRepack {
        // Band texel: two non negative integers in 32 bits, 12 for the first and 20 for the
        // second, split on nibble boundaries so every field comes back out of a power of two
        // division that is exact in float.
        //
        // One encoding covers both roles the band texture has. A header is (curve count, list
        // offset) and a list entry is (curve texel x, curve texel y), and the shader reaches both
        // through the same helper with nothing to tell them apart by, so the two have to agree on
        // where the bits sit. 12 bits holds a count of 16 and an x up to the arena's 2048 wide
        // rows; 20 bits holds a y of 255 and a linear offset up to the 524287 the arena tops out
        // at. Together that is exactly the 32 bits an RGBA8 texel has.
        internal const int BandFirstMax = 4095;
        internal const int BandSecondMax = 1048575;

        internal static void EncodeBand(float first, float second, byte[] dst, int at) {
            int a = (int)first;
            int b = (int)second;
            dst[at] = (byte)a;
            dst[at + 1] = (byte)(((a >> 8) & 0x0F) | ((b & 0x0F) << 4));
            dst[at + 2] = (byte)(b >> 4);
            dst[at + 3] = (byte)(b >> 12);
        }

        // The shader's decode, in the same order it does it: the high nibble of g carries the
        // second value's low four bits and the low nibble carries the first value's high four.
        internal static void DecodeBand(byte r, byte g, byte b, byte a, out float first, out float second) {
            int hi = g >> 4;
            int lo = g & 0x0F;
            first = r + lo * 256;
            second = hi + b * 16 + a * 4096;
        }

        // Curve texel: control points in em units as 16 bit fixed point over [-2, 2]. A real
        // outline lives well inside a couple of ems either way, and the pad curve is placed at
        // -1.5 to stay in range, so nothing a bake produces clamps. The step is 4 / 65535, which
        // is 6.1e-05 em, or 0.031 px at an em 500 px tall.
        internal const float CurveMin = -2f;
        internal const float CurveMax = 2f;
        private const float CurveToFixed = 65535f / 4f;
        private const float CurveFromFixed = 4f / 65535f;

        internal static ushort EncodeCurve(float v) {
            float t = (v - CurveMin) * CurveToFixed;
            if (!(t > 0f)) return 0;
            if (t >= 65535f) return 65535;
            return (ushort)(int)MathF.Round(t);
        }

        internal static float DecodeCurve(ushort u) => u * CurveFromFixed + CurveMin;

        // Whether a value would have to clamp, which is what the round trip test surveys a whole
        // font for.
        internal static bool CurveInRange(float v) => v >= CurveMin && v <= CurveMax;

        // Band rows y through y + rows of an arena, into rows * width * 4 bytes.
        internal static void EncodeBandRows(float[] src, int width, int y, int rows, byte[] dst) {
            int texels = rows * width;
            for (int t = 0; t < texels; t++) {
                int s = (y * width + t) * 4;
                EncodeBand(src[s], src[s + 1], dst, t * 4);
            }
        }

        // Curve rows y through y + rows, into rows * width * 8 bytes: a logical RGBA32F texel
        // becomes two RGBA8 texels one above the other, the low bytes of its four values in row
        // 2y and the high bytes in row 2y + 1. Splitting down rather than across keeps every
        // physical dimension at or under what the float path already asks for, and a curve's
        // second texel stays in the same row as its first, which is the one thing the shader's
        // aligned fetch needs.
        internal static void EncodeCurveRows(float[] src, int width, int y, int rows, byte[] dst) {
            for (int r = 0; r < rows; r++) {
                int lo = r * width * 8;
                int hi = lo + width * 4;
                for (int x = 0; x < width; x++) {
                    int s = ((y + r) * width + x) * 4;
                    for (int c = 0; c < 4; c++) {
                        ushort u = EncodeCurve(src[s + c]);
                        dst[lo + x * 4 + c] = (byte)u;
                        dst[hi + x * 4 + c] = (byte)(u >> 8);
                    }
                }
            }
        }
    }
}
