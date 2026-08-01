using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    public struct Gradient {
        public Gradient(Vector2 aXY, Color aC, Vector2 bXY, Color bC, Shape s = Shape.Linear, RepeatStyle rs = RepeatStyle.None, float aOffset = 0f, float bOffset = 0f, bool isLocal = false) {
            if (aOffset != 0 || bOffset != 0) {
                float length = Vector2.Distance(aXY, bXY);
                if (length > 0) {
                    aOffset /= length;
                    bOffset /= length;
                } else {
                    aOffset = 0;
                    bOffset = 0;
                }
            }

            AC = aC;
            AXY = aXY;
            AOffset = aOffset;
            BC = bC;
            BXY = bXY;
            BOffset = bOffset;
            S = s;
            RS = rs;
            IsLocal = isLocal;
        }

        public Gradient(Vector2 aXY, Color aC, Vector2 bXY, Color bC, Ramp ramp, Shape s = Shape.Linear, RepeatStyle rs = RepeatStyle.None, float aOffset = 0f, float bOffset = 0f, bool isLocal = false)
            : this(aXY, aC, bXY, bC, s, rs, aOffset, bOffset, isLocal) {
            R = ramp;
        }

        public Gradient(Vector2 aXY, Vector2 bXY, Palette palette, Shape s = Shape.Linear, RepeatStyle rs = RepeatStyle.None, float aOffset = 0f, float bOffset = 0f, bool isLocal = false)
            : this(aXY, Color.White, bXY, Color.White, s, rs, aOffset, bOffset, isLocal) {
            (PalA, PalB) = VertexShape.PackPalette(palette, -1);
            IsPalette = true;
        }

        public Gradient(Vector2 aXY, Vector2 bXY, Palette palette, Ramp ramp, Shape s = Shape.Linear, RepeatStyle rs = RepeatStyle.None, float aOffset = 0f, float bOffset = 0f, bool isLocal = false)
            : this(aXY, Color.White, bXY, Color.White, s, rs, aOffset, bOffset, isLocal) {
            // Row 0 stands in: rows live per batch table, so packing resolves the real one and
            // patches it over. What matters here is the ramped layout and its flag.
            (PalA, PalB) = VertexShape.PackPalette(palette, ramp != null ? 0 : -1);
            IsPalette = true;
            R = ramp;
        }

        public Gradient(Vector2 aXY, Vector2 bXY, ColorRamp colors, Shape s = Shape.Linear, RepeatStyle rs = RepeatStyle.None, float aOffset = 0f, float bOffset = 0f, bool isLocal = false)
            : this(aXY, Color.White, bXY, Color.White, s, rs, aOffset, bOffset, isLocal) {
            Colors = colors;
        }

        public Vector2 AXY;
        public Color AC;
        public float AOffset;
        public Vector2 BXY;
        public Color BC;
        public float BOffset;
        public Shape S;
        public RepeatStyle RS;
        public bool IsLocal;
        public bool IsPalette;
        public Ramp? R;
        public ColorRamp? Colors;
        internal ulong PalA;
        internal ulong PalB;

        public enum Shape {
            None = 0,
            Radial = 1,
            Linear = 2,
            Bilinear = 3,
            Conical = 4,
            ConicalAsym = 5,
            Square = 6,
            Cross = 7,
            SpiralCW = 8,
            SpiralCCW = 9,
            // Shape = 10
        }
        public enum RepeatStyle {
            None = 0,
            Sawtooth = 1,
            Triangle = 2,
            Sine = 3,
            // Clamp = 4
        }

        public static implicit operator Gradient(Color c) {
            return new Gradient(Vector2.Zero, c, Vector2.Zero, c, Shape.None);
        }
    }
}
