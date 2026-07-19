using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    /// <summary>
    /// A path point with an optional join style. A point's style applies to the joint at that point
    /// and to every following joint until another point sets a different one. Converts implicitly
    /// from <see cref="Vector2"/> and from (position, join) tuples.
    /// </summary>
    public readonly struct PathPoint {
        public PathPoint(Vector2 position, PathJoin? join = null) {
            Position = position;
            Join = join;
        }

        public readonly Vector2 Position;
        public readonly PathJoin? Join;

        public static implicit operator PathPoint(Vector2 position) => new(position);
        public static implicit operator PathPoint((Vector2 Position, PathJoin Join) p) => new(p.Position, p.Join);
    }

    /// <summary>
    /// How a path's segments connect at a joint. Joints whose segments are shorter than the stroke
    /// radius, or that fold back on themselves, fall back to round.
    /// </summary>
    public enum PathJoin {
        Round = 0,
        /// <summary>Sharp corner. Falls back to bevel past the miter limit, like SVG.</summary>
        Miter = 1,
        /// <summary>The outer corner is cut flat between the two edges.</summary>
        Bevel = 2
    }
    /// <summary>
    /// How a path ends. Butt stops at the endpoint, square extends past it by the radius.
    /// </summary>
    public enum PathCap {
        Round = 0,
        Butt = 1,
        Square = 2
    }
}
