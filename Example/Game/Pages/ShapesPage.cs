using System;
using Apos.Shapes;
using Microsoft.Xna.Framework;

namespace GameProject {
    public class ShapesPage : Page {
        public ShapesPage(ShapeBatch sb, ShapeFont font) : base(sb, font) { }

        public override string Name => "Shapes";
        public override string Description => "Every shape comes as Fill, Border, and Draw, which does both. Sizes, corners, and borders can animate freely.";

        protected override void DrawPage(float t, Vector2 mouse) {
            Vector2 c = Card(0, "Circle", "Fill, Border, and Draw. The\nradius and border breathe.");
            Sb.FillCircle(c + new Vector2(-80, -45), 22 + 8 * Wave(t, 2f), TWColor.Sky400);
            Sb.BorderCircle(c + new Vector2(0, -45), 30, TWColor.Emerald300, 2 + 10 * Wave(t, 2.5f));
            Sb.DrawCircle(c + new Vector2(80, -45), 30, TWColor.Gray800, TWColor.Amber300, 3 + 5 * Wave(t, 3f));
            Vector2 drop = c + new Vector2(0, 50);
            for (int i = 0; i < 3; i++) {
                float p = Frac(t * 0.5f + i / 3f);
                float alpha = (1 - p) * MathF.Min(1, p * 8);
                Sb.BorderCircle(drop, 6 + 60 * p, WithAlpha(TWColor.Sky300, alpha), 3f);
            }
            Sb.FillCircle(drop, 5, TWColor.Sky300);
            EndCard();

            c = Card(1, "Ellipse", "Two radii and a rotation.\nThe radii can trade places.");
            float swap = MathF.Sin(t * 1.2f);
            Sb.DrawEllipse(c + new Vector2(-62, 0), 48 + 18 * swap, 48 - 18 * swap, TWColor.Gray800, TWColor.Violet300, 4f, t * 0.4f);
            Vector2 sun = c + new Vector2(66, 0);
            float orbit = t * 1.5f;
            Vector2 moon = sun + new Vector2(MathF.Cos(orbit) * 56, MathF.Sin(orbit) * 16);
            bool behind = MathF.Sin(orbit) < 0;
            Sb.BorderEllipse(sun, 56, 16, TWColor.Gray600, 1.5f);
            if (behind) Sb.FillCircle(moon, 5, TWColor.Sky300);
            Sb.FillCircle(sun, 12, new Gradient(sun + new Vector2(-4, -4), TWColor.Amber200, sun + new Vector2(8, 8), TWColor.Orange600, Gradient.Shape.Radial));
            if (!behind) Sb.FillCircle(moon, 5, TWColor.Sky300);
            EndCard();

            c = Card(2, "Rectangle", "Each corner gets its own\nradius. Rotation is free.");
            var corners = new CornerRadii(55 * Wave(t, 4f), 55 * Wave(t, 4f, 0.25f), 55 * Wave(t, 4f, 0.5f), 55 * Wave(t, 4f, 0.75f));
            Sb.DrawRectangle(c + new Vector2(-120, -55), new Vector2(110), TWColor.Gray800, TWColor.Rose300, 4f, corners);
            Sb.FillRectangle(c + new Vector2(25, -40), new Vector2(80),
                new Gradient(Vector2.Zero, TWColor.Fuchsia400, new Vector2(80), TWColor.Purple800, isLocal: true), 16f, t * 0.7f);
            EndCard();

            c = Card(3, "Chamfer", "The cut runs from a square\ncorner all the way to a\ndiamond.");
            Sb.DrawChamfer(c + new Vector2(-120, -50), new Vector2(110), 55 * Wave(t, 4f), TWColor.Gray800, TWColor.Amber300, 4f);
            var cuts = new CornerChamfers(36 * Wave(t, 3f), 36 * Wave(t, 3f, 0.33f), 36 * Wave(t, 3f, 0.66f), 36 * Wave(t, 5f));
            Sb.FillChamfer(c + new Vector2(20, -40), new Vector2(90),
                cuts, new Gradient(c + new Vector2(20, -40), TWColor.Lime300, c + new Vector2(110, 50), TWColor.Teal700), -t * 0.3f);
            EndCard();

            c = Card(4, "Hexagon", "Flat on top and bottom. Here\nthe size and rounding pulse.");
            const float cell = 15f;
            float step = cell * 2f / MathF.Sqrt(3f) * 1.5f;
            for (int col = -6; col <= 6; col++) {
                for (int row = -3; row <= 3; row++) {
                    Vector2 at = c + new Vector2(col * step, row * cell * 2f + (col & 1) * cell);
                    float v = 0.5f + 0.5f * MathF.Sin(Vector2.Distance(at, c) * 0.05f - t * 3f);
                    Sb.FillHexagon(at, 9f + 4f * v, Color.Lerp(TWColor.Gray800, TWColor.Teal400, v), rounded: 1f + 6f * v);
                }
            }
            EndCard();

            c = Card(5, "Triangles", "Equilateral, or any three\npoints, with rounded corners.");
            Sb.DrawEquilateralTriangle(c + new Vector2(-65, 8), 30, TWColor.Gray800, TWColor.Lime300, 4f, rounded: 12 * Wave(t, 3f), rotation: t * 0.8f);
            Vector2 mid = c + new Vector2(65, 0);
            Vector2 a = mid + Polar(t * 0.6f, 48 + 10 * MathF.Sin(t));
            Vector2 b = mid + Polar(t * 0.8f + 2.1f, 48 + 10 * MathF.Sin(t + 1));
            Vector2 d = mid + Polar(t * 1.0f + 4.2f, 48 + 10 * MathF.Sin(t + 2));
            Sb.DrawTriangle(a, b, d, new Gradient(mid + new Vector2(0, -50), TWColor.Amber300, mid + new Vector2(0, 50), TWColor.Red700), TWColor.Gray100, 2f, rounded: 6f);
            EndCard();

            c = Card(6, "Arc and ring", "Arcs end round and rings end\nflat. Angles run clockwise.");
            Vector2 spin = c + new Vector2(-65, -10);
            Sb.FillRing(spin, 0, MathF.Tau, 40, 6, TWColor.Gray800);
            float head = t * 3f;
            Sb.FillArc(spin, head, head + 0.4f + 4f * Wave(t, 1.5f), 40, 6, TWColor.Sky400);
            Label(spin + new Vector2(0, 68), "FillArc", 12f, TWColor.Gray500);
            Vector2 meter = c + new Vector2(65, -10);
            float progress = Wave(t, 5f);
            Sb.FillRing(meter, 0, MathF.Tau, 40, 9, TWColor.Gray800);
            Sb.FillRing(meter, -MathHelper.PiOver2, -MathHelper.PiOver2 + MathF.Tau * progress, 40, 9,
                new Gradient(meter, TWColor.Emerald300, meter + new Vector2(0, -40), TWColor.Teal600, Gradient.Shape.ConicalAsym));
            Label(meter, $"{progress * 100:0}%", 16f, TWColor.Gray100);
            Label(meter + new Vector2(0, 68), "FillRing", 12f, TWColor.Gray500);
            EndCard();

            c = Card(7, "Line", "Two points and a radius,\nwith round caps.");
            Sb.DrawCircle(c, 88, TWColor.Gray800, TWColor.Gray600, 2f);
            for (int i = 0; i < 12; i++) {
                float angle = MathF.Tau * i / 12f;
                bool major = i % 3 == 0;
                Sb.FillLine(c + Polar(angle, major ? 68 : 74), c + Polar(angle, 80), major ? 2.5f : 1.5f, TWColor.Gray400);
            }
            float seconds = t * MathF.Tau / 6f - MathHelper.PiOver2;
            float minutes = t * MathF.Tau / 72f - MathHelper.PiOver2;
            float hours = t * MathF.Tau / 864f - MathHelper.PiOver2;
            Sb.FillLine(c, c + Polar(hours, 42), 4.5f, TWColor.Gray100);
            Sb.FillLine(c, c + Polar(minutes, 62), 3f, TWColor.Gray200);
            Sb.FillLine(c - Polar(seconds, 16), c + Polar(seconds, 70), 1.25f, TWColor.Rose400);
            Sb.FillCircle(c, 5, TWColor.Rose400);
            EndCard();
        }
    }
}
