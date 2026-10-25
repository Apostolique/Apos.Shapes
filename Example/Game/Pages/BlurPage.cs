using System;
using Apos.Shapes;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace GameProject {
    public class BlurPage : Page {
        public BlurPage(ShapeBatch sb, ShapeFont font) : base(sb, font) { }

        public override string Name => "Blur";
        public override string Description => "Blurred shapes fade out with a Gaussian edge, for shadows and glows. The blur is in world units, so it zooms with the shape.";

        protected override void DrawPage(float t, Vector2 mouse) {
            Vector2 c = Card(0, "Drop shadow", "A blurred copy under a card.\nIt spreads as the card lifts.");
            RectangleF table = Panel(c);
            Sb.FillRectangle(new Vector2(table.X, table.Y), new Vector2(table.Width, table.Height), TWColor.Slate400, 10f);
            float lift = Wave(t, 3f);
            Vector2 at = c + new Vector2(-75, -45 - 6 * lift);
            Sb.FillRectangleBlurred(at + new Vector2(0, 4 + 10 * lift), new Vector2(150, 100), new Color(TWColor.Black, 0.55f - 0.15f * lift), 4 + 14 * lift, 12f);
            Sb.FillRectangle(at, new Vector2(150, 100), TWColor.Slate100, 12f);
            Sb.FillCircle(at + new Vector2(28, 28), 12, TWColor.Sky400);
            Sb.FillRectangle(at + new Vector2(50, 20), new Vector2(80, 8), TWColor.Slate300, 4f);
            Sb.FillRectangle(at + new Vector2(50, 34), new Vector2(55, 6), TWColor.Slate300, 3f);
            Sb.FillRectangle(at + new Vector2(16, 60), new Vector2(118, 24), TWColor.Sky500, 6f);
            EndCard();

            c = Card(1, "Glow", "Neon: a blurred border under\na sharp one.");
            float buzz = 0.7f + 0.3f * MathF.Sin(t * 3f) * MathF.Sin(t * 7.3f);
            Vector2 sign = c + new Vector2(-105, -70);
            Sb.BorderRectangleBlurred(sign, new Vector2(210, 80), WithAlpha(TWColor.Pink500, buzz), 10f, 6f, 22f);
            Sb.BorderRectangle(sign, new Vector2(210, 80), TWColor.Pink200, 2f, 22f);
            Sb.FillEllipseBlurred(sign + new Vector2(105, 40), 70, 18, WithAlpha(TWColor.Pink500, 0.35f * buzz), 14f);
            Label(sign + new Vector2(105, 40), "OPEN", 34f, TWColor.Pink100);
            Vector2 bulb = c + new Vector2(0, 65);
            float hum = 0.75f + 0.25f * MathF.Sin(t * 2f);
            Sb.BorderCircleBlurred(bulb, 26, WithAlpha(TWColor.Cyan400, hum), 8f, 5f);
            Sb.BorderCircle(bulb, 28, TWColor.Cyan100, 2f);
            EndCard();

            c = Card(2, "Symmetric falloff", "The blur fades both ways, so\nthe 50% edge stays put.");
            Sb.FillCircleBlurred(c, 60, TWColor.Amber400, 0.5f + 30f * Wave(t, 4f));
            Sb.BorderCircle(c, 60, WithAlpha(TWColor.Gray100, 0.7f), 1f);
            EndCard();

            c = Card(3, "Lasers", "Blurred lines with a sharp\ncore. Each end has a radius.");
            Vector2 gun = c + new Vector2(-115, 45);
            for (int i = 0; i < 3; i++) {
                float aim = -0.4f + i * 0.2f + 0.12f * MathF.Sin(t * 1.5f + i * 2f);
                Vector2 end = gun + Polar(aim, 260);
                Color color = i == 1 ? TWColor.Lime400 : TWColor.Rose500;
                Sb.FillLineBlurred(gun, end, 3f, 9f, WithAlpha(color, 0.8f), 7f);
                Sb.FillLine(gun, end, 1.25f, Color.Lerp(color, Color.White, 0.8f));
            }
            Sb.FillCircleBlurred(gun, 14, WithAlpha(TWColor.Rose400, 0.8f), 8f);
            Sb.FillCircle(gun, 7, TWColor.Rose100);
            EndCard();

            c = Card(4, "Soft lights", "Blurred ellipses that drift\nand turn.");
            Color[] lights = [TWColor.Fuchsia500, TWColor.Sky500, TWColor.Amber400, TWColor.Emerald400];
            for (int i = 0; i < lights.Length; i++) {
                Vector2 drift = c + new Vector2(70 * MathF.Sin(t * (0.4f + i * 0.13f) + i * 1.7f), 50 * MathF.Cos(t * (0.3f + i * 0.11f) + i));
                Sb.FillEllipseBlurred(drift, 55, 30, WithAlpha(lights[i], 0.65f), 22f, t * 0.3f + i);
            }
            EndCard();

            c = Card(5, "Blurred outlines", "A band thinner than its blur\nsmears out and dims.");
            Sb.BorderCircleBlurred(c + new Vector2(-62, 0), 45, TWColor.Emerald400, 8f, 2f + 26f * Wave(t, 3f));
            Sb.BorderEllipseBlurred(c + new Vector2(62, 0), 50, 28, TWColor.Violet400, 3f + 10f * Wave(t, 2f), 6f, t * 0.6f);
            EndCard();

            c = Card(6, "Every shape", "Rectangles, chamfers, lines,\nand ellipses blur too.");
            float blur = 2f + 12f * Wave(t, 3f);
            Sb.FillRectangleBlurred(c + new Vector2(-95, -80), new Vector2(70, 60), TWColor.Sky400, blur, 10f, t * 0.5f);
            Sb.FillChamferBlurred(c + new Vector2(25, -80), new Vector2(70, 60), 18f, TWColor.Amber400, blur, -t * 0.5f);
            Sb.FillLineBlurred(c + new Vector2(-95, 50), c + new Vector2(-30, 30), 10, TWColor.Rose400, blur);
            Sb.FillEllipseBlurred(c + new Vector2(60, 45), 40, 22, TWColor.Lime300, blur, t);
            EndCard();

            c = Card(7, "Measure", "Measure.CircleBlurred gives\nthe box the blur reaches.");
            Vector2 orb = c + new Vector2(50 * MathF.Sin(t * 0.8f), 15 * MathF.Sin(t * 1.3f));
            float spread = 2f + 10f * Wave(t, 2.5f);
            Sb.FillCircleBlurred(orb, 26, TWColor.Fuchsia400, spread);
            RectangleF reach = Measure.CircleBlurred(orb, 26, spread);
            Sb.BorderRectangle(new Vector2(reach.X, reach.Y), new Vector2(reach.Width, reach.Height), TWColor.Gray500, 1f, dash: DashStyle.FromCount(24, 0.6f));
            EndCard();
        }
    }
}
