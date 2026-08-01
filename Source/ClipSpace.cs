using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    /// <summary>
    /// One vertex's view of the clip rectangle set by <see cref="ShapeBatch.SetClipRect"/>, carried
    /// as distances to the four edges so the shader can mask a shape without breaking the batch.
    /// <see cref="ShapeBatch"/> fills this in per vertex, so you only build one by hand when packing
    /// a <see cref="VertexShape"/> yourself. Use <see cref="None"/> for no clipping.
    /// </summary>
    public readonly struct ClipSpace {
        /// <param name="distances">Distances to the left, top, right, bottom clip edges. Positive inside.</param>
        /// <param name="rounding">Corner radius of the clip rectangle.</param>
        /// <param name="aaSize">Antialiasing band width of the clip edge in pixels. 0 gives a hard scissor edge.</param>
        public ClipSpace(Vector4 distances, float rounding, float aaSize) {
            Distances = distances;
            Rounding = rounding;
            AaSize = aaSize;
        }

        /// <summary>Distances to the left, top, right, bottom clip edges. Positive inside.</summary>
        public readonly Vector4 Distances;
        /// <summary>Corner radius of the clip rectangle.</summary>
        public readonly float Rounding;
        /// <summary>Antialiasing band width of the clip edge in pixels. 0 gives a hard scissor edge.</summary>
        public readonly float AaSize;

        /// <summary>Far enough away that nothing gets clipped.</summary>
        public static readonly ClipSpace None = new(new Vector4(1e9f), 0f, 0f);
    }
}
