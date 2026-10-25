using System;
using Apos.Shapes;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace GameProject {
    public class ColorsPage : Page {
        public ColorsPage(ShapeBatch sb, ShapeFont font) : base(sb, font) { }

        public override string Name => "Colors";
        public override string Description => "Palettes, ramps, and color ramps give a gradient more than two colors. Their stops can change every frame.";

        protected override void DrawPage(float t, Vector2 mouse) {
            // The classic cosine palettes from iquilezles.org/articles/palettes are tuned for Rgb.
            Sb.ColorSpace = ColorSpace.Rgb;
            var half = new Vector3(0.5f);

            Vector2 c = Card(0, "Palette", "Colors from cosines. Sliding\nthe phase costs one float.");
            Palette[] palettes = [
                new Palette(half, half, new Vector3(1f), new Vector3(0f, 0.33f, 0.67f) + new Vector3(t * 0.2f)),
                new Palette(half, half, new Vector3(1f), new Vector3(0f, 0.1f, 0.2f) + new Vector3(t * 0.2f)),
                new Palette(half, half, new Vector3(2f, 1f, 0f), new Vector3(0.5f, 0.2f, 0.25f) + new Vector3(t * 0.1f)),
                new Palette(half, half, new Vector3(1f, 1f, 0.5f), new Vector3(0.8f, 0.9f, 0.3f) + new Vector3(t * 0.2f)),
            ];
            for (int i = 0; i < palettes.Length; i++) {
                Vector2 at = c + new Vector2(-115, -80 + i * 42);
                Sb.FillRectangle(at, new Vector2(230, 30), new Gradient(at, at + new Vector2(230, 0), palettes[i]), 6f);
            }
            EndCard();

            c = Card(1, "Palette shapes", "A palette takes any gradient\nshape and repeat style.");
            var candy = new Palette(half, half, new Vector3(2f, 1f, 0f), new Vector3(0.5f, 0.2f, 0.25f));
            var wheel = new Palette(new Vector3(0.6f), new Vector3(0.35f), new Vector3(1f), new Vector3(0f, 0.33f, 0.67f));
            Vector2 left = c + new Vector2(-62, 5);
            Vector2 right = c + new Vector2(62, 5);
            Sb.FillCircle(left, 55, new Gradient(left, left + Polar(t, 55), wheel, Gradient.Shape.ConicalAsym));
            Sb.FillCircle(right, 55, new Gradient(right, right + Polar(-t * 2f, 30), candy, Gradient.Shape.SpiralCCW, Gradient.RepeatStyle.Sawtooth));
            EndCard();

            c = Card(2, "Palette.FromStops", "Fits the cosines through\nstops you pick. Close, but\nnot exact.");
            Color middle = Color.Lerp(TWColor.Amber300, TWColor.Emerald400, Wave(t, 4f));
            (float, Color)[] stops = [(0f, TWColor.Sky500), (0.5f, middle), (1f, TWColor.Rose500)];
            Vector2 bar = c + new Vector2(-115, -40);
            Sb.FillRectangle(bar, new Vector2(230, 34), new Gradient(bar, bar + new Vector2(230, 0), Palette.FromStops(ColorSpace.Rgb, stops)), 6f);
            Sb.DrawString(Font, "FromStops", bar + new Vector2(0, -18), 12f, TWColor.Gray400);
            bar += new Vector2(0, 75);
            Sb.FillRectangle(bar, new Vector2(230, 34), new Gradient(bar, bar + new Vector2(230, 0), new ColorRamp(stops)), 6f);
            Sb.DrawString(Font, "ColorRamp, same stops", bar + new Vector2(0, -18), 12f, TWColor.Gray400);
            foreach ((float pos, Color color) in stops) {
                Sb.DrawCircle(bar + new Vector2(230 * pos, 52), 7, color, TWColor.Gray100, 2f);
            }
            EndCard();

            Sb.ColorSpace = ColorSpace.Oklab;

            c = Card(3, "Ramp", "Reshapes how the gradient\ntravels. Two stops on one\nposition make a hard edge.");
            float edge = 0.2f + 0.6f * Wave(t, 5f);
            Ramp[] ramps = [null, _ease, _steps, new Ramp((0f, 0f), (edge, 0.2f), (edge, 0.8f), (1f, 1f))];
            string[] rampNames = ["no ramp", "ease", "four steps", "a moving hard edge"];
            for (int i = 0; i < ramps.Length; i++) {
                Vector2 at = c + new Vector2(-115, -68 + i * 44);
                Gradient g = ramps[i] == null
                    ? new Gradient(at, TWColor.Amber300, at + new Vector2(230, 0), TWColor.Indigo600)
                    : new Gradient(at, TWColor.Amber300, at + new Vector2(230, 0), TWColor.Indigo600, ramps[i]);
                Sb.DrawString(Font, rampNames[i], at + new Vector2(0, -15), 11f, TWColor.Gray500);
                Sb.FillRectangle(at, new Vector2(230, 20), g, 5f);
            }
            EndCard();

            c = Card(4, "Color ramp", "As many color stops as you\nwant, hard edges included.");
            float cut = 0.3f + 0.4f * Wave(t, 4f);
            ColorRamp[] colorRamps = [
                _spectrum,
                _bands,
                new ColorRamp((0f, TWColor.Sky400), (cut, TWColor.Indigo600), (cut, TWColor.Amber300), (1f, TWColor.Rose500)),
            ];
            for (int i = 0; i < colorRamps.Length; i++) {
                Vector2 at = c + new Vector2(-115, -70 + i * 55);
                Sb.FillRectangle(at, new Vector2(230, 36), new Gradient(at, at + new Vector2(230, 0), colorRamps[i]), 6f);
            }
            EndCard();

            c = Card(5, "Sunset", "A color ramp rebuilt every\nframe as the sun goes down.");
            RectangleF sky = Panel(c);
            float dusk = Wave(t, 8f);
            float glow = MathF.Sin(MathF.PI * dusk);
            Color top = Color.Lerp(TWColor.Sky600, TWColor.Indigo950, dusk);
            Color band = Color.Lerp(Color.Lerp(TWColor.Sky400, TWColor.Indigo900, dusk), TWColor.Fuchsia600, glow * 0.8f);
            Color horizon = Color.Lerp(Color.Lerp(TWColor.Sky200, TWColor.Indigo800, dusk), TWColor.Orange400, glow);
            var colors = new ColorRamp((0f, top), (0.6f, band), (1f, horizon));
            Sb.FillRectangle(new Vector2(sky.X, sky.Y), new Vector2(sky.Width, sky.Height), new Gradient(new Vector2(0, sky.Top), new Vector2(0, sky.Bottom - 40), colors), 10f);
            Sb.PushClip(sky, 10f);
            Vector2 sun = new(c.X + 30, sky.Top + 40 + 130 * dusk);
            Sb.FillCircleBlurred(sun, 26, WithAlpha(Color.Lerp(TWColor.Yellow200, TWColor.Orange400, dusk), 0.6f), 16f);
            Sb.FillCircle(sun, 18, Color.Lerp(TWColor.Yellow200, TWColor.Orange400, dusk));
            Sb.FillCircle(new Vector2(c.X - 80, sky.Bottom + 150), 190, Color.Lerp(TWColor.Green700, TWColor.Gray950, dusk));
            Sb.FillCircle(new Vector2(c.X + 110, sky.Bottom + 170), 200, Color.Lerp(TWColor.Green800, TWColor.Gray950, dusk));
            Sb.PopClip();
            EndCard();

            c = Card(6, "Rings", "A hard ramp on a repeating\nradial gradient.");
            float p = Frac(t * 0.6f);
            Ramp rings = p < 0.5f
                ? new Ramp((0f, 0f), (p, 0f), (p, 1f), (p + 0.5f, 1f), (p + 0.5f, 0f), (1f, 0f))
                : new Ramp((0f, 1f), (p - 0.5f, 1f), (p - 0.5f, 0f), (p, 0f), (p, 1f), (1f, 1f));
            Sb.FillCircle(c + new Vector2(0, 8), 92, new Gradient(c + new Vector2(0, 8), TWColor.Gray100, c + new Vector2(24, 8), TWColor.Red500, rings, Gradient.Shape.Radial, Gradient.RepeatStyle.Sawtooth));
            EndCard();

            c = Card(7, "Dithering", "Blue noise hides banding on\nslow gradients. D toggles it.");
            RectangleF night = Panel(c);
            Sb.PushClip(night, 10f);
            Sb.ColorSpace = ColorSpace.Rgb;
            Sb.FillRectangle(new Vector2(night.X, night.Y), new Vector2(night.Width, night.Height), new Gradient(new Vector2(0, night.Top), new Color(18, 20, 38), new Vector2(0, night.Bottom), new Color(2, 3, 8)));
            Vector2 moon = c + new Vector2(-50 + 30 * MathF.Sin(t * 0.3f), -30);
            Sb.FillCircle(moon, 160, new Gradient(moon, new Color(48, 52, 76), moon + new Vector2(160, 0), new Color(48, 52, 76, 0), Gradient.Shape.Radial));
            Sb.FillCircle(moon, 14, TWColor.Slate200);
            Sb.ColorSpace = ColorSpace.Oklab;
            Sb.PopClip();
            Sb.DrawString(Font, Sb.DitherStrength > 0 ? "dither on" : "dither off", new Vector2(night.X + 10, night.Bottom - 22), 12f, TWColor.Gray500);
            EndCard();
        }

        private static readonly Ramp _ease = new((0f, 0f), (0.35f, 0.04f), (0.65f, 0.96f), (1f, 1f));
        private static readonly Ramp _steps = new((0f, 0f), (0.25f, 0f), (0.25f, 1f / 3f), (0.5f, 1f / 3f), (0.5f, 2f / 3f), (0.75f, 2f / 3f), (0.75f, 1f), (1f, 1f));
        private static readonly ColorRamp _spectrum = new((0f, TWColor.Red600), (0.15f, TWColor.Orange400), (0.3f, TWColor.Amber300), (0.45f, TWColor.Emerald500), (0.6f, TWColor.Teal400), (0.75f, TWColor.Sky400), (0.9f, TWColor.Indigo600), (1f, TWColor.Purple700));
        private static readonly ColorRamp _bands = new((0f, TWColor.Emerald500), (1f / 3f, TWColor.Emerald500), (1f / 3f, TWColor.Amber300), (2f / 3f, TWColor.Amber300), (2f / 3f, TWColor.Rose500), (1f, TWColor.Rose500));
    }
}
