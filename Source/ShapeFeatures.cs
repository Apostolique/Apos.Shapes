using System;

namespace Apos.Shapes {
    /// <summary>
    /// The parts of the shader that cost the most to compile. <see cref="ShapeBatch.Warmup"/>
    /// takes them so a game can compile the version of the shader it draws with ahead of time.
    /// </summary>
    [Flags]
    public enum ShapeFeatures {
        /// <summary>Solid colors, textures and blur, which every version has.</summary>
        None = 0,
        /// <summary>Any <see cref="DashStyle"/>.</summary>
        Dashes = 1,
        /// <summary>A fill <see cref="Gradient"/> that isn't a flat color, which includes palettes and ramps.</summary>
        Gradients = 2,
        /// <summary>A border <see cref="Gradient"/> that isn't a flat color and isn't the fill's own.</summary>
        BorderGradients = 4,
        /// <summary>Text and SVG drawings.</summary>
        Text = 8,
        /// <summary>Everything.</summary>
        All = Dashes | Gradients | BorderGradients | Text
    }
}
