namespace Apos.Shapes {
    public readonly struct CornerRadii {
        public readonly float TopLeft;
        public readonly float TopRight;
        public readonly float BottomRight;
        public readonly float BottomLeft;

        public CornerRadii(float uniform) {
            TopLeft = uniform;
            TopRight = uniform;
            BottomRight = uniform;
            BottomLeft = uniform;
        }

        public CornerRadii(float topLeftAndBottomRight, float topRightAndBottomLeft) {
            TopLeft = topLeftAndBottomRight;
            TopRight = topRightAndBottomLeft;
            BottomRight = topLeftAndBottomRight;
            BottomLeft = topRightAndBottomLeft;
        }

        public CornerRadii(float topLeft, float topRightAndBottomLeft, float bottomRight) {
            TopLeft = topLeft;
            TopRight = topRightAndBottomLeft;
            BottomRight = bottomRight;
            BottomLeft = topRightAndBottomLeft;
        }

        public CornerRadii(float topLeft, float topRight, float bottomRight, float bottomLeft) {
            TopLeft = topLeft;
            TopRight = topRight;
            BottomRight = bottomRight;
            BottomLeft = bottomLeft;
        }

        public static implicit operator CornerRadii(float value) {
            return new CornerRadii(value);
        }
    }
}
