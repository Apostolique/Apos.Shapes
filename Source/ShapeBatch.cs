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

        public void Begin(Matrix? view = null, Matrix? projection = null, BlendState? blendState = null, SamplerState? samplerState = null, DepthStencilState? depthStencilState = null, RasterizerState? rasterizerState = null) {
            if (_beginCalled) {
                throw new InvalidOperationException("Begin cannot be called again until End has been successfully called.");
            }
            _beginCalled = true;

            if (view != null) {
                _view = view.Value;
            } else {
                _view = Matrix.Identity;
            }

            if (projection != null) {
                _projection = projection.Value;
            } else {
                Viewport viewport = _graphicsDevice.Viewport;
                _projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, 1);
            }

            _pixelSize = ScreenToWorldScale();

            _blendState = blendState ?? BlendState.AlphaBlend;
            _samplerState = samplerState ?? SamplerState.LinearClamp;
            _depthStencilState = depthStencilState ?? DepthStencilState.None;
            _rasterizerState = rasterizerState ?? RasterizerState.CullCounterClockwise;
        }
        public void DrawCircle(Vector2 center, float radius, Gradient fill, Gradient border, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f) {
            float aaOffset = _pixelSize * aaSize;

            if (thickness > 0f && IsTransparent(fill)) {
                float holeRadius = radius - thickness - aaOffset - _pixelSize;
                if (holeRadius > _hollowMinHolePixels * _pixelSize) {
                    GradientToWorld(ref fill, ref border, center, Vector2.Zero, rotation);
                    EmitHollowAnnulus(center, rotation, Vector2.Zero, 0f, MathF.PI, new Vector2(holeRadius), new Vector2(radius + aaOffset + _pixelSize), true, VertexShape.Shape.Circle, fill, border, thickness, radius, 1f, aaSize, 0f, 0f, 0f, 0f, 0f);
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

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius1, -radius1), VertexShape.Shape.Circle, fill, border, thickness, radius, _pixelSize, GetClipSpace(topLeft), aaSize: aaSize, colorSpace: ColorSpace);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(radius1, -radius1), VertexShape.Shape.Circle, fill, border, thickness, radius, _pixelSize, GetClipSpace(topRight), aaSize: aaSize, colorSpace: ColorSpace);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(radius1, radius1), VertexShape.Shape.Circle, fill, border, thickness, radius, _pixelSize, GetClipSpace(bottomRight), aaSize: aaSize, colorSpace: ColorSpace);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius1, radius1), VertexShape.Shape.Circle, fill, border, thickness, radius, _pixelSize, GetClipSpace(bottomLeft), aaSize: aaSize, colorSpace: ColorSpace);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillCircle(Vector2 center, float radius, Gradient g, float rotation = 0f, float aaSize = 1.5f) {
            DrawCircle(center, radius, g, g, 0f, rotation, aaSize);
        }
        public void BorderCircle(Vector2 center, float radius, Gradient g, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f) {
            DrawCircle(center, radius, Color.Transparent, g, thickness, rotation, aaSize);
        }

        public void DrawRectangle(Vector2 xy, Vector2 size, Gradient fill, Gradient border, float thickness, CornerRadii cornerRadii, float rotation = 0f, float aaSize = 1.5f) {
            PrepareQuad();

            float maxR = MathF.Min(size.X, size.Y) / 2f;
            float rTL = MathHelper.Clamp(cornerRadii.TopLeft,     0f, maxR);
            float rTR = MathHelper.Clamp(cornerRadii.TopRight,    0f, maxR);
            float rBR = MathHelper.Clamp(cornerRadii.BottomRight, 0f, maxR);
            float rBL = MathHelper.Clamp(cornerRadii.BottomLeft,  0f, maxR);

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

            if (thickness > 0f && IsTransparent(fill)) {
                float maxCorner = MathF.Max(MathF.Max(rTL, rTR), MathF.Max(rBR, rBL));
                float inset = thickness + aaOffset + _pixelSize + maxCorner;
                Vector2 hole = new(half.X - inset, half.Y - inset);
                float minHole = _hollowMinHolePixels * _pixelSize;
                if (hole.X > 4f * _pixelSize && hole.Y > 4f * _pixelSize && hole.X * hole.Y > minHole * minHole) {
                    Span<Vector2> outerCorners = stackalloc Vector2[] { new(-half1.X, -half1.Y), new(half1.X, -half1.Y), new(half1.X, half1.Y), new(-half1.X, half1.Y) };
                    Span<Vector2> innerCorners = stackalloc Vector2[] { new(-hole.X, -hole.Y), new(hole.X, -hole.Y), new(hole.X, hole.Y), new(-hole.X, hole.Y) };
                    EmitHollowFrame(center, rotation, outerCorners, innerCorners, VertexShape.Shape.Rectangle, fill, border, thickness, half.X, half.Y, aaSize, 0f, rA, rB, rC, rD);
                    return;
                }
            }

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-half1.X, -half1.Y), VertexShape.Shape.Rectangle, fill, border, thickness, half.X, _pixelSize, GetClipSpace(topLeft), half.Y, aaSize: aaSize, rounded: 0f, a: rA, b: rB, c: rC, d: rD, colorSpace: ColorSpace);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(half1.X, -half1.Y), VertexShape.Shape.Rectangle, fill, border, thickness, half.X, _pixelSize, GetClipSpace(topRight), half.Y, aaSize: aaSize, rounded: 0f, a: rA, b: rB, c: rC, d: rD, colorSpace: ColorSpace);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(half1.X, half1.Y), VertexShape.Shape.Rectangle, fill, border, thickness, half.X, _pixelSize, GetClipSpace(bottomRight), half.Y, aaSize: aaSize, rounded: 0f, a: rA, b: rB, c: rC, d: rD, colorSpace: ColorSpace);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-half1.X, half1.Y), VertexShape.Shape.Rectangle, fill, border, thickness, half.X, _pixelSize, GetClipSpace(bottomLeft), half.Y, aaSize: aaSize, rounded: 0f, a: rA, b: rB, c: rC, d: rD, colorSpace: ColorSpace);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillRectangle(Vector2 xy, Vector2 size, Gradient g, CornerRadii cornerRadii = default, float rotation = 0f, float aaSize = 1.5f) {
            DrawRectangle(xy, size, g, g, 0f, cornerRadii, rotation, aaSize);
        }
        public void BorderRectangle(Vector2 xy, Vector2 size, Gradient g, float thickness, CornerRadii cornerRadii = default, float rotation = 0f, float aaSize = 1.5f) {
            DrawRectangle(xy, size, Color.Transparent, g, thickness, cornerRadii, rotation, aaSize);
        }

        public void DrawLine(Vector2 a, Vector2 b, float radius, Gradient fill, Gradient border, float thickness = 1f, float aaSize = 1.5f) {
            if (a == b) {
                DrawCircle(a, radius, fill, border, thickness, aaSize: aaSize);
                return;
            }

            float aaOffset = _pixelSize * aaSize;

            if (thickness > 0f && IsTransparent(fill)) {
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

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius1, -radius1), VertexShape.Shape.Line, fill, border, thickness, radius, _pixelSize, GetClipSpace(topLeft), width, aaSize: aaSize, rounded: radius, colorSpace: ColorSpace);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(width1, -radius1), VertexShape.Shape.Line, fill, border, thickness, radius, _pixelSize, GetClipSpace(topRight), width, aaSize: aaSize, rounded: radius, colorSpace: ColorSpace);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(width1, radius1), VertexShape.Shape.Line, fill, border, thickness, radius, _pixelSize, GetClipSpace(bottomRight), width, aaSize: aaSize, rounded: radius, colorSpace: ColorSpace);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius1, radius1), VertexShape.Shape.Line, fill, border, thickness, radius, _pixelSize, GetClipSpace(bottomLeft), width, aaSize: aaSize, rounded: radius, colorSpace: ColorSpace);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillLine(Vector2 a, Vector2 b, float radius, Gradient g, float aaSize = 1.5f) {
            DrawLine(a, b, radius, g, g, 0f, aaSize);
        }
        public void BorderLine(Vector2 a, Vector2 b, float radius, Gradient g, float thickness = 1f, float aaSize = 1.5f) {
            DrawLine(a, b, radius, Color.Transparent, g, thickness, aaSize);
        }

        public void DrawHexagon(Vector2 center, float radius, Gradient fill, Gradient border, float thickness = 1f, float rounded = 0, float rotation = 0f, float aaSize = 1.5f) {
            PrepareQuad();

            rounded = MathF.Min(rounded, radius);

            float aaOffset = _pixelSize * aaSize;
            float radius1 = radius + aaOffset; // Account for AA.
            float width1 = 2f * radius / MathF.Sqrt(3f) + aaOffset; // Account for AA.

            radius -= rounded;

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
                    EmitHollowFrame(center, rotation, outerCorners, innerCorners, VertexShape.Shape.Hexagon, fill, border, thickness, radius, 1f, aaSize, rounded, 0f, 0f, 0f, 0f);
                    return;
                }
            }

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-size.X, -size.Y), VertexShape.Shape.Hexagon, fill, border, thickness, radius, _pixelSize, GetClipSpace(topLeft), aaSize: aaSize, rounded: rounded, colorSpace: ColorSpace);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(size.X, -size.Y), VertexShape.Shape.Hexagon, fill, border, thickness, radius, _pixelSize, GetClipSpace(topRight), aaSize: aaSize, rounded: rounded, colorSpace: ColorSpace);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(size.X, size.Y), VertexShape.Shape.Hexagon, fill, border, thickness, radius, _pixelSize, GetClipSpace(bottomRight), aaSize: aaSize, rounded: rounded, colorSpace: ColorSpace);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-size.X, size.Y), VertexShape.Shape.Hexagon, fill, border, thickness, radius, _pixelSize, GetClipSpace(bottomLeft), aaSize: aaSize, rounded: rounded, colorSpace: ColorSpace);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillHexagon(Vector2 center, float radius, Gradient g, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f) {
            DrawHexagon(center, radius, g, g, 0f, rounded, rotation, aaSize);
        }
        public void BorderHexagon(Vector2 center, float radius, Gradient g, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f) {
            DrawHexagon(center, radius, Color.Transparent, g, thickness, rounded, rotation, aaSize);
        }

        public void DrawEquilateralTriangle(Vector2 center, float radius, Gradient fill, Gradient border, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f) {
            PrepareQuad();

            rounded = MathF.Min(rounded, radius);

            float aaOffset = _pixelSize * aaSize;
            float height = radius * 3f;

            float halfWidth = height / MathF.Sqrt(3f);
            float incircle = height / 3f;
            float circumcircle = 2f * height / 3f;

            float halfWidth1 = halfWidth + aaOffset; // Account for AA.
            float incircle1 = incircle + aaOffset; // Account for AA.
            float circumcircle1 = circumcircle + aaOffset; // Account for AA.

            halfWidth -= rounded * MathF.Sqrt(3f);

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
                    EmitHollowFrame(center, rotation, outerCorners, innerCorners, VertexShape.Shape.EquilateralTriangle, fill, border, thickness, halfWidth, 1f, aaSize, rounded, 0f, 0f, 0f, 0f);
                    return;
                }
            }

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-halfWidth1, -incircle1), VertexShape.Shape.EquilateralTriangle, fill, border, thickness, halfWidth, _pixelSize, GetClipSpace(topLeft), aaSize: aaSize, rounded: rounded, colorSpace: ColorSpace);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(halfWidth1, -incircle1), VertexShape.Shape.EquilateralTriangle, fill, border, thickness, halfWidth, _pixelSize, GetClipSpace(topRight), aaSize: aaSize, rounded: rounded, colorSpace: ColorSpace);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(halfWidth1, circumcircle1), VertexShape.Shape.EquilateralTriangle, fill, border, thickness, halfWidth, _pixelSize, GetClipSpace(bottomRight), aaSize: aaSize, rounded: rounded, colorSpace: ColorSpace);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-halfWidth1, circumcircle1), VertexShape.Shape.EquilateralTriangle, fill, border, thickness, halfWidth, _pixelSize, GetClipSpace(bottomLeft), aaSize: aaSize, rounded: rounded, colorSpace: ColorSpace);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillEquilateralTriangle(Vector2 center, float radius, Gradient g, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f) {
            DrawEquilateralTriangle(center, radius, g, g, 0f, rounded, rotation, aaSize);
        }
        public void BorderEquilateralTriangle(Vector2 center, float radius, Gradient g, float thickness = 1f, float rounded = 0f, float rotation = 0f, float aaSize = 1.5f) {
            DrawEquilateralTriangle(center, radius, Color.Transparent, g, thickness, rounded, rotation, aaSize);
        }

        public void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Gradient fill, Gradient border, float thickness = 1f, float rounded = 0f, float aaSize = 1.5f) {
            PrepareQuad();

            GradientToWorld(ref fill, ref border, a, Vector2.Zero, MathF.Atan2(b.Y - a.Y, b.X - a.X));

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
                        outerCorners[i] = inCenter + (corners[i] - inCenter) * scaleOut;
                        // Offsetting outward the hole corners must stay on the rounded band, offsetting inward the erosion is an exact scale.
                        innerCorners[i] = holeOffset <= 0f ? inCenter + (corners[i] - inCenter) * scaleIn : corners[i] + Vector2.Normalize(corners[i] - inCenter) * holeOffset;
                    }
                    // This shape's local coordinates are the world coordinates themselves.
                    EmitHollowFrame(Vector2.Zero, 0f, outerCorners, innerCorners, VertexShape.Shape.Triangle, fill, border, thickness, A.X, A.Y, aaSize, rounded, B.X, B.Y, C.X, C.Y);
                    return;
                }
            }

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), topLeft, VertexShape.Shape.Triangle, fill, border, thickness, A.X, _pixelSize, GetClipSpace(topLeft), height: A.Y, aaSize: aaSize, rounded: rounded, a: B.X, b: B.Y, c: C.X, d: C.Y, colorSpace: ColorSpace);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), topRight, VertexShape.Shape.Triangle, fill, border, thickness, A.X, _pixelSize, GetClipSpace(topRight), height: A.Y, aaSize: aaSize, rounded: rounded, a: B.X, b: B.Y, c: C.X, d: C.Y, colorSpace: ColorSpace);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), bottomRight, VertexShape.Shape.Triangle, fill, border, thickness, A.X, _pixelSize, GetClipSpace(bottomRight), height: A.Y, aaSize: aaSize, rounded: rounded, a: B.X, b: B.Y, c: C.X, d: C.Y, colorSpace: ColorSpace);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), bottomLeft, VertexShape.Shape.Triangle, fill, border, thickness, A.X, _pixelSize, GetClipSpace(bottomLeft), height: A.Y, aaSize: aaSize, rounded: rounded, a: B.X, b: B.Y, c: C.X, d: C.Y, colorSpace: ColorSpace);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Gradient g, float rounded = 0f, float aaSize = 1.5f) {
            DrawTriangle(a, b, c, g, g, 0f, rounded, aaSize);
        }
        public void BorderTriangle(Vector2 a, Vector2 b, Vector2 c, Gradient g, float thickness = 1f, float rounded = 0f, float aaSize = 1.5f) {
            DrawTriangle(a, b, c, Color.Transparent, g, thickness, rounded, aaSize);
        }

        public void DrawEllipse(Vector2 center, float radius1, float radius2, Gradient fill, Gradient border, float thickness = 1f, float rotation = 0f, float aaSize = 1.5f) {
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

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius3, -radius4), VertexShape.Shape.Ellipse, fill, border, thickness, radius1, _pixelSize, GetClipSpace(topLeft), radius2, aaSize: aaSize, colorSpace: ColorSpace);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(radius3, -radius4), VertexShape.Shape.Ellipse, fill, border, thickness, radius1, _pixelSize, GetClipSpace(topRight), radius2, aaSize: aaSize, colorSpace: ColorSpace);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(radius3, radius4), VertexShape.Shape.Ellipse, fill, border, thickness, radius1, _pixelSize, GetClipSpace(bottomRight), radius2, aaSize: aaSize, colorSpace: ColorSpace);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius3, radius4), VertexShape.Shape.Ellipse, fill, border, thickness, radius1, _pixelSize, GetClipSpace(bottomLeft), radius2, aaSize: aaSize, colorSpace: ColorSpace);

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

        public void DrawArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient fill, Gradient border, float thickness = 1f, float aaSize = 1.5f) {
            PrepareQuad();

            radius1 -= 1f;

            float angleSize = MathF.Abs(Mod((angle2 - angle1) * 0.5f + MathF.PI, MathF.PI * 2f) - MathF.PI);
            float sin = MathF.Sin(angleSize);
            float cos = MathF.Cos(angleSize);

            float aaOffset = _pixelSize * aaSize;
            float rotation = (angle1 + angle2 - MathF.PI) * 0.5f;

            float holeRadius = radius1 - radius2 - aaOffset - _pixelSize;
            if (holeRadius > _hollowMinHolePixels * _pixelSize) {
                GradientToWorld(ref fill, ref border, center, Vector2.Zero, angle1);
                // Round caps and their AA fringe swing at most this far past the end angles.
                float capMargin = MathF.Asin(MathF.Min((radius2 + aaOffset + _pixelSize) / holeRadius, 1f));
                float halfSpan = angleSize + capMargin;
                bool wrap = halfSpan >= MathF.PI;
                EmitHollowAnnulus(center, rotation, Vector2.Zero, 0f, wrap ? MathF.PI : halfSpan, new Vector2(holeRadius), new Vector2(radius1 + radius2 + aaOffset + _pixelSize), wrap, VertexShape.Shape.Arc, fill, border, thickness, radius1, 1f, aaSize, 0f, sin, cos, radius2, 0f);
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

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius3, -radius3), VertexShape.Shape.Arc, fill, border, thickness, radius1, _pixelSize, GetClipSpace(topLeft), aaSize: aaSize, a: sin, b: cos, c: radius2, colorSpace: ColorSpace);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(radius3, -radius3), VertexShape.Shape.Arc, fill, border, thickness, radius1, _pixelSize, GetClipSpace(topRight), aaSize: aaSize, a: sin, b: cos, c: radius2, colorSpace: ColorSpace);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(radius3, radius3), VertexShape.Shape.Arc, fill, border, thickness, radius1, _pixelSize, GetClipSpace(bottomRight), aaSize: aaSize, a: sin, b: cos, c: radius2, colorSpace: ColorSpace);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius3, radius3), VertexShape.Shape.Arc, fill, border, thickness, radius1, _pixelSize, GetClipSpace(bottomLeft), aaSize: aaSize, a: sin, b: cos, c: radius2, colorSpace: ColorSpace);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient g, float aaSize = 1.5f) {
            DrawArc(center, angle1, angle2, radius1, radius2, g, g, 0f, aaSize);
        }
        public void BorderArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient g, float thickness = 1f, float aaSize = 1.5f) {
            DrawArc(center, angle1, angle2, radius1, radius2, Color.Transparent, g, thickness, aaSize);
        }

        public void DrawRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient fill, Gradient border, float thickness = 1f, float aaSize = 1.5f) {
            PrepareQuad();

            radius1 -= 1f;

            float angleSize = MathF.Abs(Mod((angle2 - angle1) * 0.5f + MathF.PI, MathF.PI * 2f) - MathF.PI);

            float cos = MathF.Cos(angleSize);
            float sin = MathF.Sin(angleSize);

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
                EmitHollowAnnulus(center, rotation, Vector2.Zero, 0f, wrap ? MathF.PI : halfSpan, new Vector2(holeRadius), new Vector2(radius1 + radius2 * 0.5f + aaOffset + _pixelSize), wrap, VertexShape.Shape.Ring, fill, border, thickness, radius1, 1f, aaSize, 0f, cos, sin, radius2, 0f);
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

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius3, -radius3), VertexShape.Shape.Ring, fill, border, thickness, radius1, _pixelSize, GetClipSpace(topLeft), aaSize: aaSize, a: cos, b: sin, c: radius2, colorSpace: ColorSpace);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(radius3, -radius3), VertexShape.Shape.Ring, fill, border, thickness, radius1, _pixelSize, GetClipSpace(topRight), aaSize: aaSize, a: cos, b: sin, c: radius2, colorSpace: ColorSpace);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(radius3, radius3), VertexShape.Shape.Ring, fill, border, thickness, radius1, _pixelSize, GetClipSpace(bottomRight), aaSize: aaSize, a: cos, b: sin, c: radius2, colorSpace: ColorSpace);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius3, radius3), VertexShape.Shape.Ring, fill, border, thickness, radius1, _pixelSize, GetClipSpace(bottomLeft), aaSize: aaSize, a: cos, b: sin, c: radius2, colorSpace: ColorSpace);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient g, float aaSize = 1.5f) {
            DrawRing(center, angle1, angle2, radius1, radius2, g, g, 0f, aaSize);
        }
        public void BorderRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient g, float thickness = 1f, float aaSize = 1.5f) {
            DrawRing(center, angle1, angle2, radius1, radius2, Color.Transparent, g, thickness, aaSize);
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

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(wTopLeft.X, wTopLeft.Y, 0f), GetUV(texture, topLeft), VertexShape.Shape.Texture, g, g, 0f, 1f, _pixelSize, GetClipSpace(wTopLeft));
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(wTopRight.X, wTopRight.Y, 0f), GetUV(texture, topRight), VertexShape.Shape.Texture, g, g, 0f, 1f, _pixelSize, GetClipSpace(wTopRight));
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(wBottomRight.X, wBottomRight.Y, 0f), GetUV(texture, bottomRight), VertexShape.Shape.Texture, g, g, 0f, 1f, _pixelSize, GetClipSpace(wBottomRight));
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(wBottomLeft.X, wBottomLeft.Y, 0f), GetUV(texture, bottomLeft), VertexShape.Shape.Texture, g, g, 0f, 1f, _pixelSize, GetClipSpace(wBottomLeft));

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

            _vertices[_vertexCount + 0] = new VertexShape(topLeft.Position, topLeft.TextureCoordinate, VertexShape.Shape.String, gTopLeft, gTopLeft, 0f, 1f, _pixelSize, GetClipSpace(new Vector2(topLeft.Position.X, topLeft.Position.Y)));
            _vertices[_vertexCount + 1] = new VertexShape(topRight.Position, topRight.TextureCoordinate, VertexShape.Shape.String, gTopRight, gTopRight, 0f, 1f, _pixelSize, GetClipSpace(new Vector2(topRight.Position.X, topRight.Position.Y)));
            _vertices[_vertexCount + 2] = new VertexShape(bottomRight.Position, bottomRight.TextureCoordinate, VertexShape.Shape.String, gBottomRight, gBottomRight, 0f, 1f, _pixelSize, GetClipSpace(new Vector2(bottomRight.Position.X, bottomRight.Position.Y)));
            _vertices[_vertexCount + 3] = new VertexShape(bottomLeft.Position, bottomLeft.TextureCoordinate, VertexShape.Shape.String, gBottomLeft, gBottomLeft, 0f, 1f, _pixelSize, GetClipSpace(new Vector2(bottomLeft.Position.X, bottomLeft.Position.Y)));

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }

        public void End() {
            if (!_beginCalled) {
                throw new InvalidOperationException("Begin must be called before calling End.");
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
            }
            _disposed = true;
        }

        private void Flush() {
            if (_triangleCount == 0) return;

            _viewProjection.SetValue(_view * _projection);

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

                _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _triangleCount);
            }

            _triangleCount = 0;
            _vertexCount = 0;
            _indexCount = 0;
        }
        private float ScreenToWorldScale() {
            Matrix inverseView = Matrix.Invert(_view);
            return Vector2.Distance(Vector2.Transform(new Vector2(0f, 0f), inverseView), Vector2.Transform(new Vector2(1f, 0f), inverseView));
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

        private void EmitHollowQuad(Vector2 w0, Vector2 w1, Vector2 w2, Vector2 w3, Vector2 l0, Vector2 l1, Vector2 l2, Vector2 l3, VertexShape.Shape shape, Gradient fill, Gradient border, float thickness, float sdfSize, float height, float aaSize, float rounded, float a, float b, float c, float d) {
            PrepareQuad();

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(w0, 0), l0, shape, fill, border, thickness, sdfSize, _pixelSize, GetClipSpace(w0), height, aaSize: aaSize, rounded: rounded, a: a, b: b, c: c, d: d, colorSpace: ColorSpace);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(w1, 0), l1, shape, fill, border, thickness, sdfSize, _pixelSize, GetClipSpace(w1), height, aaSize: aaSize, rounded: rounded, a: a, b: b, c: c, d: d, colorSpace: ColorSpace);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(w2, 0), l2, shape, fill, border, thickness, sdfSize, _pixelSize, GetClipSpace(w2), height, aaSize: aaSize, rounded: rounded, a: a, b: b, c: c, d: d, colorSpace: ColorSpace);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(w3, 0), l3, shape, fill, border, thickness, sdfSize, _pixelSize, GetClipSpace(w3), height, aaSize: aaSize, rounded: rounded, a: a, b: b, c: c, d: d, colorSpace: ColorSpace);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }

        // Covers the band between two concentric (possibly elliptical) rings with a fan of quads.
        // Angles are measured from the local +y axis to line up with the arc and ring SDFs. Outer
        // vertices circumscribe the band so the chords never clip it; inner vertices sit on the hole
        // boundary so their chords only dip into the hole. Quads are wound clockwise on screen.
        private void EmitHollowAnnulus(Vector2 origin, float rotation, Vector2 localCenter, float angleCenter, float halfSpan, Vector2 axesInner, Vector2 axesOuter, bool wrap, VertexShape.Shape shape, Gradient fill, Gradient border, float thickness, float sdfSize, float height, float aaSize, float rounded, float a, float b, float c, float d) {
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

                EmitHollowQuad(W(outerCur), W(outerPrev), W(innerPrev), W(innerCur), outerCur, outerPrev, innerPrev, innerCur, shape, fill, border, thickness, sdfSize, height, aaSize, rounded, a, b, c, d);

                outerPrev = outerCur;
                innerPrev = innerCur;
            }
        }

        // One quad per edge between two nested convex polygons with matching corner counts. Adjacent
        // quads share their mitre edges exactly so the frame is watertight. Corners must be listed
        // clockwise on screen.
        private void EmitHollowFrame(Vector2 origin, float rotation, ReadOnlySpan<Vector2> outer, ReadOnlySpan<Vector2> inner, VertexShape.Shape shape, Gradient fill, Gradient border, float thickness, float sdfSize, float height, float aaSize, float rounded, float a, float b, float c, float d) {
            (float sinR, float cosR) = MathF.SinCos(rotation);
            Vector2 W(Vector2 l) => origin + new Vector2(l.X * cosR - l.Y * sinR, l.X * sinR + l.Y * cosR);

            for (int i = 0; i < outer.Length; i++) {
                int j = i + 1 == outer.Length ? 0 : i + 1;
                EmitHollowQuad(W(outer[i]), W(outer[j]), W(inner[j]), W(inner[i]), outer[i], outer[j], inner[j], inner[i], shape, fill, border, thickness, sdfSize, height, aaSize, rounded, a, b, c, d);
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

            return new ClipSpace(new Vector4(lx + _clipHalf.X, ly + _clipHalf.Y, _clipHalf.X - lx, _clipHalf.Y - ly), _clipRounding, _clipAaSize * _pixelSize);
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

        private float _pixelSize = 1f;

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
    }
}
