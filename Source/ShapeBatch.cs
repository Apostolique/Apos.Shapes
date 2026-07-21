using System;
using System.Text;
using FontStashSharp;
using FontStashSharp.Interfaces;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;

namespace Apos.Shapes {
    public class ShapeBatch : IDisposable {
        public ShapeBatch(GraphicsDevice graphicsDevice, Effect? effect = null) {
            _graphicsDevice = graphicsDevice;

            _effect = effect ?? LoadEmbeddedEffect(graphicsDevice);

            _vertices = new VertexShape[_initialVertices];
            _indices = new uint[_initialIndices];

            GenerateIndexArray();

            _vertexBuffer = new DynamicVertexBuffer(_graphicsDevice, typeof(VertexShape), _vertices.Length, BufferUsage.WriteOnly);

            _indexBuffer = new IndexBuffer(_graphicsDevice, IndexElementSize.ThirtyTwoBits, _indices.Length, BufferUsage.WriteOnly);
            _indexBuffer.SetData(_indices);

            _viewProjection = _effect.Parameters["view_projection"];
            // Null-safe so custom effects compiled before these parameters existed keep working.
            _halfViewportParam = _effect.Parameters["half_viewport"];
            _ditherScale = _effect.Parameters["dither_scale"];
            _ditherMode = _effect.Parameters["dither_mode"];

            _blueNoise = BlueNoise.CreateTexture(graphicsDevice);

            _fsr = new FontStashRenderer(graphicsDevice, this);
        }

        [Obsolete("ShapeBatch no longer needs a ContentManager. Use ShapeBatch(GraphicsDevice, Effect?) instead.")]
        public ShapeBatch(GraphicsDevice graphicsDevice, ContentManager content, Effect? effect = null) : this(graphicsDevice, effect) { }

        /// <summary>
        /// Loads the shader that was precompiled at pack time and embedded in this assembly.
        /// </summary>
        private static Effect LoadEmbeddedEffect(GraphicsDevice graphicsDevice) {
#if KNI
            // The knifx covers KNI's GL family (desktop GL, GLES, WebGL); the DirectX backends load standard MGFX instead.
            GraphicsBackend backend = graphicsDevice.Adapter.Backend;
            if (backend == GraphicsBackend.DirectX11 || backend == GraphicsBackend.DirectX12) {
                return new Effect(graphicsDevice, ReadEmbeddedBytes("Apos.Shapes.apos-shapes.dx11.mgfx"));
            }
            return new Effect(graphicsDevice, ReadEmbeddedBytes("Apos.Shapes.apos-shapes.knifx"));
#else
            string name = UsesOpenGL() ? "Apos.Shapes.apos-shapes.ogl.mgfx" : "Apos.Shapes.apos-shapes.dx11.mgfx";
            return new Effect(graphicsDevice, ReadEmbeddedBytes(name));
#endif
        }

#if !KNI
        private static bool UsesOpenGL() {
            // MonoGame.Framework has the same assembly name for every platform, so ask the internal
            // Shader.Profile (0 = OpenGL, 1 = DirectX) which bytecode the runtime expects.
            var shader = typeof(Effect).Assembly.GetType("Microsoft.Xna.Framework.Graphics.Shader");
            var profile = shader?.GetProperty("Profile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (profile?.GetValue(null) is int p) {
                return p == 0;
            }
            // Only the DesktopGL assembly bundles the SDL bindings.
            return typeof(Effect).Assembly.GetType("Sdl") != null;
        }
#endif

        private static byte[] ReadEmbeddedBytes(string name) {
            using var stream = typeof(ShapeBatch).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Missing embedded resource \"{name}\".");
            byte[] bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }

        public GraphicsDevice GraphicsDevice => _graphicsDevice;

        /// <summary>
        /// Color space that gradient and border colors are interpolated in. Defaults to Oklab. Captured per shape
        /// at draw time so it can change mid batch without breaking it. Textures and strings always use raw RGBA masks.
        /// </summary>
        public ColorSpace ColorSpace { get; set; } = ColorSpace.Oklab;

        /// <summary>
        /// Strength of the screen-space dither that hides gradient color banding on 8-bit render
        /// targets, in 8-bit color steps of peak-to-peak noise. The default of 1 covers exactly the
        /// quantization interval, which removes banding while staying imperceptible. Set 0 to disable,
        /// or higher when rendering to a target that gets posterized further downstream.
        /// </summary>
        public float DitherStrength { get; set; } = 1f;

        /// <summary>
        /// Noise pattern behind the banding dither. Same GPU cost either way; see the enum for
        /// the quality trade-off.
        /// </summary>
        public DitherNoise DitherNoiseSource { get; set; } = DitherNoise.BlueNoise;

        public void Begin(Matrix? view = null, Matrix? projection = null, BlendState? blendState = null, SamplerState? samplerState = null, DepthStencilState? depthStencilState = null, RasterizerState? rasterizerState = null) {
            if (_beginCalled) {
                throw new InvalidOperationException("Begin cannot be called again until End has been successfully called.");
            }
            _beginCalled = true;
            _pathOpen = false; // Recover from a path a failed frame left unfinished.

            if (view != null) {
                _view = view.Value;
            } else {
                _view = Matrix.Identity;
            }

            Viewport viewport = _graphicsDevice.Viewport;
            if (projection != null) {
                _projection = projection.Value;
            } else {
                _projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, 1);
            }

            // Shapes live on the z = 0 plane, so the world→screen mapping only keeps a
            // perspective term when x or y feeds into clip w. Affine projections have one
            // pixel size everywhere; perspective ones resample it per draw call.
            _worldToClip = _view * _projection;
            _halfViewport = new Vector2(viewport.Width * 0.5f, viewport.Height * 0.5f);
            _isPerspective = _worldToClip.M14 != 0f || _worldToClip.M24 != 0f;
            _pixelSize = PixelSizeAt(Vector2.Zero);

