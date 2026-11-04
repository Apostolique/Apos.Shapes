using Microsoft.Xna.Framework;
using MonoGame.Extended;
using Xunit;

namespace Apos.Shapes.Tests {
    // Stars and polygons are measured through the same shrink the drawing makes, and pies through
    // the ring they draw as. These check both against shapes whose bounds are known by hand.
    public class MeasureStarAndPieTests {
        private const float Tolerance = 1e-3f;

        [Fact]
        public void Polygon_Square_ReachesItsCornersOnBothAxes() {
            // Four sides with a corner up is a diamond, its corners on the axes.
            RectangleF bb = Measure.Polygon(new Vector2(100, 100), 4, 50);

            AssertBounds(bb, 50, 50, 150, 150);
        }

        [Fact]
        public void Polygon_Triangle_StopsAtItsFlatBottom() {
            // A triangle with a corner up has its base at half the radius below the center.
            RectangleF bb = Measure.Polygon(Vector2.Zero, 3, 40);

            AssertBounds(bb, -40 * MathF.Sqrt(3f) / 2f, -40, 40 * MathF.Sqrt(3f) / 2f, 20);
        }

        [Fact]
        public void Polygon_RoundedAllTheWay_IsItsIncircle() {
            float apothem = 50 * MathF.Cos(MathF.PI / 6f);

            RectangleF bb = Measure.Polygon(Vector2.Zero, 6, 50, rounded: 1000f);

            AssertBounds(bb, -apothem, -apothem, apothem, apothem);
        }

        [Fact]
        public void Star_DentsOnTheLinesBetweenPoints_MeasuresAsThePolygon() {
            float inner = 60 * MathF.Cos(MathF.PI / 5f);

            RectangleF star = Measure.Star(new Vector2(10, 20), 5, 60, inner, rounded: 8f, rotation: 0.3f);
            RectangleF polygon = Measure.Polygon(new Vector2(10, 20), 5, 60, rounded: 8f, rotation: 0.3f);

            AssertBounds(star, polygon.Left, polygon.Top, polygon.Right, polygon.Bottom);
        }

        [Fact]
        public void Star_TwoPoints_IsARhombus() {
            RectangleF bb = Measure.Star(Vector2.Zero, 2, 80, 30);

            AssertBounds(bb, -30, -80, 30, 80);
        }

        [Fact]
        public void Star_RoundedTips_PullInFromThePoints() {
            RectangleF sharp = Measure.Star(Vector2.Zero, 5, 100, 40);
            RectangleF rounded = Measure.Star(Vector2.Zero, 5, 100, 40, rounded: 10f);

            Assert.True(rounded.Top > sharp.Top + 1f);
            Assert.True(rounded.Width < sharp.Width);
        }

        [Fact]
        public void Star_NoDents_RoundedAtAll_IsAPoint() {
            RectangleF bb = Measure.Star(new Vector2(5, 5), 5, 100, 0, rounded: 1f);

            AssertBounds(bb, 5, 5, 5, 5);
        }

        [Fact]
        public void Star_TooFewPoints_Throws() {
            Assert.Throws<ArgumentOutOfRangeException>(() => Measure.Star(Vector2.Zero, 1, 10, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => Measure.Polygon(Vector2.Zero, 2, 10));
        }

        [Fact]
        public void Pie_QuarterTurn_ReachesFromTheCenterToTheRim() {
            // From pointing right to pointing down, clockwise.
            RectangleF bb = Measure.Pie(new Vector2(10, 10), 0f, MathF.PI / 2f, 30f);

            AssertBounds(bb, 10, 10, 40, 40);
        }

        [Fact]
        public void Pie_FullTurn_IsTheCircle() {
            RectangleF bb = Measure.Pie(Vector2.Zero, 0f, MathF.Tau, 30f);

            AssertBounds(bb, -30, -30, 30, 30);
        }

        private static void AssertBounds(RectangleF bb, float left, float top, float right, float bottom) {
            Assert.InRange(bb.Left, left - Tolerance, left + Tolerance);
            Assert.InRange(bb.Top, top - Tolerance, top + Tolerance);
            Assert.InRange(bb.Right, right - Tolerance, right + Tolerance);
            Assert.InRange(bb.Bottom, bottom - Tolerance, bottom + Tolerance);
        }
    }
}
