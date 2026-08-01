namespace Apos.Shapes {
    /// <summary>
    /// How far each corner of a chamfer is cut back, measured along the edges from the corner.
    /// A chamfer of 0 leaves a square corner. The constructors match <see cref="CornerRadii"/>.
    /// </summary>
    public readonly struct CornerChamfers {
        /// <summary>Cut of the top left corner.</summary>
        public readonly float TopLeft;
        /// <summary>Cut of the top right corner.</summary>
        public readonly float TopRight;
        /// <summary>Cut of the bottom right corner.</summary>
        public readonly float BottomRight;
        /// <summary>Cut of the bottom left corner.</summary>
        public readonly float BottomLeft;

        /// <summary>The same cut on all four corners.</summary>
        /// <param name="uniform">Cut of every corner.</param>
        public CornerChamfers(float uniform) {
            TopLeft = uniform;
            TopRight = uniform;
            BottomRight = uniform;
            BottomLeft = uniform;
        }

        /// <summary>One cut per diagonal, like the two value form of the CSS border-radius shorthand.</summary>
        /// <param name="topLeftAndBottomRight">Cut of the top left and bottom right corners.</param>
        /// <param name="topRightAndBottomLeft">Cut of the top right and bottom left corners.</param>
        public CornerChamfers(float topLeftAndBottomRight, float topRightAndBottomLeft) {
            TopLeft = topLeftAndBottomRight;
            TopRight = topRightAndBottomLeft;
            BottomRight = topLeftAndBottomRight;
            BottomLeft = topRightAndBottomLeft;
        }

        /// <summary>
        /// Both ends of one diagonal on their own and the other diagonal shared, like the three
        /// value form of the CSS border-radius shorthand.
        /// </summary>
        /// <param name="topLeft">Cut of the top left corner.</param>
        /// <param name="topRightAndBottomLeft">Cut of the top right and bottom left corners.</param>
        /// <param name="bottomRight">Cut of the bottom right corner.</param>
        public CornerChamfers(float topLeft, float topRightAndBottomLeft, float bottomRight) {
            TopLeft = topLeft;
            TopRight = topRightAndBottomLeft;
            BottomRight = bottomRight;
            BottomLeft = topRightAndBottomLeft;
        }

        /// <summary>A cut per corner, clockwise from the top left.</summary>
        /// <param name="topLeft">Cut of the top left corner.</param>
        /// <param name="topRight">Cut of the top right corner.</param>
        /// <param name="bottomRight">Cut of the bottom right corner.</param>
        /// <param name="bottomLeft">Cut of the bottom left corner.</param>
        public CornerChamfers(float topLeft, float topRight, float bottomRight, float bottomLeft) {
            TopLeft = topLeft;
            TopRight = topRight;
            BottomRight = bottomRight;
            BottomLeft = bottomLeft;
        }

        /// <summary>Cuts all four corners by the same amount.</summary>
        /// <param name="value">Cut of every corner.</param>
        public static implicit operator CornerChamfers(float value) {
            return new CornerChamfers(value);
        }
    }
}
