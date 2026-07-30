namespace Apos.Shapes {
    /// <summary>
    /// How far each corner of a chamfer is cut back, measured along the edges from the corner.
    /// A chamfer of 0 leaves a square corner. The constructors match <see cref="CornerRadii"/>.
    /// </summary>
    public readonly struct CornerChamfers {
        public readonly float TopLeft;
        public readonly float TopRight;
        public readonly float BottomRight;
        public readonly float BottomLeft;

        public CornerChamfers(float uniform) {
            TopLeft = uniform;
            TopRight = uniform;
            BottomRight = uniform;
            BottomLeft = uniform;
        }

        public CornerChamfers(float topLeftAndBottomRight, float topRightAndBottomLeft) {
            TopLeft = topLeftAndBottomRight;
            TopRight = topRightAndBottomLeft;
            BottomRight = topLeftAndBottomRight;
            BottomLeft = topRightAndBottomLeft;
        }

        public CornerChamfers(float topLeft, float topRightAndBottomLeft, float bottomRight) {
            TopLeft = topLeft;
            TopRight = topRightAndBottomLeft;
            BottomRight = bottomRight;
            BottomLeft = topRightAndBottomLeft;
        }

        public CornerChamfers(float topLeft, float topRight, float bottomRight, float bottomLeft) {
            TopLeft = topLeft;
            TopRight = topRight;
            BottomRight = bottomRight;
            BottomLeft = bottomLeft;
        }

        public static implicit operator CornerChamfers(float value) {
            return new CornerChamfers(value);
        }
    }
}
