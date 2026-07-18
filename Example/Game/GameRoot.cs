using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Apos.Input;
using Apos.Shapes;
using Apos.Camera;
using FontStashSharp;
using MonoGame.Extended;

namespace GameProject {
    public class GameRoot : Game {
        public GameRoot() {
            _graphics = new GraphicsDeviceManager(this);
#if KNI
            _graphics.GraphicsProfile = GraphicsProfile.FL10_0;
#else
            _graphics.GraphicsProfile = GraphicsProfile.HiDef;
#endif
            _graphics.PreferredBackBufferWidth = 1280;
            _graphics.PreferredBackBufferHeight = 720;
            IsMouseVisible = true;
            Content.RootDirectory = "Content";
        }

        protected override void Initialize() {
            Window.AllowUserResizing = true;

            base.Initialize();
        }

        protected override void LoadContent() {
            _sb = new ShapeBatch(GraphicsDevice);

            InputHelper.Setup(this);

            IVirtualViewport defaultViewport = new DefaultViewport(GraphicsDevice, Window);
            _camera = new Camera(defaultViewport);

            _fontSystem = new FontSystem();
            _fontSystem.AddFont(TitleContainer.OpenStream($"{Content.RootDirectory}/source-code-pro-medium.ttf"));
        }

        protected override void Update(GameTime gameTime) {
            InputHelper.UpdateSetup();

            if (_resetDroppedFrames.Pressed()) _fps.DroppedFrames = 0;
            if (_toggleDebug.Pressed()) _showDebug = !_showDebug;
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
            GraphicsDevice.Clear(TWColor.Gray950);

            var font = _fontSystem.GetFont(24);
            var titleFont = _fontSystem.GetFont(48);

            _sb.Begin(_camera.View);

            // The same two colors interpolated in each supported color space.
            _sb.ColorSpace = ColorSpace.Oklch;
            _sb.FillRectangle(new Vector2(-620, -330), new Vector2(400, 44), new Gradient(new Vector2(-620, -308), TWColor.Blue600, new Vector2(-220, -308), TWColor.Red600), 8f);
            _sb.DrawString(font, "Oklch", new Vector2(-204, -322), TWColor.Gray300);
            _sb.ColorSpace = ColorSpace.Oklab;
            _sb.FillRectangle(new Vector2(-620, -274), new Vector2(400, 44), new Gradient(new Vector2(-620, -252), TWColor.Blue600, new Vector2(-220, -252), TWColor.Red600), 8f);
            _sb.DrawString(font, "Oklab", new Vector2(-204, -266), TWColor.Gray300);
            _sb.ColorSpace = ColorSpace.Rgb;
            _sb.FillRectangle(new Vector2(-620, -218), new Vector2(400, 44), new Gradient(new Vector2(-620, -196), TWColor.Blue600, new Vector2(-220, -196), TWColor.Red600), 8f);
            _sb.DrawString(font, "Rgb", new Vector2(-204, -210), TWColor.Gray300);
            _sb.ColorSpace = ColorSpace.Oklch;
            // Gray stops have no hue of their own, Oklch holds the blue hue steady.
            _sb.FillRectangle(new Vector2(-620, -162), new Vector2(400, 44), new Gradient(new Vector2(-620, -140), TWColor.Gray500, new Vector2(-220, -140), TWColor.Blue600), 8f);
            _sb.DrawString(font, "Gray to blue", new Vector2(-204, -154), TWColor.Gray300);
            _sb.ColorSpace = ColorSpace.Oklab;

            // Gradient shapes.
            _sb.FillCircle(new Vector2(-80, -250), 52, new Gradient(new Vector2(-80, -250), TWColor.Amber400, new Vector2(-28, -250), TWColor.Red600, Gradient.Shape.Radial));
            _sb.FillCircle(new Vector2(50, -250), 52, new Gradient(new Vector2(50, -250), TWColor.Sky400, new Vector2(50, -302), TWColor.Indigo700, Gradient.Shape.Conical));
            _sb.FillCircle(new Vector2(180, -250), 52, new Gradient(new Vector2(180, -250), TWColor.Lime400, new Vector2(180, -302), TWColor.Emerald700, Gradient.Shape.ConicalAsym));
            _sb.FillCircle(new Vector2(310, -250), 52, new Gradient(new Vector2(310, -250), TWColor.Fuchsia400, new Vector2(273, -287), TWColor.Purple800, Gradient.Shape.Square));
            _sb.FillCircle(new Vector2(440, -250), 52, new Gradient(new Vector2(440, -250), TWColor.Rose400, new Vector2(403, -213), TWColor.Pink800, Gradient.Shape.Cross));
            _sb.FillCircle(new Vector2(570, -250), 52, new Gradient(new Vector2(570, -250), TWColor.Cyan300, new Vector2(596, -250), TWColor.Blue700, Gradient.Shape.Radial, Gradient.RepeatStyle.Triangle));
            _sb.FillCircle(new Vector2(440, -140), 52, new Gradient(new Vector2(440, -140), TWColor.Yellow300, new Vector2(440, -166), TWColor.Orange600, Gradient.Shape.SpiralCW));
            _sb.FillCircle(new Vector2(570, -140), 52, new Gradient(new Vector2(570, -140), TWColor.Teal300, new Vector2(570, -166), TWColor.Sky700, Gradient.Shape.SpiralCCW));

            // Shapes with fills, borders, rounding and local gradients.
            _sb.DrawRectangle(new Vector2(-620, -60), new Vector2(190, 150), new Gradient(new Vector2(0, 0), TWColor.Sky400, new Vector2(190, 150), TWColor.Indigo700, isLocal: true), TWColor.Slate200, 3f, new CornerRadii(12, 48, 12, 48), rotation: 0.1f);
            _sb.DrawHexagon(new Vector2(-310, 15), 75, new Gradient(new Vector2(0, 0), TWColor.Emerald400, new Vector2(0, 75), TWColor.Teal800, Gradient.Shape.Radial, isLocal: true), TWColor.Slate200, 3f, rounded: 8f);
            _sb.DrawEquilateralTriangle(new Vector2(-150, 15), 38, new Gradient(new Vector2(0, -60), TWColor.Amber300, new Vector2(0, 60), TWColor.Orange700, isLocal: true), TWColor.Slate200, 3f, rounded: 6f);
            _sb.DrawTriangle(new Vector2(-30, 85), new Vector2(60, -55), new Vector2(150, 85), new Gradient(new Vector2(60, -55), TWColor.Pink400, new Vector2(60, 85), TWColor.Rose800), TWColor.Slate200, 3f, rounded: 6f);
            _sb.DrawEllipse(new Vector2(330, 15), 95, 55, new Gradient(new Vector2(330, -40), TWColor.Violet400, new Vector2(330, 70), TWColor.Purple800), TWColor.Slate200, 3f, rotation: -0.15f);
            _sb.FillArc(new Vector2(480, 40), MathF.PI * 0.75f, MathF.PI * 2.25f, 48, 14, new Gradient(new Vector2(480, 40), TWColor.Red500, new Vector2(480, -22), TWColor.Amber400, Gradient.Shape.Conical));
            _sb.FillRing(new Vector2(575, 40), MathF.PI * 0.75f, MathF.PI * 2.25f, 48, 14, new Gradient(new Vector2(575, 88), TWColor.Cyan400, new Vector2(575, -8), TWColor.Blue700));

            // Repeat styles, plus offsets that hold the end colors solid before the transition starts.
            _sb.FillRectangle(new Vector2(-620, 150), new Vector2(360, 36), new Gradient(new Vector2(-620, 168), TWColor.Cyan400, new Vector2(-530, 168), TWColor.Blue800, Gradient.Shape.Linear, Gradient.RepeatStyle.Sawtooth), 6f);
            _sb.FillRectangle(new Vector2(-620, 196), new Vector2(360, 36), new Gradient(new Vector2(-620, 214), TWColor.Fuchsia400, new Vector2(-530, 214), TWColor.Purple900, Gradient.Shape.Linear, Gradient.RepeatStyle.Triangle), 6f);
            _sb.FillRectangle(new Vector2(-620, 242), new Vector2(360, 36), new Gradient(new Vector2(-620, 260), TWColor.Amber300, new Vector2(-530, 260), TWColor.Red700, Gradient.Shape.Linear, Gradient.RepeatStyle.Sine), 6f);
            _sb.FillRectangle(new Vector2(-620, 288), new Vector2(360, 36), new Gradient(new Vector2(-620, 306), TWColor.Lime400, new Vector2(-260, 306), TWColor.Green800, Gradient.Shape.Linear, Gradient.RepeatStyle.None, 90f, 90f), 6f);

            // Clipping without breaking the batch.
            _sb.SetClipRect(new RectangleF(-180, 150, 360, 180), 24f);
            _sb.FillCircle(new Vector2(-130, 240), 70, TWColor.Red500);
            _sb.FillCircle(new Vector2(0, 240), 70, TWColor.Amber400);
            _sb.FillCircle(new Vector2(130, 240), 70, TWColor.Sky500);
            _sb.BorderLine(new Vector2(-220, 320), new Vector2(220, 170), 16, TWColor.White, 3f);
            _sb.SetClipRect(null);
            _sb.BorderRectangle(new Vector2(-180, 150), new Vector2(360, 180), TWColor.Gray600, 2f, new CornerRadii(24));

            // Lines and text.
            _sb.FillLine(new Vector2(280, 170), new Vector2(620, 170), 10, new Gradient(new Vector2(280, 170), TWColor.Purple500, new Vector2(620, 170), TWColor.Orange400));
            _sb.BorderLine(new Vector2(280, 220), new Vector2(620, 220), 10, new Gradient(new Vector2(280, 220), TWColor.Teal400, new Vector2(620, 220), TWColor.Pink500), 3f);
            _sb.DrawString(titleFont, "Apos.Shapes", new Vector2(280, 250), TWColor.Gray100);

            _sb.End();

            if (_showDebug) {
                _sb.Begin();
                _sb.DrawString(font, $"fps: {_fps.FramesPerSecond} - Dropped Frames: {_fps.DroppedFrames} - Draw ms: {_fps.TimePerFrame} - Update ms: {_fps.TimePerUpdate}", new Vector2(10, 10), Color.White);
                _sb.End();
            }

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

        ICondition CameraDrag = new MouseCondition(MouseButton.RightButton);

        ICondition CameraReset = new KeyboardCondition(Keys.R);

        ICondition _toggleDebug = new KeyboardCondition(Keys.F1);
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

        bool _showDebug = false;
    }
}
