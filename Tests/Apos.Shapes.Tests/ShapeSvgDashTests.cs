using Xunit;

namespace Apos.Shapes.Tests {
    // How a stroke-dasharray turns into the passes a stroke is drawn in. The viewBox is 100
    // units tall, so a length of 10 in the file is 0.1 em.
    public class ShapeSvgDashTests {
        private static SvgShape Line(string stroke) {
            string markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><line x1="0" y1="50" x2="100" y2="50" stroke="#000" {stroke}/></svg>""";
            Assert.True(ShapeSvg.TryLoad(markup, out ShapeSvg? svg));
            return Assert.Single(svg!.Shapes);
        }

        [Fact]
        public void TwoLengths_AreOneDashPerPeriod() {
            SvgShape s = Line("""stroke-dasharray="10 5" stroke-dashoffset="3" """);

            Assert.True(s.Dashed);
            Assert.Equal(0.15f, s.DashPeriod, 1e-5f);
            Assert.Equal(new[] { 0.12f }, s.DashStarts, (a, b) => MathF.Abs(a - b) < 1e-5f);
            Assert.Equal(new[] { 0.1f }, s.DashLengths, (a, b) => MathF.Abs(a - b) < 1e-5f);
        }

        [Fact]
        public void FourLengths_AreTwoDashesInOnePeriod() {
            SvgShape s = Line("""stroke-dasharray="10 5 2 5" """);

            Assert.True(s.Dashed);
            Assert.Equal(0.22f, s.DashPeriod, 1e-5f);
            Assert.Equal(new[] { 0f, 0.15f }, s.DashStarts, (a, b) => MathF.Abs(a - b) < 1e-5f);
            Assert.Equal(new[] { 0.1f, 0.02f }, s.DashLengths, (a, b) => MathF.Abs(a - b) < 1e-5f);
        }

        [Fact]
        public void OddList_RepeatsToAnEvenOne() {
            SvgShape s = Line("""stroke-dasharray="4 2 1" """);

            // 4 2 1 4 2 1: dashes of 4, 1 and 2 at 0, 6 and 11 in a period of 14.
            Assert.Equal(0.14f, s.DashPeriod, 1e-5f);
            Assert.Equal(new[] { 0f, 0.06f, 0.11f }, s.DashStarts, (a, b) => MathF.Abs(a - b) < 1e-5f);
            Assert.Equal(new[] { 0.04f, 0.01f, 0.02f }, s.DashLengths, (a, b) => MathF.Abs(a - b) < 1e-5f);
        }

        [Fact]
        public void SquareCaps_AreButtDashesGrownByTheRadius() {
            SvgShape s = Line("""stroke-width="4" stroke-linecap="square" stroke-dasharray="10 10" """);

            Assert.Equal(DashCap.Butt, s.DashCap);
            Assert.Equal(PathCap.Square, s.Cap);
            Assert.Equal(new[] { 0.18f }, s.DashStarts, (a, b) => MathF.Abs(a - b) < 1e-5f);
            Assert.Equal(new[] { 0.14f }, s.DashLengths, (a, b) => MathF.Abs(a - b) < 1e-5f);
        }

        [Fact]
        public void SquareCaps_ThatCloseEveryGap_DrawSolid() {
            SvgShape s = Line("""stroke-width="6" stroke-linecap="square" stroke-dasharray="8 4" """);

            Assert.True(s.HasStroke);
            Assert.False(s.Dashed);
        }

        [Fact]
        public void RoundCaps_ThatMeet_MergeIntoOneDash() {
            // Dashes of 10 and 2 with 2 between them: the caps of a stroke 4 wide overlap there,
            // while the 20 after the second dash stays open.
            SvgShape s = Line("""stroke-width="4" stroke-linecap="round" stroke-dasharray="10 2 2 20" """);

            Assert.Equal(DashCap.Round, s.DashCap);
            Assert.Equal(new[] { 0f }, s.DashStarts, (a, b) => MathF.Abs(a - b) < 1e-5f);
            Assert.Equal(new[] { 0.14f }, s.DashLengths, (a, b) => MathF.Abs(a - b) < 1e-5f);
        }

        [Fact]
        public void ZeroLengthDashes_WithButtCaps_DrawNothing() {
            SvgShape s = Line("""stroke-dasharray="0 5" """);

            Assert.False(s.HasStroke);
        }

        [Fact]
        public void ZeroLengthDashes_WithRoundCaps_AreDots() {
            SvgShape s = Line("""stroke-width="2" stroke-linecap="round" stroke-dasharray="0 5" """);

            Assert.True(s.Dashed);
            Assert.Equal(new[] { 0f }, s.DashLengths, (a, b) => MathF.Abs(a - b) < 1e-5f);
        }

        [Fact]
        public void OpenEnd_IsCutShortBeforeADashThatStartsPastIt() {
            // A line 98 long dashed 10 10: the dash at 100 starts past the end, but a cap 3 long
            // would reach back over it. The line is cut halfway across the space before it, less
            // the cap: (93 + 97) / 2 - 3 = 92.
            string markup = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><line x1="0" y1="50" x2="98" y2="50" stroke="#000" stroke-width="6" stroke-linecap="round" stroke-dasharray="10 10"/></svg>""";
            Assert.True(ShapeSvg.TryLoad(markup, out ShapeSvg? svg));
            SvgShape s = Assert.Single(svg!.Shapes);

            Assert.Equal(0.92f, s.Stroke[0][^1].X, 1e-4f);
            Assert.Equal(0f, s.Stroke[0][0].X, 1e-4f);
        }

        [Fact]
        public void OpenStart_IsCutAndThePatternStaysPut() {
            // Shifted 12 back, the dash from -12 to -2 ends before the start, which its cap
            // reaches across. The next one starts at 8, so the start's cap is centered on the
            // space between: (1 + 5) / 2 + 3 = 6.
            string markup = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><line x1="0" y1="50" x2="98" y2="50" stroke="#000" stroke-width="6" stroke-linecap="round" stroke-dasharray="10 10" stroke-dashoffset="12"/></svg>""";
            Assert.True(ShapeSvg.TryLoad(markup, out ShapeSvg? svg));
            SvgShape s = Assert.Single(svg!.Shapes);

            Assert.Equal(0.06f, s.Stroke[0][0].X, 1e-4f);
            Assert.Equal(0.06f, s.DashShift[0], 1e-4f);
        }
    }
}
