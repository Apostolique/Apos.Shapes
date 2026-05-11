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

        public CornerRadii(float topLeft, float topRight, float bottomRight, float bottomLeft) {
            TopLeft = topLeft;
            TopRight = topRight;
            BottomRight = bottomRight;
            BottomLeft = bottomLeft;
        }
    }
}
