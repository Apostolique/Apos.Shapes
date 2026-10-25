using System;
using Apos.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;

namespace GameProject {
    public class OverviewPage : Page {
        public OverviewPage(ShapeBatch sb, ShapeFont font, Assets assets) : base(sb, font) {
            _assets = assets;
            var random = new Random(7);
            for (int i = 0; i < _stars.Length; i++) {
                _stars[i] = new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
            }
            for (int i = 0; i < _enemies; i++) {
                _health[i] = 1f;
                _spawned[i] = -10f;
                _hit[i] = -10f;
            }
            for (int i = 0; i < _popups.Length; i++) _popupTime[i] = -10f;
            for (int i = 0; i < _trail.Length; i++) _trail[i] = _ship;
        }

        public override string Name => "Overview";
        public override string Description => "A small game scene, all of it in one ShapeBatch. Move the mouse to fly the ship.";

        public override void Update(float t, float dt, Vector2 mouse) {
            Vector2 target = Vector2.Clamp(mouse, new Vector2(_scene.Left + 40, _scene.Top + 40), new Vector2(_scene.Right - 40, _scene.Bottom - 40));
            Vector2 velocity = (target - _ship) * MathF.Min(1f, dt * 4f);
            _ship += velocity;
            if (velocity.LengthSquared() > 0.01f) {
                float goal = MathF.Atan2(velocity.Y, velocity.X);
                float diff = MathHelper.WrapAngle(goal - _heading);
                _heading += diff * MathF.Min(1f, dt * 8f);
            }

            Array.Copy(_trail, 0, _trail, 1, _trail.Length - 1);
            _trail[0] = _ship - Polar(_heading, 14);

            _energy = MathF.Min(1f, _energy + dt * 0.25f);
            int locked = Locked(t);
            if (t - _shotTime > 0.55f && _energy > 0.2f && Vector2.Distance(_ship, EnemyAt(locked, t)) < 420) {
                _shotTime = t;
                _shotTarget = locked;
                _energy -= 0.15f;
                _hit[locked] = t;
                int damage = 9 + (int)(t * 7) % 9;
                _health[locked] -= damage / 40f;

                int slot = _nextPopup++ % _popups.Length;
                _popups[slot] = EnemyAt(locked, t);
                _popupTime[slot] = t;
                _popupDamage[slot] = damage;

                if (_health[locked] <= 0f) {
                    _burst = EnemyAt(locked, t);
                    _burstTime = t;
                    _health[locked] = 1f;
                    _spawned[locked] = t;
                    _score += 100;
                }
                _score += damage;
            }
        }

        protected override void DrawPage(float t, Vector2 mouse) {
            Sb.PushClip(_scene, 18f);
            DrawSpace(t);
            DrawPlanet(t);
            DrawBeacon(t);
            DrawEnemies(t);
            DrawShip(t);
            DrawHud(t);
            Sb.PopClip();
            Sb.BorderRectangle(new Vector2(_scene.X, _scene.Y), new Vector2(_scene.Width, _scene.Height), TWColor.Gray800, 1f, 18f);
        }

        private void DrawSpace(float t) {
            Sb.FillRectangle(new Vector2(_scene.X, _scene.Y), new Vector2(_scene.Width, _scene.Height),
                new Gradient(new Vector2(0, _scene.Top), TWColor.Indigo950, new Vector2(0, _scene.Bottom), TWColor.Gray950));
            Sb.FillEllipseBlurred(new Vector2(-300 + 40 * MathF.Sin(t * 0.1f), -80), 330, 150, WithAlpha(TWColor.Fuchsia700, 0.22f), 120f, 0.3f);
            Sb.FillEllipseBlurred(new Vector2(-50, 160 + 30 * MathF.Sin(t * 0.13f)), 380, 120, WithAlpha(TWColor.Sky700, 0.18f), 110f, -0.2f);

            for (int i = 0; i < _stars.Length; i++) {
                Vector3 s = _stars[i];
                float depth = 0.3f + s.Z * 0.7f;
                float x = _scene.Left - 10 + Frac(s.X - t * 0.004f * depth) * (_scene.Width + 20);
                float y = _scene.Top + s.Y * _scene.Height;
                float twinkle = 0.55f + 0.45f * MathF.Sin(t * (1f + s.Z * 3f) + i);
                Sb.FillCircle(new Vector2(x, y), 0.6f + depth * 1.2f, WithAlpha(TWColor.Slate100, twinkle * depth));
            }
        }

