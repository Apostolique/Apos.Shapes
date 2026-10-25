using System;
using Apos.Shapes;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace GameProject {
    /// <summary>
    /// One page of the showcase. It draws in world units over a 1280x720 area centered on 0, and
    /// most pages split that area into a 4x2 grid of cards.
    /// </summary>
    public abstract class Page {
        public const float Width = 1280f;
        public const float Height = 720f;

        protected Page(ShapeBatch sb, ShapeFont font) {
            Sb = sb;
            Font = font;
        }

        public abstract string Name { get; }
        public abstract string Description { get; }

        public virtual void Update(float t, float dt, Vector2 mouse) { }

        public void Draw(float t, Vector2 mouse) {
            Sb.DrawString(Font, Name, new Vector2(Left, -352f), 24f, TWColor.Gray100);
            Sb.DrawString(Font, Description, new Vector2(Left, -322f), 14f, TWColor.Gray400);
            DrawPage(t, mouse);
        }

        protected abstract void DrawPage(float t, Vector2 mouse);

        // Draws card i of the grid and clips what comes after it to the card, until EndCard.
        // Returns the middle of the space under the caption, which is about 260x200.
        protected Vector2 Card(int i, string title, string caption) {
            RectangleF r = CardRect(i);
            var at = new Vector2(r.X, r.Y);
            Sb.DrawRectangle(at, new Vector2(r.Width, r.Height), TWColor.Gray900, TWColor.Gray800, 1f, _cardRounding);
            Sb.DrawString(Font, title, at + new Vector2(16f, 14f), 18f, TWColor.Gray100);
            Sb.DrawString(Font, caption, at + new Vector2(16f, 40f), 13f, TWColor.Gray400);
            Sb.PushClip(r, _cardRounding);
            return at + new Vector2(r.Width * 0.5f, 181f);
        }

        protected void EndCard() {
            Sb.PopClip();
        }

        // The space under a two line caption, inset from the card's edges.
        protected static RectangleF Panel(Vector2 center) {
            return new RectangleF(center.X - 133, center.Y - 95, 266, 196);
        }

        protected static RectangleF CardRect(int i) {
            int col = i % 4;
            int row = i / 4;
            return new RectangleF(Left + col * (_cardWidth + _cardGap), Top + row * (_cardHeight + _cardGap), _cardWidth, _cardHeight);
        }

        protected void Label(Vector2 center, string text, float size, Gradient fill) {
            Sb.DrawString(Font, text, center - Font.MeasureString(text, size) * 0.5f, size, fill);
        }

        // Goes from 0 to 1 and back once per period, easing at both ends.
        protected static float Wave(float t, float period = 1f, float phase = 0f) {
            return 0.5f - 0.5f * MathF.Cos(MathF.Tau * (t / period + phase));
        }

        protected static float Frac(float x) => x - MathF.Floor(x);

        protected static Vector2 Polar(float angle, float radius) {
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }

        protected static Color WithAlpha(Color c, float alpha) {
            return new Color(c, MathHelper.Clamp(alpha, 0f, 1f));
        }

        protected const float Left = -610f;
        protected const float Top = -290f;

        protected readonly ShapeBatch Sb;
        protected readonly ShapeFont Font;

        const float _cardWidth = 290f;
        const float _cardHeight = 290f;
        const float _cardGap = 20f;
        const float _cardRounding = 14f;
    }
}
