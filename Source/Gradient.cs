using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    /// <summary>
    /// The color of a fill or a border. Every draw call takes these instead of a Color, and a Color
    /// converts implicitly into a flat one, so passing a color still works everywhere.
    /// A gradient runs between two points and two colors, and <see cref="S"/> decides how the space
    /// between them is swept: down a line, out from a center, around an angle. Colors blend in the
    /// batch's <see cref="ColorSpace"/>.
    /// The two color slots can be traded for a whole ramp: a <see cref="Palette"/> generates its
    /// colors from a cosine per channel, and a <see cref="ColorRamp"/> holds arbitrary stops. A
    /// <see cref="Ramp"/> is different, it reshapes how fast the gradient travels rather than what
    /// colors it passes through, so it pairs with either.
    /// </summary>
    public struct Gradient {
        /// <param name="aXY">Where the gradient starts, in world units unless <paramref name="isLocal"/> is set.</param>
        /// <param name="aC">Color at the start.</param>
        /// <param name="bXY">Where the gradient ends, in world units unless <paramref name="isLocal"/> is set.</param>
        /// <param name="bC">Color at the end.</param>
        /// <param name="s">How the space between the two points is swept.</param>
        /// <param name="rs">What happens outside the two points.</param>
        /// <param name="aOffset">Holds the first color solid for this many world units before it starts transitioning.</param>
        /// <param name="bOffset">Holds the second color solid for this many world units before it starts transitioning.</param>
        /// <param name="isLocal">Reads the two points relative to the shape instead of the world, so the gradient moves and rotates along with it.</param>
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

        /// <summary>
        /// Two colors travelled through a <see cref="Ramp"/>, which reshapes where the colors land
        /// without changing what they are.
        /// </summary>
        /// <param name="aXY">Where the gradient starts, in world units unless <paramref name="isLocal"/> is set.</param>
        /// <param name="aC">Color at the start.</param>
        /// <param name="bXY">Where the gradient ends, in world units unless <paramref name="isLocal"/> is set.</param>
        /// <param name="bC">Color at the end.</param>
        /// <param name="ramp">Curve the gradient value runs through first.</param>
        /// <param name="s">How the space between the two points is swept.</param>
        /// <param name="rs">What happens outside the two points.</param>
        /// <param name="aOffset">Holds the first color solid for this many world units before it starts transitioning.</param>
        /// <param name="bOffset">Holds the second color solid for this many world units before it starts transitioning.</param>
        /// <param name="isLocal">Reads the two points relative to the shape instead of the world, so the gradient moves and rotates along with it.</param>
        public Gradient(Vector2 aXY, Color aC, Vector2 bXY, Color bC, Ramp ramp, Shape s = Shape.Linear, RepeatStyle rs = RepeatStyle.None, float aOffset = 0f, float bOffset = 0f, bool isLocal = false)
            : this(aXY, aC, bXY, bC, s, rs, aOffset, bOffset, isLocal) {
            R = ramp;
        }

        /// <summary>
        /// Colors from a <see cref="Palette"/> instead of two stops, so one gradient can run through
        /// many colors. Everything else about the gradient is unchanged.
        /// </summary>
        /// <param name="aXY">Where the gradient starts, in world units unless <paramref name="isLocal"/> is set.</param>
        /// <param name="bXY">Where the gradient ends, in world units unless <paramref name="isLocal"/> is set.</param>
        /// <param name="palette">Cosine palette the colors come from.</param>
        /// <param name="s">How the space between the two points is swept.</param>
        /// <param name="rs">What happens outside the two points.</param>
        /// <param name="aOffset">Holds the first color solid for this many world units before it starts transitioning.</param>
        /// <param name="bOffset">Holds the second color solid for this many world units before it starts transitioning.</param>
        /// <param name="isLocal">Reads the two points relative to the shape instead of the world, so the gradient moves and rotates along with it.</param>
        public Gradient(Vector2 aXY, Vector2 bXY, Palette palette, Shape s = Shape.Linear, RepeatStyle rs = RepeatStyle.None, float aOffset = 0f, float bOffset = 0f, bool isLocal = false)
            : this(aXY, Color.White, bXY, Color.White, s, rs, aOffset, bOffset, isLocal) {
            (PalA, PalB) = VertexShape.PackPalette(palette, -1);
            IsPalette = true;
        }

        /// <summary>
        /// A <see cref="Palette"/> travelled through a <see cref="Ramp"/>: the palette picks the
        /// colors, the ramp picks where along the gradient they land.
        /// </summary>
        /// <param name="aXY">Where the gradient starts, in world units unless <paramref name="isLocal"/> is set.</param>
        /// <param name="bXY">Where the gradient ends, in world units unless <paramref name="isLocal"/> is set.</param>
        /// <param name="palette">Cosine palette the colors come from.</param>
        /// <param name="ramp">Curve the gradient value runs through first.</param>
        /// <param name="s">How the space between the two points is swept.</param>
        /// <param name="rs">What happens outside the two points.</param>
        /// <param name="aOffset">Holds the first color solid for this many world units before it starts transitioning.</param>
        /// <param name="bOffset">Holds the second color solid for this many world units before it starts transitioning.</param>
        /// <param name="isLocal">Reads the two points relative to the shape instead of the world, so the gradient moves and rotates along with it.</param>
        public Gradient(Vector2 aXY, Vector2 bXY, Palette palette, Ramp ramp, Shape s = Shape.Linear, RepeatStyle rs = RepeatStyle.None, float aOffset = 0f, float bOffset = 0f, bool isLocal = false)
            : this(aXY, Color.White, bXY, Color.White, s, rs, aOffset, bOffset, isLocal) {
            // Row 0 stands in: rows live per batch table, so packing resolves the real one and
            // patches it over. What matters here is the ramped layout and its flag.
            (PalA, PalB) = VertexShape.PackPalette(palette, ramp != null ? 0 : -1);
            IsPalette = true;
            R = ramp;
        }

        /// <summary>
        /// Colors from a <see cref="ColorRamp"/>'s stops instead of two stops, for gradients that
        /// pass through more colors than a pair, or that need a hard edge.
        /// </summary>
        /// <param name="aXY">Where the gradient starts, in world units unless <paramref name="isLocal"/> is set.</param>
        /// <param name="bXY">Where the gradient ends, in world units unless <paramref name="isLocal"/> is set.</param>
        /// <param name="colors">Stops the colors come from.</param>
        /// <param name="s">How the space between the two points is swept.</param>
        /// <param name="rs">What happens outside the two points.</param>
        /// <param name="aOffset">Holds the first color solid for this many world units before it starts transitioning.</param>
        /// <param name="bOffset">Holds the second color solid for this many world units before it starts transitioning.</param>
        /// <param name="isLocal">Reads the two points relative to the shape instead of the world, so the gradient moves and rotates along with it.</param>
        public Gradient(Vector2 aXY, Vector2 bXY, ColorRamp colors, Shape s = Shape.Linear, RepeatStyle rs = RepeatStyle.None, float aOffset = 0f, float bOffset = 0f, bool isLocal = false)
            : this(aXY, Color.White, bXY, Color.White, s, rs, aOffset, bOffset, isLocal) {
            Colors = colors;
        }

        /// <summary>Where the gradient starts. Relative to the shape when <see cref="IsLocal"/> is set.</summary>
        public Vector2 AXY;
        /// <summary>Color at the start. Unused when the gradient carries a palette or a color ramp.</summary>
        public Color AC;
        /// <summary>How long the first color holds solid before it starts transitioning, as a fraction of the gradient's length.</summary>
        public float AOffset;
        /// <summary>Where the gradient ends. Relative to the shape when <see cref="IsLocal"/> is set.</summary>
        public Vector2 BXY;
        /// <summary>Color at the end. Unused when the gradient carries a palette or a color ramp.</summary>
        public Color BC;
        /// <summary>How long the second color holds solid before it starts transitioning, as a fraction of the gradient's length.</summary>
        public float BOffset;
        /// <summary>How the space between the two points is swept.</summary>
        public Shape S;
        /// <summary>What happens outside the two points.</summary>
        public RepeatStyle RS;
        /// <summary>Whether the two points are read relative to the shape instead of the world.</summary>
        public bool IsLocal;
        /// <summary>Whether the colors come from a <see cref="Palette"/> rather than the two stops.</summary>
        public bool IsPalette;
        /// <summary>Curve the gradient value runs through first, or null for an even travel.</summary>
        public Ramp? R;
        /// <summary>Stops the colors come from, or null to use the two of them.</summary>
        public ColorRamp? Colors;
        internal ulong PalA;
        internal ulong PalB;

        /// <summary>
        /// How the space between the gradient's two points is swept. The value it produces is what
        /// the colors, a <see cref="Ramp"/> and a <see cref="RepeatStyle"/> all read.
        /// </summary>
        public enum Shape {
            /// <summary>A solid color. This is what the implicit <see cref="Color"/> conversion uses.</summary>
            None = 0,
            /// <summary>Transitions in a circle around the first point. The second point sets the radius.</summary>
            Radial = 1,
            /// <summary>Transitions along the line from the first point to the second point.</summary>
            Linear = 2,
            /// <summary>Like <see cref="Linear"/> but mirrored on both sides of the first point.</summary>
            Bilinear = 3,
            /// <summary>
            /// Transitions with the angle around the first point and mirrors after half a turn.
            /// The second point sets the starting direction.
            /// </summary>
            Conical = 4,
            /// <summary>Transitions with the angle around the first point over a full turn.</summary>
            ConicalAsym = 5,
            /// <summary>Transitions in a square around the first point.</summary>
            Square = 6,
            /// <summary>Transitions in a cross around the first point.</summary>
            Cross = 7,
            /// <summary>
            /// Winds clockwise around the first point, transitioning with both the angle and the
            /// distance. The second point sets the width of one winding.
            /// </summary>
            SpiralCW = 8,
            /// <summary>Like <see cref="SpiralCW"/> but winds counterclockwise.</summary>
            SpiralCCW = 9,
            // Shape = 10
        }
        /// <summary>What the gradient does past its two points.</summary>
        public enum RepeatStyle {
            /// <summary>Clamps to the second color.</summary>
            None = 0,
            /// <summary>Restarts from the first color with a hard edge.</summary>
            Sawtooth = 1,
            /// <summary>Bounces back and forth between the two colors.</summary>
            Triangle = 2,
            /// <summary>Bounces back and forth with a smooth ease.</summary>
            Sine = 3,
            // Clamp = 4
        }

        /// <summary>
        /// A flat color, which is what makes every draw call take a Color where it asks for a
        /// gradient.
        /// </summary>
        /// <param name="c">The color to fill with.</param>
        public static implicit operator Gradient(Color c) {
            return new Gradient(Vector2.Zero, c, Vector2.Zero, c, Shape.None);
        }
    }
}
