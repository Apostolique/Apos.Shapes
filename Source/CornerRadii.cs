namespace Apos.Shapes {
    /// <summary>
    /// The corner radius of each corner of a rectangle, in world units. A radius of 0 leaves a
    /// square corner. Each one is clamped to half the rectangle's smaller side when the shape is
    /// drawn. Converts implicitly from a float, so a single number rounds all four the same.
    /// </summary>
    public readonly struct CornerRadii {
        /// <summary>Radius of the top left corner.</summary>
        public readonly float TopLeft;
        /// <summary>Radius of the top right corner.</summary>
        public readonly float TopRight;
        /// <summary>Radius of the bottom right corner.</summary>
        public readonly float BottomRight;
        /// <summary>Radius of the bottom left corner.</summary>
        public readonly float BottomLeft;

        /// <summary>The same radius on all four corners.</summary>
        /// <param name="uniform">Radius of every corner.</param>
        public CornerRadii(float uniform) {
            TopLeft = uniform;
            TopRight = uniform;
            BottomRight = uniform;
            BottomLeft = uniform;
        }

        /// <summary>One radius per diagonal, like the two value form of the CSS border-radius shorthand.</summary>
        /// <param name="topLeftAndBottomRight">Radius of the top left and bottom right corners.</param>
        /// <param name="topRightAndBottomLeft">Radius of the top right and bottom left corners.</param>
        public CornerRadii(float topLeftAndBottomRight, float topRightAndBottomLeft) {
            TopLeft = topLeftAndBottomRight;
            TopRight = topRightAndBottomLeft;
            BottomRight = topLeftAndBottomRight;
            BottomLeft = topRightAndBottomLeft;
        }

        /// <summary>
        /// Both ends of one diagonal on their own and the other diagonal shared, like the three
        /// value form of the CSS border-radius shorthand.
        /// </summary>
        /// <param name="topLeft">Radius of the top left corner.</param>
        /// <param name="topRightAndBottomLeft">Radius of the top right and bottom left corners.</param>
        /// <param name="bottomRight">Radius of the bottom right corner.</param>
        public CornerRadii(float topLeft, float topRightAndBottomLeft, float bottomRight) {
            TopLeft = topLeft;
            TopRight = topRightAndBottomLeft;
            BottomRight = bottomRight;
            BottomLeft = topRightAndBottomLeft;
        }

        /// <summary>A radius per corner, clockwise from the top left.</summary>
        /// <param name="topLeft">Radius of the top left corner.</param>
        /// <param name="topRight">Radius of the top right corner.</param>
        /// <param name="bottomRight">Radius of the bottom right corner.</param>
        /// <param name="bottomLeft">Radius of the bottom left corner.</param>
        public CornerRadii(float topLeft, float topRight, float bottomRight, float bottomLeft) {
            TopLeft = topLeft;
            TopRight = topRight;
            BottomRight = bottomRight;
            BottomLeft = bottomLeft;
        }

        /// <summary>Rounds all four corners by the same amount.</summary>
        /// <param name="value">Radius of every corner.</param>
        public static implicit operator CornerRadii(float value) {
            return new CornerRadii(value);
        }
    }
}
