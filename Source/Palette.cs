using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    /// <summary>
    /// Procedural gradient colors from a cosine per channel: bias + amplitude * cos(tau * (frequency * t + phase)).
    /// The idea comes from https://iquilezles.org/articles/palettes/. Channels follow the batch's
    /// <see cref="ColorSpace"/>, so in Oklab the three cosines drive lightness and the two color axes
    /// rather than red, green and blue. With whole number frequencies the palette tiles with no seam,
    /// which is what makes it pair well with <see cref="Gradient.RepeatStyle.Sawtooth"/>.
    /// </summary>
    public struct Palette {
        public Palette(Vector3 bias, Vector3 amplitude, Vector3 frequency, Vector3 phase, float alpha = 1f) {
            Bias = bias;
            Amplitude = amplitude;
            Frequency = frequency;
            Phase = phase;
            Alpha = alpha;
        }

        /// <summary>Center of each channel's oscillation, in [0, 1].</summary>
        public Vector3 Bias;
        /// <summary>How far each channel swings around its bias, in [0, 1]. The result is clamped to [0, 1].</summary>
        public Vector3 Amplitude;
        /// <summary>Cycles per gradient length. Snapped to whole numbers in [0, 15] when the shape is drawn.</summary>
        public Vector3 Frequency;
        /// <summary>Where in its cycle each channel starts, as a fraction of one cycle.</summary>
        public Vector3 Phase;
        /// <summary>Opacity of the whole palette, in [0, 1].</summary>
        public float Alpha;
    }
}