        private void DrawPlanet(float t) {
            Vector2 p = new(350, -60);
            const float radius = 115f;
            const float tilt = -0.35f;
            float orbit = t * 0.35f;
            Vector2 moon = p + Rotate(new Vector2(MathF.Cos(orbit) * 270, MathF.Sin(orbit) * 70), tilt);
            bool moonBehind = MathF.Sin(orbit) < 0;

            Sb.FillCircleBlurred(p, radius + 8, WithAlpha(TWColor.Teal400, 0.25f), 40f);
            Sb.BorderEllipse(p, 205, 44, WithAlpha(TWColor.Amber200, 0.55f), 10f, tilt);
            Sb.BorderEllipse(p, 185, 38, WithAlpha(TWColor.Amber100, 0.35f), 4f, tilt);
            if (moonBehind) DrawMoon(moon, t);

            Vector2 light = p + new Vector2(-45, -45);
            Sb.FillCircle(p, radius, new Gradient(light, TWColor.Teal300, light + new Vector2(210, 0), TWColor.Indigo950, Gradient.Shape.Radial));
            Sb.FillCircle(p, radius, new Gradient(p + new Vector2(-30, 0), new Color(TWColor.Black, 0f), p + new Vector2(radius, radius * 0.6f), new Color(TWColor.Black, 0.6f)));
            Sb.BorderCircleBlurred(p, radius, WithAlpha(TWColor.Teal200, 0.45f), 6f, 3f);

            // The front half of the ring goes over the planet. A clip turned with the ring keeps only that half.
            Vector2 front = p + Rotate(new Vector2(0, 60), tilt);
            Sb.PushClip(new RectangleF(front.X - 260, front.Y - 60, 520, 120), 0f, tilt);
            Sb.BorderEllipse(p, 205, 44, WithAlpha(TWColor.Amber200, 0.55f), 10f, tilt);
            Sb.BorderEllipse(p, 185, 38, WithAlpha(TWColor.Amber100, 0.35f), 4f, tilt);
            Sb.PopClip();

            if (!moonBehind) DrawMoon(moon, t);
        }

        private void DrawMoon(Vector2 at, float t) {
            Texture2D moon = _assets.Moon;
            Sb.Draw(moon, at, Color.White, t * 0.2f, new Vector2(moon.Width, moon.Height) * 0.5f, 0.5f);
        }

        private void DrawBeacon(float t) {
            Vector2 beacon = new(-420, 200);
            Sb.FillLine(_ship, beacon, 2f, WithAlpha(TWColor.Sky300, 0.7f), dash: new DashStyle(0f, 14f, -t * 2f, DashCap.Round, DashSnap.Off));
            for (int i = 0; i < 2; i++) {
                float p = Frac(t * 0.6f + i * 0.5f);
                Sb.BorderCircle(beacon, 10 + 50 * p, WithAlpha(TWColor.Sky300, (1 - p) * MathF.Min(1, p * 8)), 2f);
            }
            Sb.DrawChamfer(beacon - new Vector2(12), new Vector2(24), 12f, TWColor.Sky500, TWColor.Sky200, 2f, t);
            Label(beacon + new Vector2(0, 30), "BEACON", 12f, TWColor.Sky200);
        }

