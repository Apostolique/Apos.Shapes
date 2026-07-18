using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Apos.Shapes {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VertexShape : IVertexType {
        public VertexShape(Vector3 position, Vector2 textureCoordinate, Shape shape, Gradient fill, Gradient border, float thickness, float sdfSize, float pixelSize, ClipSpace clip, float height = 1.0f, float aaSize = 1.5f, float rounded = 0f, float a = 0f, float b = 0f, float c = 0f, float d = 0f) {
            if (thickness <= 0f) {
                border = fill;
                thickness = 0f;
            }

            Position = position;
            TextureCoordinate = new Vector4(textureCoordinate, rounded, Pair((int)shape, Pair(Pair((int)fill.S, (int)fill.RS), Pair((int)border.S, (int)border.RS))));
            if (shape == Shape.Texture || shape == Shape.String) {
                // Texture masks are multiplied in RGBA space, everything else is blended in Oklab.
                Fill = PairColorsRgb(fill.AC, fill.BC);
                Border = PairColorsRgb(border.AC, border.BC);
            } else {
                Fill = PairColorsOklab(fill.AC, fill.BC);
                Border = PairColorsOklab(border.AC, border.BC);
            }

            FillCoord = new Vector4(fill.AXY.X, fill.AXY.Y, fill.BXY.X, fill.BXY.Y);
            BorderCoord = new Vector4(border.AXY.X, border.AXY.Y, border.BXY.X, border.BXY.Y);

            Meta1 = new Vector4(thickness, pixelSize * aaSize, sdfSize, height);
            Meta2 = new Vector4(a, b, c, d);
            Meta3 = new Vector4(fill.AOffset, fill.BOffset, border.AOffset, border.BOffset);
            ClipDistances = clip.Distances;
            ClipRounding = clip.Rounding;
            ClipAaSize = clip.AaSize;
        }

        public Vector3 Position;
        public Vector4 TextureCoordinate;
        public Vector4 Fill;
        public Vector4 Border;
        public Vector4 FillCoord;
        public Vector4 BorderCoord;
        public Vector4 Meta1;
        public Vector4 Meta2;
        public Vector4 Meta3;
        public Vector4 ClipDistances;
        public float ClipRounding;
        public float ClipAaSize;
        public static readonly VertexDeclaration VertexDeclaration;

        readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

        public override readonly int GetHashCode() {
            return HashCode.Combine(Position, TextureCoordinate, Fill, Border, HashCode.Combine(FillCoord, BorderCoord, Meta1, Meta2, Meta3, ClipDistances, ClipRounding, ClipAaSize));
        }

        public override readonly string ToString() {
            return
                "{{Position:" + Position +
                " Fill:" + Fill +
                " Border:" + Border +
                " FillCoord:" + FillCoord +
                " BorderCoord:" + BorderCoord +
                " TextureCoordinate:" + TextureCoordinate +
                " Thickness:" + Meta1.X +
                " Shape:" + Meta1.Y +
                " PixelSize:" + Meta1.Z +
                " Width:" + Meta1.W +
                "}}";
        }

        public static bool operator ==(VertexShape left, VertexShape right) {
            return
                left.Position == right.Position &&
                left.TextureCoordinate == right.TextureCoordinate &&
                left.Fill == right.Fill &&
                left.Border == right.Border &&
                left.FillCoord == right.FillCoord &&
                left.BorderCoord == right.BorderCoord &&
                left.Meta1 == right.Meta1 &&
                left.Meta2 == right.Meta2 &&
                left.Meta3 == right.Meta3 &&
                left.ClipDistances == right.ClipDistances &&
                left.ClipRounding == right.ClipRounding &&
                left.ClipAaSize == right.ClipAaSize;
        }

        public static bool operator !=(VertexShape left, VertexShape right) {
            return !(left == right);
        }

        public override readonly bool Equals(object? obj) {
            if (obj == null)
                return false;

            if (obj.GetType() != GetType())
                return false;

            return this == ((VertexShape)obj);
        }

        public enum Shape {
            Circle = 0,
            Rectangle = 1,
            Line = 2,
            Hexagon = 3,
            EquilateralTriangle = 4,
            Triangle = 5,
            Ellipse = 6,
            Arc = 7,
            Ring = 8,
            Texture = 9,
            String = 10
        }

        static VertexShape() {
            int offset = 0;
            var elements = new VertexElement[] {
                GetVertexElement(ref offset, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 0),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 1),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 2),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 4),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 5),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 6),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 7),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 8),
                GetVertexElement(ref offset, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 9)
            };
            VertexDeclaration = new VertexDeclaration(elements);
        }
        private static VertexElement GetVertexElement(ref int offset, VertexElementFormat f, VertexElementUsage u, int usageIndex) {
            return new VertexElement(OffsetInline(ref offset, Offsets[f]), f, u, usageIndex);
        }
        private static int OffsetInline(ref int value, int offset) {
            int old = value;
            value += offset;
            return old;
        }
        private static readonly Dictionary<VertexElementFormat, int> Offsets = new() {
            [VertexElementFormat.Single] = 4,
            [VertexElementFormat.Vector2] = 8,
            [VertexElementFormat.Vector3] = 12,
            [VertexElementFormat.Vector4] = 16,
            [VertexElementFormat.Color] = 4,
            [VertexElementFormat.Byte4] = 4,
            [VertexElementFormat.Short2] = 4,
            [VertexElementFormat.Short4] = 8,
            [VertexElementFormat.NormalizedShort2] = 4,
            [VertexElementFormat.NormalizedShort4] = 8,
            [VertexElementFormat.HalfVector2] = 4,
            [VertexElementFormat.HalfVector4] = 8,
        };

        // Colors are packed as two 11 bit channels per float. 11 bits keeps Pair() results
        // exactly representable in a float (max 2^22 - 1) while exceeding 8 bit sRGB precision.
        private const int _channelMax = 2047;

        private static Vector4 PairColorsRgb(Color a, Color b) {
            return new Vector4(
                Pair(QuantizeByte(a.R), QuantizeByte(a.G)), Pair(QuantizeByte(a.B), QuantizeByte(a.A)),
                Pair(QuantizeByte(b.R), QuantizeByte(b.G)), Pair(QuantizeByte(b.B), QuantizeByte(b.A)));
        }
        private static Vector4 PairColorsOklab(Color a, Color b) {
            Vector2 pa = PackOklab(a);
            Vector2 pb = PackOklab(b);
            return new Vector4(pa.X, pa.Y, pb.X, pb.Y);
        }

        private static int QuantizeByte(byte v) {
            return (v * _channelMax + 128) / 255;
        }
        private static int QuantizeUnit(float v) {
            return (int)(Math.Clamp(v, 0f, 1f) * _channelMax + 0.5f);
        }

        private static Vector2 PackOklab(Color c) {
            float r = _srgbToLinear[c.R];
            float g = _srgbToLinear[c.G];
            float b = _srgbToLinear[c.B];

            float l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
            float m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
            float s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);

            float okL = 0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s;
            float okA = 1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s;
            float okB = 0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s;

            // a and b are remapped from [-0.4, 0.4] which covers the whole sRGB gamut.
            return new Vector2(
                Pair(QuantizeUnit(okL), QuantizeUnit(okA * 1.25f + 0.5f)),
                Pair(QuantizeUnit(okB * 1.25f + 0.5f), QuantizeByte(c.A)));
        }

        private static readonly float[] _srgbToLinear = CreateSrgbToLinear();
        private static float[] CreateSrgbToLinear() {
            var table = new float[256];
            for (int i = 0; i < 256; i++) {
                float c = i / 255f;
                table[i] = c >= 0.04045f ? MathF.Pow((c + 0.055f) / 1.055f, 2.4f) : c / 12.92f;
            }
            return table;
        }

        private static int Pair(int a, int b) {
            return a >= b ? a * a + a + b : b * b + a;
        }
        private static (int, int) Unpair(int n) {
            int f1 = (int)Math.Sqrt(n);
            int f2 = n - f1 * f1;
            int a, b;
            if (f2 < f1) {
                a = f2;
                b = f1;
            } else {
                a = f1;
                b = f2 - f1;
            }
            return (a, b);
        }
    }
}
