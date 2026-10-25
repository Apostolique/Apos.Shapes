using System;
using Apos.Shapes;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace GameProject {
    public class TextPage : Page {
        public TextPage(ShapeBatch sb, ShapeFont font) : base(sb, font) { }

        public override string Name => "Text";
        public override string Description => "Text is solved from the font's own outlines in the shader. There's no atlas, so it stays sharp at any size and zoom.";

        protected override void DrawPage(float t, Vector2 mouse) {
            Vector2 c = Card(0, "Any size", "Nothing is baked, so every\nsize reads the same curves.");
            float size = 8f * MathF.Pow(70f / 8f, Wave(t, 5f));
            Label(c, "Shapes", size, TWColor.Gray100);
            EndCard();

            c = Card(1, "Rotation", "Text turns around any origin\nyou give it.");
            const string spin = "spinning";
            Sb.DrawString(Font, spin, c + new Vector2(0, 8), 26f, TWColor.Sky300, t * 0.8f, Font.MeasureString(spin, 26f) * 0.5f);
            Vector2 word = Font.MeasureString("turn", 14f);
            for (int i = 0; i < 10; i++) {
                float angle = -t * 0.4f + MathF.Tau * i / 10f;
                Sb.DrawString(Font, "turn", c + new Vector2(0, 8) + Polar(angle, 74), 14f, TWColor.Gray500, angle, word * 0.5f);
            }
            EndCard();

            c = Card(2, "Text on a circle", "Each letter is placed by its\nown advance.");
            DrawCircleText(c + new Vector2(0, 5), 78f, t * 0.4f, "APOS.SHAPES * TEXT ALONG A CIRCLE * ");
            EndCard();

            c = Card(3, "Wavy text", "One call per letter, so each\none can bob on its own. The\ngradient runs across them.");
            const string wavy = "Wavy text!";
            const float wavySize = 34f;
            float x = c.X - Font.MeasureString(wavy, wavySize).X * 0.5f;
            var palette = new Palette(new Vector3(0.5f), new Vector3(0.5f), new Vector3(1f), new Vector3(0f, 0.33f, 0.67f) + new Vector3(t * 0.3f));
            var fill = new Gradient(new Vector2(x, 0), new Vector2(x + 300, 0), palette);
            Sb.ColorSpace = ColorSpace.Rgb;
            for (int i = 0; i < wavy.Length; i++) {
                float bob = 10f * MathF.Sin(t * 5f - i * 0.6f);
                float advance = Font.Advance(wavy[i]) * wavySize;
                Sb.DrawString(Font, wavy.AsSpan(i, 1), new Vector2(x + advance * 0.5f, c.Y + 10 + bob), wavySize, fill, 0.12f * MathF.Cos(t * 5f - i * 0.6f), new Vector2(advance * 0.5f, wavySize * 0.6f));
                x += advance;
            }
            Sb.ColorSpace = ColorSpace.Oklab;
            EndCard();

            c = Card(4, "Gradient fills", "The fill runs across the\nwhole line, not per letter.");
            Vector2 at = c + new Vector2(-115, -75);
            Sb.ColorSpace = ColorSpace.Rgb;
            Sb.DrawString(Font, "Palette", at, 32f, new Gradient(at, at + new Vector2(150, 0), palette));
            Sb.ColorSpace = ColorSpace.Oklab;
            at += new Vector2(0, 50);
            Sb.DrawString(Font, "Color ramp", at, 32f, new Gradient(at + new Vector2(t * 40, 0), at + new Vector2(t * 40 + 200, 0), _spectrum, Gradient.Shape.Linear, Gradient.RepeatStyle.Triangle));
            const string turning = "Local, turning";
            Vector2 box = Font.MeasureString(turning, 26f);
            Sb.DrawString(Font, turning, c + new Vector2(0, 65), 26f,
                new Gradient(Vector2.Zero, TWColor.Lime300, box, TWColor.Teal600, isLocal: true), 0.2f * MathF.Sin(t), box * 0.5f);
            EndCard();

            c = Card(5, "Typewriter", "A span overload draws part\nof a string without\nallocating.");
            const float typeSize = 17f;
            int shown = Math.Min((int)(Frac(t / 9f) * (_story.Length + 40)), _story.Length);
            Vector2 page = c + new Vector2(-118, -60);
            ReadOnlySpan<char> typed = _story.AsSpan(0, shown);
            Sb.DrawString(Font, typed, page, typeSize, TWColor.Gray200);
            int lineStart = typed.LastIndexOf('\n') + 1;
            int lines = typed.Count('\n');
            Vector2 caret = page + new Vector2(Font.MeasureString(typed[lineStart..], typeSize).X + 1, lines * Font.LineHeight * typeSize);
            Sb.FillRectangle(caret, new Vector2(2, Font.LineHeight * typeSize), WithAlpha(TWColor.Amber300, Wave(t, 0.9f)));
            EndCard();

            c = Card(6, "Any code point", "Any glyph the font has, read\nfrom the file on first use.");
            Sb.DrawString(Font, "Ελληνικά  Кириллица\nÀÉÎÕÜ ß æ ø ñ ç\n±≠≤≥ ∞ → ← ↑ ↓\n♠ ♣ ♥ ♦  ░ ▒ ▓ █", c + new Vector2(-118, -55), 17f, TWColor.Emerald200);
            EndCard();

            c = Card(7, "MeasureString", "Gives the box a string fills,\nso a button can fit its label.\nHover it.");
            DrawButton(c + new Vector2(0, 10), t, mouse);
            EndCard();
        }

        private void DrawCircleText(Vector2 center, float radius, float turn, string text) {
            float total = 0;
            for (int i = 0; i < text.Length; i++) total += Font.Advance(text[i]);
            float size = MathF.Tau * radius / total;
            float along = 0;
            for (int i = 0; i < text.Length; i++) {
                float advance = Font.Advance(text[i]) * size;
                float angle = turn + (along + advance * 0.5f) / radius - MathHelper.PiOver2;
                Sb.DrawString(Font, text.AsSpan(i, 1), center + Polar(angle, radius), size,
                    Color.Lerp(TWColor.Amber300, TWColor.Rose400, 0.5f + 0.5f * MathF.Sin(angle * 2)),
                    angle + MathHelper.PiOver2, new Vector2(advance * 0.5f, Font.Ascent * size));
                along += advance;
            }
        }

        private void DrawButton(Vector2 center, float t, Vector2 mouse) {
            const float size = 22f;
            const float pad = 22f;
            float phase = t / 2.5f;
            int i = (int)MathF.Floor(phase);
            float f = Frac(phase);
            float s = MathHelper.SmoothStep(0f, 1f, MathHelper.Clamp((f - 0.75f) / 0.25f, 0f, 1f));
            string from = _labels[i % _labels.Length];
            string to = _labels[(i + 1) % _labels.Length];
            float width = MathHelper.Lerp(Font.MeasureString(from, size).X, Font.MeasureString(to, size).X, s) + pad * 2f;
            float height = Font.LineHeight * size + 20f;
            var rect = new RectangleF(center.X - width * 0.5f, center.Y - height * 0.5f, width, height);
            bool hover = rect.Contains(mouse);

            if (hover) Sb.FillRectangleBlurred(new Vector2(rect.X, rect.Y), new Vector2(rect.Width, rect.Height), WithAlpha(TWColor.Sky400, 0.6f), 12f, 12f);
            Sb.DrawRectangle(new Vector2(rect.X, rect.Y), new Vector2(rect.Width, rect.Height), hover ? TWColor.Sky500 : TWColor.Gray800, TWColor.Sky400, 2f, 12f);
            Label(center, from, size, WithAlpha(TWColor.Gray100, 1f - s * 2f));
            Label(center, to, size, WithAlpha(TWColor.Gray100, s * 2f - 1f));
        }

        private const string _story = "The hero walks into\nthe cave. Something\nmoves in the dark.\nIt's a hexagon.";
        private static readonly string[] _labels = ["Play", "Continue", "Options", "Quit to desktop"];
        private static readonly ColorRamp _spectrum = new((0f, TWColor.Red500), (0.2f, TWColor.Amber300), (0.4f, TWColor.Emerald400), (0.6f, TWColor.Sky400), (0.8f, TWColor.Indigo500), (1f, TWColor.Fuchsia500));
    }
}
