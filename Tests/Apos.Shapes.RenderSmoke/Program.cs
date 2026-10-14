using System.Text;
using Apos.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// Draws SVGs that used to draw nothing or streak, one pixel per document unit, and checks pixels.
using var game = new SmokeGame();
game.Run();
Environment.Exit(game.Failures == 0 ? 0 : 1);

internal sealed class SmokeGame : Game {
    private const int Height = 100;

    public int Failures { get; private set; }

    public SmokeGame() {
        // Apos.Shapes needs a shader model the Reach profile doesn't have.
        new GraphicsDeviceManager(this) {
            GraphicsProfile = GraphicsProfile.HiDef,
            PreferredBackBufferWidth = 320,
            PreferredBackBufferHeight = 240,
        };
    }

    protected override void Draw(GameTime gameTime) {
        Console.WriteLine($"backend: {GraphicsDevice.Adapter.Backend}");

        // A single shape a few times wider than it is tall. Its curves reach past what the
        // curve texture holds unless the shape is rescaled, and an element that wide used to be
        // dropped outright.
        foreach (int width in new[] { 300, 600, 1200 }) {
            WideShapeFillsItsWholeBox(width);
        }

        DenseShapeKeepsEveryHole();

        Console.WriteLine(Failures == 0 ? "PASS" : $"FAIL: {Failures} check(s) failed");
        Exit();
    }

    private void WideShapeFillsItsWholeBox(int width) {
        Color[] px = Render(
            $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {Height}"><path d="M0,0H{width}V{Height}H0Z" fill="#f00"/></svg>""",
            width);
        string name = $"{width / Height}:1 shape";

        // Every pixel of the shape, ends included. A rescaled shape that was clamped instead
        // comes out black at both ends.
        Check(AllRed(px, width, 0, 0, width, Height, margin: 2), $"{name} is solid from end to end");
        Check(IsRed(Get(px, width, width / 2, Height / 2)), $"{name} center is red");
    }

    // One bar with 39 holes cut out of it: about 80 curves cross every row, far more than a band
    // holds, so a whole-shape bake loses the holes it can't list and streaks. It is cut into
    // tiles instead, and the seams between tiles must not show.
    private void DenseShapeKeepsEveryHole() {
        const int Width = 600;
        const int Holes = 39;
        const int Pitch = 15;
        const int HoleWidth = 6;
        var path = new StringBuilder("M0,0H600V100H0Z");
        for (int i = 0; i < Holes; i++) {
            int x = 12 + i * Pitch;
            path.Append($"M{x},20V80H{x + HoleWidth}V20Z");
        }
        Color[] px = Render(
            $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {Width} {Height}"><path d="{path}" fill="#f00"/></svg>""",
            Width);

        int emptyHoles = 0;
        int solidGaps = 0;
        for (int i = 0; i < Holes; i++) {
            int holeCenter = 12 + i * Pitch + HoleWidth / 2;
            if (Get(px, Width, holeCenter, 50) == Color.Black) emptyHoles++;
            // The solid gap after a hole, top to bottom: a seam between tiles would cut it.
            int gapCenter = holeCenter + Pitch / 2;
            if (i < Holes - 1 && AllRed(px, Width, gapCenter, 3, gapCenter + 1, Height - 3, margin: 0)) solidGaps++;
        }
        Check(emptyHoles == Holes, $"dense shape: every hole is empty ({emptyHoles}/{Holes})");
        Check(solidGaps == Holes - 1, $"dense shape: every gap between holes is solid top to bottom ({solidGaps}/{Holes - 1})");

        // The bars above and below the holes, along their whole length: a seam would cut them.
        Check(AllRed(px, Width, 3, 3, Width - 3, 18, margin: 0), "dense shape: top bar is solid along its length");
        Check(AllRed(px, Width, 3, 82, Width - 3, Height - 3, margin: 0), "dense shape: bottom bar is solid along its length");
    }

    private Color[] Render(string markup, int width) {
        if (!ShapeSvg.TryLoad(markup, out ShapeSvg? svg)) throw new InvalidOperationException("The SVG didn't load.");
        using var target = new RenderTarget2D(GraphicsDevice, width, Height, false, SurfaceFormat.Color, DepthFormat.None);
        var batch = new ShapeBatch(GraphicsDevice);
        GraphicsDevice.SetRenderTarget(target);
        GraphicsDevice.Clear(Color.Black);
        batch.Begin();
        batch.DrawSvg(svg, Vector2.Zero, Height);
        batch.End();
        GraphicsDevice.SetRenderTarget(null);
        var pixels = new Color[width * Height];
        target.GetData(pixels);
        return pixels;
    }

    private static Color Get(Color[] px, int width, int x, int y) => px[y * width + x];

    // A few levels of slack: coverage is rounded to eight bits, so a solid pixel can read 254.
    // A gap or a missing area is far darker than that.
    private static bool IsRed(Color c) => c.R >= 245 && c.G <= 10 && c.B <= 10;

    private static bool IsBlack(Color c) => c.R <= 10 && c.G <= 10 && c.B <= 10;

    // Whether every pixel in the rectangle is red, leaving out a margin of pixels at its
    // edge where the shape's own anti aliased border may fall. Prints the first one that isn't.
    private static bool AllRed(Color[] px, int width, int x0, int y0, int x1, int y1, int margin) {
        for (int y = y0 + margin; y < y1 - margin; y++) {
            for (int x = x0 + margin; x < x1 - margin; x++) {
                Color c = Get(px, width, x, y);
                if (!IsRed(c)) {
                    Console.WriteLine($"     first pixel that isn't red: ({x}, {y}) is {c}");
                    return false;
                }
            }
        }
        return true;
    }

    private void Check(bool ok, string description) {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {description}");
        if (!ok) Failures++;
    }
}
