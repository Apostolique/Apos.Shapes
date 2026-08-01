using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    /// <summary>
    /// A path point with an optional join style. A point's style applies to the joint at that point
    /// and to every following joint until another point sets a different one. Converts implicitly
    /// from <see cref="Vector2"/> and from (position, join) tuples.
    /// </summary>
    public readonly struct PathPoint {
        /// <param name="position">Where the point sits.</param>
        /// <param name="join">Join style from this joint on, or null to keep the one already in effect.</param>
        public PathPoint(Vector2 position, PathJoin? join = null) {
            Position = position;
            Join = join;
        }

        /// <summary>Where the point sits.</summary>
        public readonly Vector2 Position;
        /// <summary>Join style from this joint on, or null to keep the one already in effect.</summary>
        public readonly PathJoin? Join;

        /// <summary>A point that keeps whatever join style is already in effect.</summary>
        /// <param name="position">Where the point sits.</param>
        public static implicit operator PathPoint(Vector2 position) => new(position);
        /// <summary>A point that switches the join style, so a point list can be written as tuples.</summary>
        /// <param name="p">The position and the join style to switch to.</param>
        public static implicit operator PathPoint((Vector2 Position, PathJoin Join) p) => new(p.Position, p.Join);
    }

    /// <summary>
    /// How a path's segments connect at a joint. Joints whose segments are shorter than the stroke
    /// radius, or that fold back on themselves, fall back to round.
    /// </summary>
    public enum PathJoin {
        /// <summary>The outer corner is filled by an arc of the stroke radius.</summary>
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
        /// <summary>The end is capped with a half circle of the stroke radius.</summary>
        Round = 0,
        /// <summary>The end is cut flat at the endpoint.</summary>
        Butt = 1,
        /// <summary>The end is cut flat one radius past the endpoint.</summary>
        Square = 2
    }
}