        private void DrawEnemies(float t) {
            int locked = Locked(t);
            for (int i = 0; i < _enemies; i++) {
                Vector2 at = EnemyAt(i, t);
                float grow = MathHelper.SmoothStep(0f, 1f, MathHelper.Clamp((t - _spawned[i]) / 0.6f, 0f, 1f));
                float flash = MathHelper.Clamp(1f - (t - _hit[i]) / 0.2f, 0f, 1f);
                Sb.FillCircleBlurred(at, 20 * grow, WithAlpha(TWColor.Rose500, 0.35f + 0.5f * flash), 14f);
                Sb.DrawHexagon(at, 17 * grow, Color.Lerp(TWColor.Rose950, TWColor.Rose300, flash), TWColor.Rose400, 2.5f, rounded: 3f, rotation: t * (i % 2 == 0 ? 1 : -1));
                Sb.FillCircle(at, (4 + 1.5f * MathF.Sin(t * 6 + i)) * grow, TWColor.Rose200);

                Vector2 a = at + new Vector2(-20, -32);
                Vector2 b = at + new Vector2(20, -32);
                var segments = DashStyle.FromCount(5, 0.75f);
                Sb.FillPath([a, b], 3, WithAlpha(TWColor.Gray700, grow), cap: PathCap.Butt, dash: segments);
                Sb.PushClip(new RectangleF(a.X, a.Y - 4, 40 * _health[i], 8));
                Sb.FillPath([a, b], 3, WithAlpha(Color.Lerp(TWColor.Red500, TWColor.Lime400, _health[i]), grow), cap: PathCap.Butt, dash: segments);
                Sb.PopClip();

                if (i == locked) {
                    Sb.BorderRectangle(at - new Vector2(27), new Vector2(54), TWColor.Amber300, 2f, 10f, t * 0.5f, dash: DashStyle.FromCount(8, 0.5f, t));
                }
            }

            float age = t - _shotTime;
            if (age < 0.25f) {
                float fade = 1f - age / 0.25f;
                Vector2 from = _ship + Polar(_heading, 20);
                Vector2 to = EnemyAt(_shotTarget, t);
                Sb.FillLineBlurred(from, to, 2f, 5f, WithAlpha(TWColor.Lime400, 0.8f * fade), 5f);
                Sb.FillLine(from, to, 1f, WithAlpha(TWColor.Lime100, fade));
            }

            float burst = t - _burstTime;
            if (burst < 0.8f) {
                float p = burst / 0.8f;
                Sb.BorderCircle(_burst, 10 + 60 * p, WithAlpha(TWColor.Amber300, 1 - p), 3f * (1 - p) + 0.5f);
                Sb.FillCircleBlurred(_burst, 20 * (1 - p), WithAlpha(TWColor.Amber200, 1 - p), 12f);
            }

            for (int i = 0; i < _popups.Length; i++) {
                float popup = t - _popupTime[i];
                if (popup > 1f) continue;
                Label(_popups[i] + new Vector2(18, -20 - popup * 30), $"-{_popupDamage[i]}", 16f, WithAlpha(TWColor.Amber200, 1f - popup));
            }
        }

        private void DrawShip(float t) {
            // The trail fades with distance from the ship, out to its own length. A line from its
            // last point to its first would flip around whenever the ship doubles back past it.
            float reach = 0f;
            for (int i = 1; i < _trail.Length; i++) reach += Vector2.Distance(_trail[i - 1], _trail[i]);
            Sb.FillPath(_trail, _trailRadii, new Gradient(_trail[0], WithAlpha(TWColor.Cyan300, 0.8f), _trail[0] + new Vector2(MathF.Max(reach, 1f), 0f), WithAlpha(TWColor.Sky500, 0f), Gradient.Shape.Radial));
            Vector2 tail = _ship - Polar(_heading, 14);
            Sb.FillCircleBlurred(tail, 7 + MathF.Sin(t * 30) * 1.5f, WithAlpha(TWColor.Cyan300, 0.9f), 6f);

            Vector2 nose = _ship + Rotate(new Vector2(22, 0), _heading);
            Vector2 left = _ship + Rotate(new Vector2(-14, -14), _heading);
            Vector2 right = _ship + Rotate(new Vector2(-14, 14), _heading);
            Sb.DrawTriangle(nose, left, right, new Gradient(nose, TWColor.Slate100, tail, TWColor.Slate500), TWColor.Sky300, 2f, rounded: 3f);
            Sb.FillEllipse(_ship + Rotate(new Vector2(3, 0), _heading), 6, 3.5f, TWColor.Sky400, _heading);
            Sb.FillArc(_ship, -MathHelper.PiOver2, -MathHelper.PiOver2 + MathF.Tau * _energy, 32, 1.5f, WithAlpha(TWColor.Sky300, 0.5f));
        }

