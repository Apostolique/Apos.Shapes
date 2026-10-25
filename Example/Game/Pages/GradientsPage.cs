using System;
using Apos.Shapes;
using Microsoft.Xna.Framework;

namespace GameProject {
    public class GradientsPage : Page {
        public GradientsPage(ShapeBatch sb, ShapeFont font) : base(sb, font) { }

        public override string Name => "Gradients";
        public override string Description => "A gradient goes anywhere a color does: fills, borders, dashes, text, and SVG drawings.";

        protected override void DrawPage(float t, Vector2 mouse) {
            Vector2 c = Card(0, "Gradient shapes", "How the space between the two\npoints is swept.");
            Gradient.Shape[] shapes = [
                Gradient.Shape.Linear, Gradient.Shape.Bilinear, Gradient.Shape.Radial, Gradient.Shape.Square,
                Gradient.Shape.Conical, Gradient.Shape.ConicalAsym, Gradient.Shape.Cross, Gradient.Shape.SpiralCW,
            ];
            for (int i = 0; i < shapes.Length; i++) {
                Vector2 at = c + new Vector2(-103 + i / 4 * 130, -72 + i % 4 * 50);
                Sb.FillCircle(at, 20, new Gradient(at, TWColor.Sky300, at + Polar(t * 0.8f, 20), TWColor.Indigo800, shapes[i]));
                Sb.DrawString(Font, shapes[i].ToString(), at + new Vector2(28, -8), 12f, TWColor.Gray400);
            }
            EndCard();

            c = Card(1, "Repeat styles", "What happens past the second\npoint. The points slide.");
            Gradient.RepeatStyle[] repeats = [Gradient.RepeatStyle.None, Gradient.RepeatStyle.Sawtooth, Gradient.RepeatStyle.Triangle, Gradient.RepeatStyle.Sine];
            for (int i = 0; i < repeats.Length; i++) {
                Vector2 at = c + new Vector2(-115, -80 + i * 48);
                float slide = repeats[i] == Gradient.RepeatStyle.None ? 80 + 60 * MathF.Sin(t) : t * 30;
                Vector2 a = at + new Vector2(slide, 0);
                Sb.DrawString(Font, repeats[i].ToString(), at + new Vector2(0, -16), 12f, TWColor.Gray400);
                Sb.FillRectangle(at, new Vector2(230, 22), new Gradient(a, TWColor.Cyan300, a + new Vector2(60, 0), TWColor.Blue800, Gradient.Shape.Linear, repeats[i]), 5f);
            }
            EndCard();

            c = Card(2, "Local gradients", "isLocal pins the gradient to\nthe shape, so it turns too.");
            for (int i = 0; i < 2; i++) {
                bool local = i == 1;
                Vector2 mid = c + new Vector2(-62 + i * 124, -10);
                Gradient g = local
                    ? new Gradient(Vector2.Zero, TWColor.Amber300, new Vector2(80), TWColor.Rose600, isLocal: true)
                    : new Gradient(mid - new Vector2(40), TWColor.Amber300, mid + new Vector2(40), TWColor.Rose600);
                Sb.FillRectangle(mid - new Vector2(40), new Vector2(80), g, 12f, t * 0.8f);
                Label(mid + new Vector2(0, 75), local ? "local" : "world", 12f, TWColor.Gray500);
            }
            EndCard();

            c = Card(3, "Border gradients", "The border takes a gradient\nof its own, apart from the\nfill.");
            Vector2 disc = c + new Vector2(-62, 5);
            Sb.DrawCircle(disc, 50,
                new Gradient(disc, TWColor.Indigo800, disc + new Vector2(0, 50), TWColor.Gray900, Gradient.Shape.Radial),
                new Gradient(disc, TWColor.Amber300, disc + Polar(t * 1.5f, 50), TWColor.Rose500, Gradient.Shape.Conical), 8f);
            Vector2 box = c + new Vector2(14, -45);
            Sb.DrawRectangle(box, new Vector2(100), TWColor.Gray800,
                new Gradient(box + new Vector2(t * 40, 0), TWColor.Cyan300, box + new Vector2(t * 40 + 50, 50), TWColor.Blue700, Gradient.Shape.Linear, Gradient.RepeatStyle.Triangle), 8f, 20f);
            EndCard();

            c = Card(4, "Spirals", "SpiralCW and SpiralCCW. The\nsecond point sets the width\nof one winding.");
            for (int i = 0; i < 2; i++) {
                Vector2 mid = c + new Vector2(-62 + i * 124, 10);
                var shape = i == 0 ? Gradient.Shape.SpiralCW : Gradient.Shape.SpiralCCW;
                float turn = i == 0 ? t * 2f : -t * 2f;
                Sb.FillCircle(mid, 55, new Gradient(mid, TWColor.Yellow300, mid + Polar(turn, 22), TWColor.Orange700, shape, Gradient.RepeatStyle.Triangle));
            }
            EndCard();

            c = Card(5, "Radar", "A sweep that fades out, from\na conical gradient.");
            Sb.FillCircle(c, 92, TWColor.Emerald950);
            for (int i = 1; i <= 3; i++) Sb.BorderCircle(c, 30 * i, TWColor.Emerald800, 1f);
            Sb.FillLine(c - new Vector2(90, 0), c + new Vector2(90, 0), 0.5f, TWColor.Emerald800);
            Sb.FillLine(c - new Vector2(0, 90), c + new Vector2(0, 90), 0.5f, TWColor.Emerald800);
            float sweep = t * 2f;
            Sb.FillCircle(c, 90, new Gradient(c, WithAlpha(TWColor.Emerald400, 0.6f), c + Polar(sweep, 90), WithAlpha(TWColor.Emerald400, 0f), Gradient.Shape.ConicalAsym));
            Sb.FillLine(c, c + Polar(sweep, 90), 1f, TWColor.Emerald300);
            for (int i = 0; i < 5; i++) {
                float angle = i * 2.4f + 0.5f;
                Vector2 blip = c + Polar(angle, 25 + i * 13);
                float since = Frac((sweep - angle) / MathF.Tau);
                Sb.FillCircleBlurred(blip, 4f, WithAlpha(TWColor.Emerald300, 1f - since), 4f);
            }
            EndCard();

            c = Card(6, "Color spaces", "The same two colors blended\nin each space. Oklab is the\ndefault.");
            (ColorSpace Space, string Name)[] spaces = [(ColorSpace.Rgb, "Rgb"), (ColorSpace.Oklab, "Oklab"), (ColorSpace.Oklch, "Oklch")];
            Color from = Hue(t * 40f);
            Color to = Hue(t * 40f + 180f);
            for (int i = 0; i < spaces.Length; i++) {
                Vector2 at = c + new Vector2(-55, -60 + i * 50);
                Sb.DrawString(Font, spaces[i].Name, at + new Vector2(-62, 8), 12f, TWColor.Gray400);
                Sb.ColorSpace = spaces[i].Space;
                Sb.FillRectangle(at, new Vector2(170, 34), new Gradient(at, from, at + new Vector2(170, 0), to), 6f);
            }
            Sb.ColorSpace = ColorSpace.Oklab;
            EndCard();

            c = Card(7, "Offsets", "aOffset and bOffset hold each\nend solid for a while.");
            for (int i = 0; i < 3; i++) {
                Vector2 at = c + new Vector2(-115, -55 + i * 45);
                float hold = 70 * Wave(t, 3f, i * 0.15f);
                float aOffset = i != 1 ? hold : 0f;
                float bOffset = i != 0 ? hold : 0f;
                Sb.FillRectangle(at, new Vector2(230, 26), new Gradient(at, TWColor.Amber300, at + new Vector2(230, 0), TWColor.Red700, aOffset: aOffset, bOffset: bOffset), 5f);
            }
            EndCard();
        }

        // A bright color at an angle around the color wheel, in degrees.
        private static Color Hue(float degrees) {
            float h = Frac(degrees / 360f) * 6f;
            float x = 1f - MathF.Abs(h % 2f - 1f);
            Vector3 rgb = (int)h switch {
                0 => new Vector3(1, x, 0),
                1 => new Vector3(x, 1, 0),
                2 => new Vector3(0, 1, x),
                3 => new Vector3(0, x, 1),
                4 => new Vector3(x, 0, 1),
                _ => new Vector3(1, 0, x),
            };
            return new Color(Vector3.Lerp(new Vector3(0.25f), rgb, 0.8f) * 0.95f);
        }
    }
}
