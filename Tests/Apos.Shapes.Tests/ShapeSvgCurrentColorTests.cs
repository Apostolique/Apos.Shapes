using Microsoft.Xna.Framework;
using Xunit;

namespace Apos.Shapes.Tests {
    // Which paints a draw time currentColor replaces.
    public class ShapeSvgCurrentColorTests {
        private static ShapeSvg Load(string body) {
            string markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">{body}</svg>""";
            Assert.True(ShapeSvg.TryLoad(markup, out ShapeSvg? svg));
            return svg!;
        }

        [Fact]
        public void CurrentColor_ThatTheFileNeverSet_FollowsTheDrawCall() {
            SvgShape s = Assert.Single(Load("""<rect width="10" height="10" fill="currentColor" stroke="currentColor" stroke-opacity=".5"/>""").Shapes);

            Assert.True(s.FillCurrent);
            Assert.True(s.StrokeCurrent);
            Assert.Equal(1f, s.FillAlpha);
            Assert.Equal(0.5f, s.StrokeAlpha);
        }

        [Fact]
        public void CurrentColor_UnderAColorProperty_KeepsTheFilesColor() {
            SvgShape s = Assert.Single(Load("""<g color="#00ff00"><rect width="10" height="10" fill="currentColor"/></g>""").Shapes);

            Assert.False(s.FillCurrent);
            Assert.Equal(new Color(0, 255, 0), s.FillPaint.AC);
        }

        [Fact]
        public void CurrentColor_IsTheColorOfTheElementThatDraws() {
            // The group sets the paint and the rect sets the color, which is the one it takes.
            SvgShape s = Assert.Single(Load("""<g fill="currentColor"><rect width="10" height="10" color="#ff0000"/></g>""").Shapes);

            Assert.Equal(new Color(255, 0, 0), s.FillPaint.AC);
        }

        [Fact]
        public void OtherPaints_AreNotCurrentColor() {
            SvgShape s = Assert.Single(Load("""<rect width="10" height="10" fill="#123456" stroke="black"/>""").Shapes);

            Assert.False(s.FillCurrent);
            Assert.False(s.StrokeCurrent);
        }
    }
}