        private void DrawHud(float t) {
            Vector2 at = new(_scene.Left + 24, _scene.Top + 18);
            Sb.ColorSpace = ColorSpace.Rgb;
            var shimmer = new Palette(new Vector3(0.6f), new Vector3(0.4f), new Vector3(1f), new Vector3(0f, 0.33f, 0.67f) + new Vector3(t * 0.15f));
            Sb.DrawString(Font, "Apos.Shapes", at, 40f, new Gradient(at, at + new Vector2(400, 0), shimmer));
            Sb.ColorSpace = ColorSpace.Oklab;
            Sb.DrawString(Font, "Shapes, paths, text, SVG, and textures in one batch", at + new Vector2(2, 50), 14f, TWColor.Gray300);

            string score = $"SCORE {_score:000000}";
            Vector2 size = Font.MeasureString(score, 18f);
            Sb.DrawString(Font, score, new Vector2(_scene.Right - 24 - size.X, _scene.Top + 20), 18f, TWColor.Amber200);
            for (int i = 0; i < 3; i++) {
                float beat = 1f + 0.08f * MathF.Max(0f, MathF.Sin(t * 6f - i * 0.6f));
                Vector2 heart = new(_scene.Right - 36 - i * 34, _scene.Top + 64);
                Sb.DrawSvg(_assets.Heart, heart, 28f * beat, 0f, _assets.Heart.Measure(28f * beat) * 0.5f);
            }

            Vector2 bars = new(_scene.Left + 24, _scene.Bottom - 70);
            Sb.DrawString(Font, "HULL", bars, 12f, TWColor.Gray400);
            HudBar(bars + new Vector2(50, 7), 0.65f + 0.1f * MathF.Sin(t * 0.7f), TWColor.Lime400);
            Sb.DrawString(Font, "ENERGY", bars + new Vector2(0, 24), 12f, TWColor.Gray400);
            HudBar(bars + new Vector2(50, 31), _energy, TWColor.Sky400);

            Vector2 map = new(_scene.Right - 90, _scene.Bottom - 90);
            const float mapRadius = 66f;
            Sb.FillCircle(map, mapRadius, WithAlpha(TWColor.Gray950, 0.8f));
            Sb.PushClip(new RectangleF(map.X - mapRadius, map.Y - mapRadius, mapRadius * 2, mapRadius * 2), mapRadius);
            float sweep = t * 1.5f;
            Sb.FillCircle(map, mapRadius, new Gradient(map, WithAlpha(TWColor.Emerald400, 0.35f), map + Polar(sweep, 10), WithAlpha(TWColor.Emerald400, 0f), Gradient.Shape.ConicalAsym));
            Sb.BorderCircle(map, mapRadius * 0.5f, TWColor.Emerald900, 1f);
            Sb.FillCircle(map + new Vector2(350, -60) * _mapScale, 115 * _mapScale, TWColor.Teal700);
            for (int i = 0; i < _enemies; i++) Sb.FillCircle(map + EnemyAt(i, t) * _mapScale, 2.5f, TWColor.Rose400);
            Sb.FillCircle(map + new Vector2(-420, 200) * _mapScale, 2.5f, TWColor.Sky300);
            Sb.FillCircle(map + _ship * _mapScale, 3f, TWColor.Gray100);
            Sb.PopClip();
            Sb.BorderCircle(map, mapRadius, TWColor.Emerald700, 2f);
        }

        private void HudBar(Vector2 left, float value, Color color) {
            const float width = 160f;
            Vector2 right = left + new Vector2(width, 0);
            var segments = DashStyle.FromCount(16, 0.7f);
            Sb.FillPath([left, right], 5, TWColor.Gray800, cap: PathCap.Butt, dash: segments);
            Sb.PushClip(new RectangleF(left.X, left.Y - 6, width * MathHelper.Clamp(value, 0f, 1f), 12));
            Sb.FillPath([left, right], 5, color, cap: PathCap.Butt, dash: segments);
            Sb.PopClip();
        }

        private static Vector2 EnemyAt(int i, float t) {
            Vector2 home = new(-280 + i * 105, -40 + (i % 2) * 110);
            return home + new Vector2(45 * MathF.Sin(t * 0.5f + i * 1.3f), 35 * MathF.Cos(t * 0.7f + i * 2.1f));
        }

        private int Locked(float t) {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _enemies; i++) {
                float d = Vector2.DistanceSquared(_ship, EnemyAt(i, t));
                if (d < bestDistance) {
                    bestDistance = d;
                    best = i;
                }
            }
            return best;
        }

        private static Vector2 Rotate(Vector2 v, float angle) {
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);
            return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        }

        private static float[] TrailRadii() {
            var radii = new float[_trailLength];
            for (int i = 0; i < radii.Length; i++) radii[i] = 6f * (1f - i / (float)radii.Length) + 0.5f;
            return radii;
        }

        private static readonly RectangleF _scene = new(Left, Top, 1220, 600);
        private const int _enemies = 5;
        private const int _trailLength = 40;
        private const float _mapScale = 0.1f;

        private readonly Assets _assets;
        private readonly Vector3[] _stars = new Vector3[160];
        private readonly Vector2[] _trail = new Vector2[_trailLength];
        private static readonly float[] _trailRadii = TrailRadii();

        private Vector2 _ship = new(-200, 120);
        private float _heading = 0f;
        private float _energy = 1f;
        private int _score = 0;

        private readonly float[] _health = new float[_enemies];
        private readonly float[] _spawned = new float[_enemies];
        private readonly float[] _hit = new float[_enemies];
        private float _shotTime = -10f;
        private int _shotTarget = 0;
        private Vector2 _burst;
        private float _burstTime = -10f;

        private readonly Vector2[] _popups = new Vector2[8];
        private readonly float[] _popupTime = new float[8];
        private readonly int[] _popupDamage = new int[8];
        private int _nextPopup = 0;
    }
}
