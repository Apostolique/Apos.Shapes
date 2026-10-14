using Microsoft.Xna.Framework;
using Xunit;

namespace Apos.Shapes.Tests {
    // The clipper's one promise: cutting loops down to a box changes nothing about which points
    // inside the box are filled. Checked by comparing winding numbers before and after, which is
    // what both fill rules are read from.
    public class SvgTileClipTests {
        private const float Inf = float.PositiveInfinity;

        [Fact]
        public void Clip_WithNoBounds_ReturnsEveryCurveUnchanged() {
            var loops = new List<List<GlyphCurve>> { Square(10, 10, 60, 60, clockwise: true) };

            List<GlyphCurve> clipped = SvgTileClip.Clip(loops, -Inf, Inf, -Inf, Inf);

            Assert.Equal(4, clipped.Count);
        }

        [Fact]
        public void Clip_LoopEntirelyOutsideTheBox_Vanishes() {
            var loops = new List<List<GlyphCurve>> { Square(100, 100, 150, 150, clockwise: true) };

            List<GlyphCurve> clipped = SvgTileClip.Clip(loops, 0, 50, 0, 50);

            Assert.Empty(clipped);
        }

        [Fact]
        public void Clip_LoopEnclosingTheWholeBox_StillFillsIt() {
            // No curve of the big square comes near the box, so the loop is kept as a chord
            // loop rather than dropped. Dropping it would leave the box empty.
            var loops = new List<List<GlyphCurve>> { Square(-500, -500, 500, 500, clockwise: true) };

            List<GlyphCurve> clipped = SvgTileClip.Clip(loops, 0, 50, 0, 50);

            Assert.Equal(WindingOf(loops, new Vector2(25, 25)), WindingOf(clipped, new Vector2(25, 25)));
            Assert.NotEqual(0, WindingOf(clipped, new Vector2(25, 25)));
        }

        [Fact]
        public void Clip_HoleInsideTheBox_StaysAHole() {
            var loops = new List<List<GlyphCurve>> {
                Square(-200, -200, 200, 200, clockwise: true),
                Square(20, 20, 30, 30, clockwise: false),
            };

            List<GlyphCurve> clipped = SvgTileClip.Clip(loops, 0, 50, 0, 50);

            Assert.Equal(0, WindingOf(clipped, new Vector2(25, 25)));
            Assert.NotEqual(0, WindingOf(clipped, new Vector2(10, 10)));
        }

        [Fact]
        public void Clip_StraightEdgesCutOnBothAxes_KeepEveryPointInside() {
            // A straight run is a quadratic with its control point at the midpoint, which leaves
            // the quadratic coefficient at rounding noise. Cutting one on a second axis once
            // lost the crossing to the textbook root formula.
            var random = new Random(4);
            for (int scene = 0; scene < 400; scene++) {
                float x0 = Next(random, 0, 60), y0 = Next(random, 0, 60);
                var loops = new List<List<GlyphCurve>> {
                    Square(x0, y0, x0 + Next(random, 20, 80), y0 + Next(random, 20, 80), random.Next(2) == 0),
                };
                float bx = Next(random, 10, 60), by = Next(random, 10, 60);
                AssertSameInsideTheBox(random, loops, bx, bx + Next(random, 10, 40), by, by + Next(random, 10, 40));
            }
        }

        [Fact]
        public void Clip_RandomCurvedLoopsToRandomBoxes_KeepEveryPointInside() {
            var random = new Random(12345);
            for (int scene = 0; scene < 600; scene++) {
                var loops = new List<List<GlyphCurve>>();
                int count = random.Next(1, 5);
                for (int i = 0; i < count; i++) {
                    var center = new Vector2(Next(random, 0, 100), Next(random, 0, 100));
                    loops.Add(RandomLoop(random, center, Next(random, 15, 75), random.Next(2) == 0));
                }
                float x0 = Next(random, 0, 80), y0 = Next(random, 0, 80);
                float x1 = x0 + Next(random, 5, 45), y1 = y0 + Next(random, 5, 45);
                // Some sides are left open, the way a tile on the shape's edge is.
                AssertSameInsideTheBox(random, loops,
                    random.Next(4) == 0 ? -Inf : x0, random.Next(4) == 0 ? Inf : x1,
                    random.Next(4) == 0 ? -Inf : y0, random.Next(4) == 0 ? Inf : y1);
            }
        }

