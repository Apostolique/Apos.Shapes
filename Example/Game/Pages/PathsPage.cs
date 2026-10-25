using System;
using Apos.Shapes;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace GameProject {
    public class PathsPage : Page {
        public PathsPage(ShapeBatch sb, ShapeFont font) : base(sb, font) { }

        public override string Name => "Paths";
        public override string Description => "A path draws a polyline as one stroke, open or closed. It blends once, even where its segments meet.";

        protected override void DrawPage(float t, Vector2 mouse) {
            Vector2 c = Card(0, "Joins", "Round, miter, and bevel as\nthe angle closes. Sharp\nmiters are cut flat.");
            PathJoin[] joins = [PathJoin.Round, PathJoin.Miter, PathJoin.Bevel];
            float spread = MathHelper.Lerp(0.15f, 0.95f, Wave(t, 4f));
            for (int i = 0; i < 3; i++) {
                Vector2 apex = c + new Vector2(-85 + i * 85, -20);
                Vector2 arm = new Vector2(MathF.Sin(spread), MathF.Cos(spread)) * 36;
                Sb.FillPath([apex + new Vector2(-arm.X, arm.Y), apex, apex + arm], 9, TWColor.Sky400, join: joins[i]);
                Label(apex + new Vector2(0, 62), joins[i].ToString(), 12f, TWColor.Gray500);
            }
            EndCard();

            c = Card(1, "Caps", "Round, butt, and square. The\nend cap can be different.");
            (PathCap Start, PathCap End, string Name)[] caps = [
                (PathCap.Round, PathCap.Round, "Round"),
                (PathCap.Butt, PathCap.Butt, "Butt"),
                (PathCap.Square, PathCap.Square, "Square"),
                (PathCap.Butt, PathCap.Round, "Butt/Round"),
            ];
            for (int i = 0; i < caps.Length; i++) {
                Vector2 a = c + new Vector2(-95, -60 + i * 40);
                Vector2 b = c + new Vector2(5 + 40 * Wave(t, 3f, i * 0.2f), -60 + i * 40);
                Sb.FillPath([a, b], 11, TWColor.Emerald300, cap: caps[i].Start, capEnd: caps[i].End);
                Sb.FillLine(a, b, 0.75f, TWColor.Gray950);
                Sb.DrawString(Font, caps[i].Name, c + new Vector2(64, -68 + i * 40), 12f, TWColor.Gray500);
            }
            EndCard();

            c = Card(2, "A radius per point", "So a stroke can swell and\ntaper, like a pen does.");
            Span<Vector2> body = stackalloc Vector2[32];
            Span<float> radii = stackalloc float[32];
            for (int i = 0; i < body.Length; i++) {
                float x = -120 + i * 7.5f;
                body[i] = c + new Vector2(x, 38 * MathF.Sin(x * 0.03f - t * 3f));
                radii[i] = 1.5f + 11f * MathF.Sin(MathF.PI * i / (body.Length - 1)) * (0.75f + 0.25f * MathF.Sin(i * 0.5f - t * 5f));
            }
            Sb.FillPath(body, radii, new Gradient(c + new Vector2(-120, 0), TWColor.Lime300, c + new Vector2(120, 0), TWColor.Teal600));
            EndCard();

            c = Card(3, "Built point by point", "BeginPath, PathTo, and\nEndPath. No array needed.");
            Sb.BeginFillPath(1f, TWColor.Cyan300);
            const int trail = 90;
            for (int i = 0; i <= trail; i++) {
                float s = t - 2.5f * (1f - i / (float)trail);
                Sb.PathTo(c + Lissajous(s), 0.5f + 6.5f * i / trail);
            }
            Sb.EndPath();
            Sb.FillCircleBlurred(c + Lissajous(t), 9f, WithAlpha(TWColor.Cyan400, 0.6f), 8f);
            EndCard();

            c = Card(4, "Closed", "The last point joins the\nfirst, with no seam.");
            Span<Vector2> star = stackalloc Vector2[10];
            float inner = 30 + 35 * Wave(t, 4f);
            for (int i = 0; i < star.Length; i++) {
                star[i] = c + Polar(t * 0.3f + MathF.PI * i / 5f - MathHelper.PiOver2, i % 2 == 0 ? 80 : inner);
            }
            Sb.FillPath(star, 7, WithAlpha(TWColor.Fuchsia400, 0.6f), join: PathJoin.Miter, closed: true);
            EndCard();

            c = Card(5, "Joins per point", "Each point can switch the\njoin from there on.");
            float amp = 40 + 15 * MathF.Sin(t * 1.3f);
            PathJoin[] perPoint = [PathJoin.Miter, PathJoin.Round, PathJoin.Bevel, PathJoin.Miter];
            Span<PathPoint> zig = stackalloc PathPoint[6];
            for (int i = 0; i < zig.Length; i++) {
                Vector2 p = c + new Vector2(-110 + i * 44, (i % 2 == 0 ? 1 : -1) * amp * 0.5f + 10);
                zig[i] = i > 0 && i < zig.Length - 1 ? new PathPoint(p, perPoint[i - 1]) : new PathPoint(p);
                if (i > 0 && i < zig.Length - 1) {
                    Label(p + new Vector2(0, i % 2 == 0 ? 30 : -30), perPoint[i - 1].ToString(), 12f, TWColor.Gray500);
                }
            }
            Sb.FillPath(zig, 10, TWColor.Amber300);
            EndCard();

            c = Card(6, "Fill and border", "DrawPath gives the stroke a\nfill and a border.");
            Span<Vector2> road = stackalloc Vector2[26];
            for (int i = 0; i < road.Length; i++) {
                float x = -160 + i * 12.8f;
                road[i] = c + new Vector2(x, 45 * MathF.Sin(x * 0.018f + t * 0.8f));
            }
            Sb.DrawPath(road, 20, TWColor.Gray700, TWColor.Gray400, 3f);
            Sb.FillPath(road, 1.5f, TWColor.Yellow300, cap: PathCap.Butt, dash: new DashStyle(12f, 10f, -t * 2f, snap: DashSnap.Off));
            float carX = -150 + Frac(t * 0.12f) * 300;
            float slope = 45 * 0.018f * MathF.Cos(carX * 0.018f + t * 0.8f);
            Vector2 dir = Vector2.Normalize(new Vector2(1, slope));
            Vector2 car = c + new Vector2(carX, 45 * MathF.Sin(carX * 0.018f + t * 0.8f)) + new Vector2(-dir.Y, dir.X) * 9;
            Sb.FillRectangle(car - new Vector2(12, 6), new Vector2(24, 12), TWColor.Rose400, 4f, MathF.Atan2(dir.Y, dir.X));
            EndCard();

            c = Card(7, "Measure", "The box a path covers, to\ncull it against a camera.");
            Span<Vector2> wobble = stackalloc Vector2[7];
            for (int i = 0; i < wobble.Length; i++) {
                Vector2 home = new Vector2(-90 + i * 30, (i % 2 == 0 ? -20 : 20));
                wobble[i] = c + home + Polar(t * (0.7f + i * 0.13f) + i, 25);
            }
            Sb.FillPath(wobble, 6, TWColor.Violet300, join: PathJoin.Miter);
            RectangleF box = Measure.Path(wobble, 6, PathJoin.Miter);
            Sb.BorderRectangle(new Vector2(box.X, box.Y), new Vector2(box.Width, box.Height), TWColor.Gray500, 1f, dash: DashStyle.FromCount(28, 0.6f));
            EndCard();
        }

        private static Vector2 Lissajous(float s) => new Vector2(105 * MathF.Sin(s * 1.1f), 70 * MathF.Sin(s * 1.7f + 0.5f));
    }
}
