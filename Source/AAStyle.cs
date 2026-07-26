namespace Apos.Shapes {
    /// <summary>
    /// Where a shape's anti-aliasing band sits against the edge it is drawn from. The two
    /// styles are exact about different things: <see cref="Outside"/> about the color asked
    /// for, <see cref="Centered"/> about the size asked for.
    /// </summary>
    public enum AAStyle {
        /// <summary>
        /// The band sits outside the edge, so the fade never eats into the shape and the color
        /// comes out as asked. Shapes that share an edge stay seamless, since both sides cover
        /// the pixels between them and the coverage adds up to one. The fade lands on pixels
        /// the shape doesn't reach, so the shape reads a little heavier than it is: bolder
        /// rather than bigger, and a thin border is where that shows.
        /// </summary>
        Outside = 0,
        /// <summary>
        /// The band straddles the edge, so a shape covers exactly the pixels its size asks for.
        /// A circle of radius 50 measures 100 across at any zoom, and a border of 2 measures 2.
        /// The band is also as wide as the pixel it crosses, 1 pixel square to the grid and √2
        /// across a diagonal, which leaves a pixel the edge misses at its own color. Pass an
        /// <c>aaSize</c> of 1 to keep whole pixels whole. Anything wider softens an edge that
        /// lands on the grid and dims the pixel beside it by the same amount. Two shapes that
        /// meet off the grid show a faint seam, since half coverage over half coverage
        /// composites to three quarters, not one.
        /// </summary>
        Centered = 1
    }
}
