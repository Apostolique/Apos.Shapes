using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Apos.Shapes;
using FontStashSharp;

namespace GameProject {
    public class GameRootKNI : Game {
        public GameRootKNI() {
            _graphics = new GraphicsDeviceManager(this);
#if KNI
            _graphics.GraphicsProfile = GraphicsProfile.FL10_0;
#else
            _graphics.GraphicsProfile = GraphicsProfile.HiDef;
#endif
            IsMouseVisible = true;
            Content.RootDirectory = "Content";
        }

        protected override void Initialize() {
            Window.AllowUserResizing = true;

            base.Initialize();
        }

        protected override void LoadContent() {
            _s = new SpriteBatch(GraphicsDevice);
            _sb = new ShapeBatch(GraphicsDevice, Content);


            _fontSystem = new FontSystem();
            _fontSystem.AddFont(TitleContainer.OpenStream($"{Content.RootDirectory}/source-code-pro-medium.ttf"));
        }

        protected override void Update(GameTime gameTime) {

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime) {
            GraphicsDevice.Clear(Color.Black);

            _sb.Begin(Matrix.Identity);
            _sb.BorderLine(new Vector2(100, 20), new Vector2(450, -15), 20, Color.White, 2f);

            _sb.DrawCircle(new Vector2(120, 120), 75, new Color(96, 165, 250), new Color(191, 219, 254), 4f);
            _sb.DrawCircle(new Vector2(120, 120), 30, Color.White, Color.Black, 20f);

            _sb.DrawCircle(new Vector2(370, 120), 100, new Color(96, 165, 250), new Color(191, 219, 254), 4f);
            _sb.DrawCircle(new Vector2(370, 120), 40, Color.White, Color.Black, 20f);

            _sb.DrawCircle(new Vector2(190, 270), 10, Color.Black, Color.White, 2f);
            _sb.DrawCircle(new Vector2(220, 270), 10, Color.Black, Color.White, 2f);

            _sb.FillCircle(new Vector2(235, 400), 30, new Color(220, 38, 38));
            _sb.FillRectangle(new Vector2(235, 370), new Vector2(135, 60), new Color(220, 38, 38));
            _sb.FillCircle(new Vector2(235, 400), 20, Color.White);
            _sb.FillRectangle(new Vector2(235, 380), new Vector2(125, 40), Color.White);
            _sb.End();

            var font = _fontSystem.GetFont(24);
            _s.Begin();
            _s.DrawString(font, $"fps: ?? - Dropped Frames: ?? - Draw ms: ?? - Update ms: ??", new Vector2(10, 10), Color.White);
            _s.End();

            base.Draw(gameTime);
        }

        GraphicsDeviceManager _graphics;
        SpriteBatch _s;
        ShapeBatch _sb;

        FontSystem _fontSystem;
    }
}
