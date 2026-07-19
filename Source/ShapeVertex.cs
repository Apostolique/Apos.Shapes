using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Apos.Shapes {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VertexShape : IVertexType {
        public VertexShape(Vector3 position, Vector2 textureCoordinate, Shape shape, Gradient fill, Gradient border, float thickness, float sdfSize, ClipSpace clip, float height = 1.0f, float aaSize = 1.5f, float rounded = 0f, float a = 0f, float b = 0f, float c = 0f, float d = 0f, ColorSpace colorSpace = ColorSpace.Oklab) {
            if (thickness <= 0f) {
                border = fill;
                thickness = 0f;
            }

            if (shape == Shape.Texture || shape == Shape.String) {
                // Texture masks are multiplied in RGBA space, everything else is blended in the chosen color space.
                colorSpace = ColorSpace.Rgb;
            }

            Position = position;
            TextureCoordinate = new Vector4(textureCoordinate, rounded, PackMeta(shape, fill, border, colorSpace));

            if (colorSpace == ColorSpace.Oklch) {
                (FillA, FillB) = PackOklchPair(fill.AC, fill.BC);
                (BorderA, BorderB) = PackOklchPair(border.AC, border.BC);
            } else if (colorSpace == ColorSpace.Oklab) {
                FillA = PackOklab(fill.AC);
                FillB = PackOklab(fill.BC);
                BorderA = PackOklab(border.AC);
                BorderB = PackOklab(border.BC);
            } else {
                FillA = PackRgb(fill.AC);
                FillB = PackRgb(fill.BC);
                BorderA = PackRgb(border.AC);
                BorderB = PackRgb(border.BC);
            }

            FillCoord = new Vector4(fill.AXY.X, fill.AXY.Y, fill.BXY.X, fill.BXY.Y);
            BorderCoord = new Vector4(border.AXY.X, border.AXY.Y, border.BXY.X, border.BXY.Y);

            // The AA width travels in pixels; the shader scales it by the per-pixel
            // world footprint it derives from screen-space derivatives.
            Meta1 = new Vector4(thickness, aaSize, sdfSize, height);
            Meta2 = new Vector4(a, b, c, d);
            Meta3 = new Vector4(fill.AOffset, fill.BOffset, border.AOffset, border.BOffset);
            ClipDistances = clip.Distances;
            ClipRounding = clip.Rounding;
            ClipAaSize = clip.AaSize;
        }

        public Vector3 Position;
        public Vector4 TextureCoordinate;
        public ulong FillA;
        public ulong FillB;
        public ulong BorderA;
        public ulong BorderB;
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
            return HashCode.Combine(Position, TextureCoordinate, HashCode.Combine(FillA, FillB, BorderA, BorderB), HashCode.Combine(FillCoord, BorderCoord, Meta1, Meta2, Meta3, ClipDistances, ClipRounding, ClipAaSize));
        }

        public override readonly string ToString() {
            return
                "{{Position:" + Position +
                " FillA:" + FillA +
                " FillB:" + FillB +
                " BorderA:" + BorderA +
                " BorderB:" + BorderB +
                " FillCoord:" + FillCoord +
                " BorderCoord:" + BorderCoord +
                " TextureCoordinate:" + TextureCoordinate +
                " Thickness:" + Meta1.X +
                " PixelSize:" + Meta1.Z +
                " Width:" + Meta1.W +
                "}}";
        }

        public static bool operator ==(VertexShape left, VertexShape right) {
            return
                left.Position == right.Position &&
                left.TextureCoordinate == right.TextureCoordinate &&
                left.FillA == right.FillA &&
                left.FillB == right.FillB &&
                left.BorderA == right.BorderA &&
                left.BorderB == right.BorderB &&
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
            String = 10,
            Path = 11
        }

        static VertexShape() {
            int offset = 0;
            var elements = new VertexElement[] {
                GetVertexElement(ref offset, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 0),
                GetVertexElement(ref offset, VertexElementFormat.NormalizedShort4, VertexElementUsage.TextureCoordinate, 1),
                GetVertexElement(ref offset, VertexElementFormat.NormalizedShort4, VertexElementUsage.TextureCoordinate, 2),
                GetVertexElement(ref offset, VertexElementFormat.NormalizedShort4, VertexElementUsage.TextureCoordinate, 3),
                GetVertexElement(ref offset, VertexElementFormat.NormalizedShort4, VertexElementUsage.TextureCoordinate, 4),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 5),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 6),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 7),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 8),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 9),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.Position, 1),
                GetVertexElement(ref offset, VertexElementFormat.Vector2, VertexElementUsage.Normal, 0)
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

        // The shape uses 4 bits, gradient shapes 4 bits each, repeat styles 2 bits each and the
        // color space 4 bits. The total stays under 2^20 so it survives the trip through a float exactly.
        private static float PackMeta(Shape shape, Gradient fill, Gradient border, ColorSpace colorSpace) {
            return (int)shape + 16 * ((int)fill.S + 16 * ((int)fill.RS + 4 * ((int)border.S + 16 * ((int)border.RS + 4 * (int)colorSpace))));
        }

        // Colors are stored as four 16 bit normalized shorts. Only the positive half of the snorm
        // range is used so every channel arrives in [0, 1] on the GPU.
        private static ulong PackColor(Vector4 v) {
            return PackChannel(v.X) | PackChannel(v.Y) << 16 | PackChannel(v.Z) << 32 | PackChannel(v.W) << 48;
        }
        private static ulong PackChannel(float v) {
            return (ulong)(int)(Math.Clamp(v, 0f, 1f) * 32767f + 0.5f);
        }

        private static ulong PackRgb(Color c) {
            return PackColor(new Vector4(c.R, c.G, c.B, c.A) / 255f);
        }
        private static ulong PackOklab(Color c) {
            Vector3 lab = ToOklab(c);
            // a and b are remapped from [-0.4, 0.4] which covers the whole sRGB gamut.
            return PackColor(new Vector4(lab.X, lab.Y * 1.25f + 0.5f, lab.Z * 1.25f + 0.5f, c.A / 255f));
        }
        private static (ulong, ulong) PackOklchPair(Color a, Color b) {
            Vector3 labA = ToOklab(a);
            Vector3 labB = ToOklab(b);
            float chromaA = MathF.Sqrt(labA.Y * labA.Y + labA.Z * labA.Z);
            float chromaB = MathF.Sqrt(labB.Y * labB.Y + labB.Z * labB.Z);
            float hueA = MathF.Atan2(labA.Z, labA.Y);
            float hueB = MathF.Atan2(labB.Z, labB.Y);

            // Grays have no hue of their own, they take the other stop's hue so the lerp doesn't drift.
            const float achromatic = 1e-4f;
            if (chromaA < achromatic) hueA = chromaB < achromatic ? 0f : hueB;
            if (chromaB < achromatic) hueB = chromaA < achromatic ? 0f : hueA;

            return (PackOklch(labA.X, chromaA, hueA, a.A), PackOklch(labB.X, chromaB, hueB, b.A));
        }
        private static ulong PackOklch(float l, float chroma, float hue, byte alpha) {
            // Chroma is remapped from [0, 0.4], hue from [-pi, pi].
            return PackColor(new Vector4(l, chroma * 2.5f, hue / MathF.Tau + 0.5f, alpha / 255f));
        }

        private static Vector3 ToOklab(Color c) {
            float r = _srgbToLinear[c.R];
            float g = _srgbToLinear[c.G];
            float b = _srgbToLinear[c.B];

            float l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
            float m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
            float s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);

            return new Vector3(
                0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
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
    }
}