        private static void AssertSameInsideTheBox(Random random, List<List<GlyphCurve>> loops,
                                                   float minX, float maxX, float minY, float maxY) {
            List<GlyphCurve> clipped = SvgTileClip.Clip(loops, minX, maxX, minY, maxY);
            for (int k = 0; k < 25; k++) {
                var p = new Vector2(
                    Next(random, float.IsInfinity(minX) ? -20 : minX, float.IsInfinity(maxX) ? 140 : maxX),
                    Next(random, float.IsInfinity(minY) ? -20 : minY, float.IsInfinity(maxY) ? 140 : maxY));
                Assert.True(WindingOf(loops, p) == WindingOf(clipped, p),
                    $"winding changed at ({p.X:0.00}, {p.Y:0.00}) for box x[{minX}, {maxX}] y[{minY}, {maxY}]");
            }
        }

        private static float Next(Random random, float lo, float hi) => lo + (float)random.NextDouble() * (hi - lo);

        // The signed number of times the loops go around a point: a ray towards +x, with each
        // quadratic flattened finely.
        private static int WindingOf(List<List<GlyphCurve>> loops, Vector2 p) {
            var all = new List<GlyphCurve>();
            foreach (List<GlyphCurve> loop in loops) all.AddRange(loop);
            return WindingOf(all, p);
        }

        private static int WindingOf(List<GlyphCurve> curves, Vector2 p) {
            int winding = 0;
            foreach (GlyphCurve c in curves) {
                const int Steps = 96;
                Vector2 a = c.P1;
                for (int i = 1; i <= Steps; i++) {
                    float t = i / (float)Steps;
                    float u = 1 - t;
                    Vector2 b = c.P1 * (u * u) + c.P2 * (2 * u * t) + c.P3 * (t * t);
                    if ((a.Y <= p.Y) != (b.Y <= p.Y)) {
                        float x = a.X + (p.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                        if (x > p.X) winding += b.Y > a.Y ? 1 : -1;
                    }
                    a = b;
                }
            }
            return winding;
        }

        private static GlyphCurve Line(Vector2 a, Vector2 b) => new() { P1 = a, P2 = (a + b) * 0.5f, P3 = b };

        private static List<GlyphCurve> Square(float x0, float y0, float x1, float y1, bool clockwise) {
            Vector2 a = new(x0, y0), b = new(x1, y0), c = new(x1, y1), d = new(x0, y1);
            return clockwise
                ? new List<GlyphCurve> { Line(a, b), Line(b, c), Line(c, d), Line(d, a) }
                : new List<GlyphCurve> { Line(a, d), Line(d, c), Line(c, b), Line(b, a) };
        }

        private static List<GlyphCurve> RandomLoop(Random random, Vector2 center, float radius, bool reverse) {
            int n = random.Next(3, 9);
            var points = new List<Vector2>();
            for (int i = 0; i < n; i++) {
                float angle = (float)(2 * Math.PI * i / n + random.NextDouble() * 0.3);
                float r = radius * (0.5f + (float)random.NextDouble() * 0.5f);
                points.Add(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * r);
            }
            if (reverse) points.Reverse();
            var loop = new List<GlyphCurve>();
            for (int i = 0; i < n; i++) {
                Vector2 a = points[i], b = points[(i + 1) % n];
                Vector2 bulge = new Vector2(-(b - a).Y, (b - a).X) * ((float)random.NextDouble() - 0.5f) * 0.6f;
                loop.Add(new GlyphCurve { P1 = a, P2 = (a + b) * 0.5f + bulge, P3 = b });
            }
            return loop;
        }
    }
}
