using System.Diagnostics;
using System.Text;
using Microsoft.Xna.Framework;
using Xunit;

namespace Apos.Shapes.Tests {
    // Which elements a document draws and where, for the elements that copy, pick or place other
    // ones: use, symbol, switch and a nested svg. Boxes are in the em frame, where one em is the
    // viewBox height and y points up from the viewBox's top edge.
    public class ShapeSvgStructureTests {
        private static ShapeSvg Load(string body) {
            string markup = $"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 100 100">{body}</svg>""";
            Assert.True(ShapeSvg.TryLoad(markup, out ShapeSvg? svg));
            return svg!;
        }

        private static void AssertBox(SvgShape shape, float x1, float y1, float x2, float y2) {
            Assert.Equal(x1 / 100f, shape.Min.X, 0.002f);
            Assert.Equal(-y2 / 100f, shape.Min.Y, 0.002f);
            Assert.Equal(x2 / 100f, shape.Max.X, 0.002f);
            Assert.Equal(-y1 / 100f, shape.Max.Y, 0.002f);
        }

        [Fact]
        public void Use_DrawsTheElementAgainMovedByItsXAndY() {
            ShapeSvg svg = Load("""<rect id="r" width="10" height="10"/><use href="#r" x="30" y="40"/>""");

            Assert.Equal(2, svg.Shapes.Count);
            AssertBox(svg.Shapes[0], 0, 0, 10, 10);
            AssertBox(svg.Shapes[1], 30, 40, 40, 50);
            Assert.Equal(0, svg.Skipped);
        }

        [Fact]
        public void Use_CanPointAheadIntoDefsAndTakesTheTransformOfTheUse() {
            ShapeSvg svg = Load("""<use xlink:href="#r" transform="translate(50 0) scale(2)"/><defs><rect id="r" x="5" width="10" height="10"/></defs>""");

            SvgShape shape = Assert.Single(svg.Shapes);
            AssertBox(shape, 60, 0, 80, 20);
        }

        [Fact]
        public void Use_PassesItsStyleDownToWhatItDraws() {
            ShapeSvg svg = Load("""<defs><rect id="r" width="10" height="10"/></defs><use href="#r" fill="#00ff00"/>""");

            Assert.Equal(new Color(0, 255, 0), Assert.Single(svg.Shapes).FillPaint.AC);
        }

        [Fact]
        public void Symbol_OnlyDrawsWhereAUseStampsItOutAndFitsItsViewBoxIntoTheUse() {
            ShapeSvg svg = Load("""<symbol id="s" viewBox="0 0 10 10"><rect width="10" height="10"/></symbol><use href="#s" x="20" y="20" width="40" height="40"/>""");

            SvgShape shape = Assert.Single(svg.Shapes);
            AssertBox(shape, 20, 20, 60, 60);
        }

        [Fact]
        public void Symbol_KeepsItsAspectRatioCenteredUnlessToldOtherwise() {
            ShapeSvg svg = Load("""
                <symbol id="s" viewBox="0 0 10 10"><rect width="10" height="10"/></symbol>
                <symbol id="t" viewBox="0 0 10 10" preserveAspectRatio="xMinYMin"><rect width="10" height="10"/></symbol>
                <symbol id="u" viewBox="0 0 10 10" preserveAspectRatio="none"><rect width="10" height="10"/></symbol>
                <use href="#s" width="40" height="20"/>
                <use href="#t" width="40" height="20"/>
                <use href="#u" width="40" height="20"/>
                """);

            Assert.Equal(3, svg.Shapes.Count);
            AssertBox(svg.Shapes[0], 10, 0, 30, 20);
            AssertBox(svg.Shapes[1], 0, 0, 20, 20);
            AssertBox(svg.Shapes[2], 0, 0, 40, 20);
        }

        [Fact]
        public void NestedSvg_MapsItsViewBoxOntoItsOwnBox() {
            ShapeSvg svg = Load("""<svg x="50" y="10" width="20" height="20" viewBox="0 0 10 10"><rect width="10" height="10"/></svg>""");

            SvgShape shape = Assert.Single(svg.Shapes);
            AssertBox(shape, 50, 10, 70, 30);
        }

        [Fact]
        public void Use_ThatPointsAtItsOwnAncestor_IsDroppedInsteadOfLoopingForever() {
            ShapeSvg svg = Load("""<g id="a"><rect width="10" height="10"/><use href="#a" x="20"/></g>""");

            // The group draws once, its use stamps it out once more, and the use inside that
            // copy is the one that stops.
            Assert.Equal(2, svg.Shapes.Count);
            Assert.Contains("use", svg.SkippedNames);
        }

        [Fact]
        public void Use_ThatMissesItsTarget_IsDropped() {
            ShapeSvg svg = Load("""<use href="#nothing"/>""");

            Assert.Empty(svg.Shapes);
            Assert.Contains("use", svg.SkippedNames);
        }

        [Fact]
        public void Uses_ThatMultiplyEachOther_StopAtTheCapQuickly() {
            // Ten levels of ten uses each would be ten billion rectangles.
            var body = new StringBuilder("""<defs><rect id="l0" width="1" height="1"/>""");
            for (int level = 1; level <= 10; level++) {
                body.Append($"""<g id="l{level}">""");
                for (int i = 0; i < 10; i++) body.Append($"""<use href="#l{level - 1}"/>""");
                body.Append("</g>");
            }
            body.Append("""</defs><use href="#l10"/>""");

            var watch = Stopwatch.StartNew();
            ShapeSvg svg = Load(body.ToString());

            Assert.True(svg.Shapes.Count <= 100_000);
            Assert.Contains("use", svg.SkippedNames);
            Assert.True(watch.Elapsed.TotalSeconds < 30);
        }

        [Fact]
        public void DeeplyNestedGroups_LoadWithoutRunningOutOfStack() {
            var body = new StringBuilder();
            for (int i = 0; i < 100_000; i++) body.Append("<g>");
            body.Append("""<rect width="1" height="1"/>""");
            for (int i = 0; i < 100_000; i++) body.Append("</g>");

            ShapeSvg svg = Load(body.ToString());

            Assert.Empty(svg.Shapes);
            Assert.Contains("deep nesting", svg.SkippedNames);
        }

        [Fact]
        public void Switch_DrawsOnlyTheFirstChildWhoseConditionsHold() {
            ShapeSvg svg = Load("""
                <switch>
                  <rect requiredExtensions="http://example.com/ext" width="10" height="10"/>
                  <rect systemLanguage="xx-nope" width="20" height="20"/>
                  <rect width="30" height="30"/>
                  <rect width="40" height="40"/>
                </switch>
                """);

            SvgShape shape = Assert.Single(svg.Shapes);
            AssertBox(shape, 0, 0, 30, 30);
        }

        [Fact]
        public void Gradient_InsideAnElementThatIsNotDrawn_StillPaints() {
            ShapeSvg svg = Load("""
                <symbol><linearGradient id="g"><stop offset="0" stop-color="#f00"/><stop offset="1" stop-color="#00f"/></linearGradient></symbol>
                <rect width="10" height="10" fill="url(#g)"/>
                """);

            Assert.NotEqual(Gradient.Shape.None, Assert.Single(svg.Shapes).FillPaint.S);
        }
    }
}
