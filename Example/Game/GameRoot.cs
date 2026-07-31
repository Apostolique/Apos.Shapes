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
            if (_toggleDither.Pressed()) _ditherMode = (_ditherMode + 1) % 3;
            if (_strengthUp.Pressed()) _demoStrength = MathF.Min(_demoStrength + 1f, 16f);
            if (_strengthDown.Pressed()) _demoStrength = MathF.Max(_demoStrength - 1f, 1f);
            if (_toggleScene.Pressed()) {
                int n = Enum.GetNames<Scene>().Length;
                int step = _sceneBack.Held() ? n - 1 : 1;
                _currentScene = (Scene)(((int)_currentScene + step) % n);
            }
            UpdateDashOffset(gameTime);
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

            _sb.DitherStrength = _ditherMode == 2 ? 0f : _demoStrength;
            _sb.DitherNoiseSource = _ditherMode == 1 ? DitherNoise.InterleavedGradient : DitherNoise.BlueNoise;

            switch (_currentScene) {
                case Scene.Circle: DrawCircleScene(font); break;
                case Scene.Rectangle: DrawRectangleScene(font); break;
                case Scene.Chamfer: DrawChamferScene(font); break;
                case Scene.Polygon: DrawPolygonScene(font); break;
                case Scene.Line: DrawLineScene(font); break;
                case Scene.Path: DrawPathScene(font); break;
                case Scene.Arc: DrawArcScene(font); break;
                case Scene.Gradient: DrawGradientScene(font); break;
                case Scene.Palette: DrawPaletteScene(font); break;
                case Scene.ColorSpace: DrawColorSpaceScene(font); break;
                case Scene.Clip: DrawClipScene(font); break;
                case Scene.Text: DrawTextScene(font, titleFont); break;
                case Scene.Dash: DrawDashScene(); break;
                case Scene.Closed: DrawClosedScene(); break;
                case Scene.Blur: DrawBlurScene(font); break;
                case Scene.Banding: DrawBandingScene(font); break;
                default: DrawMainScene(font, titleFont); break;
            }

            DrawSceneHeader(font);

            if (_showDebug) {
                _sb.Begin();
                _sb.DrawString(font, $"fps: {_fps.FramesPerSecond} - Dropped Frames: {_fps.DroppedFrames} - Draw ms: {_fps.TimePerFrame} - Update ms: {_fps.TimePerUpdate}", new Vector2(10, 40), Color.White);
                _sb.End();
            }

            base.Draw(gameTime);
        }

        // The scene's name and where it sits in the cycle, in screen space so panning and zooming
        // the scene underneath leaves it where it is. Bottom right, because the top edge belongs
        // to each scene's first label and the bottom left to the scenes' own footers. Main fills
        // its bottom row edge to edge but leaves the top strip clear, so it alone keeps the
        // header up top.
        private void DrawSceneHeader(FontStashSharp.SpriteFontBase font) {
            int count = Enum.GetNames<Scene>().Length;
            string text = $"{(int)_currentScene + 1}/{count}  {SceneTitle(_currentScene)}    [Tab] next  [Shift+Tab] back";
            Vector2 size = font.MeasureString(text);
            var vp = GraphicsDevice.Viewport;
            Vector2 at = _currentScene == Scene.Main
                ? new Vector2(10f, 4f)
                : new Vector2(vp.Width - size.X - 10f, vp.Height - size.Y - 8f);
            _sb.Begin();
            _sb.DrawString(font, text, at, TWColor.Gray500);
            _sb.End();
        }

        private static string SceneTitle(Scene s) => s switch {
            Scene.Main => "Everything at once",
            Scene.Circle => "Circle and ellipse",
            Scene.Rectangle => "Rectangle",
            Scene.Chamfer => "Chamfer",
            Scene.Polygon => "Hexagon and triangles",
            Scene.Line => "Line",
            Scene.Path => "Path",
            Scene.Arc => "Arc and ring",
            Scene.Gradient => "Gradients",
            Scene.Palette => "Palettes",
            Scene.ColorSpace => "Color spaces",
            Scene.Clip => "Clipping",
            Scene.Text => "Text",
            Scene.Dash => "Dashes",
            Scene.Closed => "Closed paths",
            Scene.Blur => "Blur",
            _ => "Dithering",
        };

        // Everything the library draws, in one frame. It is also what the README banner is
        // rendered from, so it stays a catalog; the scenes after it take one feature each.
        private void DrawMainScene(FontStashSharp.SpriteFontBase font, FontStashSharp.SpriteFontBase titleFont) {
            _sb.Begin(_camera.View);

            float offset = _dashOffset;
            var half = new Vector3(0.5f);
            // Palettes from the classic parameter sets at iquilezles.org/articles/palettes, which
            // are tuned for Rgb channels. The wheel and the bar lift the bias and shrink the
            // amplitude so their troughs stay clear of the background.
            var muted = new Palette(new Vector3(0.6f), new Vector3(0.4f), new Vector3(1f), new Vector3(0.3f, 0.2f, 0.2f));
            var wheel = new Palette(new Vector3(0.6f), new Vector3(0.35f), new Vector3(1f), new Vector3(0f, 0.33f, 0.67f));
            var candy = new Palette(half, half, new Vector3(2f, 1f, 0f), new Vector3(0.5f, 0.2f, 0.25f));

            // The same two colors interpolated in each color space, then a cosine palette in the
            // fourth bar.
            _sb.ColorSpace = ColorSpace.Oklch;
            _sb.FillRectangle(new Vector2(-620, -324), new Vector2(400, 44), new Gradient(new Vector2(-620, -302), TWColor.Blue600, new Vector2(-220, -302), TWColor.Red600), 8f);
            _sb.DrawString(font, "Oklch", new Vector2(-204, -316), TWColor.Gray300);
            _sb.ColorSpace = ColorSpace.Oklab;
            _sb.FillRectangle(new Vector2(-620, -268), new Vector2(400, 44), new Gradient(new Vector2(-620, -246), TWColor.Blue600, new Vector2(-220, -246), TWColor.Red600), 8f);
            _sb.DrawString(font, "Oklab", new Vector2(-204, -260), TWColor.Gray300);
            _sb.ColorSpace = ColorSpace.Rgb;
            _sb.FillRectangle(new Vector2(-620, -212), new Vector2(400, 44), new Gradient(new Vector2(-620, -190), TWColor.Blue600, new Vector2(-220, -190), TWColor.Red600), 8f);
            _sb.DrawString(font, "Rgb", new Vector2(-204, -204), TWColor.Gray300);
            _sb.FillRectangle(new Vector2(-620, -156), new Vector2(400, 44), new Gradient(new Vector2(-620, -134), new Vector2(-220, -134), muted), 8f);
            _sb.DrawString(font, "Palette", new Vector2(-204, -148), TWColor.Gray300);
            _sb.ColorSpace = ColorSpace.Oklab;

            // Gradient shapes in a 4x2 grid that shares its top and bottom edges with the bars.
            _sb.FillCircle(new Vector2(178, -272), 52, new Gradient(new Vector2(178, -272), TWColor.Amber400, new Vector2(230, -272), TWColor.Red600, Gradient.Shape.Radial));
            _sb.FillCircle(new Vector2(308, -272), 52, new Gradient(new Vector2(308, -272), TWColor.Sky400, new Vector2(308, -324), TWColor.Indigo700, Gradient.Shape.Conical));
            _sb.FillCircle(new Vector2(568, -272), 52, new Gradient(new Vector2(568, -272), TWColor.Fuchsia400, new Vector2(531, -309), TWColor.Purple800, Gradient.Shape.Square));
            _sb.FillCircle(new Vector2(178, -164), 52, new Gradient(new Vector2(178, -164), TWColor.Rose400, new Vector2(141, -127), TWColor.Pink800, Gradient.Shape.Cross));
            _sb.FillCircle(new Vector2(308, -164), 52, new Gradient(new Vector2(308, -164), TWColor.Cyan300, new Vector2(334, -164), TWColor.Blue700, Gradient.Shape.Radial, Gradient.RepeatStyle.Triangle));
            _sb.FillCircle(new Vector2(438, -164), 52, new Gradient(new Vector2(438, -164), TWColor.Yellow300, new Vector2(438, -190), TWColor.Orange600, Gradient.Shape.SpiralCW));
            // A palette rides the two color slots a pair of stops uses, so any gradient shape takes one.
            _sb.ColorSpace = ColorSpace.Rgb;
            _sb.FillCircle(new Vector2(438, -272), 52, new Gradient(new Vector2(438, -272), new Vector2(438, -324), wheel, Gradient.Shape.Conical));
            _sb.FillCircle(new Vector2(568, -164), 52, new Gradient(new Vector2(568, -164), new Vector2(568, -216), candy, Gradient.Shape.SpiralCCW));
            _sb.ColorSpace = ColorSpace.Oklab;

            // One of each silhouette on a shared center line.
            _sb.DrawRectangle(new Vector2(-620, -50), new Vector2(170, 140), new Gradient(new Vector2(0, 0), TWColor.Sky400, new Vector2(170, 140), TWColor.Indigo700, isLocal: true), TWColor.Slate200, 3f, new CornerRadii(12, 48, 12, 48), rotation: 0.08f);
            _sb.DrawChamfer(new Vector2(-400, -38), new Vector2(140, 116), new CornerChamfers(38f, 38f, 12f, 12f), new Gradient(new Vector2(0, 0), TWColor.Fuchsia400, new Vector2(140, 116), TWColor.Purple800, isLocal: true), TWColor.Slate200, 3f);
            _sb.DrawHexagon(new Vector2(-135, 20), 66, new Gradient(new Vector2(0, 0), TWColor.Emerald400, new Vector2(0, 66), TWColor.Teal800, Gradient.Shape.Radial, isLocal: true), TWColor.Slate200, 3f, rounded: 8f);
            _sb.DrawTriangle(new Vector2(-25, 85), new Vector2(35, -60), new Vector2(95, 85), new Gradient(new Vector2(35, -60), TWColor.Amber300, new Vector2(35, 85), TWColor.Orange700), TWColor.Slate200, 3f, rounded: 6f);
            _sb.FillArc(new Vector2(200, 20), MathF.PI * 0.75f, MathF.PI * 2.25f, 46, 12, new Gradient(new Vector2(200, 20), TWColor.Red500, new Vector2(200, -38), TWColor.Amber400, Gradient.Shape.Conical));
            _sb.FillRing(new Vector2(345, 20), MathF.PI * 0.75f, MathF.PI * 2.25f, 46, 12, new Gradient(new Vector2(345, 64), TWColor.Cyan400, new Vector2(345, -24), TWColor.Blue700));
            // A closed path wraps back on itself, and the wrap joint blends like any other.
            _sb.FillPath(Star(new Vector2(525, 20), 72, 30, 5), 8, TWColor.Lime300, closed: true);

            // Repeat styles over a frame a quarter of the bar, and a palette whose whole number
            // frequency tiles the Sawtooth with no seam. Its phase slides over time.
            _sb.FillRectangle(new Vector2(-620, 150), new Vector2(360, 36), new Gradient(new Vector2(-620, 168), TWColor.Cyan400, new Vector2(-528, 168), TWColor.Blue800, Gradient.Shape.Linear, Gradient.RepeatStyle.Sawtooth), 6f);
            _sb.FillRectangle(new Vector2(-620, 196), new Vector2(360, 36), new Gradient(new Vector2(-620, 214), TWColor.Fuchsia400, new Vector2(-528, 214), TWColor.Purple900, Gradient.Shape.Linear, Gradient.RepeatStyle.Triangle), 6f);
            _sb.FillRectangle(new Vector2(-620, 242), new Vector2(360, 36), new Gradient(new Vector2(-620, 260), TWColor.Amber300, new Vector2(-528, 260), TWColor.Red700, Gradient.Shape.Linear, Gradient.RepeatStyle.Sine), 6f);
            _sb.ColorSpace = ColorSpace.Rgb;
            float slide = offset * 0.2f;
            _sb.FillRectangle(new Vector2(-620, 288), new Vector2(360, 36), new Gradient(new Vector2(-620, 306), new Vector2(-528, 306), new Palette(half, half, new Vector3(1f), new Vector3(slide, 0.1f + slide, 0.2f + slide)), Gradient.Shape.Linear, Gradient.RepeatStyle.Sawtooth), 6f);
            _sb.ColorSpace = ColorSpace.Oklab;

            // Clipping without breaking the batch. The window outline is dashed, and it marches.
            _sb.SetClipRect(new RectangleF(-180, 150, 360, 174), 24f);
            _sb.FillCircle(new Vector2(-130, 237), 70, TWColor.Red500);
            _sb.FillCircle(new Vector2(0, 237), 70, TWColor.Amber400);
            _sb.FillCircle(new Vector2(130, 237), 70, TWColor.Sky500);
            _sb.BorderLine(new Vector2(-220, 320), new Vector2(220, 172), 16, TWColor.White, 3f);
            _sb.SetClipRect(null);
            _sb.BorderRectangle(new Vector2(-181, 149), new Vector2(362, 176), TWColor.Gray600, 2f, new CornerRadii(24), dash: new DashStyle(12f, 9f, offset));

            // Lines, text over a blurred glow, and a translucent path that blends once even at the
            // joints.
            _sb.FillLine(new Vector2(280, 166), new Vector2(620, 166), 9, new Gradient(new Vector2(280, 166), TWColor.Purple500, new Vector2(620, 166), TWColor.Orange400), dash: new DashStyle(26f, 18f, offset, cap: DashCap.Round));
            _sb.BorderLine(new Vector2(280, 212), new Vector2(620, 212), 10, new Gradient(new Vector2(280, 212), TWColor.Teal400, new Vector2(620, 212), TWColor.Pink500), 3f);
            _sb.FillEllipseBlurred(new Vector2(450, 268), 150, 26, new Color(TWColor.Indigo500, 0.45f), 16f);
            _sb.DrawString(titleFont, "Apos.Shapes", new Vector2(325, 240), TWColor.Gray100);
            _sb.FillPath([new Vector2(280, 330), new Vector2(365, 302), new Vector2(450, 330), new Vector2(535, 302), new Vector2(620, 330)], 9, new Gradient(new Vector2(280, 316), new Color(TWColor.Sky400, 0.6f), new Vector2(620, 316), new Color(TWColor.Fuchsia500, 0.6f)));

            _sb.End();
        }

        // Every dashed shape and both dash types. Tab cycles to it.
        private void DrawDashScene() {
            _sb.Begin(_camera.View);

            float offset = _dashOffset;

            // Closed outlines, basic dashes: the border band is masked along the perimeter.
            _sb.BorderCircle(new Vector2(-500, -220), 100, TWColor.Sky400, 6f, dash: new DashStyle(24f, 16f, offset));
            _sb.BorderRectangle(new Vector2(-360, -300), new Vector2(220, 160), TWColor.Amber400, 6f, new CornerRadii(30), dash: new DashStyle(24f, 16f, offset));
            _sb.BorderHexagon(new Vector2(0, -220), 90, TWColor.Emerald400, 6f, rounded: 10f, dash: new DashStyle(24f, 16f, offset));
            _sb.BorderEquilateralTriangle(new Vector2(190, -230), 55, TWColor.Rose400, 6f, rounded: 8f, dash: new DashStyle(24f, 16f, offset));
            _sb.BorderTriangle(new Vector2(290, -140), new Vector2(380, -320), new Vector2(470, -140), TWColor.Violet400, 6f, rounded: 6f, dash: new DashStyle(24f, 16f, offset));
            _sb.BorderEllipse(new Vector2(-150, -85), 230, 22, TWColor.Sky300, 5f, dash: new DashStyle(24f, 16f, offset));

            // Rounded dashes over an opaque fill: the gaps show the fill, the dash ends are round.
            _sb.DrawEllipse(new Vector2(560, -220), 80, 62, TWColor.Gray800, TWColor.Cyan300, 10f, dash: new DashStyle(26f, 22f, offset, cap: DashCap.Round));
            _sb.DrawRectangle(new Vector2(-620, -60), new Vector2(200, 130), TWColor.Gray800, TWColor.Lime300, 8f, new CornerRadii(20), dash: new DashStyle(24f, 20f, offset, cap: DashCap.Round));

            // Strokes: the stroke itself is cut into dashes, each with its own fill, border and caps.
            _sb.FillLine(new Vector2(-340, -40), new Vector2(120, -40), 10, TWColor.Orange400, dash: new DashStyle(30f, 22f, offset));
            _sb.DrawLine(new Vector2(-340, 10), new Vector2(120, 10), 10, TWColor.Gray800, TWColor.Pink400, 3f, dash: new DashStyle(34f, 24f, offset));
            _sb.FillLine(new Vector2(-340, 60), new Vector2(120, 60), 8, TWColor.Teal300, dash: new DashStyle(0f, 34f, cap: DashCap.Round, offset: offset));
            _sb.FillArc(new Vector2(300, 30), MathF.PI * 0.75f, MathF.PI * 2.25f, 90, 12, TWColor.Red400, dash: new DashStyle(36f, 24f, cap: DashCap.Round, offset: offset));
            // A counted pattern: always exactly 8 repeats, however long the contour is.
            _sb.FillRing(new Vector2(540, 30), MathF.PI * 0.75f, MathF.PI * 2.25f, 90, 12, TWColor.Blue400, dash: DashStyle.FromCount(8, 0.66f, offset));

            // Paths: the pattern flows through the joints and borders trace every dash.
            _sb.FillPath([new Vector2(-600, 220), new Vector2(-480, 160), new Vector2(-360, 260), new Vector2(-240, 160), new Vector2(-120, 220)], 10, TWColor.Fuchsia400, dash: new DashStyle(30f, 20f, offset));
            _sb.DrawPath([new Vector2(-20, 260), new Vector2(80, 160), new Vector2(180, 260), new Vector2(280, 160)], 12, TWColor.Gray800, TWColor.Yellow300, 3f, join: PathJoin.Miter, dash: new DashStyle(36f, 22f, cap: DashCap.Round, offset: offset));
            _sb.FillPath([new Vector2(360, 260), new Vector2(440, 170), new Vector2(520, 260), new Vector2(600, 170)], 8, TWColor.Green300, join: PathJoin.Bevel, dash: new DashStyle(24f, 18f, offset: offset));

            _sb.End();
        }

        // Chamfers, which are rectangles with their corners cut straight across. The cut runs from
        // a square corner all the way to a diamond, one per corner if you want, and it takes
        // gradients, dashes, blur, rotation and clipping like every other shape. Tab cycles to it.
        private void DrawChamferScene(FontStashSharp.SpriteFontBase font) {
            _sb.Begin(_camera.View);

            float offset = _dashOffset;
            Vector2 card = new(150, 104);

            _sb.DrawString(font, "Fill, Border, Draw, per corner, the largest cut, and rotated", new Vector2(-620, -348), TWColor.Gray400);
            _sb.FillChamfer(new Vector2(-620, -320), card, 24f,
                new Gradient(new Vector2(0, 0), TWColor.Sky400, new Vector2(150, 104), TWColor.Indigo700, isLocal: true));
            _sb.BorderChamfer(new Vector2(-452, -320), card, 24f, TWColor.Emerald300, 5f);
            _sb.DrawChamfer(new Vector2(-284, -320), card, 24f, TWColor.Gray800, TWColor.Amber300, 5f);
            // One cut per corner, down to a square one on the top left.
            _sb.DrawChamfer(new Vector2(-116, -320), card, new CornerChamfers(0f, 46f, 16f, 30f),
                new Gradient(new Vector2(75, 52), TWColor.Rose400, new Vector2(75, 104), TWColor.Purple800, Gradient.Shape.Radial, isLocal: true), TWColor.Slate200, 4f);
            // A square cut to the limit is a diamond.
            _sb.FillChamfer(new Vector2(60, -320), new Vector2(104), 52f,
                new Gradient(new Vector2(112, -268), TWColor.Lime300, new Vector2(112, -216), TWColor.Green700, Gradient.Shape.Radial));
            _sb.DrawChamfer(new Vector2(260, -320), card, 24f, TWColor.Gray800, TWColor.Cyan300, 5f, rotation: 0.25f);
            _sb.FillChamfer(new Vector2(450, -320), card, 24f,
                new Gradient(new Vector2(525, -268), TWColor.Yellow300, new Vector2(525, -320), TWColor.Orange600, Gradient.Shape.Conical));

            // The whole range of the cut at one size, so the two ends read as the rectangle and the
            // diamond they are.
            _sb.DrawString(font, "The cut, from 0 up to half the smaller side", new Vector2(-620, -184), TWColor.Gray400);
            float[] cuts = [0f, 5f, 11f, 17f, 23f, 29f, 33f, 35f];
            for (int i = 0; i < cuts.Length; i++) {
                _sb.DrawChamfer(new Vector2(-620 + i * 155, -156), new Vector2(100, 70), cuts[i], TWColor.Gray800, TWColor.Slate200, 3f);
            }

            // Dashes walk eight vertices instead of four, and every cut size hands the corner over
            // differently, so this is the row to scrub with Left and Right.
            _sb.DrawString(font, "Dashed: caps, per corner, a diamond, a fixed count, dots, a gradient", new Vector2(-620, -60), TWColor.Gray400);
            _sb.BorderChamfer(new Vector2(-620, -32), card, 26f, TWColor.Sky300, 6f, dash: new DashStyle(24f, 16f, offset));
            _sb.DrawChamfer(new Vector2(-452, -32), card, 26f, TWColor.Gray800, TWColor.Lime300, 8f, dash: new DashStyle(24f, 20f, offset, cap: DashCap.Round));
            _sb.BorderChamfer(new Vector2(-284, -32), card, new CornerChamfers(0f, 46f, 16f, 30f), TWColor.Amber300, 6f, dash: new DashStyle(22f, 16f, offset));
            _sb.BorderChamfer(new Vector2(-116, -32), new Vector2(104), 52f, TWColor.Rose400, 7f, dash: new DashStyle(26f, 18f, offset));
            _sb.BorderChamfer(new Vector2(30, -32), card, 26f, TWColor.Violet300, 6f, rotation: 0.25f, dash: DashStyle.FromCount(10, 0.6f, offset));
            // A dash of no length with round caps is a dotted outline.
            _sb.BorderChamfer(new Vector2(250, -32), card, 26f, TWColor.Teal300, 7f, dash: new DashStyle(0f, 24f, offset, cap: DashCap.Round));
            _sb.BorderChamfer(new Vector2(430, -32), card, 26f,
                new Gradient(new Vector2(430, -32), TWColor.Orange400, new Vector2(580, 72), TWColor.Red600), 6f, dash: new DashStyle(24f, 16f, offset));

            _sb.DrawString(font, "Blurred, then clipped, and the box Measure hands back", new Vector2(-620, 100), TWColor.Gray400);
            // A drop shadow: an opaque card over a blurred copy of its own silhouette. The lighter
            // surface under it is what makes a black shadow visible at all on a dark background.
            _sb.FillRectangle(new Vector2(-636, 128), new Vector2(182, 140), TWColor.Gray700, new CornerRadii(10));
            _sb.FillChamferBlurred(new Vector2(-613, 145), card, 24f, TWColor.Black, 10f);
            _sb.FillChamfer(new Vector2(-620, 136), card, 24f, TWColor.Sky500);
            _sb.BorderChamferBlurred(new Vector2(-430, 136), card, 24f, TWColor.Cyan300, 6f, 14f);
            // A glow: the same blurred fill stacked under a crisp one reads as light off it.
            _sb.FillChamferBlurred(new Vector2(-270, 148), new Vector2(140, 88), 26f, TWColor.Fuchsia500, 16f);
            _sb.FillChamfer(new Vector2(-252, 162), new Vector2(104, 60), 18f, TWColor.Fuchsia200);

            // Clipped: the window cuts the first and last shapes, the middle one keeps its cuts.
            _sb.SetClipRect(new RectangleF(-60, 126, 340, 124), 18f);
            _sb.FillChamfer(new Vector2(-95, 134), new Vector2(150, 108), 34f, TWColor.Red500);
            _sb.FillChamfer(new Vector2(60, 134), new Vector2(150, 108), 34f, TWColor.Amber400);
            _sb.FillChamfer(new Vector2(215, 134), new Vector2(150, 108), 34f, TWColor.Sky500);
            _sb.SetClipRect(null);
            _sb.BorderRectangle(new Vector2(-60, 126), new Vector2(340, 124), TWColor.Gray600, 2f, new CornerRadii(18));

            // Measure.Chamfer answers with the box the shape covers, rotation and all, which is
            // what a camera culls against. The dashed outline is that box.
            Vector2 mAt = new(450, 130);
            Vector2 mSize = new(150, 100);
            CornerChamfers mCut = new(10f, 40f, 10f, 40f);
            _sb.FillChamfer(mAt, mSize, mCut, TWColor.Teal400, rotation: 0.5f);
            RectangleF mb = Measure.Chamfer(mAt, mSize, mCut, 0.5f);
            _sb.BorderRectangle(new Vector2(mb.X, mb.Y), new Vector2(mb.Width, mb.Height), TWColor.Gray500, 1f, dash: new DashStyle(8f, 6f));

            _sb.DrawString(font, "[Tab] next scene   [Left/Right] scrub the dash phase   [P] play/pause", new Vector2(-620, 300), TWColor.Gray300);

            _sb.End();
        }

        // The per shape scenes below share one grid: four labelled rows, the label on the left at
        // the top of each, and a footer. Row centers sit at -262, -104, 52 and 210, so nothing a
        // row draws should reach more than 54 either side of its own center.
        private const float _rowA = -262f, _rowB = -104f, _rowC = 52f, _rowD = 210f;

        private void DrawCircleScene(FontStashSharp.SpriteFontBase font) {
            _sb.Begin(_camera.View);
            void L(float y, string t) => _sb.DrawString(font, t, new Vector2(-620, y), TWColor.Gray400);

            L(-348, "Fill, Border at rising thickness, Draw, and gradients");
            _sb.FillCircle(new Vector2(-560, _rowA), 50, TWColor.Sky400);
            for (int i = 0; i < 3; i++) {
                _sb.BorderCircle(new Vector2(-430 + i * 130, _rowA), 50, TWColor.Emerald300, 2f + i * 5f);
            }
            _sb.DrawCircle(new Vector2(-40, _rowA), 50, TWColor.Gray800, TWColor.Amber300, 5f);
            _sb.FillCircle(new Vector2(90, _rowA), 50, new Gradient(new Vector2(90, _rowA), TWColor.Fuchsia400, new Vector2(140, _rowA), TWColor.Purple800, Gradient.Shape.Radial));
            _sb.FillCircle(new Vector2(220, _rowA), 50, new Gradient(new Vector2(220, _rowA), TWColor.Lime300, new Vector2(220, _rowA - 50), TWColor.Teal700, Gradient.Shape.Conical));
            _sb.FillCircle(new Vector2(350, _rowA), 50, new Gradient(new Vector2(350, _rowA - 50), TWColor.Rose300, new Vector2(350, _rowA + 50), TWColor.Red800));

            // Down to a radius the anti-aliasing is most of, which is where a fade that reaches
            // outward shows up as a shape bigger than the one asked for.
            L(-186, "Radius, from 2 up to 50");
            float[] radii = [2f, 4f, 7f, 11f, 16f, 22f, 30f, 39f, 50f];
            float rx = -600f;
            foreach (float r in radii) {
                _sb.FillCircle(new Vector2(rx + r, _rowB), r, TWColor.White);
                rx += r * 2f + 34f;
            }

            L(-30, "Ellipse: two radii, so it also takes a rotation");
            _sb.FillEllipse(new Vector2(-540, _rowC), 76, 40, TWColor.Sky400);
            _sb.BorderEllipse(new Vector2(-370, _rowC), 76, 40, TWColor.Emerald300, 6f);
            _sb.DrawEllipse(new Vector2(-200, _rowC), 76, 40, TWColor.Gray800, TWColor.Amber300, 5f);
            _sb.DrawEllipse(new Vector2(-20, _rowC), 76, 40, TWColor.Gray800, TWColor.Cyan300, 5f, rotation: 0.6f);
            _sb.FillEllipse(new Vector2(160, _rowC), 40, 50, new Gradient(new Vector2(160, _rowC - 50), TWColor.Violet300, new Vector2(160, _rowC + 50), TWColor.Purple800));
            _sb.FillEllipse(new Vector2(300, _rowC), 90, 14, TWColor.Pink400);

            L(128, "Dashed, blurred, and the box Measure hands back");
            _sb.BorderCircle(new Vector2(-560, _rowD), 50, TWColor.Sky300, 6f, dash: new DashStyle(22f, 16f, _dashOffset));
            _sb.BorderEllipse(new Vector2(-400, _rowD), 76, 46, TWColor.Lime300, 6f, dash: new DashStyle(22f, 16f, _dashOffset, cap: DashCap.Round));
            _sb.FillCircleBlurred(new Vector2(-210, _rowD), 44, TWColor.Amber400, 9f);
            _sb.BorderCircleBlurred(new Vector2(-80, _rowD), 44, TWColor.Cyan300, 5f, 10f);
            _sb.FillEllipseBlurred(new Vector2(90, _rowD), 70, 34, TWColor.Fuchsia500, 10f);
            _sb.FillEllipse(new Vector2(280, _rowD), 70, 40, TWColor.Teal400, rotation: 0.5f);
            RectangleF eb = Measure.Ellipse(new Vector2(280, _rowD), 70, 40, 0.5f);
            _sb.BorderRectangle(new Vector2(eb.X, eb.Y), new Vector2(eb.Width, eb.Height), TWColor.Gray500, 1f, dash: new DashStyle(8f, 6f));

            Footer(font, "Scroll to zoom, right drag to pan");
            _sb.End();
        }

        private void DrawRectangleScene(FontStashSharp.SpriteFontBase font) {
            _sb.Begin(_camera.View);
            void L(float y, string t) => _sb.DrawString(font, t, new Vector2(-620, y), TWColor.Gray400);
            Vector2 size = new(140, 96);
            Vector2 At(float x, float cy) => new(x, cy - size.Y * 0.5f);

            L(-348, "Fill, Border, Draw, rounded, rotated, and a local gradient");
            _sb.FillRectangle(At(-620, _rowA), size, TWColor.Sky400);
            _sb.BorderRectangle(At(-460, _rowA), size, TWColor.Emerald300, 6f);
            _sb.DrawRectangle(At(-300, _rowA), size, TWColor.Gray800, TWColor.Amber300, 5f, default);
            _sb.DrawRectangle(At(-140, _rowA), size, TWColor.Gray800, TWColor.Rose300, 5f, new CornerRadii(24));
            _sb.DrawRectangle(At(30, _rowA), size, TWColor.Gray800, TWColor.Cyan300, 5f, new CornerRadii(24), 0.3f);
            _sb.FillRectangle(At(230, _rowA), size, new Gradient(new Vector2(0, 0), TWColor.Violet400, new Vector2(140, 96), TWColor.Purple800, isLocal: true), new CornerRadii(24));

            // The four constructors CornerRadii offers, in the order the corners are named.
            L(-186, "CornerRadii: one value, two, three, and one per corner");
            _sb.DrawRectangle(At(-620, _rowB), size, TWColor.Gray800, TWColor.Slate200, 3f, new CornerRadii(20));
            _sb.DrawRectangle(At(-460, _rowB), size, TWColor.Gray800, TWColor.Slate200, 3f, new CornerRadii(8, 40));
            _sb.DrawRectangle(At(-300, _rowB), size, TWColor.Gray800, TWColor.Slate200, 3f, new CornerRadii(40, 8, 40));
            _sb.DrawRectangle(At(-140, _rowB), size, TWColor.Gray800, TWColor.Slate200, 3f, new CornerRadii(0, 16, 32, 48));

            L(-30, "The radius, from 0 up to half the smaller side");
            float[] rr = [0f, 6f, 13f, 21f, 30f, 40f, 48f];
            for (int i = 0; i < rr.Length; i++) {
                _sb.DrawRectangle(At(-620 + i * 176, _rowC), size, TWColor.Gray800, TWColor.Slate200, 3f, new CornerRadii(rr[i]));
            }

            L(128, "Dashed, blurred, and a drop shadow");
            _sb.BorderRectangle(At(-620, _rowD), size, TWColor.Sky300, 6f, new CornerRadii(20), dash: new DashStyle(22f, 16f, _dashOffset));
            _sb.DrawRectangle(At(-460, _rowD), size, TWColor.Gray800, TWColor.Lime300, 8f, new CornerRadii(20), dash: new DashStyle(22f, 18f, _dashOffset, cap: DashCap.Round));
            _sb.BorderRectangleBlurred(At(-300, _rowD), size, TWColor.Cyan300, 5f, 10f, new CornerRadii(20));
            _sb.FillRectangle(new Vector2(-140, _rowD - 66), new Vector2(178, 132), TWColor.Gray700, new CornerRadii(10));
            _sb.FillRectangleBlurred(At(-113, _rowD) + new Vector2(7, 9), size, TWColor.Black, 10f, new CornerRadii(20));
            _sb.FillRectangle(At(-120, _rowD), size, TWColor.Sky500, new CornerRadii(20));
            _sb.FillRectangle(At(80, _rowD), size, TWColor.Teal400, new CornerRadii(20), 0.5f);
            RectangleF rb = Measure.Rectangle(At(80, _rowD), size, new CornerRadii(20), 0.5f);
            _sb.BorderRectangle(new Vector2(rb.X, rb.Y), new Vector2(rb.Width, rb.Height), TWColor.Gray500, 1f, dash: new DashStyle(8f, 6f));

            Footer(font, "The radii are clamped to half the smaller side");
            _sb.End();
        }

        private void DrawPolygonScene(FontStashSharp.SpriteFontBase font) {
            _sb.Begin(_camera.View);
            void L(float y, string t) => _sb.DrawString(font, t, new Vector2(-620, y), TWColor.Gray400);

            L(-348, "Hexagon: the radius runs to the flat edges, and rounded takes the corners off");
            _sb.FillHexagon(new Vector2(-540, _rowA), 50, TWColor.Sky400);
            _sb.BorderHexagon(new Vector2(-400, _rowA), 50, TWColor.Emerald300, 6f);
            _sb.DrawHexagon(new Vector2(-260, _rowA), 50, TWColor.Gray800, TWColor.Amber300, 5f);
            for (int i = 0; i < 4; i++) {
                _sb.DrawHexagon(new Vector2(-110 + i * 140, _rowA), 50, TWColor.Gray800, TWColor.Slate200, 3f, rounded: i * 14f);
            }
            _sb.FillHexagon(new Vector2(460, _rowA), 50, TWColor.Rose400, rotation: 0.5f);

            L(-186, "Equilateral triangle: the radius is the circle that fits inside it");
            _sb.FillEquilateralTriangle(new Vector2(-540, _rowB), 32, TWColor.Sky400);
            _sb.BorderEquilateralTriangle(new Vector2(-400, _rowB), 32, TWColor.Emerald300, 6f);
            _sb.DrawEquilateralTriangle(new Vector2(-260, _rowB), 32, TWColor.Gray800, TWColor.Amber300, 5f);
            for (int i = 0; i < 3; i++) {
                _sb.DrawEquilateralTriangle(new Vector2(-110 + i * 140, _rowB), 32, TWColor.Gray800, TWColor.Slate200, 3f, rounded: i * 9f);
            }
            _sb.FillEquilateralTriangle(new Vector2(330, _rowB), 32, TWColor.Rose400, rotation: MathF.PI);

            L(-30, "Triangle: three points in any order, with the corners rounded off");
            for (int i = 0; i < 4; i++) {
                float x = -600 + i * 150;
                _sb.DrawTriangle(new Vector2(x, _rowC + 46), new Vector2(x + 55, _rowC - 46), new Vector2(x + 118, _rowC + 46),
                                 TWColor.Gray800, TWColor.Slate200, 3f, rounded: i * 8f);
            }
            _sb.FillTriangle(new Vector2(30, _rowC + 46), new Vector2(140, _rowC - 30), new Vector2(190, _rowC + 46),
                             new Gradient(new Vector2(110, _rowC - 46), TWColor.Amber300, new Vector2(110, _rowC + 46), TWColor.Red700));

            L(128, "Dashed, at every corner style");
            _sb.BorderHexagon(new Vector2(-540, _rowD), 50, TWColor.Sky300, 6f, rounded: 10f, dash: new DashStyle(22f, 16f, _dashOffset));
            _sb.BorderEquilateralTriangle(new Vector2(-380, _rowD), 32, TWColor.Lime300, 6f, rounded: 8f, dash: new DashStyle(20f, 15f, _dashOffset));
            _sb.BorderTriangle(new Vector2(-260, _rowD + 46), new Vector2(-190, _rowD - 46), new Vector2(-120, _rowD + 46),
                               TWColor.Amber300, 6f, rounded: 6f, dash: new DashStyle(20f, 15f, _dashOffset));
            _sb.DrawHexagon(new Vector2(0, _rowD), 50, TWColor.Gray800, TWColor.Fuchsia300, 8f, rounded: 8f, dash: new DashStyle(20f, 18f, _dashOffset, cap: DashCap.Round));
            _sb.FillHexagon(new Vector2(160, _rowD), 50, TWColor.Teal400, rounded: 12f, rotation: 0.4f);
            RectangleF hb = Measure.Hexagon(new Vector2(160, _rowD), 50, 12f, 0.4f);
            _sb.BorderRectangle(new Vector2(hb.X, hb.Y), new Vector2(hb.Width, hb.Height), TWColor.Gray500, 1f, dash: new DashStyle(8f, 6f));

            Footer(font, "Rotation turns every one of them about its own center");
            _sb.End();
        }

        private void DrawLineScene(FontStashSharp.SpriteFontBase font) {
            _sb.Begin(_camera.View);
            void L(float y, string t) => _sb.DrawString(font, t, new Vector2(-620, y), TWColor.Gray400);

            L(-348, "Two points and a radius, which is half the thickness. The caps are round");
            // Stacked by their own radius, so a thick one never runs into its neighbour.
            float[] rs = [2f, 4f, 7f, 11f, 16f];
            float ly = _rowA - 50f;
            foreach (float r in rs) {
                ly += r;
                _sb.FillLine(new Vector2(-600, ly), new Vector2(-190, ly), r, TWColor.Sky400);
                ly += r + 5f;
            }
            _sb.DrawLine(new Vector2(-130, _rowA + 40), new Vector2(310, _rowA - 40), 18, TWColor.Gray800, TWColor.Amber300, 4f);
            _sb.FillLine(new Vector2(390, _rowA + 40), new Vector2(600, _rowA - 40), 18,
                         new Gradient(new Vector2(390, _rowA), TWColor.Fuchsia400, new Vector2(600, _rowA), TWColor.Purple800));

            L(-186, "Dashed: flat caps, round caps, and a dash of no length, which is a dot");
            _sb.FillLine(new Vector2(-600, _rowB - 34), new Vector2(400, _rowB - 34), 12, TWColor.Lime300, dash: new DashStyle(30f, 22f, _dashOffset));
            _sb.DrawLine(new Vector2(-600, _rowB), new Vector2(400, _rowB), 12, TWColor.Gray800, TWColor.Pink400, 3f, dash: new DashStyle(34f, 24f, _dashOffset));
            _sb.FillLine(new Vector2(-600, _rowB + 34), new Vector2(400, _rowB + 34), 10, TWColor.Teal300, dash: new DashStyle(0f, 30f, _dashOffset, cap: DashCap.Round));

            L(-30, "Blurred, and blurred with a radius per end");
            _sb.FillLineBlurred(new Vector2(-600, _rowC - 28), new Vector2(180, _rowC - 28), 12, TWColor.Amber400, 4f);
            _sb.FillLineBlurred(new Vector2(-600, _rowC + 28), new Vector2(180, _rowC + 28), 14, 2f, TWColor.Cyan300, 4f);
            _sb.BorderLineBlurred(new Vector2(290, _rowC), new Vector2(600, _rowC), 22, TWColor.Fuchsia400, 4f, 6f);

            L(128, "A line whose two points are the same draws as a circle");
            _sb.FillLine(new Vector2(-560, _rowD), new Vector2(-560, _rowD), 44, TWColor.Rose400);
            _sb.BorderLine(new Vector2(-420, _rowD), new Vector2(-120, _rowD), 30, TWColor.Emerald300, 6f);
            _sb.FillLine(new Vector2(-40, _rowD), new Vector2(300, _rowD), 30,
                         new Gradient(new Vector2(-40, _rowD), new Color(TWColor.Sky400, 0.55f), new Vector2(300, _rowD), new Color(TWColor.Fuchsia500, 0.55f)));
            Vector2 la = new(380, _rowD - 40), lb = new(600, _rowD + 40);
            _sb.FillLine(la, lb, 22, TWColor.Teal400);
            RectangleF lbx = Measure.Line(la, lb, 22);
            _sb.BorderRectangle(new Vector2(lbx.X, lbx.Y), new Vector2(lbx.Width, lbx.Height), TWColor.Gray500, 1f, dash: new DashStyle(8f, 6f));

            Footer(font, "For more than two points, use a path");
            _sb.End();
        }

        private void DrawPathScene(FontStashSharp.SpriteFontBase font) {
            _sb.Begin(_camera.View);
            void L(float y, string t) => _sb.DrawString(font, t, new Vector2(-620, y), TWColor.Gray400);
            void Under(float x, float y, string t) => _sb.DrawString(font, t, new Vector2(x, y), TWColor.Gray500);

            L(-348, "Joins: round, miter, and bevel");
            PathJoin[] joins = [PathJoin.Round, PathJoin.Miter, PathJoin.Bevel];
            for (int i = 0; i < 3; i++) {
                float x = -600 + i * 220;
                _sb.FillPath([new Vector2(x, _rowA + 34), new Vector2(x + 80, _rowA - 34), new Vector2(x + 160, _rowA + 34)], 16, TWColor.Sky400, join: joins[i]);
                Under(x + 40, _rowA + 46, joins[i].ToString());
            }
            _sb.DrawPath([new Vector2(80, _rowA + 44), new Vector2(160, _rowA - 44), new Vector2(240, _rowA + 44), new Vector2(320, _rowA - 44)],
                         14, TWColor.Gray800, TWColor.Amber300, 3f, join: PathJoin.Miter);

            L(-180, "Caps: round, butt, and square, and cap and capEnd are set apart");
            PathCap[] caps = [PathCap.Round, PathCap.Butt, PathCap.Square];
            for (int i = 0; i < 3; i++) {
                float y = _rowB - 34 + i * 34;
                _sb.FillPath([new Vector2(-600, y), new Vector2(-380, y)], 15, TWColor.Emerald300, cap: caps[i]);
                Under(-360, y - 12, caps[i].ToString());
            }
            _sb.FillPath([new Vector2(-160, _rowB + 40), new Vector2(-60, _rowB - 40), new Vector2(40, _rowB + 40), new Vector2(140, _rowB - 40), new Vector2(240, _rowB + 40)],
                         14, TWColor.Rose400, cap: PathCap.Butt, capEnd: PathCap.Square);

            // A radius per point, which is what a pressure sensitive pen gives you.
            L(-30, "A radius per point, so the stroke swells and tapers");
            Vector2[] wave = new Vector2[25];
            float[] wr = new float[25];
            for (int i = 0; i < 25; i++) {
                wave[i] = new Vector2(-600 + i * 40, _rowC + MathF.Sin(i * 0.55f) * 34);
                wr[i] = 2 + i * 0.7f;
            }
            _sb.FillPath(wave, wr, new Gradient(new Vector2(-600, _rowC), TWColor.Cyan300, new Vector2(360, _rowC), TWColor.Blue700));

            L(128, "Dashed, closed, and joins set per point");
            _sb.FillPath([new Vector2(-600, _rowD + 40), new Vector2(-500, _rowD - 40), new Vector2(-400, _rowD + 40), new Vector2(-300, _rowD - 40)],
                         12, TWColor.Lime300, dash: new DashStyle(26f, 18f, _dashOffset));
            _sb.FillPath([new Vector2(-220, _rowD + 44), new Vector2(-130, _rowD - 44), new Vector2(-40, _rowD + 44)], 13, TWColor.Amber300,
                         join: PathJoin.Miter, closed: true, dash: new DashStyle(24f, 16f, _dashOffset));
            _sb.FillPath([
                new Vector2(40, _rowD + 40),
                (new Vector2(130, _rowD - 40), PathJoin.Miter),
                new Vector2(220, _rowD + 40),
                (new Vector2(310, _rowD - 40), PathJoin.Bevel),
                new Vector2(400, _rowD + 40)
            ], 13, TWColor.Fuchsia400, cap: PathCap.Butt, capEnd: PathCap.Square);

            Footer(font, "A translucent path blends once even where its segments meet");
            _sb.End();
        }

        private void DrawArcScene(FontStashSharp.SpriteFontBase font) {
            _sb.Begin(_camera.View);
            void L(float y, string t) => _sb.DrawString(font, t, new Vector2(-620, y), TWColor.Gray400);

            L(-348, "Arc: a stroke along a circle, with round caps. 0 points right, angles grow clockwise");
            float[] spans = [0.4f, 1f, 1.6f, 2.2f, 2.8f, MathF.PI];
            for (int i = 0; i < spans.Length; i++) {
                _sb.FillArc(new Vector2(-540 + i * 150, _rowA), -spans[i], spans[i], 46, 11, TWColor.Sky400);
            }
            _sb.FillArc(new Vector2(420, _rowA), 0f, MathF.Tau, 46, 11, TWColor.Emerald300);

            L(-186, "Ring: the same, cut flat at both ends");
            for (int i = 0; i < spans.Length; i++) {
                _sb.FillRing(new Vector2(-540 + i * 150, _rowB), -spans[i], spans[i], 46, 11, TWColor.Amber300);
            }
            _sb.FillRing(new Vector2(420, _rowB), 0f, MathF.Tau, 46, 11, TWColor.Rose400);

            L(-30, "The second radius is the band's half thickness");
            float[] bands = [2f, 4f, 7f, 10f, 13f, 15f];
            for (int i = 0; i < bands.Length; i++) {
                _sb.FillArc(new Vector2(-540 + i * 150, _rowC), MathF.PI * 0.75f, MathF.PI * 2.25f, 38, bands[i], TWColor.Violet300);
            }

            L(128, "Dashed and drawn: a stroke, so each dash gets its own fill and border");
            _sb.FillArc(new Vector2(-540, _rowD), MathF.PI * 0.75f, MathF.PI * 2.25f, 46, 12, TWColor.Lime300, dash: new DashStyle(24f, 18f, _dashOffset));
            _sb.FillArc(new Vector2(-380, _rowD), MathF.PI * 0.75f, MathF.PI * 2.25f, 46, 12, TWColor.Teal300, dash: new DashStyle(26f, 20f, _dashOffset, cap: DashCap.Round));
            _sb.DrawRing(new Vector2(-220, _rowD), MathF.PI * 0.75f, MathF.PI * 2.25f, 46, 12, TWColor.Gray800, TWColor.Pink400, 3f, dash: new DashStyle(28f, 20f, _dashOffset));
            // A counted pattern always lays down the same number of repeats, whatever the size.
            _sb.FillRing(new Vector2(-60, _rowD), 0f, MathF.Tau, 46, 12, TWColor.Cyan300, dash: DashStyle.FromCount(9, 0.6f, _dashOffset));
            _sb.FillArc(new Vector2(120, _rowD), MathF.PI * 0.6f, MathF.PI * 2.4f, 46, 12,
                        new Gradient(new Vector2(120, _rowD), TWColor.Red500, new Vector2(120, _rowD - 58), TWColor.Amber300, Gradient.Shape.Conical));

            Footer(font, "Only the swept part is measured, so a small arc of a big circle has a small box");
            _sb.End();
        }

        private void DrawGradientScene(FontStashSharp.SpriteFontBase font) {
            _sb.Begin(_camera.View);
            void L(float y, string t) => _sb.DrawString(font, t, new Vector2(-620, y), TWColor.Gray400);
            void Under(float x, float y, string t) => _sb.DrawString(font, t, new Vector2(x, y), TWColor.Gray500);

            L(-348, "Gradient.Shape, on a circle so the shape of the gradient is the only thing changing");
            (string Name, Gradient.Shape Shape)[] shapes = [
                ("Radial", Gradient.Shape.Radial), ("Linear", Gradient.Shape.Linear), ("Bilinear", Gradient.Shape.Bilinear),
                ("Conical", Gradient.Shape.Conical), ("ConicalAsym", Gradient.Shape.ConicalAsym), ("Square", Gradient.Shape.Square),
                ("Cross", Gradient.Shape.Cross), ("SpiralCW", Gradient.Shape.SpiralCW),
            ];
            for (int i = 0; i < shapes.Length; i++) {
                float x = -560 + i * 152;
                _sb.FillCircle(new Vector2(x, _rowA), 46, new Gradient(new Vector2(x, _rowA), TWColor.Sky300, new Vector2(x + 46, _rowA), TWColor.Indigo800, shapes[i].Shape));
                Under(x - 44, _rowA + 52, shapes[i].Name);
            }

            L(-180, "Gradient.RepeatStyle, over a frame a third of the bar wide");
            (string Name, Gradient.RepeatStyle Style)[] styles = [
                ("None", Gradient.RepeatStyle.None), ("Sawtooth", Gradient.RepeatStyle.Sawtooth),
                ("Triangle", Gradient.RepeatStyle.Triangle), ("Sine", Gradient.RepeatStyle.Sine),
            ];
            for (int i = 0; i < styles.Length; i++) {
                float y = _rowB - 42 + i * 28;
                _sb.FillRectangle(new Vector2(-600, y), new Vector2(900, 22),
                    new Gradient(new Vector2(-600, y), TWColor.Cyan400, new Vector2(-300, y), TWColor.Blue800, Gradient.Shape.Linear, styles[i].Style), 4f);
                Under(320, y - 2, styles[i].Name);
            }

            L(-30, "Offsets hold each end solid before the transition starts");
            float[][] offs = [[0f, 0f], [0.25f, 0f], [0f, 0.25f], [0.3f, 0.3f]];
            for (int i = 0; i < offs.Length; i++) {
                float y = _rowC - 42 + i * 28;
                _sb.FillRectangle(new Vector2(-600, y), new Vector2(900, 22),
                    new Gradient(new Vector2(-600, y), TWColor.Amber300, new Vector2(300, y), TWColor.Red700, Gradient.Shape.Linear, Gradient.RepeatStyle.None, offs[i][0], offs[i][1]), 4f);
                Under(320, y - 2, $"{offs[i][0]:0.##} / {offs[i][1]:0.##}");
            }

            L(128, "A local gradient is placed on the shape, so it travels and turns with it");
            for (int i = 0; i < 4; i++) {
                _sb.FillRectangle(new Vector2(-600 + i * 170, _rowD - 38), new Vector2(110, 76),
                    new Gradient(new Vector2(0, 0), TWColor.Fuchsia400, new Vector2(110, 76), TWColor.Purple900, isLocal: true), new CornerRadii(16), i * 0.13f);
            }
            _sb.FillCircle(new Vector2(180, _rowD), 48, new Gradient(new Vector2(180, _rowD), TWColor.Lime300, new Vector2(180, _rowD - 48), TWColor.Green800, Gradient.Shape.SpiralCCW));
            _sb.FillHexagon(new Vector2(300, _rowD), 46, new Gradient(new Vector2(0, 0), TWColor.Teal300, new Vector2(0, 46), TWColor.Sky800, Gradient.Shape.Radial, isLocal: true));

            Footer(font, "Any shape takes a gradient wherever it takes a Color");
            _sb.End();
        }

        private void DrawPaletteScene(FontStashSharp.SpriteFontBase font) {
            _sb.Begin(_camera.View);
            void L(float y, string t) => _sb.DrawString(font, t, new Vector2(-620, y), TWColor.Gray400);
            void Under(float x, float y, string t) => _sb.DrawString(font, t, new Vector2(x, y), TWColor.Gray500);

            // The classic parameter sets from iquilezles.org/articles/palettes, which are tuned
            // for raw RGB channels. The last row goes back to Oklab.
            _sb.ColorSpace = ColorSpace.Rgb;
            var half = new Vector3(0.5f);

            L(-348, "A Palette colors a gradient from cosines: bias + amplitude * cos(tau * (frequency * t + phase))");
            (string Name, Palette P)[] classics = [
                ("1, 1, 1 / 0, .33, .67", new Palette(half, half, new Vector3(1f), new Vector3(0f, 0.33f, 0.67f))),
                ("1, 1, 1 / 0, .1, .2", new Palette(half, half, new Vector3(1f), new Vector3(0f, 0.1f, 0.2f))),
                ("1, 1, 1 / .3, .2, .2", new Palette(half, half, new Vector3(1f), new Vector3(0.3f, 0.2f, 0.2f))),
                ("2, 1, 0 / .5, .2, .25", new Palette(half, half, new Vector3(2f, 1f, 0f), new Vector3(0.5f, 0.2f, 0.25f))),
            ];
            for (int i = 0; i < classics.Length; i++) {
                float y = _rowA - 42 + i * 28;
                _sb.FillRectangle(new Vector2(-600, y), new Vector2(900, 22),
                    new Gradient(new Vector2(-600, y), new Vector2(300, y), classics[i].P), 4f);
                Under(320, y - 2, classics[i].Name);
            }

            L(-180, "The colors ride the same machinery as two stops, so every Gradient.Shape takes one");
            var rainbow = classics[0].P;
            (string Name, Gradient.Shape Shape)[] shapes = [
                ("Radial", Gradient.Shape.Radial), ("Linear", Gradient.Shape.Linear), ("Conical", Gradient.Shape.Conical),
                ("Square", Gradient.Shape.Square), ("SpiralCW", Gradient.Shape.SpiralCW),
            ];
            for (int i = 0; i < shapes.Length; i++) {
                float x = -560 + i * 152;
                _sb.FillCircle(new Vector2(x, _rowB), 46, new Gradient(new Vector2(x, _rowB), new Vector2(x + 46, _rowB), rainbow, shapes[i].Shape));
                Under(x - 44, _rowB + 52, shapes[i].Name);
            }
            _sb.DrawCircle(new Vector2(200, _rowB), 46, new Gradient(new Vector2(200, _rowB), new Vector2(246, _rowB), rainbow), TWColor.Gray300, 5f);
            Under(200 - 44, _rowB + 52, "with a border");

            L(-30, "Whole number frequencies wrap on themselves, so a Sawtooth repeat tiles with no seam");
            for (int i = 0; i < 4; i++) {
                float y = _rowC - 42 + i * 28;
                float ph = i * 0.25f;
                var p = new Palette(half, half, new Vector3(1f), new Vector3(ph, 0.33f + ph, 0.67f + ph));
                _sb.FillRectangle(new Vector2(-600, y), new Vector2(900, 22),
                    new Gradient(new Vector2(-600, y), new Vector2(-300, y), p, Gradient.Shape.Linear, Gradient.RepeatStyle.Sawtooth), 4f);
                Under(320, y - 2, $"phase +{ph:0.##}");
            }

            L(128, "Channels follow ColorSpace, so in Oklab the cosines swing lightness and the color axes");
            var labPastel = new Palette(new Vector3(0.75f, 0.5f, 0.5f), new Vector3(0.2f, 0.35f, 0.35f), new Vector3(1f, 1f, 2f), new Vector3(0f, 0.25f, 0.5f));
            (string Name, ColorSpace Space, Palette P)[] spaced = [
                ("same numbers, Rgb", ColorSpace.Rgb, labPastel),
                ("same numbers, Oklab", ColorSpace.Oklab, labPastel),
                ("Oklch", ColorSpace.Oklch, new Palette(new Vector3(0.8f, 0.5f, 0.5f), new Vector3(0.1f, 0.25f, 0.5f), new Vector3(1f, 1f, 1f), new Vector3(0f, 0f, 0f))),
            ];
            for (int i = 0; i < spaced.Length; i++) {
                float y = _rowD - 42 + i * 28;
                _sb.ColorSpace = spaced[i].Space;
                _sb.FillRectangle(new Vector2(-600, y), new Vector2(900, 22),
                    new Gradient(new Vector2(-600, y), new Vector2(300, y), spaced[i].P), 4f);
                Under(320, y - 2, spaced[i].Name);
            }
            // Animating the phase slides every color along the palette for the cost of a float.
            _sb.ColorSpace = ColorSpace.Rgb;
            float slide = _dashOffset * 0.2f;
            _sb.FillRectangle(new Vector2(-600, _rowD + 42), new Vector2(900, 22),
                new Gradient(new Vector2(-600, _rowD + 42), new Vector2(300, _rowD + 42), new Palette(half, half, new Vector3(1f), new Vector3(slide, 0.33f + slide, 0.67f + slide))), 4f);
            Under(320, _rowD + 40, "animated phase");
            _sb.ColorSpace = ColorSpace.Oklab;

            Footer(font, "A palette packs into the two color slots a pair of stops uses, so it batches like everything else");
            _sb.End();
        }

        private void DrawColorSpaceScene(FontStashSharp.SpriteFontBase font) {
            _sb.Begin(_camera.View);
            void L(float y, string t) => _sb.DrawString(font, t, new Vector2(-620, y), TWColor.Gray400);

            L(-348, "The same two stops interpolated in each space. ColorSpace is read per shape");
            (string Name, ColorSpace Space)[] spaces = [("Oklch", ColorSpace.Oklch), ("Oklab", ColorSpace.Oklab), ("Rgb", ColorSpace.Rgb)];
            for (int i = 0; i < spaces.Length; i++) {
                float y = _rowA - 54 + i * 36;
                _sb.ColorSpace = spaces[i].Space;
                _sb.FillRectangle(new Vector2(-600, y), new Vector2(820, 30),
                    new Gradient(new Vector2(-600, y), TWColor.Blue600, new Vector2(220, y), TWColor.Red600), 8f);
                _sb.DrawString(font, spaces[i].Name, new Vector2(240, y + 2), TWColor.Gray300);
            }

            L(-186, "Gray has no hue of its own, so Oklch borrows the other stop's");
            for (int i = 0; i < spaces.Length; i++) {
                float y = _rowB - 54 + i * 36;
                _sb.ColorSpace = spaces[i].Space;
                _sb.FillRectangle(new Vector2(-600, y), new Vector2(820, 30),
                    new Gradient(new Vector2(-600, y), TWColor.Gray500, new Vector2(220, y), TWColor.Blue600), 8f);
                _sb.DrawString(font, spaces[i].Name, new Vector2(240, y + 2), TWColor.Gray300);
            }

            L(-30, "Yellow to blue, the pair that separates them most");
            for (int i = 0; i < spaces.Length; i++) {
                float y = _rowC - 54 + i * 36;
                _sb.ColorSpace = spaces[i].Space;
                _sb.FillRectangle(new Vector2(-600, y), new Vector2(820, 30),
                    new Gradient(new Vector2(-600, y), TWColor.Yellow300, new Vector2(220, y), TWColor.Blue700), 8f);
                _sb.DrawString(font, spaces[i].Name, new Vector2(240, y + 2), TWColor.Gray300);
            }

            _sb.ColorSpace = ColorSpace.Oklab;
            L(128, "Oklab is the default. Textures and text are always plain RGBA masks");
            _sb.FillRectangle(new Vector2(-600, _rowD - 30), new Vector2(500, 60),
                new Gradient(new Vector2(-600, _rowD), TWColor.Emerald300, new Vector2(-100, _rowD), TWColor.Fuchsia600), 12f);

            Footer(font, "Mixing spaces inside one frame never breaks the batch");
            _sb.End();
        }

        private void DrawClipScene(FontStashSharp.SpriteFontBase font) {
            _sb.Begin(_camera.View);
            void L(float y, string t) => _sb.DrawString(font, t, new Vector2(-620, y), TWColor.Gray400);

            L(-348, "A clip rectangle, with and without rounding");
            for (int i = 0; i < 2; i++) {
                float x = -600 + i * 460;
                float round = i * 26f;
                _sb.SetClipRect(new RectangleF(x, _rowA - 50, 400, 100), round);
                _sb.FillCircle(new Vector2(x + 60, _rowA + 10), 60, TWColor.Red500);
                _sb.FillCircle(new Vector2(x + 200, _rowA + 10), 60, TWColor.Amber400);
                _sb.FillCircle(new Vector2(x + 340, _rowA + 10), 60, TWColor.Sky500);
                _sb.SetClipRect(null);
                _sb.BorderRectangle(new Vector2(x, _rowA - 50), new Vector2(400, 100), TWColor.Gray600, 2f, new CornerRadii(round));
            }

            L(-186, "The clip turns too, and it cuts strokes and text the same way");
            // Turned, so the clip is not axis aligned. A wide box grows tall fast once it turns,
            // so this one is kept short enough to stay inside its row.
            _sb.SetClipRect(new RectangleF(-600, _rowB - 28, 220, 56), 14f, 0.22f);
            _sb.FillLine(new Vector2(-640, _rowB + 26), new Vector2(-330, _rowB - 26), 18, TWColor.Emerald400);
            _sb.FillLine(new Vector2(-640, _rowB - 16), new Vector2(-330, _rowB + 34), 12, TWColor.Fuchsia400);
            _sb.SetClipRect(null);
            _sb.BorderRectangle(new Vector2(-600, _rowB - 28), new Vector2(220, 56), TWColor.Gray600, 2f, new CornerRadii(14), 0.22f);
            _sb.SetClipRect(new RectangleF(-300, _rowB - 40, 420, 80), 20f);
            _sb.FillRectangle(new Vector2(-320, _rowB - 52), new Vector2(460, 104), TWColor.Indigo700);
            _sb.DrawString(font, "clipped text runs off the edge", new Vector2(-280, _rowB - 12), TWColor.Amber200);
            _sb.SetClipRect(null);
            _sb.BorderRectangle(new Vector2(-300, _rowB - 40), new Vector2(420, 80), TWColor.Gray600, 2f, new CornerRadii(20));

            L(-30, "Setting it back to null draws unclipped again, in the same batch");
            _sb.SetClipRect(new RectangleF(-600, _rowC - 48, 300, 96), 16f);
            _sb.FillHexagon(new Vector2(-450, _rowC), 70, TWColor.Teal400);
            _sb.SetClipRect(null);
            _sb.FillHexagon(new Vector2(-160, _rowC), 46, TWColor.Teal400);
            _sb.BorderRectangle(new Vector2(-600, _rowC - 48), new Vector2(300, 96), TWColor.Gray600, 2f, new CornerRadii(16));
            _sb.SetClipRect(new RectangleF(0, _rowC - 48, 300, 96), 16f);
            _sb.FillChamfer(new Vector2(20, _rowC - 70), new Vector2(260, 140), 40f, TWColor.Rose400);
            _sb.SetClipRect(null);
            _sb.BorderRectangle(new Vector2(0, _rowC - 48), new Vector2(300, 96), TWColor.Gray600, 2f, new CornerRadii(16));

            L(128, "A blurred shape clips like any other");
            _sb.SetClipRect(new RectangleF(-600, _rowD - 54, 520, 108), 24f);
            _sb.FillCircleBlurred(new Vector2(-470, _rowD), 60, TWColor.Amber400, 14f);
            _sb.FillCircleBlurred(new Vector2(-250, _rowD), 60, TWColor.Cyan300, 14f);
            _sb.SetClipRect(null);
            _sb.BorderRectangle(new Vector2(-600, _rowD - 54), new Vector2(520, 108), TWColor.Gray600, 2f, new CornerRadii(24));

            Footer(font, "The clip has its own anti-aliased edge, so it never comes out jagged");
            _sb.End();
        }

        private void DrawTextScene(FontStashSharp.SpriteFontBase font, FontStashSharp.SpriteFontBase titleFont) {
            _sb.Begin(_camera.View);
            void L(float y, string t) => _sb.DrawString(font, t, new Vector2(-620, y), TWColor.Gray400);

            L(-348, "Text goes through the FontStashSharp API, into the same batch as the shapes");
            _sb.DrawString(titleFont, "Apos.Shapes", new Vector2(-600, _rowA - 40), TWColor.Gray100);
            _sb.DrawString(font, "Shape rendering in MonoGame", new Vector2(-600, _rowA + 16), TWColor.Gray400);

            L(-186, "Any color, and it composites with whatever is under it");
            _sb.FillRectangle(new Vector2(-600, _rowB - 40), new Vector2(560, 80),
                new Gradient(new Vector2(-600, _rowB), TWColor.Indigo700, new Vector2(-40, _rowB), TWColor.Fuchsia800), 14f);
            _sb.DrawString(font, "over a gradient", new Vector2(-560, _rowB - 12), TWColor.White);
            _sb.DrawString(font, "translucent", new Vector2(-260, _rowB - 12), new Color(TWColor.White, 0.5f));
            _sb.DrawString(font, "colored", new Vector2(60, _rowB - 12), TWColor.Amber300);

            L(-30, "Mixed with shapes, one draw call for the lot");
            for (int i = 0; i < 4; i++) {
                float x = -600 + i * 190;
                _sb.DrawRectangle(new Vector2(x, _rowC - 40), new Vector2(160, 80), TWColor.Gray800, TWColor.Slate300, 2f, new CornerRadii(14));
                _sb.FillCircle(new Vector2(x + 34, _rowC), 18, TWColor.Sky400);
                _sb.DrawString(font, $"item {i + 1}", new Vector2(x + 62, _rowC - 12), TWColor.Gray200);
            }

            L(128, "Clipped, so a label can be cut to its own box");
            _sb.SetClipRect(new RectangleF(-600, _rowD - 30, 300, 60), 12f);
            _sb.FillRectangle(new Vector2(-600, _rowD - 30), new Vector2(300, 60), TWColor.Slate800, new CornerRadii(12));
            _sb.DrawString(font, "a label too long for its box", new Vector2(-580, _rowD - 12), TWColor.Gray100);
            _sb.SetClipRect(null);
            _sb.BorderRectangle(new Vector2(-600, _rowD - 30), new Vector2(300, 60), TWColor.Gray600, 2f, new CornerRadii(12));

            Footer(font, "The font is loaded once and drawn straight into the ShapeBatch");
            _sb.End();
        }

        private void Footer(FontStashSharp.SpriteFontBase font, string text) {
            _sb.DrawString(font, text, new Vector2(-620, 300), TWColor.Gray500);
        }

        // Blurred shapes. The falloff is a world space Gaussian rather than a screen space AA
        // width, so zooming in on this scene grows every blur along with the shape it belongs to,
        // where the anti-aliasing on the other scenes stays the same thickness at any zoom.
        private void DrawBlurScene(FontStashSharp.SpriteFontBase font) {
            _sb.Begin(_camera.View);

            // Drop shadows, which is the case the flat color assumption is built around: an opaque
            // card over a blurred copy of its own silhouette, offset down and to the right.
            _sb.DrawString(font, "Drop shadows, rising blur", new Vector2(-620, -336), TWColor.Gray400);
            for (int i = 0; i < 4; i++) {
                float blur = 3f + i * 7f;
                var at = new Vector2(-600 + i * 170, -300);
                var size = new Vector2(130, 90);
                _sb.FillRectangleBlurred(at + new Vector2(6, 8), size, TWColor.Black, blur, new CornerRadii(18));
                _sb.FillRectangle(at, size, TWColor.Sky500, new CornerRadii(18));
            }

            // The falloff is symmetric about the contour, so a shape softens without growing. The
            // hairline ring sits on the unblurred radius: every circle's 50% edge stays under it.
            _sb.DrawString(font, "Symmetric falloff: the 50% edge never leaves the hairline", new Vector2(-620, -180), TWColor.Gray400);
            for (int i = 0; i < 5; i++) {
                float blur = 1f + i * 8f;
                var at = new Vector2(-540 + i * 150, -80);
                _sb.FillCircleBlurred(at, 46f, TWColor.Amber400, blur);
                _sb.BorderCircle(at, 46f, TWColor.Gray100, 1f);
            }

            // Blurred outlines carry one color and no fill. A band thinner than the blur smears
            // into itself and dims, the way a real blur of a thin ring does.
            _sb.DrawString(font, "Blurred borders, thinning band at a fixed blur", new Vector2(-620, 10), TWColor.Gray400);
            for (int i = 0; i < 5; i++) {
                float thickness = 40f - i * 8f;
                _sb.BorderCircleBlurred(new Vector2(-540 + i * 150, 110), 52f, TWColor.Emerald400, 6f, thickness);
            }

            // Glow: the same blurred fill stacked under a crisp shape reads as light coming off it.
            _sb.FillEllipseBlurred(new Vector2(330, 110), 120f, 46f, TWColor.Fuchsia500, 26f);
            _sb.FillEllipse(new Vector2(330, 110), 96f, 26f, TWColor.Fuchsia200);
            _sb.BorderRectangleBlurred(new Vector2(450, 50), new Vector2(150, 120), TWColor.Cyan300, 5f, 10f, new CornerRadii(28));

            _sb.DrawString(font, "[Tab] example scene   scroll to zoom: the blur scales, the AA does not", new Vector2(-620, 300), TWColor.Gray300);

            _sb.End();
        }

        // Closed paths, where the last point joins back to the first. Tab cycles to it.
        private void DrawClosedScene() {
            _sb.Begin(_camera.View);

            float offset = _dashOffset;

            // The wrap point is a joint like any other, so the pattern comes back around to meet where
            // it started, at every corner style.
            _sb.FillPath([new Vector2(-600, -140), new Vector2(-470, -310), new Vector2(-340, -140)], 12, TWColor.Indigo300, join: PathJoin.Miter, closed: true, dash: new DashStyle(34f, 22f, offset));
            _sb.DrawPath(Polygon(new Vector2(-140, -220), 100f, 5), 12, TWColor.Gray800, TWColor.Amber300, 3f, closed: true, dash: new DashStyle(30f, 20f, offset));
            _sb.FillPath(Polygon(new Vector2(160, -220), 100f, 4), 11, TWColor.Rose400, join: PathJoin.Bevel, closed: true, dash: new DashStyle(28f, 20f, offset));
            // Sharp corners turning both ways, the tightest case for a pattern walking a corner.
            _sb.FillPath(Star(new Vector2(470, -220), 110f, 48f, 5), 9, TWColor.Lime300, closed: true, dash: new DashStyle(26f, 18f, offset));

            // A curve flattened to a polyline. The shader has no shape for it, but as a closed path
            // it dashes like anything else, which is how any curve you can sample gets dashed.
            _sb.FillPath(Lobed(new Vector2(-250, 90), 220f, 60f, 3, 96), 12, TWColor.Cyan400, closed: true, dash: new DashStyle(40f, 26f, cap: DashCap.Round, offset: offset));

            // Undashed and translucent: the wrap joint partitions the stroke like every other joint, so
            // it blends exactly once and no seam shows where the loop closes.
            _sb.FillPath(Polygon(new Vector2(400, 90), 120f, 6), 16, new Color(TWColor.Fuchsia400, 0.5f), closed: true);

            _sb.End();
        }

        // Corner points of a regular polygon, flat side down, for closed path demos.
        private static Vector2[] Polygon(Vector2 center, float radius, int sides) {
            Vector2[] p = new Vector2[sides];
            for (int i = 0; i < sides; i++) {
                float a = MathF.Tau * i / sides - MathF.PI * 0.5f;
                p[i] = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            }
            return p;
        }
        // A star, alternating between the two radii, for corners that turn both ways.
        private static Vector2[] Star(Vector2 center, float outer, float inner, int points) {
            Vector2[] p = new Vector2[points * 2];
            for (int i = 0; i < p.Length; i++) {
                float a = MathF.PI * i / points - MathF.PI * 0.5f;
                p[i] = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (i % 2 == 0 ? outer : inner);
            }
            return p;
        }
        // A closed curve with no shape of its own: a circle whose radius swings by amplitude over
        // lobes turns, sampled into a polyline.
        private static Vector2[] Lobed(Vector2 center, float radius, float amplitude, int lobes, int segments) {
            Vector2[] p = new Vector2[segments];
            for (int i = 0; i < segments; i++) {
                float a = MathF.Tau * i / segments;
                float r = radius + amplitude * MathF.Cos(a * lobes);
                p[i] = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
            }
            return p;
        }

        // Night scene built from slow dark gradients, the worst case for 8-bit banding.
        // Space toggles the dither so the bands snap in and out; zoom and drag still work.
        private void DrawBandingScene(FontStashSharp.SpriteFontBase font) {
            _sb.Begin(_camera.View);
            _sb.ColorSpace = ColorSpace.Rgb;

            // Sky: 12 quantization steps stretched over the whole screen height.
            _sb.FillRectangle(new Vector2(-640, -360), new Vector2(1280, 720),
                new Gradient(new Vector2(0, -360), new Color(14, 16, 30), new Vector2(0, 360), new Color(2, 3, 8)));
            // Moon glow: radial falloff to transparent, banding from color and alpha together.
            _sb.FillCircle(new Vector2(-250, -130), 640,
                new Gradient(new Vector2(-250, -130), new Color(44, 48, 70), new Vector2(-250, 510), new Color(44, 48, 70, 0), Gradient.Shape.Radial));
            // Warm lamp glow.
            _sb.FillCircle(new Vector2(420, 260), 520,
                new Gradient(new Vector2(420, 260), new Color(66, 44, 20), new Vector2(420, 780), new Color(66, 44, 20, 0), Gradient.Shape.Radial));

            _sb.ColorSpace = ColorSpace.Oklab;
            string mode = _ditherMode == 0 ? "Blue noise" : _ditherMode == 1 ? "IGN" : "Off";
            _sb.DrawString(font, $"Dither: {mode}  strength: {_demoStrength}  [Space] mode  [Up/Down] strength  [Tab] example scene", new Vector2(-620, -344), TWColor.Gray300);
            _sb.End();
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
        // Left and right scrub the dash pattern a step at a time, which is how corner and cap
        // artifacts get caught: they only show at the phases where an edge sits on the joint.
        // Scrubbing pauses the animation so a phase can be held still, P plays it again.
        private void UpdateDashOffset(GameTime gameTime) {
            if (_dashPlay.Pressed()) _dashPaused = !_dashPaused;

            float step = _dashStepFast.Held() ? 0.01f : 0.001f;
            if (_dashBack.Held()) {
                _dashOffset -= step;
                _dashPaused = true;
            }
            if (_dashForward.Held()) {
                _dashOffset += step;
                _dashPaused = true;
            }

            if (!_dashPaused) {
                _dashOffset += (float)gameTime.ElapsedGameTime.TotalMilliseconds * 0.0005f;
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
        ICondition _toggleDither = new KeyboardCondition(Keys.Space);
        ICondition _strengthUp = new KeyboardCondition(Keys.Up);
        ICondition _strengthDown = new KeyboardCondition(Keys.Down);
        ICondition _toggleScene = new KeyboardCondition(Keys.Tab);
        ICondition _sceneBack = new AnyCondition(new KeyboardCondition(Keys.LeftShift), new KeyboardCondition(Keys.RightShift));
        ICondition _dashBack = new KeyboardCondition(Keys.Left);
        ICondition _dashForward = new KeyboardCondition(Keys.Right);
        ICondition _dashStepFast = new AnyCondition(new KeyboardCondition(Keys.LeftShift), new KeyboardCondition(Keys.RightShift));
        ICondition _dashPlay = new KeyboardCondition(Keys.P);

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
        int _ditherMode = 0;
        float _demoStrength = 1f;

        enum Scene {
            Main,
            Circle,
            Rectangle,
            Chamfer,
            Polygon,
            Line,
            Path,
            Arc,
            Gradient,
            Palette,
            ColorSpace,
            Clip,
            Text,
            Dash,
            Closed,
            Blur,
            Banding
        }
        Scene _currentScene = Scene.Main;
        float _dashOffset = 0f;
        bool _dashPaused = false;
    }
}
