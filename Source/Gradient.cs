using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    public struct Gradient {
        public Gradient(Color aC, Vector2 aXY, Color bC, Vector2 bXY, Shape s = Shape.Linear, RepeatStyle rs = RepeatStyle.None) {
            AC = aC;
            AXY = aXY;
            BC = bC;
            BXY = bXY;
            S = s;
            RS = rs;
        }

        public Color AC;
        public Vector2 AXY;
        public Color BC;
        public Vector2 BXY;
        public Shape S;
        public RepeatStyle RS;

        public enum Shape {
            None = 0,
            Radial = 1,
            Linear = 2,
            Bilinear = 3,
            Conical = 4,
            ConicalAsym = 5,
            Square = 6,
            Cross = 7,
            // SpiralCW = 8,
            // SpiralCCW = 9,
            // Shape = 10
        }
        public enum RepeatStyle {
            None = 0,
            Sawtooth = 1,
            Triangle = 2,
            Sine = 3,
            // Clamp = 4
        }
    }
}
