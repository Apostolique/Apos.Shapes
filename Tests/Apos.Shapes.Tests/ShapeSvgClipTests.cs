using Microsoft.Xna.Framework;
using Xunit;

namespace Apos.Shapes.Tests {
    // What a drawing is cut to. A fill is cut at load where it can be, which leaves the draw call
    // nothing to clip. Boxes are in the em frame: one em is the viewBox height, y points up.
    public class ShapeSvgClipTests {
        private static ShapeSvg Load(string body, string root = "") {
            string markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100" {root}>{body}</svg>""";
            Assert.True(ShapeSvg.TryLoad(markup, out ShapeSvg? svg));
            return svg!;
        }

        private static (Vector2 Min, Vector2 Max) FillBox(SvgShape s) {
            return (s.Origin + s.Fill!.Min * s.FillScale, s.Origin + s.Fill.Max * s.FillScale);
        }

        [Fact]
        public void Fill_PastTheViewBox_IsCutAtLoad() {
            SvgShape s = Assert.Single(Load("""<rect x="-10" y="70" width="40" height="40"/>""").Shapes);

            (Vector2 min, Vector2 max) = FillBox(s);
            Assert.Equal(0f, min.X, 0.002f);
            Assert.Equal(-1f, min.Y, 0.002f);
            Assert.Equal(0.3f, max.X, 0.002f);
            Assert.Equal(-0.7f, max.Y, 0.002f);
            Assert.Null(s.Clips);
        }

        [Fact]
        public void Fill_InsideTheViewBox_IsUntouched() {
            SvgShape s = Assert.Single(Load("""<rect x="10" y="10" width="40" height="40" stroke="#000"/>""").Shapes);

            Assert.Null(s.Clips);
            Assert.Equal(-0.2f, s.Fill!.Min.X, 0.002f);
        }

        [Fact]
        public void Stroke_PastTheViewBox_IsClippedWhenItDraws() {
            SvgShape s = Assert.Single(Load("""<line x1="150" y1="50" x2="250" y2="50" stroke="#000" stroke-width="4"/>""").Shapes);

            SvgClip clip = Assert.Single(s.Clips!);
            Assert.Equal(new Vector2(1f, -0.5f), clip.Center);
            Assert.Equal(new Vector2(1f, 0.5f), clip.Half);
        }

        [Fact]
        public void Element_WhollyOutsideTheViewBox_IsDropped() {
            Assert.Empty(Load("""<rect x="300" y="10" width="40" height="40"/>""").Shapes);
        }

        [Fact]
        public void OverflowVisible_KeepsWhatIsPastTheViewBox() {
            SvgShape s = Assert.Single(Load("""<rect x="-10" y="70" width="40" height="40"/>""", """overflow="visible" """).Shapes);

            Assert.Equal(-0.1f, FillBox(s).Min.X, 0.002f);
        }

        [Fact]
        public void ClipPath_OfOneRect_CutsTheFillsUnderIt() {
            SvgShape s = Assert.Single(Load("""
                <clipPath id="c"><rect x="10" y="10" width="20" height="20"/></clipPath>
                <g clip-path="url(#c)"><circle cx="10" cy="10" r="15"/></g>
                """).Shapes);

            (Vector2 min, Vector2 max) = FillBox(s);
            Assert.Equal(0.1f, min.X, 0.002f);
            Assert.Equal(-0.25f, min.Y, 0.002f);
            Assert.Equal(0.25f, max.X, 0.002f);
            Assert.Equal(-0.1f, max.Y, 0.002f);
            Assert.Null(s.Clips);
        }

        [Fact]
        public void ClipPath_OfOneCircle_IsARoundClipWhenItDraws() {
            SvgShape s = Assert.Single(Load("""
                <clipPath id="c"><circle cx="50" cy="50" r="20"/></clipPath>
                <rect x="20" y="20" width="60" height="60" clip-path="url(#c)"/>
                """).Shapes);

            SvgClip clip = Assert.Single(s.Clips!);
            Assert.Equal(0.2f, clip.Rounding, 0.0001f);
            Assert.Equal(0.2f, clip.Half.X, 0.0001f);
            Assert.Equal(0.2f, clip.Half.Y, 0.0001f);
        }

        [Fact]
        public void ClipPath_ThatTurns_TurnsTheClip() {
            SvgShape s = Assert.Single(Load("""
                <clipPath id="c"><rect x="40" y="40" width="20" height="20" transform="rotate(30 50 50)"/></clipPath>
                <rect x="20" y="20" width="60" height="60" stroke="#000" clip-path="url(#c)"/>
                """).Shapes);

            SvgClip clip = Assert.Single(s.Clips!);
            Assert.Equal(MathF.PI / 6f, clip.Angle, 0.0001f);
            Assert.Equal(0.5f, clip.Center.X, 0.0001f);
            Assert.Equal(-0.5f, clip.Center.Y, 0.0001f);
        }

        [Fact]
        public void ClipPath_OfSeveralShapes_IsSkippedAndCutsNothing() {
            ShapeSvg svg = Load("""
                <clipPath id="c"><rect width="5" height="5"/><rect x="10" width="5" height="5"/></clipPath>
                <rect x="20" y="20" width="60" height="60" clip-path="url(#c)"/>
                """);

            SvgShape s = Assert.Single(svg.Shapes);
            Assert.Null(s.Clips);
            Assert.Equal(0.6f, FillBox(s).Max.X - FillBox(s).Min.X, 0.002f);
            Assert.Contains("clipPath", svg.SkippedNames);
        }

        [Fact]
        public void NestedSvg_CutsWhatItDrawsToItsBox() {
            SvgShape s = Assert.Single(Load("""<svg x="40" y="60" width="30" height="30" viewBox="0 0 10 10"><circle cx="5" cy="5" r="7"/></svg>""").Shapes);

            (Vector2 min, Vector2 max) = FillBox(s);
            Assert.Equal(0.4f, min.X, 0.002f);
            Assert.Equal(0.7f, max.X, 0.002f);
        }

        [Fact]
        public void ElementsUnderTheSameClip_ShareIt() {
            ShapeSvg svg = Load("""
                <clipPath id="c"><circle cx="50" cy="50" r="20"/></clipPath>
                <g clip-path="url(#c)"><rect x="20" y="20" width="60" height="60"/><rect x="25" y="25" width="60" height="60"/></g>
                """);

            Assert.Equal(2, svg.Shapes.Count);
            Assert.Equal(svg.Shapes[0].Clips!, svg.Shapes[1].Clips!);
        }
    }
}
