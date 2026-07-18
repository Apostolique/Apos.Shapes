namespace Apos.Shapes {
    /// <summary>
    /// Color space that gradient and border colors are interpolated in.
    /// </summary>
    public enum ColorSpace {
        /// <summary>Holds chroma and takes the shortest path around the hue wheel. Keeps colors vivid.</summary>
        Oklch = 0,
        /// <summary>Interpolates in a straight line through Oklab. Distant hues pass through gray.</summary>
        Oklab = 1,
        /// <summary>Interpolates the raw sRGB channels.</summary>
        Rgb = 2
    }
}
