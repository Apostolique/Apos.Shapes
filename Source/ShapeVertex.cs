using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Apos.Shapes {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VertexShape : IVertexType {
        public VertexShape(Vector3 position, Vector2 textureCoordinate, float shape, Color c1, Color c2, float thickness, float sdfSize, float pixelSize, float height = 1.0f, float aaSize = 2f, float rounded = 0f, float a = 0f, float b = 0f, float c = 0f, float d = 0f) {
            if (thickness <= 0f) {
                c2 = c1;
                thickness = 0f;
            }

            Position = position;
            TextureCoordinate = textureCoordinate;
            Color1 = c1;
            Color2 = c2;

            Meta1 = new Vector4(thickness, shape, sdfSize, height);
            Meta2 = new Vector4(pixelSize, aaSize, rounded, 0f);
            Meta3 = new Vector4(a, b, c, d);
        }

        public Vector3 Position;
        public Vector2 TextureCoordinate;
        public Color Color1;
        public Color Color2;
        public Vector4 Meta1;
        public Vector4 Meta2;
        public Vector4 Meta3;
        public static readonly VertexDeclaration VertexDeclaration;

        readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

        public override readonly int GetHashCode() {
            return System.HashCode.Combine(Position, TextureCoordinate, Color1, Color2, Meta1);
        }

        public override readonly string ToString() {
            return
                "{{Position:" + Position +
                " Color1:" + Color1 +
                " Color2:" + Color2 +
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
                left.Color1 == right.Color1 &&
                left.Color2 == right.Color2 &&
                left.Meta1 == right.Meta1 &&
                left.Meta2 == right.Meta2 &&
                left.Meta3 == right.Meta3;
        }

        public static bool operator !=(VertexShape left, VertexShape right) {
            return !(left == right);
        }

        public override readonly bool Equals(object obj) {
            if (obj == null)
                return false;

            if (obj.GetType() != GetType())
                return false;

            return this == ((VertexShape)obj);
        }

        static VertexShape() {
            int offset = 0;
            var elements = new VertexElement[] {
                GetVertexElement(ref offset, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                GetVertexElement(ref offset, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
                GetVertexElement(ref offset, VertexElementFormat.Color, VertexElementUsage.Color, 0),
                GetVertexElement(ref offset, VertexElementFormat.Color, VertexElementUsage.Color, 1),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 1),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 2),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3),
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
        private static readonly Dictionary<VertexElementFormat, int> Offsets = new Dictionary<VertexElementFormat, int>() {
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
    }
}
