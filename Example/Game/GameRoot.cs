using System;
using Apos.Input;
using Apos.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
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
            // Every page draws text, dashes, and gradients, so the full shader gets compiled up front.
            _sb = new ShapeBatch(GraphicsDevice, warmup: false);
            _sb.Warmup(ShapeFeatures.All);

            InputHelper.Setup(this);

            using (var ttf = TitleContainer.OpenStream($"{Content.RootDirectory}/source-code-pro-medium.ttf")) {
                _font = new ShapeFont(ttf);
            }

            var assets = new Assets(GraphicsDevice);
            _pages = [
                new OverviewPage(_sb, _font, assets),
                new ShapesPage(_sb, _font),
                new PathsPage(_sb, _font),
                new DashesPage(_sb, _font),
                new GradientsPage(_sb, _font),
                new ColorsPage(_sb, _font),
                new BlurPage(_sb, _font),
                new ClippingPage(_sb, _font),
                new TextPage(_sb, _font),
                new SvgAndTexturesPage(_sb, _font, assets),
            ];
        }

        protected override void Update(GameTime gameTime) {
            InputHelper.UpdateSetup();
            _fps.Update(gameTime);

            if (_quit.Pressed()) Exit();

            if (_next.Pressed()) GoTo(_page + (_shift.Held() ? -1 : 1));
            for (int i = 0; i < _pageKeys.Length; i++) {
                if (_pageKeys[i].Pressed()) GoTo(i);
            }
            if (_pause.Pressed()) _paused = !_paused;
            if (_toggleDither.Pressed()) _dither = !_dither;
            if (_toggleStats.Pressed()) _showStats = !_showStats;
            if (_reset.Pressed()) {
                _pan = Vector2.Zero;
                _targetZoom = 1f;
            }

            UpdateCamera();
            UpdateTabs();

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (!_paused) {
                _time += dt;
                _pages[_page].Update(_time, dt, ScreenToWorld(MousePosition));
            }

            InputHelper.UpdateCleanup();
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime) {
            _fps.Draw(gameTime);
            GraphicsDevice.Clear(TWColor.Gray950);

            _sb.DitherStrength = _dither ? 1f : 0f;

            _sb.Begin(View);
            _pages[_page].Draw(_time, ScreenToWorld(MousePosition));
            _sb.ColorSpace = ColorSpace.Oklab;
            _sb.End();

            _sb.Begin();
            DrawTabs();
            DrawHints();
            _sb.End();

            base.Draw(gameTime);
        }

        private void GoTo(int page) {
            _page = (page + _pages.Length) % _pages.Length;
        }

        // The pages are laid out for 1280x720 and scaled to fit the window, then zoomed and panned on top.
        private float Scale {
            get {
                Viewport vp = GraphicsDevice.Viewport;
                return MathF.Min(vp.Width / Page.Width, vp.Height / Page.Height) * _zoom;
            }
        }
        private Vector2 ScreenCenter => new Vector2(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height) * 0.5f;
        private Matrix View =>
            Matrix.CreateTranslation(-_pan.X, -_pan.Y, 0f) *
            Matrix.CreateScale(Scale, Scale, 1f) *
            Matrix.CreateTranslation(ScreenCenter.X, ScreenCenter.Y, 0f);
        private Vector2 ScreenToWorld(Vector2 p) => (p - ScreenCenter) / Scale + _pan;
        private static Vector2 MousePosition => InputHelper.NewMouse.Position.ToVector2();

        private void UpdateCamera() {
            if (MouseCondition.Scrolled()) {
                _targetZoom = MathHelper.Clamp(_targetZoom * MathF.Exp(MouseCondition.ScrollDelta * 0.001f), 0.25f, 40f);
            }

            // Zooms in log space toward the cursor, so the point under it stays put.
            Vector2 before = ScreenToWorld(MousePosition);
            _zoom = MathF.Exp(MathHelper.Lerp(MathF.Log(_zoom), MathF.Log(_targetZoom), 0.15f));
            if (MathF.Abs(_zoom - _targetZoom) < 0.0001f) _zoom = _targetZoom;
            _pan += before - ScreenToWorld(MousePosition);

            if (_drag.Held()) {
                Vector2 delta = (InputHelper.NewMouse.Position - InputHelper.OldMouse.Position).ToVector2();
                _pan -= delta / Scale;
            }
        }

        private void UpdateTabs() {
            _hoveredTab = -1;
            float x = TabsLeft();
            for (int i = 0; i < _pages.Length; i++) {
                float w = TabWidth(i);
                var r = new RectangleF(x, TabsTop, w, _tabHeight);
                if (r.Contains(MousePosition)) {
                    _hoveredTab = i;
                    if (_click.Pressed()) GoTo(i);
                }
                x += w + _tabGap;
            }
        }

        private void DrawTabs() {
            float x = TabsLeft();
            for (int i = 0; i < _pages.Length; i++) {
                float w = TabWidth(i);
                var at = new Vector2(x, TabsTop);
                var size = new Vector2(w, _tabHeight);
                if (i == _page) {
                    _sb.FillRectangle(at, size, TWColor.Gray100, _tabHeight * 0.5f);
                } else {
                    _sb.DrawRectangle(at, size, i == _hoveredTab ? TWColor.Gray800 : TWColor.Gray900, TWColor.Gray800, 1f, _tabHeight * 0.5f);
                }
                Color text = i == _page ? TWColor.Gray950 : i == _hoveredTab ? TWColor.Gray100 : TWColor.Gray400;
                _sb.DrawString(_font, $"{(i + 1) % 10} {_pages[i].Name}", at + new Vector2(_tabPadding, 5f), _tabText, text);
                x += w + _tabGap;
            }
        }

        private void DrawHints() {
            string hints = $"[Tab] next  [{(_paused ? "Space] play" : "Space] pause")}  [Scroll] zoom  [Right drag] pan  [R] reset  [D] dither {(_dither ? "on" : "off")}  [F1] stats";
            Vector2 size = _font.MeasureString(hints, 13f);
            _sb.DrawString(_font, hints, new Vector2(GraphicsDevice.Viewport.Width - size.X - 12f, 10f), 13f, TWColor.Gray500);

            if (_showStats) {
                string stats = $"{_fps.FramesPerSecond} fps  draw {_fps.TimePerFrame:0.00} ms  update {_fps.TimePerUpdate:0.00} ms  zoom {_zoom:0.00}x";
                Vector2 statsSize = _font.MeasureString(stats, 13f);
                _sb.DrawString(_font, stats, new Vector2(GraphicsDevice.Viewport.Width - statsSize.X - 12f, 28f), 13f, TWColor.Amber300);
            }
        }

        private float TabWidth(int i) => _font.MeasureString($"0 {_pages[i].Name}", _tabText).X + _tabPadding * 2f;
        private float TabsTop => GraphicsDevice.Viewport.Height - _tabHeight - 10f;
        private float TabsLeft() {
            float total = -_tabGap;
            for (int i = 0; i < _pages.Length; i++) total += TabWidth(i) + _tabGap;
            return MathF.Round((GraphicsDevice.Viewport.Width - total) * 0.5f);
        }

        GraphicsDeviceManager _graphics;
        ShapeBatch _sb;
        ShapeFont _font;
        Page[] _pages;
        FPSCounter _fps = new FPSCounter();

        int _page = 0;
        int _hoveredTab = -1;
        float _time = 0f;
        bool _paused = false;
        bool _dither = true;
        bool _showStats = false;

        Vector2 _pan = Vector2.Zero;
        float _zoom = 1f;
        float _targetZoom = 1f;

        const float _tabHeight = 26f;
        const float _tabText = 14f;
        const float _tabPadding = 10f;
        const float _tabGap = 6f;

        ICondition _quit = new AnyCondition(new KeyboardCondition(Keys.Escape), new GamePadCondition(GamePadButton.Back, 0));
        ICondition _next = new KeyboardCondition(Keys.Tab);
        ICondition _shift = new AnyCondition(new KeyboardCondition(Keys.LeftShift), new KeyboardCondition(Keys.RightShift));
        ICondition _pause = new KeyboardCondition(Keys.Space);
        ICondition _toggleDither = new KeyboardCondition(Keys.D);
        ICondition _toggleStats = new KeyboardCondition(Keys.F1);
        ICondition _reset = new KeyboardCondition(Keys.R);
        ICondition _drag = new AnyCondition(new MouseCondition(MouseButton.RightButton), new MouseCondition(MouseButton.MiddleButton));
        ICondition _click = new MouseCondition(MouseButton.LeftButton);
        ICondition[] _pageKeys = [
            new KeyboardCondition(Keys.D1), new KeyboardCondition(Keys.D2), new KeyboardCondition(Keys.D3),
            new KeyboardCondition(Keys.D4), new KeyboardCondition(Keys.D5), new KeyboardCondition(Keys.D6),
            new KeyboardCondition(Keys.D7), new KeyboardCondition(Keys.D8), new KeyboardCondition(Keys.D9),
            new KeyboardCondition(Keys.D0),
        ];
    }
}
