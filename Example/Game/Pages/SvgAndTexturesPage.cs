using System;
using Apos.Shapes;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace GameProject {
    public class SvgAndTexturesPage : Page {
        public SvgAndTexturesPage(ShapeBatch sb, ShapeFont font, Assets assets) : base(sb, font) {
            _assets = assets;
        }

        public override string Name => "SVG and textures";
        public override string Description => "SVG drawings are solved from their curves like text. Textures use the SpriteBatch API. Both mix with shapes in one batch.";

        protected override void DrawPage(float t, Vector2 mouse) {
            Vector2 c = Card(0, "SVG at any size", "Each fill is solved from its\nown curves, like a glyph.");
            float size = 30f * MathF.Pow(180f / 30f, Wave(t, 5f));
            Sb.DrawSvg(_assets.Icon, c - _assets.Icon.Measure(size) * 0.5f, size);
            EndCard();

            c = Card(1, "Fill rules", "nonzero and evenodd, read\nfrom the file's fill-rule.");
            for (int i = 0; i < 2; i++) {
                ShapeSvg svg = i == 0 ? _assets.NonZero : _assets.EvenOdd;
                Vector2 mid = c + new Vector2(0, -45 + i * 95);
                Vector2 box = svg.Measure(70f);
                Sb.DrawSvg(svg, mid, 70f, 0.1f * MathF.Sin(t + i * 2f), box * 0.5f);
                Sb.DrawString(Font, i == 0 ? "nonzero" : "evenodd", mid + new Vector2(-125, -50), 11f, TWColor.Gray500);
            }
            EndCard();

            c = Card(2, "Gradients and strokes", "Gradients out of defs, and\nstrokes with caps and dashes.");
            Vector2 sun = c + new Vector2(-50, -40);
            Sb.DrawSvg(_assets.Sunrise, sun, 80f, 0.15f * MathF.Sin(t * 0.8f), _assets.Sunrise.Measure(80f) * 0.5f);
            Vector2 chart = c + new Vector2(45, 45);
            Sb.DrawSvg(_assets.Chart, chart, 85f, 0.1f * MathF.Sin(t * 0.6f + 1f), _assets.Chart.Measure(85f) * 0.5f);
            EndCard();

            c = Card(3, "Recolor", "One color or gradient of your\nown replaces every paint.");
            const float iconSize = 72f;
            Vector2 half = _assets.Heart.Measure(iconSize) * 0.5f;
            Vector2 a = c + new Vector2(-62, -48);
            Vector2 b = c + new Vector2(62, -48);
            Vector2 d = c + new Vector2(-62, 52);
            Vector2 e = c + new Vector2(62, 52);
            Sb.DrawSvg(_assets.Heart, a, iconSize, 0f, half);
            Sb.DrawSvg(_assets.Heart, b, iconSize, Color.Lerp(TWColor.Amber300, TWColor.Rose400, Wave(t, 3f)), 0f, half);
            Sb.DrawSvg(_assets.Heart, d, iconSize, new Gradient(d, TWColor.Sky400, d + Polar(t, 36), TWColor.Fuchsia600, Gradient.Shape.Conical), 0f, half);
            Sb.ColorSpace = ColorSpace.Rgb;
            var palette = new Palette(new Vector3(0.5f), new Vector3(0.5f), new Vector3(2f, 1f, 0f), new Vector3(0.5f, 0.2f, 0.25f) + new Vector3(t * 0.2f));
            Sb.DrawSvg(_assets.Heart, e, iconSize, new Gradient(e - half, e + half, palette), MathF.Sin(t) * 0.3f, half);
            Sb.ColorSpace = ColorSpace.Oklab;
            EndCard();

            c = Card(4, "Textures", "The SpriteBatch API, in the\nsame batch as the shapes.");
            Vector2 center = new Vector2(_assets.Planet.Width, _assets.Planet.Height) * 0.5f;
            Vector2 orbit = c + new Vector2(MathF.Cos(t) * 100, MathF.Sin(t) * 30);
            bool behind = MathF.Sin(t) < 0;
            if (behind) Sb.Draw(_assets.Moon, orbit, Color.White, -t, center, 0.45f);
            Sb.Draw(_assets.Planet, c, Color.White, t * 0.5f, center, 1.3f + 0.3f * MathF.Sin(t * 1.3f));
            if (!behind) Sb.Draw(_assets.Moon, orbit, Color.White, -t, center, 0.45f);
            EndCard();

            c = Card(5, "Source rectangles", "Frames from a sprite sheet\nmake a coin spin.");
            for (int i = 0; i < 3; i++) {
                int frame = (int)(t * 12f + i * 3) % Assets.CoinFrames;
                var source = new RectangleF(frame * Assets.CoinSize, 0, Assets.CoinSize, Assets.CoinSize);
                Vector2 at = c + new Vector2(-80 + i * 80, 8 * MathF.Sin(t * 3f + i));
                Sb.Draw(_assets.Coins, at, source, Color.White, 0f, new Vector2(Assets.CoinSize * 0.5f), 1.3f);
            }
            EndCard();

            c = Card(6, "Shapes in between", "Shadows under the sprites,\nbars over them.");
            Sb.FillRectangle(new Vector2(c.X - 133, c.Y + 42), new Vector2(266, 59), TWColor.Gray700, 10f);
            for (int i = 0; i < 3; i++) {
                float hop = MathF.Abs(MathF.Sin(t * 2.5f + i * 1.2f));
                Vector2 ground = c + new Vector2(-80 + i * 80, 55);
                Sb.FillEllipseBlurred(ground, 24 - 8 * hop, 6 - 2 * hop, new Color(TWColor.Black, 0.8f - 0.4f * hop), 4f);
                Vector2 body = ground - new Vector2(0, 28 + 40 * hop);
                Sb.Draw(_assets.Planet, body, Color.White, t * 0.4f + i, center, 0.75f);
                float health = Wave(t, 4f + i, i * 0.3f);
                Sb.FillRectangle(body + new Vector2(-24, -40), new Vector2(48, 7), TWColor.Gray950, 3.5f);
                Sb.FillRectangle(body + new Vector2(-23, -39), new Vector2(46 * health, 5), Color.Lerp(TWColor.Red500, TWColor.Lime400, health), 2.5f);
            }
            EndCard();

            c = Card(7, "Tinting", "The mask color multiplies the\ntexture.");
            for (int i = 0; i < 6; i++) {
                Vector2 at = c + new Vector2(-80 + i % 3 * 80, -40 + i / 3 * 85);
                Color mask = Color.Lerp(_tints[i], _tints[(i + 1) % _tints.Length], Wave(t, 3f, i / 6f));
                Sb.Draw(_assets.Moon, at, mask, t * 0.3f, center, 0.95f);
            }
            EndCard();
        }

        private static readonly Color[] _tints = [TWColor.Rose400, TWColor.Amber300, TWColor.Lime300, TWColor.Cyan300, TWColor.Indigo400, TWColor.Fuchsia400];

        private readonly Assets _assets;
    }
}
