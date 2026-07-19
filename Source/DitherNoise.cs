namespace Apos.Shapes {
    /// <summary>
    /// Noise pattern used by the gradient banding dither. Both cost the same on the GPU;
    /// they differ only in the spatial structure of the noise.
    /// </summary>
    public enum DitherNoise {
        /// <summary>
        /// Interleaved gradient noise (Jimenez 2014), computed in the shader. Close to blue
        /// noise spectrally but shows faint diagonal structure at exaggerated strengths.
        /// </summary>
        InterleavedGradient,
        /// <summary>
        /// A 64x64 void-and-cluster blue noise tile. Isotropic with no visible structure,
        /// the highest quality noise for dithering.
        /// </summary>
        BlueNoise
    }
}
