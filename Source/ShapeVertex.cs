using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Apos.Shapes {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VertexShape : IVertexType {
        public VertexShape(Vector3 position, Vector2 textureCoordinate, Shape shape, Gradient fill, Gradient border, float thickness, float sdfSize, float pixelSize, Vector2 clipXY, float height = 1.0f, float aaSize = 2f, float rounded = 0f, float a = 0f, float b = 0f, float c = 0f, float d = 0f) {
            if (thickness <= 0f) {
                border = fill;
                thickness = 0f;
            }

            Position = position;
            TextureCoordinate = new Vector4(textureCoordinate, rounded, Pair((int)shape, Pair(Pair((int)fill.S, (int)fill.RS), Pair((int)border.S, (int)border.RS))));
            Fill = PairColors(fill.AC, fill.BC);
            Border = PairColors(border.AC, border.BC);

            FillCoord = new Vector4(fill.AXY.X, fill.AXY.Y, fill.BXY.X, fill.BXY.Y);
            BorderCoord = new Vector4(border.AXY.X, border.AXY.Y, border.BXY.X, border.BXY.Y);

            Meta1 = new Vector4(thickness, pixelSize * aaSize, sdfSize, height);
            Meta2 = new Vector4(a, b, c, d);
            Meta3 = new Vector4(fill.AOffset, fill.BOffset, border.AOffset, border.BOffset);
            ClipRect = new Vector4(clipXY, 0f, 0f);
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
        public Vector4 ClipRect;
        public static readonly VertexDeclaration VertexDeclaration;

        readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

        public override readonly int GetHashCode() {
            return HashCode.Combine(Position, TextureCoordinate, Fill, Border, HashCode.Combine(FillCoord, BorderCoord, Meta1, Meta2, Meta3, ClipRect));
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
                left.ClipRect == right.ClipRect;
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
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 8)
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

        private static Vector4 PairColors(Color a, Color b) {
            return new Vector4(Pair(a.R, a.G), Pair(a.B, a.A), Pair(b.R, b.G), Pair(b.B, b.A));
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
