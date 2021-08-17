using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Apos.Input;
using Apos.Shapes;
using Apos.Camera;
using FontStashSharp;

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
            _sb = new ShapeBatch(GraphicsDevice, Content);

            InputHelper.Setup(this);

            IVirtualViewport defaultViewport = new DefaultViewport(GraphicsDevice, Window);
            _camera = new Camera(defaultViewport);

            _fontSystem = new FontSystem();
            _fontSystem.AddFont(TitleContainer.OpenStream($"{Content.RootDirectory}/source-code-pro-medium.ttf"));
        }

        protected override void Update(GameTime gameTime) {
            InputHelper.UpdateSetup();

            if (_resetDroppedFrames.Pressed()) _fps.DroppedFrames = 0;
            _fps.Update(gameTime);

            if (_quit.Pressed())
                Exit();
            UpdateCameraInput();

            _camera.Z = _camera.ScaleToZ(ExpToScale(Interpolate(ScaleToExp(_camera.ZToScale(_camera.Z, 0f)), _targetExp, _speed, _snapDistance)), 0f);
            _camera.Rotation = Interpolate(_camera.Rotation, _targetRotation, _speed, _snapDistance);

            InputHelper.UpdateCleanup();
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime) {
            _fps.Draw(gameTime);
            GraphicsDevice.Clear(Color.Black);

            _sb.Begin(_camera.View);
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
            _s.DrawString(font, $"fps: {_fps.FramesPerSecond} - Dropped Frames: {_fps.DroppedFrames} - Draw ms: {_fps.TimePerFrame} - Update ms: {_fps.TimePerUpdate}", new Vector2(10, 10), Color.White);
            _s.End();

            base.Draw(gameTime);
        }

        private void UpdateCameraInput() {
            int x = InputHelper.NewMouse.X;
            int y = InputHelper.NewMouse.Y;

            if (MouseCondition.Scrolled()) {
                int scrollDelta = MouseCondition.ScrollDelta;
                _targetExp = MathHelper.Clamp(_targetExp - scrollDelta * _expDistance, _maxExp, _minExp);
            }

            if (RotateLeft.Pressed()) {
                _targetRotation += MathHelper.PiOver4;
            }
            if (RotateRight.Pressed()) {
                _targetRotation -= MathHelper.PiOver4;
            }

            _mouseWorld = _camera.ScreenToWorld(x, y);

            if (CameraDrag.Pressed()) {
                _dragAnchor = _mouseWorld;
                _isDragged = true;
            }
            if (_isDragged && CameraDrag.HeldOnly()) {
                _camera.XY += _dragAnchor - _mouseWorld;
                _mouseWorld = _dragAnchor;
            }
            if (_isDragged && CameraDrag.Released()) {
                _isDragged = false;
            }

            if (CameraReset.Pressed()) {
                _camera.XY = Vector2.Zero;
                _camera.Rotation = 0f;
                _camera.Scale = Vector2.One;
                _camera.FocalLength = 1f;
            }
        }
        private float ScaleToExp(float scale) {
            return -MathF.Log(scale);
        }
        private float ExpToScale(float exp) {
            return MathF.Exp(-exp);
        }

        /// <summary>
        /// Poor man's tweening function.
        /// If the result is stored in the `from` value, it will create a nice interpolation over multiple frames.
        /// </summary>
        /// <param name="from">The value to start from.</param>
        /// <param name="target">The value to reach.</param>
        /// <param name="speed">A value between 0f and 1f.</param>
        /// <param name="snapNear">When the difference between the target and the result is smaller than this value, the target will be returned.</param>
        private static float Interpolate(float from, float target, float speed, float snapNear) {
            float result = MathHelper.Lerp(from, target, speed);

            if (from < target) {
                result = MathHelper.Clamp(result, from, target);
            } else {
                result = MathHelper.Clamp(result, target, from);
            }

            if (MathF.Abs(target - result) < snapNear) {
                return target;
            } else {
                return result;
            }
        }

        GraphicsDeviceManager _graphics;
        SpriteBatch _s;
        ShapeBatch _sb;

        FontSystem _fontSystem;
        FPSCounter _fps = new FPSCounter();

        ICondition _quit =
            new AnyCondition(
                new KeyboardCondition(Keys.Escape),
                new GamePadCondition(GamePadButton.Back, 0)
            );
        ICondition RotateLeft = new KeyboardCondition(Keys.OemComma);
        ICondition RotateRight = new KeyboardCondition(Keys.OemPeriod);

        ICondition CameraDrag = new MouseCondition(MouseButton.MiddleButton);

        ICondition CameraReset = new KeyboardCondition(Keys.R);

        ICondition _resetDroppedFrames = new KeyboardCondition(Keys.F2);

        Camera _camera;
        Vector2 _mouseWorld = Vector2.Zero;
        Vector2 _dragAnchor = Vector2.Zero;
        bool _isDragged = false;

        float _targetExp = 0f;
        float _targetRotation = 0f;
        float _speed = 0.08f;
        float _snapDistance = 0.001f;

        float _expDistance = 0.002f;
        float _maxExp = -5f;
        float _minExp = 5f;
    }
}
