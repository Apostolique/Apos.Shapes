using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Apos.Input;
using Apos.Shapes;

namespace GameProject {
    public class GameRoot : Game {
        public GameRoot() {
            _graphics = new GraphicsDeviceManager(this);
            _graphics.GraphicsProfile = GraphicsProfile.HiDef;
            IsMouseVisible = true;
            Content.RootDirectory = "Content";
        }

        protected override void Initialize() {
            Window.AllowUserResizing = true;

            base.Initialize();
        }

        protected override void LoadContent() {
            _s = new SpriteBatch(GraphicsDevice);
            _sb = new ShapeBatch(GraphicsDevice, Content.Load<Effect>("Shapes"));

            InputHelper.Setup(this);
        }

        protected override void Update(GameTime gameTime) {
            InputHelper.UpdateSetup();

            if (_quit.Pressed())
                Exit();

            InputHelper.UpdateCleanup();
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime) {
            GraphicsDevice.Clear(Color.Black);

            _sb.Begin(Matrix.CreateTranslation(0f, 0f, 0f) * Matrix.CreateScale(1f, 1f, 1f));
            _sb.DrawCircle(new Vector2(100, 100), 50, Color.Red, Color.White, 4f);
            _sb.DrawCircle(new Vector2(170, 150), 75, Color.Blue * 0.5f, Color.White, 2f);
            _sb.DrawCircle(new Vector2(400, 120), 100, Color.Green, Color.White, 10f);
            _sb.End();

            _sb.Begin(Matrix.CreateTranslation(0f, 0f, 0f) * Matrix.CreateScale(3f, 1f, 1f));
            _sb.DrawCircle(new Vector2(100, 300), 50, Color.Red, Color.White, 4f);
            _sb.DrawCircle(new Vector2(100, 300), 20, Color.Black * 0.7f, Color.Black, 4f);
            _sb.End();

            base.Draw(gameTime);
        }

        GraphicsDeviceManager _graphics;
        SpriteBatch _s;
        ShapeBatch _sb;

        ICondition _quit =
            new AnyCondition(
                new KeyboardCondition(Keys.Escape),
                new GamePadCondition(GamePadButton.Back, 0)
            );
    }
}
