using System.Text;
using Xunit;

namespace Apos.Shapes.Tests {
    // How a document's elements are baked, which needs no graphics device. One em is the
    // viewBox height, and an element's curves are held relative to its own center.
    public class ShapeSvgBakeTests {
        private static ShapeSvg Load(string markup) {
            Assert.True(ShapeSvg.TryLoad(markup, out ShapeSvg? svg));
            return svg!;
        }

        private static string Rect(int width, int height) =>
            $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}"><path d="M0,0H{width}V{height}H0Z" fill="#f00"/></svg>""";

        // One bar with holes cut out of it, so every row crosses far more curves than a band
        // holds when the holes are many.
        private static string Comb(int holes) {
            var path = new StringBuilder("M0,0H600V100H0Z");
            for (int i = 0; i < holes; i++) {
                int x = 12 + i * 15;
                path.Append($"M{x},20V80H{x + 6}V20Z");
            }
            return $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 600 100"><path d="{path}" fill="#f00"/></svg>""";
        }

        [Fact]
        public void Load_ElementInsideTheReach_IsBakedAtItsOwnScale() {
            ShapeSvg svg = Load(Rect(100, 100));

            SvgShape shape = Assert.Single(svg.Shapes);
            Assert.Equal(1f, shape.FillScale);
            Assert.Null(shape.FillTiles);
            Assert.Equal(0, svg.Skipped);
        }

        [Fact]
        public void Load_ElementUpToTwoPointFourEmsWide_IsNotRescaled() {
            ShapeSvg svg = Load(Rect(240, 100));

            Assert.Equal(1f, Assert.Single(svg.Shapes).FillScale);
        }

        [Theory]
        [InlineData(300)]
        [InlineData(600)]
        [InlineData(6000)]
        public void Load_ElementWiderThanTheReach_IsKeptNotDropped(int width) {
            ShapeSvg svg = Load(Rect(width, 100));

            SvgShape shape = Assert.Single(svg.Shapes);
            Assert.True(shape.HasFill);
            Assert.Equal(0, svg.Skipped);
        }

        [Fact]
        public void Load_ElementWiderThanTheReach_IsBakedShrunkAndDrawnBackUp() {
            // 600 by 100 is six ems wide, so its curves reach 3 ems from its center. They are
            // held at 1.2 and drawn at 2.5 times the scale, which lands them at 3 again.
            ShapeSvg svg = Load(Rect(600, 100));

            SvgShape shape = Assert.Single(svg.Shapes);
            Assert.Equal(2.5f, shape.FillScale, 0.01f);
            Assert.Equal(1.2f, shape.Fill!.Max.X, 0.01f);
            Assert.Equal(3f, shape.Fill.Max.X * shape.FillScale, 0.01f);
        }

        [Fact]
        public void Load_ElementWiderThanTheReach_StillMeasuresItsFullWidth() {
            ShapeSvg svg = Load(Rect(600, 100));

            Assert.Equal(6f, svg.Max.X - svg.Min.X, 0.01f);
            Assert.Equal(6f, svg.Width / svg.Height, 0.001f);
        }

        [Theory]
        [InlineData("wide")]
        [InlineData("very wide")]
        [InlineData("dense")]
        public void Load_AnyElement_KeepsEveryControlPointInsideWhatTheCurveTextureHolds(string kind) {
            // KNI's GL backends store each curve coordinate as 16 bit fixed point over [-2, 2]
            // ems and clamp anything outside it, which draws the shape wrong.
            string markup = kind switch {
                "wide" => Rect(600, 100),
                "very wide" => Rect(6000, 100),
                _ => Comb(holes: 39),
            };

            SvgShape shape = Assert.Single(Load(markup).Shapes);

            var glyphs = new List<BakedGlyph> { shape.Fill! };
            if (shape.FillTiles != null) glyphs.AddRange(shape.FillTiles);
            foreach (BakedGlyph glyph in glyphs) {
                foreach (float value in glyph.CurveTexels) {
                    Assert.True(GlyphRepack.CurveInRange(value), $"{value} is outside the curve texture's range");
                }
            }
        }

        [Fact]
        public void Load_TallElement_IsShrunkTheSameWay() {
            ShapeSvg svg = Load(Rect(100, 600));

            SvgShape shape = Assert.Single(svg.Shapes);
            Assert.Equal(1f, shape.FillScale, 0.01f);
            // 100 wide and 600 tall: the viewBox height is the em, so the shape is 1/6 em wide
            // and exactly one em tall, well inside the reach.
        }

        [Fact]
        public void Load_ElementWithFewCurvesPerRow_IsNotTiled() {
            ShapeSvg svg = Load(Comb(holes: 4));

            SvgShape shape = Assert.Single(svg.Shapes);
            Assert.Null(shape.FillTiles);
            Assert.Equal(0, shape.Fill!.Clamped);
        }

        [Fact]
        public void Load_ElementWithMoreCurvesPerRowThanABandHolds_IsCutIntoTilesThatFit() {
            ShapeSvg svg = Load(Comb(holes: 39));

            SvgShape shape = Assert.Single(svg.Shapes);
            Assert.True(shape.Fill!.Clamped > 0, "the whole element should overflow, or this test proves nothing");
            Assert.NotNull(shape.FillTiles);
            Assert.True(shape.FillTiles!.Length > 1);
            foreach (BakedGlyph tile in shape.FillTiles) {
                Assert.Equal(0, tile.Clamped);
            }
        }

        [Fact]
        public void Load_TiledElement_TilesMeetExactlyAndCoverTheWholeBox() {
            ShapeSvg svg = Load(Comb(holes: 39));
            SvgShape shape = Assert.Single(svg.Shapes);
            BakedGlyph whole = shape.Fill!;
            BakedGlyph[] tiles = shape.FillTiles!;

            Assert.Equal(whole.Min.X, tiles.Min(t => t.Min.X));
            Assert.Equal(whole.Max.X, tiles.Max(t => t.Max.X));
            Assert.Equal(whole.Min.Y, tiles.Min(t => t.Min.Y));
            Assert.Equal(whole.Max.Y, tiles.Max(t => t.Max.Y));

            // A seam is a side another tile starts on, and only that side skips the margin.
            foreach (BakedGlyph tile in tiles) {
                Assert.Equal(tile.Min.X > whole.Min.X, tile.SeamMinX);
                Assert.Equal(tile.Max.X < whole.Max.X, tile.SeamMaxX);
                Assert.Equal(tile.Min.Y > whole.Min.Y, tile.SeamMinY);
                Assert.Equal(tile.Max.Y < whole.Max.Y, tile.SeamMaxY);
            }

            // Every edge shared between tiles is the very same float on both sides.
            foreach (BakedGlyph tile in tiles.Where(t => t.SeamMaxX)) {
                Assert.Contains(tiles, other => other.Min.X == tile.Max.X && other.Min.Y == tile.Min.Y);
            }
        }

        [Fact]
        public void Load_TiledElement_AreaIsTheSameAsTheWhole() {
            // Tiles partition the box, so their areas add up to it.
            ShapeSvg svg = Load(Comb(holes: 39));
            SvgShape shape = Assert.Single(svg.Shapes);
            BakedGlyph whole = shape.Fill!;

            float wholeArea = (whole.Max.X - whole.Min.X) * (whole.Max.Y - whole.Min.Y);
            float tileArea = shape.FillTiles!.Sum(t => (t.Max.X - t.Min.X) * (t.Max.Y - t.Min.Y));

            Assert.Equal(wholeArea, tileArea, wholeArea * 1e-4f);
        }
    }
}
