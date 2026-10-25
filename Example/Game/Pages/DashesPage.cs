using System;
using Apos.Shapes;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace GameProject {
    public class DashesPage : Page {
        public DashesPage(ShapeBatch sb, ShapeFont font) : base(sb, font) { }

        public override string Name => "Dashes";
        public override string Description => "Outlines and strokes can be dashed. The pattern bends around corners, and sliding its offset animates it.";

        protected override void DrawPage(float t, Vector2 mouse) {
            Vector2 c = Card(0, "Marching ants", "A selection box. The offset\nis in periods, so it loops.");
            Sb.FillRectangle(c + new Vector2(-105, -62), new Vector2(210, 124), WithAlpha(TWColor.Sky500, 0.12f), 6f);
            Sb.BorderRectangle(c + new Vector2(-105, -62), new Vector2(210, 124), TWColor.Sky300, 2f, 6f, dash: new DashStyle(8f, 6f, t * 1.5f));
            for (int i = 0; i < 3; i++) {
                Vector2 unit = c + new Vector2(-55 + i * 55, 8 * MathF.Sin(t * 2f + i));
                Sb.BorderCircle(unit, 21, TWColor.Lime300, 2.5f, dash: new DashStyle(0f, 8f, -t, DashCap.Round));
                Sb.FillCircle(unit, 14, TWColor.Gray600);
            }
            EndCard();

            c = Card(1, "Dots", "A dash of length 0 with round\ncaps is a dot.");
            Sb.BorderCircle(c + new Vector2(-60, -25), 42, TWColor.Pink400, 6f, dash: new DashStyle(0f, 14f, t * 0.5f, DashCap.Round));
            Sb.DrawEllipse(c + new Vector2(60, -25), 52, 34, TWColor.Gray800, TWColor.Amber300, 6f, dash: new DashStyle(14f, 12f, t, DashCap.Round));
            Sb.FillLine(c + new Vector2(-110, 60), c + new Vector2(110, 60), 4, TWColor.Teal300, dash: new DashStyle(0f, 16f, -t * 2f, DashCap.Round));
            EndCard();

            c = Card(2, "Strokes", "Lines, arcs, and paths are cut\ninto dashes, caps and all.");
            Sb.DrawLine(c + new Vector2(-110, -60), c + new Vector2(110, -60), 9, TWColor.Gray800, TWColor.Pink400, 2f, dash: new DashStyle(28f, 16f, t));
            Sb.FillArc(c + new Vector2(-60, 30), t, t + 4.2f, 40, 7, TWColor.Orange400, dash: new DashStyle(6f, 20f, cap: DashCap.Round));
            Sb.FillPath([c + new Vector2(20, 60), c + new Vector2(50, 0), c + new Vector2(80, 60), c + new Vector2(110, 0)], 6, TWColor.Lime300,
                join: PathJoin.Miter, dash: new DashStyle(14f, 10f, -t * 1.5f));
            EndCard();

            c = Card(3, "A fixed count", "FromCount keeps 12 dashes as\nthe shape grows and shrinks.");
            Sb.BorderHexagon(c + new Vector2(-60, 0), 30 + 25 * Wave(t, 3f), TWColor.Emerald300, 6f, rounded: 6f, dash: DashStyle.FromCount(12, 0.55f, t * 0.5f));
            float span = 1f + 4.5f * Wave(t, 4f);
            Sb.FillArc(c + new Vector2(65, 0), -MathHelper.PiOver2 - span * 0.5f, -MathHelper.PiOver2 + span * 0.5f, 45, 7, TWColor.Sky400,
                dash: DashStyle.FromCount(8, 0.2f, cap: DashCap.Round));
            EndCard();

            c = Card(4, "Corners", "Dashes bend around corners at\nfull width, sharp or rounded.");
            Sb.BorderEquilateralTriangle(c + new Vector2(-60, 12), 30, TWColor.Amber300, 9f, rounded: 2f, rotation: t * 0.4f, dash: new DashStyle(18f, 12f, t * 0.6f, DashCap.Round));
            float cut = 10 + 30 * Wave(t, 3f);
            Sb.DrawChamfer(c + new Vector2(10, -50), new Vector2(100), cut, TWColor.Gray800, TWColor.Cyan300, 8f, dash: DashStyle.FromCount(12, 0.6f, t * 0.3f));
            EndCard();

            c = Card(5, "Gradient dashes", "Each dash takes the fill or\nborder gradient.");
            Vector2 ring = c + new Vector2(-60, 0);
            Sb.BorderCircle(ring, 50, new Gradient(ring, TWColor.Sky400, ring + Polar(t, 50), TWColor.Fuchsia500, Gradient.Shape.ConicalAsym), 10f,
                dash: DashStyle.FromCount(16, 0.6f));
            Sb.FillLine(c + new Vector2(20, -55), c + new Vector2(110, 55), 8,
                new Gradient(c + new Vector2(20, -55), TWColor.Amber300, c + new Vector2(110, 55), TWColor.Rose500), dash: new DashStyle(20f, 12f, -t));
            EndCard();

            c = Card(6, "Segmented bars", "A counted dash, clipped to\nthe value it shows.");
            Bar(c + new Vector2(0, -55), 0.15f + 0.85f * Wave(t, 6f), TWColor.Rose500);
            Bar(c + new Vector2(0, -20), Wave(t, 4f, 0.3f), TWColor.Sky400);
            // Off keeps every dash where it is, so the ring fills in without the pattern moving.
            Vector2 stamina = c + new Vector2(-75, 50);
            var segments = new DashStyle(9f, 5f, snap: DashSnap.Off);
            float full = -MathHelper.PiOver2 + MathF.Tau;
            Sb.FillRing(stamina, -MathHelper.PiOver2, full, 28, 6, TWColor.Gray800, dash: segments);
            Sb.FillRing(stamina, -MathHelper.PiOver2, MathHelper.Lerp(-MathHelper.PiOver2, full, Wave(t, 5f)), 28, 6, TWColor.Emerald400, dash: segments);
            Sb.DrawString(Font, "HP, mana,\nand stamina", c + new Vector2(-25, 30), 13f, TWColor.Gray500);
            EndCard();

            c = Card(7, "Snapping", "Off, Tiling, and EndToEnd fit\nthe pattern to the shape.");
            (DashSnap Snap, string Name)[] snaps = [(DashSnap.Off, "Off"), (DashSnap.Tiling, "Tiling"), (DashSnap.EndToEnd, "EndToEnd")];
            for (int i = 0; i < snaps.Length; i++) {
                float y = -50 + i * 50;
                Sb.DrawString(Font, snaps[i].Name, c + new Vector2(-110, y - 28), 12f, TWColor.Gray500);
                Sb.FillLine(c + new Vector2(-105, y), c + new Vector2(100, y), 7, TWColor.Violet300, dash: new DashStyle(22f, 14f, snap: snaps[i].Snap));
            }
            EndCard();
        }

        private void Bar(Vector2 center, float value, Color color) {
            const float half = 110f;
            var segments = DashStyle.FromCount(10, 0.82f);
            Vector2 a = center - new Vector2(half, 0);
            Vector2 b = center + new Vector2(half, 0);
            Sb.FillPath([a, b], 8, TWColor.Gray800, cap: PathCap.Butt, dash: segments);
            Sb.PushClip(new RectangleF(a.X, a.Y - 10, half * 2 * value, 20));
            Sb.FillPath([a, b], 8, color, cap: PathCap.Butt, dash: segments);
            Sb.PopClip();
        }
    }
}