            _blendState = blendState ?? BlendState.AlphaBlend;
            _samplerState = samplerState ?? SamplerState.LinearClamp;
            _depthStencilState = depthStencilState ?? DepthStencilState.None;
            _rasterizerState = rasterizerState ?? RasterizerState.CullCounterClockwise;
        }
        public void DrawCircle(Vector2 center, float radius, Gradient fill, Gradient border, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f, DashStyle dash = default) {
            UpdatePixelSize(center, radius);
            float aaOffset = _pixelSize * aaSize;

            ResolvedDash rd = dash.Resolve(MathF.Tau * radius, closed: true);

            if (thickness > 0f && IsTransparent(fill)) {
                float holeRadius = radius - thickness - aaOffset - _pixelSize;
                if (holeRadius > _hollowMinHolePixels * _pixelSize) {
                    GradientToWorld(ref fill, ref border, center, Vector2.Zero, rotation);
                    EmitHollowAnnulus(center, rotation, Vector2.Zero, 0f, MathF.PI, new Vector2(holeRadius), new Vector2(radius + aaOffset + _pixelSize), true, VertexShape.Shape.Circle, fill, border, thickness, radius, 1f, aaSize, 0f, rd.Period, rd.FracPhase, 0f, 0f, rd.TypeDigit);
                    return;
                }
            }

            PrepareQuad();

            float radius1 = radius + aaOffset; // Account for AA.

            var topLeft = center + new Vector2(-radius1);
            var topRight = center + new Vector2(radius1, -radius1);
            var bottomRight = center + new Vector2(radius1);
            var bottomLeft = center + new Vector2(-radius1, radius1);

            if (rotation != 0f) {
                topLeft = Rotate(topLeft, center, rotation);
                topRight = Rotate(topRight, center, rotation);
                bottomRight = Rotate(bottomRight, center, rotation);
                bottomLeft = Rotate(bottomLeft, center, rotation);
            }

            GradientToWorld(ref fill, ref border, center, Vector2.Zero, rotation);

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius1, -radius1), VertexShape.Shape.Circle, fill, border, thickness, radius, GetClipSpace(topLeft), aaSize: aaSize, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(radius1, -radius1), VertexShape.Shape.Circle, fill, border, thickness, radius, GetClipSpace(topRight), aaSize: aaSize, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(radius1, radius1), VertexShape.Shape.Circle, fill, border, thickness, radius, GetClipSpace(bottomRight), aaSize: aaSize, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius1, radius1), VertexShape.Shape.Circle, fill, border, thickness, radius, GetClipSpace(bottomLeft), aaSize: aaSize, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillCircle(Vector2 center, float radius, Gradient g, float rotation = 0f, float aaSize = 1.5f) {
            DrawCircle(center, radius, g, g, 0f, rotation, aaSize);
        }
        public void BorderCircle(Vector2 center, float radius, Gradient g, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f, DashStyle dash = default) {
            DrawCircle(center, radius, Color.Transparent, g, thickness, rotation, aaSize, dash);
        }

        public void DrawRectangle(Vector2 xy, Vector2 size, Gradient fill, Gradient border, float thickness, CornerRadii cornerRadii, float rotation = 0f, float aaSize = 1.5f, DashStyle dash = default) {
            PrepareQuad();

            float maxR = MathF.Min(size.X, size.Y) / 2f;
            float rTL = MathHelper.Clamp(cornerRadii.TopLeft,     0f, maxR);
            float rTR = MathHelper.Clamp(cornerRadii.TopRight,    0f, maxR);
            float rBR = MathHelper.Clamp(cornerRadii.BottomRight, 0f, maxR);
            float rBL = MathHelper.Clamp(cornerRadii.BottomLeft,  0f, maxR);

            UpdatePixelSize(xy + size / 2f, (size / 2f).Length());
            float aaOffset = _pixelSize * aaSize;
            Vector2 xy1 = xy - new Vector2(aaOffset); // Account for AA.
            Vector2 size1 = size + new Vector2(aaOffset * 2f); // Account for AA.
            Vector2 half = size / 2f;
            Vector2 half1 = half + new Vector2(aaOffset); // Account for AA.

            var topLeft = xy1;
            var topRight = xy1 + new Vector2(size1.X, 0);
            var bottomRight = xy1 + size1;
            var bottomLeft = xy1 + new Vector2(0, size1.Y);

            Vector2 center = xy1 + half1;
            if (rotation != 0f) {
                topLeft = Rotate(topLeft, center, rotation);
                topRight = Rotate(topRight, center, rotation);
                bottomRight = Rotate(bottomRight, center, rotation);
                bottomLeft = Rotate(bottomLeft, center, rotation);
            }

            GradientToWorld(ref fill, ref border, xy + half, -half, rotation);

            float rA = rTR;
            float rB = rBR;
            float rC = rTL;
            float rD = rBL;

            // Dashed rectangles ship their corner radii as 11 bit fractions of the largest allowed
            // radius, freeing two channels for the pattern. Snapping the radii to that grid here
            // keeps the CPU perimeter bit-exact with the one the shader walks.
            ResolvedDash rd = default;
            if (dash.IsEnabled) {
                if (maxR > 0f) {
                    rA = MathF.Round(rA / maxR * 2047f) / 2047f * maxR;
                    rB = MathF.Round(rB / maxR * 2047f) / 2047f * maxR;
                    rC = MathF.Round(rC / maxR * 2047f) / 2047f * maxR;
                    rD = MathF.Round(rD / maxR * 2047f) / 2047f * maxR;
                }
                // Each corner is walked on its own pattern radius, so the straight runs between
                // them shorten to match; the shader derives the same four radii.
                float boxCap = MathF.Min(size.X, size.Y) * 0.5f * 0.5f;
                float pA2 = PatternRadius(rA, thickness, boxCap);
                float pB2 = PatternRadius(rB, thickness, boxCap);
                float pC2 = PatternRadius(rC, thickness, boxCap);
                float pD2 = PatternRadius(rD, thickness, boxCap);
                float perimeter = 2f * (size.X + size.Y) - (2f - MathF.PI / 2f) * (pA2 + pB2 + pC2 + pD2);
                rd = dash.Resolve(perimeter, closed: true);
            }
            float a = rA;
            float b = rB;
            float c = rC;
            float d = rD;
            if (rd.TypeDigit > 0) {
                float mr = MathF.Max(maxR, 1e-9f);
                a = DashStyle.Pack11(rA / mr, rB / mr);
                b = DashStyle.Pack11(rC / mr, rD / mr);
                c = rd.Period;
                d = rd.FracPhase;
            }

            if (thickness > 0f && IsTransparent(fill)) {
                float maxCorner = MathF.Max(MathF.Max(rTL, rTR), MathF.Max(rBR, rBL));
                float inset = thickness + aaOffset + _pixelSize + maxCorner;
                Vector2 hole = new(half.X - inset, half.Y - inset);
                float minHole = _hollowMinHolePixels * _pixelSize;
                if (hole.X > 4f * _pixelSize && hole.Y > 4f * _pixelSize && hole.X * hole.Y > minHole * minHole) {
                    Span<Vector2> outerCorners = stackalloc Vector2[] { new(-half1.X, -half1.Y), new(half1.X, -half1.Y), new(half1.X, half1.Y), new(-half1.X, half1.Y) };
                    Span<Vector2> innerCorners = stackalloc Vector2[] { new(-hole.X, -hole.Y), new(hole.X, -hole.Y), new(hole.X, hole.Y), new(-hole.X, hole.Y) };
                    EmitHollowFrame(center, rotation, outerCorners, innerCorners, VertexShape.Shape.Rectangle, fill, border, thickness, half.X, half.Y, aaSize, 0f, a, b, c, d, rd.TypeDigit);
                    return;
                }
            }

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-half1.X, -half1.Y), VertexShape.Shape.Rectangle, fill, border, thickness, half.X, GetClipSpace(topLeft), half.Y, aaSize: aaSize, rounded: 0f, a: a, b: b, c: c, d: d, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(half1.X, -half1.Y), VertexShape.Shape.Rectangle, fill, border, thickness, half.X, GetClipSpace(topRight), half.Y, aaSize: aaSize, rounded: 0f, a: a, b: b, c: c, d: d, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(half1.X, half1.Y), VertexShape.Shape.Rectangle, fill, border, thickness, half.X, GetClipSpace(bottomRight), half.Y, aaSize: aaSize, rounded: 0f, a: a, b: b, c: c, d: d, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-half1.X, half1.Y), VertexShape.Shape.Rectangle, fill, border, thickness, half.X, GetClipSpace(bottomLeft), half.Y, aaSize: aaSize, rounded: 0f, a: a, b: b, c: c, d: d, colorSpace: ColorSpace, dash: rd.TypeDigit);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillRectangle(Vector2 xy, Vector2 size, Gradient g, CornerRadii cornerRadii = default, float rotation = 0f, float aaSize = 1.5f) {
            DrawRectangle(xy, size, g, g, 0f, cornerRadii, rotation, aaSize);
        }
        public void BorderRectangle(Vector2 xy, Vector2 size, Gradient g, float thickness, CornerRadii cornerRadii = default, float rotation = 0f, float aaSize = 1.5f, DashStyle dash = default) {
            DrawRectangle(xy, size, Color.Transparent, g, thickness, cornerRadii, rotation, aaSize, dash);
        }

        public void DrawLine(Vector2 a, Vector2 b, float radius, Gradient fill, Gradient border, float thickness = 1f, float aaSize = 1.5f, DashStyle dash = default) {
            if (a == b) {
                DrawCircle(a, radius, fill, border, thickness, aaSize: aaSize);
                return;
            }

            UpdatePixelSize(a, b, radius);
            float aaOffset = _pixelSize * aaSize;

            // The stroke itself is cut into dashes along the spine, so each dash keeps its own
            // fill and border. That also rules out the hollow mesh: dash faces cross the interior.
            ResolvedDash rd = dash.Resolve(Vector2.Distance(a, b), closed: false);

            if (rd.TypeDigit == 0 && thickness > 0f && IsTransparent(fill)) {
                float holeRadius = radius - thickness - aaOffset - _pixelSize;
                if (holeRadius > _hollowMinHolePixels * _pixelSize) {
                    float length = Vector2.Distance(a, b);
                    float lineRotation = MathF.Atan2(b.Y - a.Y, b.X - a.X);
                    GradientToWorld(ref fill, ref border, a, Vector2.Zero, lineRotation);

                    float outerRadius = radius + aaOffset + _pixelSize;
                    (float sinL, float cosL) = MathF.SinCos(lineRotation);
                    Vector2 dir = new(cosL, sinL);
                    Vector2 perp = new(-sinL, cosL);

                    EmitHollowQuad(a - perp * outerRadius, a + dir * length - perp * outerRadius, a + dir * length - perp * holeRadius, a - perp * holeRadius,
                        new Vector2(0f, -outerRadius), new Vector2(length, -outerRadius), new Vector2(length, -holeRadius), new Vector2(0f, -holeRadius),
                        VertexShape.Shape.Line, fill, border, thickness, radius, length, aaSize, radius, 0f, 0f, 0f, 0f);
                    EmitHollowQuad(a + dir * length + perp * outerRadius, a + perp * outerRadius, a + perp * holeRadius, a + dir * length + perp * holeRadius,
                        new Vector2(length, outerRadius), new Vector2(0f, outerRadius), new Vector2(0f, holeRadius), new Vector2(length, holeRadius),
                        VertexShape.Shape.Line, fill, border, thickness, radius, length, aaSize, radius, 0f, 0f, 0f, 0f);
                    EmitHollowAnnulus(a, lineRotation, Vector2.Zero, -MathF.PI * 0.5f, MathF.PI * 0.5f, new Vector2(holeRadius), new Vector2(outerRadius), false, VertexShape.Shape.Line, fill, border, thickness, radius, length, aaSize, radius, 0f, 0f, 0f, 0f);
                    EmitHollowAnnulus(a, lineRotation, new Vector2(length, 0f), MathF.PI * 0.5f, MathF.PI * 0.5f, new Vector2(holeRadius), new Vector2(outerRadius), false, VertexShape.Shape.Line, fill, border, thickness, radius, length, aaSize, radius, 0f, 0f, 0f, 0f);
                    return;
                }
            }

            PrepareQuad();

            var radius1 = radius + aaOffset; // Account for AA.

            var c = Slide(b, a, radius1);
            var d = Slide(a, b, radius1);

            var topLeft = Clockwise(c, d, radius1);
            var topRight = CounterClockwise(d, c, radius1);
            var bottomRight = Clockwise(d, c, radius1);
            var bottomLeft = CounterClockwise(c, d, radius1);

            var width = Vector2.Distance(a, b);
            var width1 = width + radius1; // Account for AA.

            GradientToWorld(ref fill, ref border, a, Vector2.Zero, MathF.Atan2(b.Y - a.Y, b.X - a.X));

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius1, -radius1), VertexShape.Shape.Line, fill, border, thickness, radius, GetClipSpace(topLeft), width, aaSize: aaSize, rounded: radius, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(width1, -radius1), VertexShape.Shape.Line, fill, border, thickness, radius, GetClipSpace(topRight), width, aaSize: aaSize, rounded: radius, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(width1, radius1), VertexShape.Shape.Line, fill, border, thickness, radius, GetClipSpace(bottomRight), width, aaSize: aaSize, rounded: radius, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius1, radius1), VertexShape.Shape.Line, fill, border, thickness, radius, GetClipSpace(bottomLeft), width, aaSize: aaSize, rounded: radius, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillLine(Vector2 a, Vector2 b, float radius, Gradient g, float aaSize = 1.5f, DashStyle dash = default) {
            DrawLine(a, b, radius, g, g, 0f, aaSize, dash);
        }
        public void BorderLine(Vector2 a, Vector2 b, float radius, Gradient g, float thickness = 1f, float aaSize = 1.5f, DashStyle dash = default) {
            DrawLine(a, b, radius, Color.Transparent, g, thickness, aaSize, dash);
        }

        /// <summary>
        /// Draws a polyline through the given points as one continuous shape. Segments partition the
        /// stroke along shared seams, so translucent strokes blend once instead of stacking where
        /// segments meet, and gradients span the whole stroke. Joins can be round, miter, or bevel and
        /// caps can be round, butt, or square; capEnd styles the end of the path separately. Miter joins
        /// sharper than the miter limit, measured like SVG's miterlimit, fall back to bevel. A path that
        /// crosses over itself still overlaps like separate shapes would, as do joins whose segments are
        /// much shorter than the stroke radius.
        /// Setting closed joins the last point back to the first: the wrap becomes a joint like any
        /// other, the cap styles go unused, and a dash pattern runs around the loop without a seam.
        /// </summary>
        public void DrawPath(ReadOnlySpan<Vector2> points, float radius, Gradient fill, Gradient border, float thickness = 1f, PathJoin join = PathJoin.Round, PathCap cap = PathCap.Round, PathCap? capEnd = null, float miterLimit = 4f, float aaSize = 1.5f, bool closed = false, DashStyle dash = default) {
            // Every segment needs a direction, so drop consecutive duplicates.
            Span<Vector2> pts = points.Length <= 256 ? stackalloc Vector2[points.Length] : new Vector2[points.Length];
            int n = 0;
            foreach (Vector2 p in points) {
                if (n == 0 || Vector2.DistanceSquared(p, pts[n - 1]) > 1e-12f) pts[n++] = p;
            }
            // A closed path already returns to its first point, so a repeat of it is a duplicate too.
            if (closed && n > 1 && Vector2.DistanceSquared(pts[n - 1], pts[0]) <= 1e-12f) n--;
            DrawPathCore(pts[..n], default, radius, fill, border, thickness, join, cap, capEnd ?? cap, miterLimit, aaSize, closed, dash);
        }
        /// <summary>
        /// Starts building a path point by point, an alternative to DrawPath that needs no point array.
        /// Feed points with PathTo, then call EndPath to draw it. All the DrawPath rules apply: joins can
        /// change along the way through PathTo, and the whole path draws as one continuous shape.
        /// </summary>
        public void BeginPath(float radius, Gradient fill, Gradient border, float thickness = 1f, PathJoin join = PathJoin.Round, PathCap cap = PathCap.Round, PathCap? capEnd = null, float miterLimit = 4f, float aaSize = 1.5f, DashStyle dash = default) {
            if (_pathOpen) {
                throw new InvalidOperationException("BeginPath cannot be called again until EndPath has been called.");
            }
            _pathOpen = true;
            _pathPointCount = 0;
            _pathRadius = radius;
            _pathFill = fill;
            _pathBorder = border;
            _pathThickness = thickness;
            _pathJoin = join;
            _pathCap = cap;
            _pathCapEnd = capEnd;
            _pathMiterLimit = miterLimit;
            _pathAaSize = aaSize;
            _pathDash = dash;
        }
        public void BeginFillPath(float radius, Gradient g, PathJoin join = PathJoin.Round, PathCap cap = PathCap.Round, PathCap? capEnd = null, float miterLimit = 4f, float aaSize = 1.5f, DashStyle dash = default) {
            BeginPath(radius, g, g, 0f, join, cap, capEnd, miterLimit, aaSize, dash);
        }
        public void BeginBorderPath(float radius, Gradient g, float thickness = 1f, PathJoin join = PathJoin.Round, PathCap cap = PathCap.Round, PathCap? capEnd = null, float miterLimit = 4f, float aaSize = 1.5f, DashStyle dash = default) {
            BeginPath(radius, Color.Transparent, g, thickness, join, cap, capEnd, miterLimit, aaSize, dash);
        }
        /// <summary>
        /// Adds the next point to the path started by BeginPath. Passing a join switches the style for
        /// the joint at this point and every following joint until another point switches it again.
        /// </summary>
        public void PathTo(Vector2 point, PathJoin? join = null) {
            if (!_pathOpen) {
                throw new InvalidOperationException("BeginPath must be called before PathTo.");
            }
            EnsureSizeOrDouble(ref _pathPoints, _pathPointCount + 1);
            _pathPoints[_pathPointCount++] = new PathPoint(point, join);
        }
        /// <summary>Draws the path built since BeginPath, capped at both ends.</summary>
        public void EndPath() {
            EndPathCore(false);
        }
        /// <summary>
        /// Draws the path built since BeginPath as a closed loop, joining the last point back to the
        /// first. The wrap gets a joint like every other point instead of two caps, so the cap styles
        /// go unused and a dash pattern runs around the loop without a seam.
        /// </summary>
        public void ClosePath() {
            EndPathCore(true);
        }
        private void EndPathCore(bool closed) {
            if (!_pathOpen) {
                throw new InvalidOperationException("BeginPath must be called before EndPath or ClosePath.");
            }
            _pathOpen = false;
            DrawPathPoints(new ReadOnlySpan<PathPoint>(_pathPoints, 0, _pathPointCount), _pathRadius, _pathFill, _pathBorder, _pathThickness, _pathJoin, _pathCap, _pathCapEnd, _pathMiterLimit, _pathAaSize, closed, _pathDash);
        }

        // The styled entry point behind the ShapeBatchPathExtensions overloads. Kept internal so plain
        // Vector2 point lists keep binding the overload above without ambiguity on any C# version.
        internal void DrawPathPoints(ReadOnlySpan<PathPoint> points, float radius, Gradient fill, Gradient border, float thickness, PathJoin join, PathCap cap, PathCap? capEnd, float miterLimit, float aaSize, bool closed = false, DashStyle dash = default) {
            Span<Vector2> pts = points.Length <= 256 ? stackalloc Vector2[points.Length] : new Vector2[points.Length];
            Span<PathJoin> joins = points.Length <= 256 ? stackalloc PathJoin[points.Length] : new PathJoin[points.Length];
            int n = 0;
            PathJoin running = join;
            foreach (PathPoint p in points) {
                if (p.Join.HasValue) running = p.Join.Value;
                if (n == 0 || Vector2.DistanceSquared(p.Position, pts[n - 1]) > 1e-12f) {
                    pts[n] = p.Position;
                    joins[n] = running;
                    n++;
                } else {
                    // A dropped duplicate still carries its style forward.
                    joins[n - 1] = running;
                }
            }
            // A closed path already returns to its first point, so a repeat of it is a duplicate too.
            if (closed && n > 1 && Vector2.DistanceSquared(pts[n - 1], pts[0]) <= 1e-12f) {
                n--;
                joins[0] = joins[n];
            }
            DrawPathCore(pts[..n], joins[..n], radius, fill, border, thickness, join, cap, capEnd ?? cap, miterLimit, aaSize, closed, dash);
        }
        // joins holds the effective join per point (empty for a uniform path); the joint at the end of
        // segment j reads entry j + 1, wrapping with the segments when the path is closed.
        private void DrawPathCore(ReadOnlySpan<Vector2> pts, ReadOnlySpan<PathJoin> joins, float radius, Gradient fill, Gradient border, float thickness, PathJoin join, PathCap capStart, PathCap capEnd, float miterLimit, float aaSize, bool closed = false, DashStyle dash = default) {
            int n = pts.Length;
            if (n == 0) return;
            // A loop needs a triangle at least; anything shorter has no interior to go around and
            // draws as the open stroke it already is.
            if (closed && n < 3) closed = false;
            if (n == 1) {
                if (capStart == PathCap.Round) {
                    DrawCircle(pts[0], radius, fill, border, thickness, aaSize: aaSize);
                } else if (capStart == PathCap.Square) {
                    DrawRectangle(pts[0] - new Vector2(radius), new Vector2(radius * 2f), fill, border, thickness, default, aaSize: aaSize);
                }
                return;
            }
            if (n == 2 && capStart == PathCap.Round && capEnd == PathCap.Round) {
                DrawLine(pts[0], pts[1], radius, fill, border, thickness, aaSize, dash);
                return;
            }

            // A closed path has one segment per point and a joint at every point, including the wrap
            // back to the start; an open one is a segment and a joint short, and takes caps instead.
            int segs = closed ? n : n - 1;
            int jointCount = closed ? n : n - 2;

            bool dashed = dash.IsEnabled;

            if (_isPerspective) {
                float worst = 0f;
                for (int i = 0; i < n; i++) {
                    worst = MathF.Max(worst, SamplePixelSize(pts[i], radius));
                }
                _pixelSize = worst;
            }
            float aaOffset = _pixelSize * aaSize;
            float h = radius + aaOffset; // Quads reach the outer AA edge.

            // One anchor for the whole path so gradients run seamlessly across segments.
            GradientToWorld(ref fill, ref border, pts[0], Vector2.Zero, MathF.Atan2(pts[1].Y - pts[0].Y, pts[1].X - pts[0].X));

            // The capsule SDFs of two segments agree along the bisector of their joint, so cutting both
            // quads there splits the stroke into regions that each blend exactly once and meet invisibly.
            Span<PathJoint> joints = jointCount <= 256 ? stackalloc PathJoint[jointCount] : new PathJoint[jointCount];
            Vector2 uPrev = (pts[1] - pts[0]) / Vector2.Distance(pts[0], pts[1]);
            float lenPrev = Vector2.Distance(pts[0], pts[1]);
            for (int j = 0; j < jointCount; j++) {
                // Joint j sits at the end of segment j. On a closed path the last one lands back on
                // the first point and turns into the first segment.
                Vector2 joint = pts[(j + 1) % n];
                Vector2 d = pts[(j + 2) % n] - joint;
                float len = d.Length();
                Vector2 u = d / len;
                ref PathJoint jd = ref joints[j];

                float c2 = Vector2.Dot(uPrev, u);
                float s2 = uPrev.X * u.Y - uPrev.Y * u.X;
                float cHalf = MathF.Sqrt(MathF.Max((1f + c2) * 0.5f, 0f));
                if (cHalf < 0.05f) {
                    // Near reversal the bisector degenerates; both sides fall back to overlapping round caps.
                    jd.Mode = PathJointMode.Reversal;
                    jd.ThetaCode = 1024f;
                } else {
                    float sHalf = MathF.Sqrt(MathF.Max((1f - c2) * 0.5f, 0f));
                    float sign = s2 >= 0f ? 1f : -1f;
                    float run = h * sHalf / cHalf; // How far along each segment the inner miter reaches.
                    jd.Sign = sign;
                    jd.CHalf = cHalf;
                    jd.SHalf = sHalf;
                    jd.Theta = 2f * MathF.Atan2(sHalf, cHalf);
                    if (run <= MathF.Min(lenPrev, len) * 0.5f) {
                        jd.Mode = PathJointMode.Partition;
                        Vector2 m = (new Vector2(-uPrev.Y, uPrev.X) + new Vector2(-u.Y, u.X)) / (2f * cHalf);
                        jd.BIn = joint + m * (sign * h / cHalf);
                        PathJoin requested = joins.IsEmpty ? join : joins[(j + 1) % n];
                        if (requested != PathJoin.Round && jd.Theta > 1e-4f) {
                            PathJoin effective = requested;
                            // SVG semantics: the miter ratio is 1 / cos of the half turn.
                            if (effective == PathJoin.Miter && 1f > cHalf * miterLimit) effective = PathJoin.Bevel;
                            // A nearly straight bevel is a miter; its cut plane would run parallel to the stroke.
                            if (effective == PathJoin.Bevel && sHalf < 0.01f) effective = PathJoin.Miter;
                            jd.Join = effective;
                            // Miter: the h-offset lines' outer intersection. Bevel: just past the cut plane's AA.
                            jd.MOut = effective == PathJoin.Miter
                                ? joint - m * (sign * h / cHalf)
                                : joint - m * (sign * (radius * cHalf + aaOffset + _pixelSize));
                        }
                    } else {
                        // The inner miter outruns a short segment. Overlapping slabs stay hole-free and only
                        // double blend inside the joint, where the stroke genuinely covers itself.
                        jd.Mode = PathJointMode.Overlap;
                    }
                    if (dashed) {
                        // For dashing the joint rounds the spine corner with a fillet arc tangent
                        // to both segments, replacing the two tangent runs it rounds off. The
                        // shader decodes the signed angle from an 11 bit code (1024 = 0); quantize
                        // here so both length walks agree exactly. Overlap joints run the pattern
                        // straight out instead: their segments are too short for the arc, and they
                        // already overlap like separate shapes.
                        if (jd.Mode == PathJointMode.Partition) {
                            jd.ThetaCode = 1024f + MathF.Round(sign * jd.Theta / MathF.PI * 1023f);
                            float thetaQ = MathF.Abs(jd.ThetaCode - 1024f) / 1023f * MathF.PI;
                            float tanQ = MathF.Tan(thetaQ * 0.5f);
                            // A fillet of exactly radius has an inward offset of zero: it collapses
                            // to the point where the two inner offsets meet, which lies on the
                            // stroke's inner edge, and every dash boundary in the fan runs out of
                            // it. Widening the fillet lifts its center clear of the stroke so no two
                            // boundaries meet anywhere that gets drawn. It may spend at most half of
                            // each neighbouring segment, the same budget the inner miter run is
                            // already known to fit in, so the fallback is never worse than radius.
                            float fit = tanQ > 1e-6f ? MathF.Min(lenPrev, len) * 0.5f / tanQ : float.MaxValue;
                            // Only basic dashes may widen it. A rounded dash is the capsule around
                            // the spine, so the spine is its body and not just where its ends land;
                            // move it off the real corner and the dash stops following the stroke.
                            // They have no fan center to escape anyway: a rounded dash reaches it as
                            // a half disc rather than tapering to a point.
                            float want = dash.Cap == DashCap.Round ? radius : 2f * radius;
                            // The code spans [radius, 2 * radius]; the shader mirrors this mapping.
                            jd.DashRadiusCode = Math.Clamp(MathF.Round((MathF.Min(fit, want) / radius - 1f) * 127f), 0f, 127f);
                            jd.DashArc = (thetaQ - 2f * tanQ) * radius * (1f + jd.DashRadiusCode / 127f);
                        } else {
                            jd.ThetaCode = 1024f;
                        }
                    }
                    if (jd.Theta > 1e-4f) {
                        // Same sagitta rule as EmitHollowAnnulus: chords circumscribe the join arc.
                        float phi = MathF.Acos(MathF.Max(1f - _hollowMaxSagPixels * _pixelSize / h, 0f));
                        jd.Chords = Math.Clamp((int)MathF.Ceiling(jd.Theta / MathF.Max(phi, 1e-4f)), 1, _hollowMaxSectors);
                        float overshoot = 1f / MathF.Cos(jd.Theta / (2f * jd.Chords));
                        jd.Overshoot = overshoot;
                        jd.E1Prev = joint - new Vector2(-uPrev.Y, uPrev.X) * (sign * h * overshoot);
                        jd.E1Next = joint - new Vector2(-u.Y, u.X) * (sign * h * overshoot);
                    } else {
                        // Straight through: one shared wall point keeps the seam watertight.
                        jd.Chords = 0;
                        jd.E1Prev = jd.E1Next = joint - new Vector2(-uPrev.Y, uPrev.X) * (sign * h);
                    }
                }
                uPrev = u;
                lenPrev = len;
            }

            // The dash pattern runs along the polyline with each joint's corner rounded off for
            // dashing (see PathDashCut in the shader); each quad carries its segment's start
            // length so dashes flow through the joints unbroken. Closed contours snap the period
            // to a whole number of repeats, so the pattern meets itself at the wrap with no seam.
            ResolvedDash rd = default;
            if (dashed) {
                float totalLen = 0f;
                for (int i = 0; i < segs; i++) {
                    totalLen += Vector2.Distance(pts[i], pts[(i + 1) % n]);
                }
                for (int j = 0; j < jointCount; j++) {
                    totalLen += joints[j].DashArc;
                }
                rd = dash.Resolve(totalLen, closed);
            }

            // Cross sections at each end: two corners at a cap, up to three points at a joint.
            // Every polygon is convex, so a fan from the first vertex triangulates it.
            Span<Vector2> poly = stackalloc Vector2[6];
            float startLen = 0f;
            for (int i = 0; i < segs; i++) {
                Vector2 a = pts[i];
                Vector2 b = pts[(i + 1) % n];
                Vector2 d = b - a;
                float len = d.Length();
                Vector2 u = d / len;
                Vector2 nrm = new(-u.Y, u.X);

                // A closed path joints at both ends of every segment, the first one included; an open
                // one caps where it runs out of joints.
                bool hasStart = closed || i > 0;
                bool hasEnd = closed || i < segs - 1;
                int js = closed ? (i + jointCount - 1) % jointCount : i - 1;
                int je = i;

                int count = 0;
                float modeStart = 0f;
                float modeEnd = 0f;
                float angStart = 0f;
                float angEnd = 0f;
                // Start end, walked from +nrm to -nrm so the polygon winds clockwise on screen.
                if (!hasStart || joints[js].Mode == PathJointMode.Reversal) {
                    // Reversal fallbacks keep round caps; only true path ends take the cap style.
                    float ext = !hasStart && capStart == PathCap.Butt ? aaOffset : h;
                    if (!hasStart) modeStart = (float)capStart;
                    Vector2 back = a - u * ext;
                    poly[count++] = back + nrm * h;
                    poly[count++] = back - nrm * h;
                } else {
                    ref PathJoint jd = ref joints[js];
                    if (jd.Join == PathJoin.Miter) {
                        modeStart = 3f;
                        if (jd.Sign > 0f) {
                            poly[count++] = jd.BIn;
                            poly[count++] = jd.MOut;
                        } else {
                            poly[count++] = jd.MOut;
                            poly[count++] = jd.BIn;
                        }
                    } else if (jd.Join == PathJoin.Bevel) {
                        modeStart = 4f;
                        angStart = MathF.Atan2(-jd.Sign * jd.CHalf, -jd.SHalf);
                        // Where the cut plane's AA edge leaves the outer offset line.
                        Vector2 x0 = a - u * ((aaOffset * (1f - jd.CHalf) + _pixelSize) / jd.SHalf) - nrm * (jd.Sign * h);
                        if (jd.Sign > 0f) {
                            poly[count++] = jd.BIn;
                            poly[count++] = jd.MOut;
                            poly[count++] = x0;
                        } else {
                            poly[count++] = x0;
                            poly[count++] = jd.MOut;
                            poly[count++] = jd.BIn;
                        }
                    } else {
                        Vector2 inner = jd.Mode == PathJointMode.Partition ? jd.BIn : a + nrm * (jd.Sign * h);
                        if (jd.Sign > 0f) {
                            poly[count++] = inner;
                            poly[count++] = a;
                            poly[count++] = jd.E1Next;
                        } else {
                            poly[count++] = jd.E1Next;
                            poly[count++] = a;
                            poly[count++] = inner;
                        }
                    }
                }
                // End end, walked from -nrm back to +nrm.
                if (!hasEnd || joints[je].Mode == PathJointMode.Reversal) {
                    float ext = !hasEnd && capEnd == PathCap.Butt ? aaOffset : h;
                    if (!hasEnd) modeEnd = (float)capEnd;
                    Vector2 fwd = b + u * ext;
                    poly[count++] = fwd - nrm * h;
                    poly[count++] = fwd + nrm * h;
                } else {
                    ref PathJoint jd = ref joints[je];
                    if (jd.Join == PathJoin.Miter) {
                        modeEnd = 3f;
                        if (jd.Sign > 0f) {
                            poly[count++] = jd.MOut;
                            poly[count++] = jd.BIn;
                        } else {
                            poly[count++] = jd.BIn;
                            poly[count++] = jd.MOut;
                        }
                    } else if (jd.Join == PathJoin.Bevel) {
                        modeEnd = 4f;
                        angEnd = MathF.Atan2(-jd.Sign * jd.CHalf, jd.SHalf);
                        Vector2 x1 = b + u * ((aaOffset * (1f - jd.CHalf) + _pixelSize) / jd.SHalf) - nrm * (jd.Sign * h);
                        if (jd.Sign > 0f) {
                            poly[count++] = x1;
                            poly[count++] = jd.MOut;
                            poly[count++] = jd.BIn;
                        } else {
                            poly[count++] = jd.BIn;
                            poly[count++] = jd.MOut;
                            poly[count++] = x1;
                        }
                    } else {
                        Vector2 inner = jd.Mode == PathJointMode.Partition ? jd.BIn : b + nrm * (jd.Sign * h);
                        if (jd.Sign > 0f) {
                            poly[count++] = jd.E1Prev;
                            poly[count++] = b;
                            poly[count++] = inner;
                        } else {
                            poly[count++] = inner;
                            poly[count++] = b;
                            poly[count++] = jd.E1Prev;
                        }
                    }
                }

                float modes = modeStart + 8f * modeEnd;
                float thetaCodeStart = hasStart ? joints[js].ThetaCode : 1024f;
                float thetaCodeEnd = hasEnd ? joints[je].ThetaCode : 1024f;
                float radiusCodeStart = hasStart ? joints[js].DashRadiusCode : 0f;
                float radiusCodeEnd = hasEnd ? joints[je].DashRadiusCode : 0f;
                for (int k = 1; k + 1 < count; k += 2) {
                    EmitPathQuad(a, u, nrm, len, poly[0], poly[k], poly[k + 1], poly[Math.Min(k + 2, count - 1)], fill, border, thickness, radius, aaSize, modes, angStart, angEnd, thetaCodeStart, thetaCodeEnd, radiusCodeStart, radiusCodeEnd, startLen, rd);
                }

                // Round join: a fan around the joint covers the outer wedge between the two walls. Any
                // point there is nearest the shared joint, so this segment's SDF is exact for the arc.
                if (hasEnd && joints[je].Mode != PathJointMode.Reversal && joints[je].Join == PathJoin.Round && joints[je].Chords > 0) {
                    ref PathJoint jd = ref joints[je];
                    float baseAngle = MathF.Atan2(-jd.Sign * nrm.Y, -jd.Sign * nrm.X);
                    float step = jd.Sign * jd.Theta / jd.Chords;
                    float reach = h * jd.Overshoot;
                    for (int t = 0; t < jd.Chords; t += 2) {
                        Vector2 v0 = PathFanVertex(jd, b, baseAngle, step, reach, t);
                        Vector2 v1 = PathFanVertex(jd, b, baseAngle, step, reach, t + 1);
                        Vector2 v2 = PathFanVertex(jd, b, baseAngle, step, reach, Math.Min(t + 2, jd.Chords));
                        if (jd.Sign > 0f) {
                            EmitPathQuad(a, u, nrm, len, b, v0, v1, v2, fill, border, thickness, radius, aaSize, 0f, 0f, 0f, thetaCodeStart, thetaCodeEnd, radiusCodeStart, radiusCodeEnd, startLen, rd);
                        } else {
                            EmitPathQuad(a, u, nrm, len, b, v2, v1, v0, fill, border, thickness, radius, aaSize, 0f, 0f, 0f, thetaCodeStart, thetaCodeEnd, radiusCodeStart, radiusCodeEnd, startLen, rd);
                        }
                    }
                }

                startLen += len + (hasEnd ? joints[je].DashArc : 0f);
            }
        }
        public void FillPath(ReadOnlySpan<Vector2> points, float radius, Gradient g, PathJoin join = PathJoin.Round, PathCap cap = PathCap.Round, PathCap? capEnd = null, float miterLimit = 4f, float aaSize = 1.5f, bool closed = false, DashStyle dash = default) {
            DrawPath(points, radius, g, g, 0f, join, cap, capEnd, miterLimit, aaSize, closed, dash);
        }
        public void BorderPath(ReadOnlySpan<Vector2> points, float radius, Gradient g, float thickness = 1f, PathJoin join = PathJoin.Round, PathCap cap = PathCap.Round, PathCap? capEnd = null, float miterLimit = 4f, float aaSize = 1.5f, bool closed = false, DashStyle dash = default) {
            DrawPath(points, radius, Color.Transparent, g, thickness, join, cap, capEnd, miterLimit, aaSize, closed, dash);
        }

        public void DrawHexagon(Vector2 center, float radius, Gradient fill, Gradient border, float thickness = 1f, float rounded = 0, float rotation = 0f, float aaSize = 1.5f, DashStyle dash = default) {
            PrepareQuad();

            rounded = MathF.Min(rounded, radius);

            UpdatePixelSize(center, 2f * radius / MathF.Sqrt(3f));
            float aaOffset = _pixelSize * aaSize;
            float radius1 = radius + aaOffset; // Account for AA.
            float width1 = 2f * radius / MathF.Sqrt(3f) + aaOffset; // Account for AA.

            radius -= rounded;

            // Six sides plus the six corner arcs, walked on the pattern radius rather than the
            // rounding, with the hexagon re-inset to keep those arcs tangent to the same edges.
            float hexOut = radius + rounded; // Apothem of the outline itself.
            float hexPatternR = PatternRadius(rounded, thickness, hexOut * 0.5f);
            ResolvedDash rd = dash.Resolve(4f * MathF.Sqrt(3f) * (hexOut - hexPatternR) + MathF.Tau * hexPatternR, closed: true);

            Vector2 size = new(width1, radius1);

            var topLeft = center - size;
            var topRight = center + new Vector2(size.X, -size.Y);
            var bottomRight = center + size;
            var bottomLeft = center + new Vector2(-size.X, size.Y);

            if (rotation != 0f) {
                topLeft = Rotate(topLeft, center, rotation);
                topRight = Rotate(topRight, center, rotation);
                bottomRight = Rotate(bottomRight, center, rotation);
                bottomLeft = Rotate(bottomLeft, center, rotation);
            }

            GradientToWorld(ref fill, ref border, center, Vector2.Zero, rotation);

            if (thickness > 0f && IsTransparent(fill)) {
                float holeOffset = rounded - thickness - aaOffset - _pixelSize;
                // Offsetting outward the hole corners must stay on the rounded band, offsetting inward the erosion is an exact hexagon.
                float holeApothem = radius + (holeOffset <= 0f ? holeOffset : holeOffset * 0.8660254f);
                if (holeApothem > _hollowMinHolePixels * _pixelSize) {
                    Span<Vector2> outerCorners = stackalloc Vector2[6];
                    Span<Vector2> innerCorners = stackalloc Vector2[6];
                    HexagonCorners(radius + rounded + aaOffset + _pixelSize, outerCorners);
                    HexagonCorners(holeApothem, innerCorners);
                    EmitHollowFrame(center, rotation, outerCorners, innerCorners, VertexShape.Shape.Hexagon, fill, border, thickness, radius, 1f, aaSize, rounded, rd.Period, rd.FracPhase, 0f, 0f, rd.TypeDigit);
                    return;
                }
            }

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-size.X, -size.Y), VertexShape.Shape.Hexagon, fill, border, thickness, radius, GetClipSpace(topLeft), aaSize: aaSize, rounded: rounded, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(size.X, -size.Y), VertexShape.Shape.Hexagon, fill, border, thickness, radius, GetClipSpace(topRight), aaSize: aaSize, rounded: rounded, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(size.X, size.Y), VertexShape.Shape.Hexagon, fill, border, thickness, radius, GetClipSpace(bottomRight), aaSize: aaSize, rounded: rounded, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-size.X, size.Y), VertexShape.Shape.Hexagon, fill, border, thickness, radius, GetClipSpace(bottomLeft), aaSize: aaSize, rounded: rounded, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillHexagon(Vector2 center, float radius, Gradient g, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f) {
            DrawHexagon(center, radius, g, g, 0f, rounded, rotation, aaSize);
        }
        public void BorderHexagon(Vector2 center, float radius, Gradient g, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashStyle dash = default) {
            DrawHexagon(center, radius, Color.Transparent, g, thickness, rounded, rotation, aaSize, dash);
        }

        public void DrawEquilateralTriangle(Vector2 center, float radius, Gradient fill, Gradient border, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashStyle dash = default) {
            PrepareQuad();

            rounded = MathF.Min(rounded, radius);

            UpdatePixelSize(center, radius * 2f);
            float aaOffset = _pixelSize * aaSize;
            float height = radius * 3f;

            float halfWidth = height / MathF.Sqrt(3f);
            float incircle = height / 3f;
            float circumcircle = 2f * height / 3f;

            float halfWidth1 = halfWidth + aaOffset; // Account for AA.
            float incircle1 = incircle + aaOffset; // Account for AA.
            float circumcircle1 = circumcircle + aaOffset; // Account for AA.

            halfWidth -= rounded * MathF.Sqrt(3f);

            // Three sides plus the three corner arcs, walked on the pattern radius rather than the
            // rounding, with the triangle re-inset to keep those arcs tangent to the same edges.
            float eqOut = halfWidth / MathF.Sqrt(3f) + rounded; // Apothem of the outline itself.
            float eqPatternR = PatternRadius(rounded, thickness, eqOut * 0.5f);
            ResolvedDash rd = dash.Resolve(6f * MathF.Sqrt(3f) * (eqOut - eqPatternR) + MathF.Tau * eqPatternR, closed: true);

            var topLeft = center - new Vector2(halfWidth1, incircle1);
            var topRight = center + new Vector2(halfWidth1, -incircle1);
            var bottomRight = center + new Vector2(halfWidth1, circumcircle1);
            var bottomLeft = center + new Vector2(-halfWidth1, circumcircle1);

            if (rotation != 0f) {
                topLeft = Rotate(topLeft, center, rotation);
                topRight = Rotate(topRight, center, rotation);
                bottomRight = Rotate(bottomRight, center, rotation);
                bottomLeft = Rotate(bottomLeft, center, rotation);
            }

            GradientToWorld(ref fill, ref border, center, Vector2.Zero, rotation);

            if (thickness > 0f && IsTransparent(fill)) {
                float inradius = halfWidth / MathF.Sqrt(3f);
                float holeOffset = rounded - thickness - aaOffset - _pixelSize;
                if (inradius + MathF.Min(holeOffset, 0f) > _hollowMinHolePixels * _pixelSize) {
                    Span<Vector2> corners = stackalloc Vector2[] { new(-halfWidth, -inradius), new(halfWidth, -inradius), new(0f, 2f * inradius) };
                    Span<Vector2> outerCorners = stackalloc Vector2[3];
                    Span<Vector2> innerCorners = stackalloc Vector2[3];
                    float scaleOut = (inradius + rounded + aaOffset + _pixelSize) / inradius;
                    float scaleIn = (inradius + holeOffset) / inradius;
                    for (int i = 0; i < 3; i++) {
                        outerCorners[i] = corners[i] * scaleOut;
                        // Offsetting outward the hole corners must stay on the rounded band, offsetting inward the erosion is an exact scale.
                        innerCorners[i] = holeOffset <= 0f ? corners[i] * scaleIn : corners[i] + Vector2.Normalize(corners[i]) * holeOffset;
                    }
                    EmitHollowFrame(center, rotation, outerCorners, innerCorners, VertexShape.Shape.EquilateralTriangle, fill, border, thickness, halfWidth, 1f, aaSize, rounded, rd.Period, rd.FracPhase, 0f, 0f, rd.TypeDigit);
                    return;
                }
            }

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-halfWidth1, -incircle1), VertexShape.Shape.EquilateralTriangle, fill, border, thickness, halfWidth, GetClipSpace(topLeft), aaSize: aaSize, rounded: rounded, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(halfWidth1, -incircle1), VertexShape.Shape.EquilateralTriangle, fill, border, thickness, halfWidth, GetClipSpace(topRight), aaSize: aaSize, rounded: rounded, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(halfWidth1, circumcircle1), VertexShape.Shape.EquilateralTriangle, fill, border, thickness, halfWidth, GetClipSpace(bottomRight), aaSize: aaSize, rounded: rounded, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-halfWidth1, circumcircle1), VertexShape.Shape.EquilateralTriangle, fill, border, thickness, halfWidth, GetClipSpace(bottomLeft), aaSize: aaSize, rounded: rounded, a: rd.Period, b: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillEquilateralTriangle(Vector2 center, float radius, Gradient g, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f) {
            DrawEquilateralTriangle(center, radius, g, g, 0f, rounded, rotation, aaSize);
        }
        public void BorderEquilateralTriangle(Vector2 center, float radius, Gradient g, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f, DashStyle dash = default) {
            DrawEquilateralTriangle(center, radius, Color.Transparent, g, thickness, rounded, rotation, aaSize, dash);
        }

        public void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Gradient fill, Gradient border, float thickness = 1f, float rounded = 0f, float aaSize = 1.5f, DashStyle dash = default) {
            PrepareQuad();

            GradientToWorld(ref fill, ref border, a, Vector2.Zero, MathF.Atan2(b.Y - a.Y, b.X - a.X));

            UpdatePixelSize(a, b, c, 0f);
            float aaOffset = _pixelSize * aaSize;
            float winding = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (winding > 0) {
                (b, c) = (c, b);
            }

            float sideA = Vector2.Distance(a, b);
            float sideB = Vector2.Distance(b, c);
            float sideC = Vector2.Distance(c, a);

            float longestSide;

            Vector2 A;
            Vector2 B;
            Vector2 C;

            if (sideA > sideB && sideA > sideC) {
                longestSide = sideA;
                A = a;
                B = b;
            } else if (sideB > sideC) {
                longestSide = sideB;
                A = b;
                B = c;
            } else {
                longestSide = sideC;
                A = c;
                B = a;
            }

            float area = 0.5f * MathF.Abs(a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
            float height = 2f * area / longestSide;

            float offset = aaOffset;

            var D = Slide(B, A, offset);
            var E = Slide(A, B, offset);

            var topLeft = Clockwise(E, D, offset);
            var topRight = CounterClockwise(D, E, offset);
            var bottomRight = Clockwise(D, E, height + offset);
            var bottomLeft = CounterClockwise(E, D, height + offset);

            float inCenterX = (sideB * a.X + sideC * b.X + sideA * c.X) / (sideB + sideC + sideA);
            float inCenterY = (sideB * a.Y + sideC * b.Y + sideA * c.Y) / (sideB + sideC + sideA);
            float inRadius = MathF.Sqrt((-sideB + sideC + sideA) * (sideB - sideC + sideA) * (sideB + sideC - sideA) / (sideB + sideC + sideA)) / 2f;
            float ratioDistance = (inRadius - rounded) / inRadius;

            if (ratioDistance < 0.001f) {
                ratioDistance = 0.001f;
                rounded = inRadius - inRadius * ratioDistance;
            }

            A = new Vector2(inCenterX + (ratioDistance * (a.X - inCenterX)), inCenterY + (ratioDistance * (a.Y - inCenterY)));
            B = new Vector2(inCenterX + (ratioDistance * (b.X - inCenterX)), inCenterY + (ratioDistance * (b.Y - inCenterY)));
            C = new Vector2(inCenterX + (ratioDistance * (c.X - inCenterX)), inCenterY + (ratioDistance * (c.Y - inCenterY)));

            // Three sides of the shrunk triangle plus the corner arcs of the dilation, which sum
            // to a full turn. Dashed triangles put A at the local origin so the pattern can ride
            // in the two channels A normally occupies. The pattern walks corners wider than the
            // rounding, so the walk is over the triangle inset by that much again.
            Vector2 pA = A, pB = B, pC = C;
            float triPatternR = rounded;
            if (dash.IsEnabled) {
                float triInR = 2f * TriangleArea(A, B, C) / MathF.Max(Vector2.Distance(A, B) + Vector2.Distance(B, C) + Vector2.Distance(C, A), 1e-6f);
                triPatternR = PatternRadius(rounded, thickness, rounded + triInR * 0.5f);
                InsetTriangle(ref pA, ref pB, ref pC, triPatternR - rounded);
            }
            ResolvedDash rd = dash.Resolve(Vector2.Distance(pA, pB) + Vector2.Distance(pB, pC) + Vector2.Distance(pC, pA) + MathF.Tau * triPatternR, closed: true);
            Vector2 shift = rd.TypeDigit > 0 ? A : Vector2.Zero;
            float sdfA = rd.TypeDigit > 0 ? rd.Period : A.X;
            float sdfB = rd.TypeDigit > 0 ? rd.FracPhase : A.Y;

            if (thickness > 0f && IsTransparent(fill)) {
                float inradius = inRadius * ratioDistance;
                float holeOffset = rounded - thickness - aaOffset - _pixelSize;
                if (inradius + MathF.Min(holeOffset, 0f) > _hollowMinHolePixels * _pixelSize) {
                    Vector2 inCenter = new(inCenterX, inCenterY);
                    float scaleOut = (inradius + rounded + aaOffset + _pixelSize) / inradius;
                    float scaleIn = (inradius + holeOffset) / inradius;
                    Span<Vector2> corners = stackalloc Vector2[] { A, C, B }; // Reversed: a, b, c are stored counter clockwise on screen.
                    Span<Vector2> outerCorners = stackalloc Vector2[3];
                    Span<Vector2> innerCorners = stackalloc Vector2[3];
                    for (int i = 0; i < 3; i++) {
                        outerCorners[i] = inCenter + (corners[i] - inCenter) * scaleOut - shift;
                        // Offsetting outward the hole corners must stay on the rounded band, offsetting inward the erosion is an exact scale.
                        innerCorners[i] = (holeOffset <= 0f ? inCenter + (corners[i] - inCenter) * scaleIn : corners[i] + Vector2.Normalize(corners[i] - inCenter) * holeOffset) - shift;
                    }
                    // This shape's local coordinates are the world coordinates themselves, minus the
                    // dash shift; passing the shift as the frame origin keeps the world positions put.
                    EmitHollowFrame(shift, 0f, outerCorners, innerCorners, VertexShape.Shape.Triangle, fill, border, thickness, sdfA, sdfB, aaSize, rounded, B.X - shift.X, B.Y - shift.Y, C.X - shift.X, C.Y - shift.Y, rd.TypeDigit);
                    return;
                }
            }

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), topLeft - shift, VertexShape.Shape.Triangle, fill, border, thickness, sdfA, GetClipSpace(topLeft), height: sdfB, aaSize: aaSize, rounded: rounded, a: B.X - shift.X, b: B.Y - shift.Y, c: C.X - shift.X, d: C.Y - shift.Y, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), topRight - shift, VertexShape.Shape.Triangle, fill, border, thickness, sdfA, GetClipSpace(topRight), height: sdfB, aaSize: aaSize, rounded: rounded, a: B.X - shift.X, b: B.Y - shift.Y, c: C.X - shift.X, d: C.Y - shift.Y, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), bottomRight - shift, VertexShape.Shape.Triangle, fill, border, thickness, sdfA, GetClipSpace(bottomRight), height: sdfB, aaSize: aaSize, rounded: rounded, a: B.X - shift.X, b: B.Y - shift.Y, c: C.X - shift.X, d: C.Y - shift.Y, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), bottomLeft - shift, VertexShape.Shape.Triangle, fill, border, thickness, sdfA, GetClipSpace(bottomLeft), height: sdfB, aaSize: aaSize, rounded: rounded, a: B.X - shift.X, b: B.Y - shift.Y, c: C.X - shift.X, d: C.Y - shift.Y, colorSpace: ColorSpace, dash: rd.TypeDigit);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Gradient g, float rounded = 0f, float aaSize = 1.5f) {
            DrawTriangle(a, b, c, g, g, 0f, rounded, aaSize);
        }
        public void BorderTriangle(Vector2 a, Vector2 b, Vector2 c, Gradient g, float thickness = 1f, float rounded = 0f, float aaSize = 1.5f, DashStyle dash = default) {
            DrawTriangle(a, b, c, Color.Transparent, g, thickness, rounded, aaSize, dash);
        }

        public void DrawEllipse(Vector2 center, float radius1, float radius2, Gradient fill, Gradient border, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f) {
            UpdatePixelSize(center, MathF.Max(radius1, radius2));
            float aaOffset = _pixelSize * aaSize;

            if (thickness > 0f && IsTransparent(fill)) {
                float minAxis = MathF.Min(radius1, radius2);
                float holeMin = minAxis - thickness - aaOffset - _pixelSize;
                if (holeMin > _hollowMinHolePixels * _pixelSize) {
                    GradientToWorld(ref fill, ref border, center, Vector2.Zero, rotation);
                    Vector2 axes = new(radius1, radius2);
                    // Scaling instead of a fixed offset: the ellipse offset outward by s fits inside the
                    // ellipse scaled by 1 + s / minAxis, and the mirrored bound holds for the hole.
                    EmitHollowAnnulus(center, rotation, Vector2.Zero, 0f, MathF.PI, axes * (holeMin / minAxis), axes * (1f + (aaOffset + _pixelSize) / minAxis), true, VertexShape.Shape.Ellipse, fill, border, thickness, radius1, radius2, aaSize, 0f, 0f, 0f, 0f, 0f);
                    return;
                }
            }

            PrepareQuad();

            float radius3 = radius1 + aaOffset; // Account for AA.
            float radius4 = radius2 + aaOffset; // Account for AA.

            var topLeft = center + new Vector2(-radius3, -radius4);
            var topRight = center + new Vector2(radius3, -radius4);
            var bottomRight = center + new Vector2(radius3, radius4);
            var bottomLeft = center + new Vector2(-radius3, radius4);

            if (rotation != 0f) {
                topLeft = Rotate(topLeft, center, rotation);
                topRight = Rotate(topRight, center, rotation);
                bottomRight = Rotate(bottomRight, center, rotation);
                bottomLeft = Rotate(bottomLeft, center, rotation);
            }

            GradientToWorld(ref fill, ref border, center, Vector2.Zero, rotation);

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius3, -radius4), VertexShape.Shape.Ellipse, fill, border, thickness, radius1, GetClipSpace(topLeft), radius2, aaSize: aaSize, colorSpace: ColorSpace);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(radius3, -radius4), VertexShape.Shape.Ellipse, fill, border, thickness, radius1, GetClipSpace(topRight), radius2, aaSize: aaSize, colorSpace: ColorSpace);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(radius3, radius4), VertexShape.Shape.Ellipse, fill, border, thickness, radius1, GetClipSpace(bottomRight), radius2, aaSize: aaSize, colorSpace: ColorSpace);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius3, radius4), VertexShape.Shape.Ellipse, fill, border, thickness, radius1, GetClipSpace(bottomLeft), radius2, aaSize: aaSize, colorSpace: ColorSpace);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillEllipse(Vector2 center, float width, float height, Gradient g, float rotation = 0f, float aaSize = 1.5f) {
            DrawEllipse(center, width, height, g, g, 0f, rotation, aaSize);
        }
        public void BorderEllipse(Vector2 center, float width, float height, Gradient g, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f) {
            DrawEllipse(center, width, height, Color.Transparent, g, thickness, rotation, aaSize);
        }

        public void DrawArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient fill, Gradient border, float thickness = 1f, float aaSize = 1.5f, DashStyle dash = default) {
            PrepareQuad();

            radius1 -= 1f;

            float angleSize = MathF.Abs(Mod((angle2 - angle1) * 0.5f + MathF.PI, MathF.PI * 2f) - MathF.PI);
            float sin = MathF.Sin(angleSize);
            float cos = MathF.Cos(angleSize);

            // The arc is dashed along its centerline like a curved line, caps included.
            ResolvedDash rd = dash.Resolve(2f * angleSize * radius1, closed: false);
            float dashHeight = rd.TypeDigit > 0 ? rd.Period : 1f;

            UpdatePixelSize(center, radius1 + radius2);
            float aaOffset = _pixelSize * aaSize;
            float rotation = (angle1 + angle2 - MathF.PI) * 0.5f;

            float holeRadius = radius1 - radius2 - aaOffset - _pixelSize;
            if (holeRadius > _hollowMinHolePixels * _pixelSize) {
                GradientToWorld(ref fill, ref border, center, Vector2.Zero, angle1);
                // Round caps and their AA fringe swing at most this far past the end angles.
                float capMargin = MathF.Asin(MathF.Min((radius2 + aaOffset + _pixelSize) / holeRadius, 1f));
                float halfSpan = angleSize + capMargin;
                bool wrap = halfSpan >= MathF.PI;
                EmitHollowAnnulus(center, rotation, Vector2.Zero, 0f, wrap ? MathF.PI : halfSpan, new Vector2(holeRadius), new Vector2(radius1 + radius2 + aaOffset + _pixelSize), wrap, VertexShape.Shape.Arc, fill, border, thickness, radius1, dashHeight, aaSize, 0f, sin, cos, radius2, rd.FracPhase, rd.TypeDigit);
                return;
            }

            float radius3 = radius1 + radius2 + aaOffset; // Account for AA.

            var topLeft = center + new Vector2(-radius3);
            var topRight = center + new Vector2(radius3, -radius3);
            var bottomRight = center + new Vector2(radius3);
            var bottomLeft = center + new Vector2(-radius3, radius3);

            if (rotation != 0f) {
                topLeft = Rotate(topLeft, center, rotation);
                topRight = Rotate(topRight, center, rotation);
                bottomRight = Rotate(bottomRight, center, rotation);
                bottomLeft = Rotate(bottomLeft, center, rotation);
            }

            GradientToWorld(ref fill, ref border, center, Vector2.Zero, angle1);

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius3, -radius3), VertexShape.Shape.Arc, fill, border, thickness, radius1, GetClipSpace(topLeft), height: dashHeight, aaSize: aaSize, a: sin, b: cos, c: radius2, d: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(radius3, -radius3), VertexShape.Shape.Arc, fill, border, thickness, radius1, GetClipSpace(topRight), height: dashHeight, aaSize: aaSize, a: sin, b: cos, c: radius2, d: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(radius3, radius3), VertexShape.Shape.Arc, fill, border, thickness, radius1, GetClipSpace(bottomRight), height: dashHeight, aaSize: aaSize, a: sin, b: cos, c: radius2, d: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius3, radius3), VertexShape.Shape.Arc, fill, border, thickness, radius1, GetClipSpace(bottomLeft), height: dashHeight, aaSize: aaSize, a: sin, b: cos, c: radius2, d: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient g, float aaSize = 1.5f, DashStyle dash = default) {
            DrawArc(center, angle1, angle2, radius1, radius2, g, g, 0f, aaSize, dash);
        }
        public void BorderArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient g, float thickness = 1f, float aaSize = 1.5f, DashStyle dash = default) {
            DrawArc(center, angle1, angle2, radius1, radius2, Color.Transparent, g, thickness, aaSize, dash);
        }

        public void DrawRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient fill, Gradient border, float thickness = 1f, float aaSize = 1.5f, DashStyle dash = default) {
            PrepareQuad();

            radius1 -= 1f;

            float angleSize = MathF.Abs(Mod((angle2 - angle1) * 0.5f + MathF.PI, MathF.PI * 2f) - MathF.PI);

            float cos = MathF.Cos(angleSize);
            float sin = MathF.Sin(angleSize);

            // The ring band is dashed along its centerline; the dashes end flat like the ring itself.
            ResolvedDash rd = dash.Resolve(2f * angleSize * radius1, closed: false);
            float dashHeight = rd.TypeDigit > 0 ? rd.Period : 1f;

            UpdatePixelSize(center, radius1 + radius2);
            float aaOffset = _pixelSize * aaSize;
            float rotation = (angle1 + angle2 - MathF.PI) * 0.5f;

            // The ring band is radius2 / 2 thick on each side of the centerline, see RingSDF.
            float holeRadius = radius1 - radius2 * 0.5f - aaOffset - _pixelSize;
            if (holeRadius > _hollowMinHolePixels * _pixelSize) {
                GradientToWorld(ref fill, ref border, center, Vector2.Zero, angle1);
                // Flat caps only swing past the end angles by their AA fringe.
                float capMargin = MathF.Asin(MathF.Min((aaOffset + _pixelSize) / holeRadius, 1f));
                float halfSpan = angleSize + capMargin;
                bool wrap = halfSpan >= MathF.PI;
                EmitHollowAnnulus(center, rotation, Vector2.Zero, 0f, wrap ? MathF.PI : halfSpan, new Vector2(holeRadius), new Vector2(radius1 + radius2 * 0.5f + aaOffset + _pixelSize), wrap, VertexShape.Shape.Ring, fill, border, thickness, radius1, dashHeight, aaSize, 0f, cos, sin, radius2, rd.FracPhase, rd.TypeDigit);
                return;
            }

            float radius3 = radius1 + radius2 + aaOffset; // Account for AA.

            var topLeft = center + new Vector2(-radius3);
            var topRight = center + new Vector2(radius3, -radius3);
            var bottomRight = center + new Vector2(radius3);
            var bottomLeft = center + new Vector2(-radius3, radius3);

            if (rotation != 0f) {
                topLeft = Rotate(topLeft, center, rotation);
                topRight = Rotate(topRight, center, rotation);
                bottomRight = Rotate(bottomRight, center, rotation);
                bottomLeft = Rotate(bottomLeft, center, rotation);
            }

            GradientToWorld(ref fill, ref border, center, Vector2.Zero, angle1);

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius3, -radius3), VertexShape.Shape.Ring, fill, border, thickness, radius1, GetClipSpace(topLeft), height: dashHeight, aaSize: aaSize, a: cos, b: sin, c: radius2, d: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(radius3, -radius3), VertexShape.Shape.Ring, fill, border, thickness, radius1, GetClipSpace(topRight), height: dashHeight, aaSize: aaSize, a: cos, b: sin, c: radius2, d: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(radius3, radius3), VertexShape.Shape.Ring, fill, border, thickness, radius1, GetClipSpace(bottomRight), height: dashHeight, aaSize: aaSize, a: cos, b: sin, c: radius2, d: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius3, radius3), VertexShape.Shape.Ring, fill, border, thickness, radius1, GetClipSpace(bottomLeft), height: dashHeight, aaSize: aaSize, a: cos, b: sin, c: radius2, d: rd.FracPhase, colorSpace: ColorSpace, dash: rd.TypeDigit);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient g, float aaSize = 1.5f, DashStyle dash = default) {
            DrawRing(center, angle1, angle2, radius1, radius2, g, g, 0f, aaSize, dash);
        }
        public void BorderRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient g, float thickness = 1f, float aaSize = 1.5f, DashStyle dash = default) {
            DrawRing(center, angle1, angle2, radius1, radius2, Color.Transparent, g, thickness, aaSize, dash);
        }

        public void Draw(Texture2D texture, Matrix3x2 world, Matrix3x2? source = null, Color? mask = null) {
            if (_texture == null) {
                _texture = texture;
            } else if (_texture != texture) {
                Flush();
                _texture = texture;
            }

            PrepareQuad();

            Vector2 topLeft;
            Vector2 topRight;
            Vector2 bottomRight;
            Vector2 bottomLeft;
            if (source == null) {
                topLeft = new Vector2(0, 0);
                topRight = new Vector2(texture.Width, 0);
                bottomRight = new Vector2(texture.Width, texture.Height);
                bottomLeft = new Vector2(0, texture.Height);
            } else {
                topLeft = Vector2.Transform(new Vector2(0f, 0f), source.Value);
                topRight = Vector2.Transform(new Vector2(1f, 0f), source.Value);
                bottomRight = Vector2.Transform(new Vector2(1f, 1f), source.Value);
                bottomLeft = Vector2.Transform(new Vector2(0, 1f), source.Value);
            }

            Vector2 wTopLeft = Vector2.Transform(new Vector2(0, 0), world);
            Vector2 wTopRight = Vector2.Transform(new Vector2(1f, 0), world);
            Vector2 wBottomRight = Vector2.Transform(new Vector2(1f, 1f), world);
            Vector2 wBottomLeft = Vector2.Transform(new Vector2(0, 1f), world);

            Gradient g = new(Vector2.Zero, mask ?? Color.White, Vector2.Zero, mask ?? Color.White, Gradient.Shape.None);

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(wTopLeft.X, wTopLeft.Y, 0f), GetUV(texture, topLeft), VertexShape.Shape.Texture, g, g, 0f, 1f, GetClipSpace(wTopLeft));
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(wTopRight.X, wTopRight.Y, 0f), GetUV(texture, topRight), VertexShape.Shape.Texture, g, g, 0f, 1f, GetClipSpace(wTopRight));
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(wBottomRight.X, wBottomRight.Y, 0f), GetUV(texture, bottomRight), VertexShape.Shape.Texture, g, g, 0f, 1f, GetClipSpace(wBottomRight));
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(wBottomLeft.X, wBottomLeft.Y, 0f), GetUV(texture, bottomLeft), VertexShape.Shape.Texture, g, g, 0f, 1f, GetClipSpace(wBottomLeft));

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void Draw(Texture2D texture, Vector2 xy) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(xy));
        }
        public void Draw(Texture2D texture, Vector2 xy, Color mask) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(xy), mask: mask);
        }
        public void Draw(Texture2D texture, Vector2 xy, RectangleF source, Color mask) {
            Draw(texture, Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(xy), Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position), mask: mask);
        }
        public void Draw(Texture2D texture, Vector2 xy, Color mask, float rotation, Vector2 origin, Vector2 scale) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(xy), Matrix3x2.CreateScale(texture.Width, texture.Height), mask: mask);
        }
        public void Draw(Texture2D texture, Vector2 xy, Color mask, float rotation, Vector2 origin, float scale) {
            Draw(texture, xy, mask, rotation, origin, new Vector2(scale));
        }
        public void Draw(Texture2D texture, Vector2 xy, RectangleF source, Color mask, float rotation, Vector2 origin, Vector2 scale) {
            Draw(texture, Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(xy), Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position), mask: mask);
        }
        public void Draw(Texture2D texture, Vector2 xy, RectangleF source, Color mask, float rotation, Vector2 origin, float scale) {
            Draw(texture, xy, source, mask, rotation, origin, new Vector2(scale));
        }
        public void Draw(Texture2D texture, Vector2 xy, Color mask, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(xy), (effects & (SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically)) != 0 ? Matrix3x2.CreateScale(1f) * Matrix3x2.CreateTranslation(-0.5f, -0.5f) * Matrix3x2.CreateScale((effects & SpriteEffects.FlipHorizontally) != 0 ? -1f : 1f, (effects & SpriteEffects.FlipVertically) != 0 ? -1f : 1f) * Matrix3x2.CreateTranslation(0.5f, 0.5f) * Matrix3x2.CreateScale(texture.Width, texture.Height) : Matrix3x2.CreateScale(texture.Width, texture.Height), mask: mask);
        }
        public void Draw(Texture2D texture, Vector2 xy, Color mask, float rotation, Vector2 origin, float scale, SpriteEffects effects) {
            Draw(texture, xy, mask, rotation, origin, new Vector2(scale), effects);
        }
        public void Draw(Texture2D texture, Vector2 xy, RectangleF source, Color mask, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects) {
            Draw(texture, Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(xy), (effects & (SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically)) != 0 ? Matrix3x2.CreateScale(1f) * Matrix3x2.CreateTranslation(-0.5f, -0.5f) * Matrix3x2.CreateScale((effects & SpriteEffects.FlipHorizontally) != 0 ? -1f : 1f, (effects & SpriteEffects.FlipVertically) != 0 ? -1f : 1f) * Matrix3x2.CreateTranslation(0.5f, 0.5f) * Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position) : Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position), mask: mask);
        }
        public void Draw(Texture2D texture, Vector2 xy, RectangleF source, Color mask, float rotation, Vector2 origin, float scale, SpriteEffects effects) {
            Draw(texture, xy, source, mask, rotation, origin, new Vector2(scale), effects);
        }
        public void Draw(Texture2D texture, RectangleF destination) {
            Draw(texture, Matrix3x2.CreateScale(destination.Width, destination.Height) * Matrix3x2.CreateTranslation(destination.Position));
        }
        public void Draw(Texture2D texture, RectangleF destination, Color mask) {
            Draw(texture, Matrix3x2.CreateScale(destination.Width, destination.Height) * Matrix3x2.CreateTranslation(destination.Position), mask: mask);
        }
        public void Draw(Texture2D texture, RectangleF destination, RectangleF source, Color mask) {
            Draw(texture, Matrix3x2.CreateScale(destination.Width, destination.Height) * Matrix3x2.CreateTranslation(destination.Position), Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position), mask: mask);
        }
        public void Draw(Texture2D texture, RectangleF destination, Color mask, float rotation, Vector2 origin) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(destination.Width / texture.Width, destination.Height / texture.Height) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(destination.Position), Matrix3x2.CreateScale(texture.Width, texture.Height), mask: mask);
        }
        public void Draw(Texture2D texture, RectangleF destination, RectangleF source, Color mask, float rotation, Vector2 origin) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(destination.Width / texture.Width, destination.Height / texture.Height) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(destination.Position), Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position), mask: mask);
        }
        public void Draw(Texture2D texture, RectangleF destination, Color mask, float rotation, Vector2 origin, SpriteEffects effects) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(destination.Width / texture.Width, destination.Height / texture.Height) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(destination.Position), (effects & (SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically)) != 0 ? Matrix3x2.CreateScale(1f) * Matrix3x2.CreateTranslation(-0.5f, -0.5f) * Matrix3x2.CreateScale((effects & SpriteEffects.FlipHorizontally) != 0 ? -1f : 1f, (effects & SpriteEffects.FlipVertically) != 0 ? -1f : 1f) * Matrix3x2.CreateTranslation(0.5f, 0.5f) * Matrix3x2.CreateScale(texture.Width, texture.Height) : Matrix3x2.CreateScale(texture.Width, texture.Height), mask: mask);
        }
        public void Draw(Texture2D texture, RectangleF destination, RectangleF source, Color mask, float rotation, Vector2 origin, SpriteEffects effects) {
            Draw(texture, Matrix3x2.CreateScale(texture.Width, texture.Height) * Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(destination.Width / texture.Width, destination.Height / texture.Height) * Matrix3x2.CreateRotationZ(rotation) * Matrix3x2.CreateTranslation(destination.Position), (effects & (SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically)) != 0 ? Matrix3x2.CreateScale(1f) * Matrix3x2.CreateTranslation(-0.5f, -0.5f) * Matrix3x2.CreateScale((effects & SpriteEffects.FlipHorizontally) != 0 ? -1f : 1f, (effects & SpriteEffects.FlipVertically) != 0 ? -1f : 1f) * Matrix3x2.CreateTranslation(0.5f, 0.5f) * Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position) : Matrix3x2.CreateScale(source.Width, source.Height) * Matrix3x2.CreateTranslation(source.Position), mask: mask);
        }

        public float DrawString(SpriteFontBase font, string text, Vector2 position, Color color, float rotation = 0, Vector2 origin = default, Vector2? scale = null, float layerDepth = 0.0f, float characterSpacing = 0.0f, float lineSpacing = 0.0f, TextStyle textStyle = TextStyle.None, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) {
            return font.DrawText(_fsr, text, position, color, rotation, origin, scale, layerDepth, characterSpacing, lineSpacing, textStyle, effect, effectAmount);
        }
        public float DrawString(SpriteFontBase font, string text, Vector2 position, Color[] colors, float rotation = 0, Vector2 origin = default, Vector2? scale = null, float layerDepth = 0.0f, float characterSpacing = 0.0f, float lineSpacing = 0.0f, TextStyle textStyle = TextStyle.None, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) {
            return font.DrawText(_fsr, text, position, colors, rotation, origin, scale, layerDepth, characterSpacing, lineSpacing, textStyle, effect, effectAmount);
        }
        public float DrawString(SpriteFontBase font, StringSegment text, Vector2 position, Color color, float rotation = 0, Vector2 origin = default, Vector2? scale = null, float layerDepth = 0.0f, float characterSpacing = 0.0f, float lineSpacing = 0.0f, TextStyle textStyle = TextStyle.None, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) {
            return font.DrawText(_fsr, text, position, color, rotation, origin, scale, layerDepth, characterSpacing, lineSpacing, textStyle, effect, effectAmount);
        }
        public float DrawString(SpriteFontBase font, StringSegment text, Vector2 position, Color[] colors, float rotation = 0, Vector2 origin = default, Vector2? scale = null, float layerDepth = 0.0f, float characterSpacing = 0.0f, float lineSpacing = 0.0f, TextStyle textStyle = TextStyle.None, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) {
            return font.DrawText(_fsr, text, position, colors, rotation, origin, scale, layerDepth, characterSpacing, lineSpacing, textStyle, effect, effectAmount);
        }
        public float DrawString(SpriteFontBase font, StringBuilder text, Vector2 position, Color color, float rotation = 0, Vector2 origin = default, Vector2? scale = null, float layerDepth = 0.0f, float characterSpacing = 0.0f, float lineSpacing = 0.0f, TextStyle textStyle = TextStyle.None, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) {
            return font.DrawText(_fsr, text, position, color, rotation, origin, scale, layerDepth, characterSpacing, lineSpacing, textStyle, effect, effectAmount);
        }
        public float DrawString(SpriteFontBase font, StringBuilder text, Vector2 position, Color[] colors, float rotation = 0, Vector2 origin = default, Vector2? scale = null, float layerDepth = 0.0f, float characterSpacing = 0.0f, float lineSpacing = 0.0f, TextStyle textStyle = TextStyle.None, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) {
            return font.DrawText(_fsr, text, position, colors, rotation, origin, scale, layerDepth, characterSpacing, lineSpacing, textStyle, effect, effectAmount);
        }

        /// <summary>
        /// Clips upcoming draws to the given rectangle without breaking the batch. Pass null to stop clipping.
        /// </summary>
        /// <param name="clipRect">The clip bounds in the same coordinate space as the shapes.</param>
        /// <param name="rounding">Corner radius of the clip rectangle. A fully rounded square gives a circle mask.</param>
        /// <param name="rotation">Rotation of the clip rectangle around its center in radians.</param>
        /// <param name="aaSize">Antialiasing band width of the clip edge in pixels. 0 gives a hard scissor edge.</param>
        public void SetClipRect(RectangleF? clipRect, float rounding = 0f, float rotation = 0f, float aaSize = 1.5f) {
            if (clipRect == null) {
                _hasClip = false;
                return;
            }
            RectangleF r = clipRect.Value;
            _hasClip = true;
            _clipCenter = new Vector2(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            _clipHalf = new Vector2(r.Width / 2f, r.Height / 2f);
            _clipRounding = MathHelper.Clamp(rounding, 0f, MathF.Min(_clipHalf.X, _clipHalf.Y));
            _clipU = rotation != 0f ? new Vector2(MathF.Cos(rotation), MathF.Sin(rotation)) : Vector2.UnitX;
            _clipAaSize = MathF.Max(aaSize, 0f);
        }

        private void DrawStringTexture(Texture2D texture, ref VertexPositionColorTexture topLeft, ref VertexPositionColorTexture topRight, ref VertexPositionColorTexture bottomLeft, ref VertexPositionColorTexture bottomRight) {
            if (_fontTexture == null) {
                _fontTexture = texture;
            } else if (_fontTexture != texture) {
                Flush();
                _fontTexture = texture;
            }

            PrepareQuad();

            Gradient gTopLeft = new(Vector2.Zero, topLeft.Color, Vector2.Zero, topLeft.Color, Gradient.Shape.None);
            Gradient gTopRight = new(Vector2.Zero, topRight.Color, Vector2.Zero, topRight.Color, Gradient.Shape.None);
            Gradient gBottomRight = new(Vector2.Zero, bottomRight.Color, Vector2.Zero, bottomRight.Color, Gradient.Shape.None);
            Gradient gBottomLeft = new(Vector2.Zero, bottomLeft.Color, Vector2.Zero, bottomLeft.Color, Gradient.Shape.None);

            _vertices[_vertexCount + 0] = new VertexShape(topLeft.Position, topLeft.TextureCoordinate, VertexShape.Shape.String, gTopLeft, gTopLeft, 0f, 1f, GetClipSpace(new Vector2(topLeft.Position.X, topLeft.Position.Y)));
            _vertices[_vertexCount + 1] = new VertexShape(topRight.Position, topRight.TextureCoordinate, VertexShape.Shape.String, gTopRight, gTopRight, 0f, 1f, GetClipSpace(new Vector2(topRight.Position.X, topRight.Position.Y)));
            _vertices[_vertexCount + 2] = new VertexShape(bottomRight.Position, bottomRight.TextureCoordinate, VertexShape.Shape.String, gBottomRight, gBottomRight, 0f, 1f, GetClipSpace(new Vector2(bottomRight.Position.X, bottomRight.Position.Y)));
            _vertices[_vertexCount + 3] = new VertexShape(bottomLeft.Position, bottomLeft.TextureCoordinate, VertexShape.Shape.String, gBottomLeft, gBottomLeft, 0f, 1f, GetClipSpace(new Vector2(bottomLeft.Position.X, bottomLeft.Position.Y)));

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }

        public void End() {
            if (!_beginCalled) {
                throw new InvalidOperationException("Begin must be called before calling End.");
            }
            if (_pathOpen) {
                throw new InvalidOperationException("EndPath or ClosePath must be called before calling End.");
            }
            _beginCalled = false;

            Flush();

            // TODO: Restore old states like rasterizer, depth stencil, blend state?
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing) {
            if (_disposed) return;
            if (disposing) {
                _vertexBuffer.Dispose();
                _indexBuffer.Dispose();
                _blueNoise.Dispose();
            }
            _disposed = true;
        }

        private void Flush() {
            if (_triangleCount == 0) return;

            _viewProjection.SetValue(_view * _projection);
            _halfViewportParam?.SetValue(_halfViewport);
            _ditherScale?.SetValue(DitherStrength / 255f);
            _ditherMode?.SetValue(DitherNoiseSource == DitherNoise.BlueNoise ? 1f : 0f);

            if (_indicesChanged) {
                _vertexBuffer.Dispose();
                _indexBuffer.Dispose();

                _vertexBuffer = new DynamicVertexBuffer(_graphicsDevice, typeof(VertexShape), _vertices.Length, BufferUsage.WriteOnly);

                GenerateIndexArray();

                _indexBuffer = new IndexBuffer(_graphicsDevice, typeof(uint), _indices.Length, BufferUsage.WriteOnly);
                _indexBuffer.SetData(_indices);

                _indicesChanged = false;
            }

            _vertexBuffer.SetData(_vertices, 0, _vertexCount, SetDataOptions.Discard);
            _graphicsDevice.SetVertexBuffer(_vertexBuffer);

            _graphicsDevice.Indices = _indexBuffer;

            _graphicsDevice.BlendState = _blendState;
            _graphicsDevice.SamplerStates[0] = _samplerState;
            _graphicsDevice.DepthStencilState = _depthStencilState;
            _graphicsDevice.RasterizerState = _rasterizerState;

            foreach (EffectPass pass in _effect.CurrentTechnique.Passes) {
                pass.Apply();
                if (_texture != null) _graphicsDevice.Textures[0] = _texture;
                if (_fontTexture != null) _graphicsDevice.Textures[1] = _fontTexture;
                _graphicsDevice.Textures[2] = _blueNoise;
                _graphicsDevice.SamplerStates[2] = SamplerState.PointWrap;

                _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _triangleCount);
            }

            _triangleCount = 0;
            _vertexCount = 0;
            _indexCount = 0;
        }
        // World units per pixel at a point on the z = 0 plane: the largest singular value
        // of the screen→world Jacobian of the view, projection and viewport chain. It is a
        // conservative bound used to grow quads around the AA band and to inset hollow
        // holes; the shader derives the actual per-pixel AA width itself.
        private float PixelSizeAt(Vector2 p) {
            float cx = p.X * _worldToClip.M11 + p.Y * _worldToClip.M21 + _worldToClip.M41;
            float cy = p.X * _worldToClip.M12 + p.Y * _worldToClip.M22 + _worldToClip.M42;
            float cw = p.X * _worldToClip.M14 + p.Y * _worldToClip.M24 + _worldToClip.M44;
            float iw2 = 1f / MathF.Max(cw * cw, 1e-12f);
            // World→screen Jacobian. The screen y flip is dropped, only magnitudes matter.
            float jxx = (_worldToClip.M11 * cw - cx * _worldToClip.M14) * iw2 * _halfViewport.X;
            float jxy = (_worldToClip.M21 * cw - cx * _worldToClip.M24) * iw2 * _halfViewport.X;
            float jyx = (_worldToClip.M12 * cw - cy * _worldToClip.M14) * iw2 * _halfViewport.Y;
            float jyy = (_worldToClip.M22 * cw - cy * _worldToClip.M24) * iw2 * _halfViewport.Y;
            float a = jxx * jxx + jxy * jxy + jyx * jyx + jyy * jyy;
            float det = MathF.Abs(jxx * jyy - jxy * jyx);
            if (det <= 1e-20f) return _pixelSize; // Degenerate view, keep the previous estimate.
            float sMax = MathF.Sqrt(0.5f * (a + MathF.Sqrt(MathF.Max(a * a - 4f * det * det, 0f))));
            return sMax / det; // 1 / σmin of world→screen = σmax of screen→world.
        }

        // Under perspective the pixel size varies across the plane, so each draw call
        // resamples around its own shape and keeps the worst case for quad expansion.
        // Clip w is linear over the plane, so boundary samples dominate interior ones.
        private void UpdatePixelSize(Vector2 center, float extent) {
            if (!_isPerspective) return;
            _pixelSize = SamplePixelSize(center, extent);
        }
        private void UpdatePixelSize(Vector2 a, Vector2 b, float extent) {
            if (!_isPerspective) return;
            _pixelSize = MathF.Max(SamplePixelSize(a, extent), SamplePixelSize(b, extent));
        }
        private void UpdatePixelSize(Vector2 a, Vector2 b, Vector2 c, float extent) {
            if (!_isPerspective) return;
            _pixelSize = MathF.Max(MathF.Max(SamplePixelSize(a, extent), SamplePixelSize(b, extent)), SamplePixelSize(c, extent));
        }
        private float SamplePixelSize(Vector2 center, float extent) {
            float p = PixelSizeAt(center);
            if (extent <= 0f) return p;
            float d = extent * 0.70710678f;
            p = MathF.Max(p, PixelSizeAt(center + new Vector2(extent, 0f)));
            p = MathF.Max(p, PixelSizeAt(center - new Vector2(extent, 0f)));
            p = MathF.Max(p, PixelSizeAt(center + new Vector2(0f, extent)));
            p = MathF.Max(p, PixelSizeAt(center - new Vector2(0f, extent)));
            p = MathF.Max(p, PixelSizeAt(center + new Vector2(d, d)));
            p = MathF.Max(p, PixelSizeAt(center - new Vector2(d, d)));
            p = MathF.Max(p, PixelSizeAt(center + new Vector2(d, -d)));
            p = MathF.Max(p, PixelSizeAt(center + new Vector2(-d, d)));
            return p;
        }

        private static Vector2 Slide(Vector2 a, Vector2 b, float distance) {
            var c = Vector2.Normalize(b - a) * distance;
            return b + c;
        }
        private static Vector2 Clockwise(Vector2 a, Vector2 b, float distance) {
            var c = Vector2.Normalize(b - a) * distance;
            return new Vector2(c.Y, -c.X) + a;
        }
        private static Vector2 CounterClockwise(Vector2 a, Vector2 b, float distance) {
            var c = Vector2.Normalize(b - a) * distance;
            return new Vector2(-c.Y, c.X) + a;
        }
        private static Vector2 Rotate(Vector2 a, Vector2 origin, float rotation) {
            return new Vector2(origin.X + (a.X - origin.X) * MathF.Cos(rotation) - (a.Y - origin.Y) * MathF.Sin(rotation), origin.Y + (a.X - origin.X) * MathF.Sin(rotation) + (a.Y - origin.Y) * MathF.Cos(rotation));
        }
        private static float Mod(float x, float m) {
            return (x % m + m) % m;
        }
        private static void GradientToWorld(ref Gradient g1, ref Gradient g2, Vector2 center, Vector2 offset, float rotation) {
            if (g1.IsLocal) GradientToWorld(ref g1, center, offset, rotation);
            if (g2.IsLocal) GradientToWorld(ref g2, center, offset, rotation);
        }
        private static void GradientToWorld(ref Gradient g, Vector2 center, Vector2 offset, float rotation) {
            g.AXY = Rotate(g.AXY + offset, Vector2.Zero, rotation);
            g.BXY = Rotate(g.BXY + offset, Vector2.Zero, rotation);

            g.AXY += center;
            g.BXY += center;
        }

        private static Vector2 GetUV(Texture2D texture, Vector2 xy) {
            return new Vector2(xy.X / texture.Width, xy.Y / texture.Height);
        }

        private void PrepareQuad() {
            if (!_beginCalled) {
                throw new InvalidOperationException("Begin must be called before drawing.");
            }
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;
        }

        // Hollow shapes (rings, arcs, transparent fills with a border) only shade a thin band, so they
        // get a mesh that skips the interior instead of one big quad. The pixel shader reads the SDF
        // from linearly interpolated local coordinates, so any mesh renders identically as long as each
        // vertex carries the local coordinate that matches its world position. Only worth doing once
        // the skipped interior is large on screen: the mesh multiplies vertex count.
        private const float _hollowMinHolePixels = 32f;
        // Chords may overshoot the band by up to this sagitta; sector counts scale with radius to hold it.
        private const float _hollowMaxSagPixels = 2f;
        private const int _hollowMaxSectors = 128;

        private static bool IsTransparent(in Gradient g) {
            return g.AC.A == 0 && g.BC.A == 0;
        }

        private void EmitHollowQuad(Vector2 w0, Vector2 w1, Vector2 w2, Vector2 w3, Vector2 l0, Vector2 l1, Vector2 l2, Vector2 l3, VertexShape.Shape shape, Gradient fill, Gradient border, float thickness, float sdfSize, float height, float aaSize, float rounded, float a, float b, float c, float d, int dash = 0) {
            PrepareQuad();

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(w0, 0), l0, shape, fill, border, thickness, sdfSize, GetClipSpace(w0), height, aaSize: aaSize, rounded: rounded, a: a, b: b, c: c, d: d, colorSpace: ColorSpace, dash: dash);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(w1, 0), l1, shape, fill, border, thickness, sdfSize, GetClipSpace(w1), height, aaSize: aaSize, rounded: rounded, a: a, b: b, c: c, d: d, colorSpace: ColorSpace, dash: dash);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(w2, 0), l2, shape, fill, border, thickness, sdfSize, GetClipSpace(w2), height, aaSize: aaSize, rounded: rounded, a: a, b: b, c: c, d: d, colorSpace: ColorSpace, dash: dash);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(w3, 0), l3, shape, fill, border, thickness, sdfSize, GetClipSpace(w3), height, aaSize: aaSize, rounded: rounded, a: a, b: b, c: c, d: d, colorSpace: ColorSpace, dash: dash);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }

        // Covers the band between two concentric (possibly elliptical) rings with a fan of quads.
        // Angles are measured from the local +y axis to line up with the arc and ring SDFs. Outer
        // vertices circumscribe the band so the chords never clip it; inner vertices sit on the hole
        // boundary so their chords only dip into the hole. Quads are wound clockwise on screen.
        private void EmitHollowAnnulus(Vector2 origin, float rotation, Vector2 localCenter, float angleCenter, float halfSpan, Vector2 axesInner, Vector2 axesOuter, bool wrap, VertexShape.Shape shape, Gradient fill, Gradient border, float thickness, float sdfSize, float height, float aaSize, float rounded, float a, float b, float c, float d, int dash = 0) {
            float rMax = MathF.Max(axesOuter.X, axesOuter.Y);
            float phi = MathF.Acos(MathF.Max(1f - _hollowMaxSagPixels * _pixelSize / rMax, 0f));
            int n = Math.Clamp((int)MathF.Ceiling(halfSpan / MathF.Max(phi, 1e-4f)), 3, _hollowMaxSectors);
            float step = 2f * halfSpan / n;
            float overshoot = 1f / MathF.Cos(step * 0.5f);

            (float sinR, float cosR) = MathF.SinCos(rotation);
            Vector2 W(Vector2 l) => origin + new Vector2(l.X * cosR - l.Y * sinR, l.X * sinR + l.Y * cosR);

            (float sinT, float cosT) = MathF.SinCos(angleCenter - halfSpan);
            Vector2 first = new(sinT, cosT);
            Vector2 outerPrev = localCenter + axesOuter * first * overshoot;
            Vector2 innerPrev = localCenter + axesInner * first;
            for (int i = 1; i <= n; i++) {
                Vector2 dir;
                if (i < n) {
                    (sinT, cosT) = MathF.SinCos(angleCenter - halfSpan + i * step);
                    dir = new Vector2(sinT, cosT);
                } else if (wrap) {
                    dir = first; // Exact repeat keeps the wrap seam watertight.
                } else {
                    (sinT, cosT) = MathF.SinCos(angleCenter + halfSpan);
                    dir = new Vector2(sinT, cosT);
                }

                Vector2 outerCur = localCenter + axesOuter * dir * overshoot;
                Vector2 innerCur = localCenter + axesInner * dir;

                EmitHollowQuad(W(outerCur), W(outerPrev), W(innerPrev), W(innerCur), outerCur, outerPrev, innerPrev, innerCur, shape, fill, border, thickness, sdfSize, height, aaSize, rounded, a, b, c, d, dash);

                outerPrev = outerCur;
                innerPrev = innerCur;
            }
        }

        private enum PathJointMode : byte {
            Partition = 0, // Both quads end on the shared inner miter point: exact single-blend seam.
            Overlap = 1,   // Segment too short for the miter: slabs run to their full corners and overlap.
            Reversal = 2   // Bisector degenerates: both segments get overlapping round caps.
        }
        private struct PathJoint {
            public PathJointMode Mode;
            public PathJoin Join;   // Effective join; non-round styles fall back to Round outside Partition mode.
            public float Sign;      // Which side of the incoming segment the inner miter is on.
            public Vector2 BIn;     // Inner miter point, on both offset lines (Partition mode only).
            public Vector2 MOut;    // Outer miter tip, or the bevel cut's far corner on the bisector.
            public Vector2 E1Prev;  // Outer wall corner of the incoming segment; first fan vertex.
            public Vector2 E1Next;  // Outer wall corner of the outgoing segment; last fan vertex.
            public float Theta;     // Turn angle of the joint.
            public float CHalf;     // Cosine and sine of half the turn angle.
            public float SHalf;
            public int Chords;
            public float Overshoot; // Chord vertices circumscribe the join arc by this factor.
            public float ThetaCode; // Signed turn angle as the shader's 11 bit code, 1024 = 0. Dashed only.
            public float DashRadiusCode; // Fillet radius as a 7 bit code over [radius, 2 * radius]. Dashed only.
            public float DashArc;   // Pattern length the joint's fan consumes. Dashed only.
        }

        // Mirrors the shader's PatternRadius: the radius the dash pattern walks a corner on, which
        // runs wider than the shape's rounding so the arc center every cut in that corner converges
        // on lands past the border band's inner edge instead of inside it.
        private static float PatternRadius(float rounded, float thickness, float cap) {
            return MathF.Max(rounded, MathF.Min(1.5f * thickness, cap));
        }

        private static float TriangleArea(Vector2 a, Vector2 b, Vector2 c) {
            return 0.5f * MathF.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X));
        }

        // Pushes every edge inward by delta, sliding each vertex along its miter. Mirrors the
        // shader's MiterShift; parallel inset leaves the edge directions, and so the corner arc
        // spans, exactly as they were.
        private static void InsetTriangle(ref Vector2 a, ref Vector2 b, ref Vector2 c, float delta) {
            if (delta <= 0f) return;
            float orr = ((b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X)) >= 0f ? 1f : -1f;
            Vector2 g0 = Vector2.Normalize(b - a);
            Vector2 g1 = Vector2.Normalize(c - b);
            Vector2 g2 = Vector2.Normalize(a - c);
            Vector2 n0 = orr * new Vector2(g0.Y, -g0.X);
            Vector2 n1 = orr * new Vector2(g1.Y, -g1.X);
            Vector2 n2 = orr * new Vector2(g2.Y, -g2.X);
            Vector2 Shift(Vector2 nIn, Vector2 nOut) {
                Vector2 s = nIn + nOut;
                return -s * (delta / MathF.Max(1f + Vector2.Dot(nIn, nOut), 1e-3f));
            }
            Vector2 sa = Shift(n2, n0);
            Vector2 sb = Shift(n0, n1);
            Vector2 sc = Shift(n1, n2);
            a += sa;
            b += sb;
            c += sc;
        }

        // The first and last vertices reuse the stored wall corners so the fan shares them bitwise with
        // the segment quads, mirroring the exact-repeat trick in EmitHollowAnnulus.
        private static Vector2 PathFanVertex(in PathJoint jd, Vector2 joint, float baseAngle, float step, float reach, int t) {
            if (t == 0) return jd.E1Prev;
            if (t == jd.Chords) return jd.E1Next;
            (float sin, float cos) = MathF.SinCos(baseAngle + step * t);
            return joint + new Vector2(cos, sin) * reach;
        }

        // A path quad's local coordinates live in its segment's frame: x along the segment, y across it.
        // Meta2 carries the packed end modes and the bevel plane angles for StrokeSDF. Dashed paths
        // instead pack each end's signed turn angle as two 11 bit codes, from which the shader derives
        // the bevel planes, to make room for the segment's start length and the period, and put the
        // fraction and phase in the unused rounding slot. Each end's fillet radius code rides above
        // the end modes, which only need six bits, keeping that value well under 2^22.
        private void EmitPathQuad(Vector2 origin, Vector2 u, Vector2 nrm, float len, Vector2 w0, Vector2 w1, Vector2 w2, Vector2 w3, in Gradient fill, in Gradient border, float thickness, float radius, float aaSize, float modes, float angStart, float angEnd, float thetaCodeStart, float thetaCodeEnd, float radiusCodeStart, float radiusCodeEnd, float startLen, in ResolvedDash rd) {
            Vector2 Local(Vector2 w) {
                Vector2 r = w - origin;
                return new Vector2(Vector2.Dot(r, u), Vector2.Dot(r, nrm));
            }
            if (rd.TypeDigit > 0) {
                float angs = thetaCodeStart * 2048f + thetaCodeEnd;
                float packed = modes + 64f * (radiusCodeStart + 128f * radiusCodeEnd);
                EmitHollowQuad(w0, w1, w2, w3, Local(w0), Local(w1), Local(w2), Local(w3), VertexShape.Shape.Path, fill, border, thickness, radius, len, aaSize, rd.FracPhase, packed, angs, startLen, rd.Period, rd.TypeDigit);
            } else {
                EmitHollowQuad(w0, w1, w2, w3, Local(w0), Local(w1), Local(w2), Local(w3), VertexShape.Shape.Path, fill, border, thickness, radius, len, aaSize, 0f, modes, angStart, angEnd, 0f);
            }
        }

        // One quad per edge between two nested convex polygons with matching corner counts. Adjacent
        // quads share their mitre edges exactly so the frame is watertight. Corners must be listed
        // clockwise on screen.
        private void EmitHollowFrame(Vector2 origin, float rotation, ReadOnlySpan<Vector2> outer, ReadOnlySpan<Vector2> inner, VertexShape.Shape shape, Gradient fill, Gradient border, float thickness, float sdfSize, float height, float aaSize, float rounded, float a, float b, float c, float d, int dash = 0) {
            (float sinR, float cosR) = MathF.SinCos(rotation);
            Vector2 W(Vector2 l) => origin + new Vector2(l.X * cosR - l.Y * sinR, l.X * sinR + l.Y * cosR);

            for (int i = 0; i < outer.Length; i++) {
                int j = i + 1 == outer.Length ? 0 : i + 1;
                EmitHollowQuad(W(outer[i]), W(outer[j]), W(inner[j]), W(inner[i]), outer[i], outer[j], inner[j], inner[i], shape, fill, border, thickness, sdfSize, height, aaSize, rounded, a, b, c, d, dash);
            }
        }

        private static void HexagonCorners(float apothem, Span<Vector2> corners) {
            float circumradius = 2f * apothem / MathF.Sqrt(3f);
            float half = apothem / MathF.Sqrt(3f);
            corners[0] = new Vector2(circumradius, 0f);
            corners[1] = new Vector2(half, apothem);
            corners[2] = new Vector2(-half, apothem);
            corners[3] = new Vector2(-circumradius, 0f);
            corners[4] = new Vector2(-half, -apothem);
            corners[5] = new Vector2(half, -apothem);
        }

        private ClipSpace GetClipSpace(Vector2 xy) {
            if (!_hasClip) return ClipSpace.None;

            Vector2 d = xy - _clipCenter;
            float lx = d.X * _clipU.X + d.Y * _clipU.Y;
            float ly = d.Y * _clipU.X - d.X * _clipU.Y;

            return new ClipSpace(new Vector4(lx + _clipHalf.X, ly + _clipHalf.Y, _clipHalf.X - lx, _clipHalf.Y - ly), _clipRounding, _clipAaSize);
        }

        private static bool EnsureSizeOrDouble<T>(ref T[] array, int neededCapacity) {
            if (array.Length < neededCapacity) {
                Array.Resize(ref array, array.Length * 2);
                return true;
            }
            return false;
        }

        private void GenerateIndexArray() {
            for (uint i = _fromIndex, j = _fromVertex; i < _indices.Length; i += 6, j += 4) {
                _indices[i + 0] = j + 0;
                _indices[i + 1] = j + 1;
                _indices[i + 2] = j + 3;
                _indices[i + 3] = j + 1;
                _indices[i + 4] = j + 2;
                _indices[i + 5] = j + 3;
            }
            _fromIndex = (uint)_indices.Length;
            _fromVertex = (uint)_vertices.Length;
        }

        private class FontStashRenderer(GraphicsDevice gd, ShapeBatch sb) : IFontStashRenderer2 {
            public GraphicsDevice GraphicsDevice => _graphicsDevice;

            public void DrawQuad(Texture2D texture, ref VertexPositionColorTexture topLeft, ref VertexPositionColorTexture topRight, ref VertexPositionColorTexture bottomLeft, ref VertexPositionColorTexture bottomRight) {
                _sb.DrawStringTexture(texture, ref topLeft, ref topRight, ref bottomLeft, ref bottomRight);
            }

            readonly GraphicsDevice _graphicsDevice = gd;
            readonly ShapeBatch _sb = sb;
        }

        private Texture2D? _texture = null;
        private Texture2D? _fontTexture = null;

        private const int _initialVertices = 2048 * 4;
        private const int _initialIndices = 2048 * 6;

        private readonly GraphicsDevice _graphicsDevice;
        private VertexShape[] _vertices;
        private uint[] _indices;
        private int _triangleCount = 0;
        private int _vertexCount = 0;
        private int _indexCount = 0;

        private DynamicVertexBuffer _vertexBuffer;
        private IndexBuffer _indexBuffer;

        private BlendState _blendState = null!;
        private SamplerState _samplerState = null!;
        private DepthStencilState _depthStencilState = null!;
        private RasterizerState _rasterizerState = null!;

        private Matrix _view;
        private Matrix _projection;
        private readonly Effect _effect;
        private readonly EffectParameter _viewProjection;
        private readonly EffectParameter? _halfViewportParam;
        private readonly EffectParameter? _ditherScale;
        private readonly EffectParameter? _ditherMode;
        private readonly Texture2D _blueNoise;

        private float _pixelSize = 1f;
        private Matrix _worldToClip = Matrix.Identity;
        private Vector2 _halfViewport = Vector2.One;
        private bool _isPerspective = false;

        private bool _beginCalled = false;
        private bool _disposed = false;

        private bool _indicesChanged = false;
        private uint _fromIndex = 0;
        private uint _fromVertex = 0;

        private readonly FontStashRenderer _fsr;

        private bool _hasClip = false;
        private Vector2 _clipCenter;
        private Vector2 _clipHalf;
        private Vector2 _clipU = Vector2.UnitX;
        private float _clipRounding;
        private float _clipAaSize;

        // Streaming path state for BeginPath/PathTo/EndPath. The point buffer is reused across paths.
        private PathPoint[] _pathPoints = new PathPoint[64];
        private int _pathPointCount;
        private bool _pathOpen;
        private float _pathRadius;
        private Gradient _pathFill;
        private Gradient _pathBorder;
        private float _pathThickness;
        private PathJoin _pathJoin;
        private PathCap _pathCap;
        private PathCap? _pathCapEnd;
        private float _pathMiterLimit;
        private float _pathAaSize;
        private DashStyle _pathDash;
    }

    /// <summary>
    /// Path overloads whose points carry join styles. A point's style applies to the joint at that
    /// point and to every following joint until another point sets a different one; the join parameter
    /// seeds the style before the first styled point. Points convert implicitly from Vector2 and from
    /// (position, join) tuples. These are extension methods so plain Vector2 point lists keep binding
    /// the ShapeBatch overloads without ambiguity on every C# language version.
    /// </summary>
    public static class ShapeBatchPathExtensions {
        public static void DrawPath(this ShapeBatch sb, ReadOnlySpan<PathPoint> points, float radius, Gradient fill, Gradient border, float thickness = 1f, PathJoin join = PathJoin.Round, PathCap cap = PathCap.Round, PathCap? capEnd = null, float miterLimit = 4f, float aaSize = 1.5f, bool closed = false, DashStyle dash = default) {
            sb.DrawPathPoints(points, radius, fill, border, thickness, join, cap, capEnd, miterLimit, aaSize, closed, dash);
        }
        public static void FillPath(this ShapeBatch sb, ReadOnlySpan<PathPoint> points, float radius, Gradient g, PathJoin join = PathJoin.Round, PathCap cap = PathCap.Round, PathCap? capEnd = null, float miterLimit = 4f, float aaSize = 1.5f, bool closed = false, DashStyle dash = default) {
            sb.DrawPathPoints(points, radius, g, g, 0f, join, cap, capEnd, miterLimit, aaSize, closed, dash);
        }
        public static void BorderPath(this ShapeBatch sb, ReadOnlySpan<PathPoint> points, float radius, Gradient g, float thickness = 1f, PathJoin join = PathJoin.Round, PathCap cap = PathCap.Round, PathCap? capEnd = null, float miterLimit = 4f, float aaSize = 1.5f, bool closed = false, DashStyle dash = default) {
            sb.DrawPathPoints(points, radius, Color.Transparent, g, thickness, join, cap, capEnd, miterLimit, aaSize, closed, dash);
        }
    }
}
