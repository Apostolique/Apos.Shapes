using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    public readonly struct ClipSpace {
        public ClipSpace(Vector4 distances, float rounding, float aaSize) {
            Distances = distances;
            Rounding = rounding;
            AaSize = aaSize;
        }

        /// <summary>Distances to the left, top, right, bottom clip edges. Positive inside.</summary>
        public readonly Vector4 Distances;
        /// <summary>Corner radius of the clip rectangle.</summary>
        public readonly float Rounding;
        /// <summary>Antialiasing band width of the clip edge in world units. 0 gives a hard scissor edge.</summary>
        public readonly float AaSize;

        /// <summary>Far enough away that nothing gets clipped.</summary>
        public static readonly ClipSpace None = new(new Vector4(1e9f), 0f, 0f);
    }
}
