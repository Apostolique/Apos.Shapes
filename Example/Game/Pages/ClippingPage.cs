using System;
using Apos.Shapes;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace GameProject {
    public class ClippingPage : Page {
        public ClippingPage(ShapeBatch sb, ShapeFont font) : base(sb, font) { }

        public override string Name => "Clipping";
        public override string Description => "Clips are rectangles that can be rounded and rotated. PushClip nests them, and every card on these pages is one.";

        protected override void DrawPage(float t, Vector2 mouse) {
            Vector2 c = Card(0, "Clip rectangles", "Bouncing balls inside a\nrounded window.");
            var window = new RectangleF(c.X - 105, c.Y - 70, 210, 140);
            Sb.PushClip(window, 26f);
            Sb.FillRectangle(new Vector2(window.X, window.Y), new Vector2(window.Width, window.Height), TWColor.Gray950);
            Color[] balls = [TWColor.Red500, TWColor.Amber400, TWColor.Sky500, TWColor.Emerald400];
            for (int i = 0; i < balls.Length; i++) {
                var at = new Vector2(Bounce(t * (0.31f + i * 0.07f) + i * 0.3f) * 110, Bounce(t * (0.43f + i * 0.05f) + i * 0.6f) * 75);
                Sb.FillCircle(c + at, 26, balls[i]);
            }
            Sb.PopClip();
            Sb.BorderRectangle(new Vector2(window.X, window.Y), new Vector2(window.Width, window.Height), TWColor.Gray500, 2f, 26f);
            EndCard();

            c = Card(1, "Rotated clips", "A clip can turn, and its\nedge is antialiased.");
            float turn = t * 0.5f;
            var frame = new RectangleF(c.X - 85, c.Y - 50, 170, 100);
            Sb.PushClip(frame, 16f, turn);
            Sb.FillRectangle(c - new Vector2(145, 110), new Vector2(290, 220),
                new Gradient(c, TWColor.Amber300, c + new Vector2(18, 18), TWColor.Gray800, _half, Gradient.Shape.Linear, Gradient.RepeatStyle.Sawtooth));
            Sb.PopClip();
            Sb.BorderRectangle(new Vector2(frame.X, frame.Y), new Vector2(frame.Width, frame.Height), TWColor.Gray100, 2f, 16f, turn);
            EndCard();

            c = Card(2, "Nested clips", "PushClip and PopClip. Draws\nland where every clip overlaps.");
            var outer = new RectangleF(c.X - 100, c.Y - 70, 200, 140);
            Vector2 lens = c + new Vector2(70 * MathF.Sin(t * 0.9f), 45 * MathF.Sin(t * 1.4f));
            var inner = new RectangleF(lens.X - 45, lens.Y - 45, 90, 90);
            var rainbow = new Palette(new Vector3(0.5f), new Vector3(0.5f), new Vector3(1f), new Vector3(0f, 0.33f, 0.67f));
            Sb.PushClip(outer, 20f);
            Sb.FillRectangle(new Vector2(outer.X, outer.Y), new Vector2(outer.Width, outer.Height), TWColor.Gray800);
            Sb.PushClip(inner, 45f * Wave(t, 5f), t);
            Sb.ColorSpace = ColorSpace.Rgb;
            Sb.FillRectangle(c - new Vector2(145, 110), new Vector2(290, 220), new Gradient(c, c + new Vector2(60, 0), rainbow, Gradient.Shape.Radial, Gradient.RepeatStyle.Sawtooth));
            Sb.ColorSpace = ColorSpace.Oklab;
            Sb.PopClip();
            Sb.PopClip();
            Sb.BorderRectangle(new Vector2(outer.X, outer.Y), new Vector2(outer.Width, outer.Height), TWColor.Gray500, 2f, 20f);
            EndCard();

            c = Card(3, "Scrolling list", "A list scrolls inside a clip,\nunder its own frame.");
            var list = new RectangleF(c.X - 110, c.Y - 85, 220, 175);
            const float row = 34f;
            float scroll = Frac(t * 0.08f) * _items.Length * row;
            Sb.PushClip(list, 12f);
            for (int i = 0; i < _items.Length * 2; i++) {
                (string name, Color color) = _items[i % _items.Length];
                var at = new Vector2(list.X, list.Y + i * row - scroll);
                Sb.FillRectangle(at + new Vector2(4, 2), new Vector2(list.Width - 8, row - 4), i % 2 == 0 ? TWColor.Gray800 : TWColor.Gray900, 8f);
                Sb.FillHexagon(at + new Vector2(22, row * 0.5f), 8, color, rounded: 2f);
                Sb.DrawString(Font, name, at + new Vector2(40, 9), 13f, TWColor.Gray200);
            }
            Sb.FillRectangle(new Vector2(list.X, list.Y), new Vector2(list.Width, 30), new Gradient(new Vector2(0, list.Y), TWColor.Gray950, new Vector2(0, list.Y + 30), WithAlpha(TWColor.Gray950, 0f)));
            Sb.FillRectangle(new Vector2(list.X, list.Bottom - 30), new Vector2(list.Width, 30), new Gradient(new Vector2(0, list.Bottom), TWColor.Gray950, new Vector2(0, list.Bottom - 30), WithAlpha(TWColor.Gray950, 0f)));
            Sb.PopClip();
            Sb.BorderRectangle(new Vector2(list.X, list.Y), new Vector2(list.Width, list.Height), TWColor.Gray600, 1.5f, 12f);
            EndCard();

            c = Card(4, "Marquee", "Text clips like any shape, so\na ticker can scroll through.");
            Ticker(c + new Vector2(0, -35), "ALERT  A wild hexagon appeared  +  Score 128 450  +  New high score  +  ", 24f, TWColor.Amber300, t * 60f);
            Ticker(c + new Vector2(0, 35), "Shapes, text, and drawings all clip the same way  /  ", 16f, TWColor.Sky300, t * 35f);
            EndCard();

            c = Card(5, "Hard or soft", "aaSize 0 makes a hard\nscissor. The default fades.");
            for (int i = 0; i < 2; i++) {
                Vector2 mid = c + new Vector2(-62 + i * 124, 0);
                var box = new RectangleF(mid.X - 38, mid.Y - 38, 76, 76);
                Sb.PushClip(box, 0f, t * 0.3f, i == 0 ? 0f : 1.5f);
                Sb.FillCircle(mid, 60, new Gradient(mid, TWColor.Lime300, mid + new Vector2(0, 60), TWColor.Teal700, Gradient.Shape.Radial));
                Sb.PopClip();
                Label(mid + new Vector2(0, 80), i == 0 ? "aaSize: 0" : "aaSize: 1.5", 12f, TWColor.Gray500);
            }
            EndCard();

            c = Card(6, "Iris", "A round clip that grows is a\nscene transition.");
            RectangleF view = Panel(c);
            Vector2 sky = new(view.X, view.Y);
            Vector2 skySize = new(view.Width, view.Height);
            Sb.PushClip(view, 10f);
            Sb.FillRectangle(sky, skySize, new Gradient(new Vector2(0, view.Top), TWColor.Indigo950, new Vector2(0, view.Bottom), TWColor.Indigo800));
            Sb.FillCircle(c + new Vector2(60, -40), 16, TWColor.Slate200);
            float iris = 150f * Wave(t, 5f);
            Vector2 focus = c + new Vector2(-30, 10);
            Sb.PushClip(new RectangleF(focus.X - iris, focus.Y - iris, iris * 2, iris * 2), iris);
            Sb.FillRectangle(sky, skySize, new Gradient(new Vector2(0, view.Top), TWColor.Sky500, new Vector2(0, view.Bottom), TWColor.Sky200));
            Sb.FillCircleBlurred(c + new Vector2(60, -40), 26, WithAlpha(TWColor.Yellow200, 0.6f), 12f);
            Sb.FillCircle(c + new Vector2(60, -40), 20, TWColor.Amber300);
            Sb.PopClip();
            Sb.FillCircle(new Vector2(c.X - 70, view.Bottom + 150), 190, TWColor.Green800);
            Sb.FillCircle(new Vector2(c.X + 110, view.Bottom + 170), 200, TWColor.Green900);
            Sb.PopClip();
            EndCard();

            c = Card(7, "Everything clips", "Gradients, dashes, blur, and\ntext all clip the same way.");
            Vector2 spot = c + new Vector2(60 * MathF.Sin(t * 0.7f), 40 * MathF.Sin(t * 1.1f));
            var lensBox = new RectangleF(spot.X - 75, spot.Y - 50, 150, 100);
            Sb.PushClip(lensBox, 18f, MathF.Sin(t) * 0.3f);
            Sb.FillRectangle(c - new Vector2(145, 110), new Vector2(290, 220), TWColor.Gray800);
            Sb.FillCircleBlurred(c + new Vector2(-50, -30), 40, TWColor.Fuchsia500, 18f);
            Sb.FillPath([c + new Vector2(-120, 60), c + new Vector2(-40, -10), c + new Vector2(40, 50), c + new Vector2(120, -20)], 6, TWColor.Lime300, dash: new DashStyle(14f, 10f, t));
            Label(c + new Vector2(20, -40), "hidden text", 22f, new Gradient(c - new Vector2(60, 0), TWColor.Amber300, c + new Vector2(100, 0), TWColor.Rose500));
            Sb.PopClip();
            Sb.BorderRectangle(new Vector2(lensBox.X, lensBox.Y), new Vector2(lensBox.Width, lensBox.Height), TWColor.Gray400, 1.5f, 18f, MathF.Sin(t) * 0.3f, dash: new DashStyle(6f, 5f, t));
            EndCard();
        }

        private void Ticker(Vector2 center, string text, float size, Color color, float distance) {
            const float half = 120f;
            Vector2 textSize = Font.MeasureString(text, size);
            var bar = new RectangleF(center.X - half, center.Y - textSize.Y * 0.5f - 6, half * 2, textSize.Y + 12);
            Sb.FillRectangle(new Vector2(bar.X, bar.Y), new Vector2(bar.Width, bar.Height), TWColor.Gray950, 6f);
            Sb.PushClip(bar, 6f);
            float x = bar.X - distance % textSize.X;
            for (; x < bar.Right; x += textSize.X) {
                Sb.DrawString(Font, text, new Vector2(x, center.Y - textSize.Y * 0.5f), size, color);
            }
            Sb.PopClip();
        }

        // Goes from -1 to 1 and back in straight lines, which is how a ball bounces between two walls.
        private static float Bounce(float x) => MathF.Abs(Frac(x) * 2f - 1f) * 2f - 1f;

        private static readonly Ramp _half = new((0f, 0f), (0.5f, 0f), (0.5f, 1f), (1f, 1f));
        private static readonly (string Name, Color Color)[] _items = [
            ("Iron sword", TWColor.Slate300), ("Health potion", TWColor.Rose500), ("Mana potion", TWColor.Sky500),
            ("Old map", TWColor.Amber200), ("Rope", TWColor.Orange300), ("Lantern", TWColor.Yellow300),
            ("Gold coin", TWColor.Amber400), ("Dragon scale", TWColor.Emerald500),
        ];
    }
}
