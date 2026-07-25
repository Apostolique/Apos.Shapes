using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Apos.Shapes {
    /// <summary>
    /// Arc length along an ellipse, which is what dashing one needs and the only shape whose
    /// contour coordinate has no closed form: it is an incomplete elliptic integral of the second
    /// kind. The perimeter itself is computed exactly here, per shape; the shader gets a table.
    /// </summary>
    internal static class EllipseArc {
        /// <summary>Columns of the table, along the contour.</summary>
        public const int Width = 256;
        /// <summary>Rows of the table, over the aspect ratio.</summary>
        public const int Height = 64;

        /// <summary>
        /// Quarter of the perimeter, the arc from the tip of the major axis to the tip of the minor
        /// one. That is a * E(m), the complete elliptic integral of the second kind with modulus
        /// m = 1 - (b/a)^2, evaluated by the arithmetic-geometric mean, which converges quadratically
        /// and lands within a couple of ulps for every aspect ratio.
        /// </summary>
        public static float Quarter(float radius1, float radius2) {
            double a = Math.Max(radius1, radius2);
            double b = Math.Min(radius1, radius2);
            if (!(a > 0d)) return 0f;
            if (b <= 0d) return (float)a; // A collapsed minor axis is a segment walked one way.

            double rho = b / a;
            double m = 1d - rho * rho;
            double an = 1d;
            double bn = rho;
            double sum = 0.5d * m; // The n = 0 term, 2^-1 * c0^2 with c0^2 = m.
            double pow2 = 1d;
            for (int i = 0; i < 40; i++) {
                double cn = 0.5d * (an - bn);
                double next = 0.5d * (an + bn);
                bn = Math.Sqrt(an * bn);
                an = next;
                sum += pow2 * cn * cn;
                pow2 *= 2d;
                if (cn == 0d) break;
            }
            return (float)(a * (Math.PI / (2d * an)) * (1d - sum));
        }

        /// <summary>
        /// Uploads the table the shader walks the contour with. Rows are the aspect ratio b/a,
        /// columns run along one quadrant, which symmetry extends to the whole ellipse. Two maps
        /// are needed and they want different parameterizations, so each texel packs both as 16 bit
        /// fractions: the inverse, theta at a given arc length, in RG, and the forward, arc length
        /// at a given theta, in BA. Both axes are sqrt warped.
        ///
        /// The bytes are baked, like the shader and the blue noise tile, because computing them is
        /// ~30 ms of quadrature and root finding, which the first dashed ellipse would pay as a two
        /// frame hitch. See Tools/EllipseArcGen, which produces the file and carries the math and
        /// its reasoning.
        /// </summary>
        public static Texture2D CreateTexture(GraphicsDevice graphicsDevice) {
            using var stream = typeof(EllipseArc).Assembly.GetManifestResourceStream("Apos.Shapes.ellipse-arc.lut")
                ?? throw new InvalidOperationException("Missing embedded resource \"Apos.Shapes.ellipse-arc.lut\".");
            byte[] bytes = new byte[Width * Height * 4];
            stream.ReadExactly(bytes);

            var texture = new Texture2D(graphicsDevice, Width, Height, false, SurfaceFormat.Color);
            texture.SetData(bytes);
            return texture;
        }
    }
}
