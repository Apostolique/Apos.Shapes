using System;
using System.Drawing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Apos.Shapes {
    public class ShapeBatch {
        public ShapeBatch(GraphicsDevice graphicsDevice, ContentManager content) {
            _graphicsDevice = graphicsDevice;

            _effect = content.Load<Effect>("apos-shapes");

            _vertices = new VertexShape[_initialVertices];
            _indices = new uint[_initialIndices];

            GenerateIndexArray();

            _vertexBuffer = new DynamicVertexBuffer(_graphicsDevice, typeof(VertexShape), _vertices.Length, BufferUsage.WriteOnly);

            _indexBuffer = new IndexBuffer(_graphicsDevice, typeof(uint), _indices.Length, BufferUsage.WriteOnly);
            _indexBuffer.SetData(_indices);
        }

        public void Begin(Matrix? view = null, Matrix? projection = null) {
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
            _aaOffset = _pixelSize * _aaSize;
        }
        public void DrawCircle(Vector2 center, float radius, Color c1, Color c2, float thickness = 1f) {
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            float radius1 = radius + _aaOffset; // Account for AA.

            var topLeft = center + new Vector2(-radius1);
            var topRight = center + new Vector2(radius1, -radius1);
            var bottomRight = center + new Vector2(radius1);
            var bottomLeft = center + new Vector2(-radius1, radius1);

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius1, -radius1), 0f, c1, c2, thickness, radius, _pixelSize, aaSize: _aaSize);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(radius1, -radius1), 0f, c1, c2, thickness, radius, _pixelSize, aaSize: _aaSize);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(radius1, radius1), 0f, c1, c2, thickness, radius, _pixelSize, aaSize: _aaSize);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius1, radius1), 0f, c1, c2, thickness, radius, _pixelSize, aaSize: _aaSize);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillCircle(Vector2 center, float radius, Color c) {
            DrawCircle(center, radius, c, c, 0f);
        }
        public void BorderCircle(Vector2 center, float radius, Color c, float thickness = 1f) {
            DrawCircle(center, radius, Color.Transparent, c, thickness);
        }
        public void DrawRectangle(Vector2 xy, Vector2 size, Color c1, Color c2, float thickness = 1f, float rounded = 0f, float rotation = 0f) {
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            rounded = MathF.Min(MathF.Min(rounded, size.X / 2f), size.Y / 2f);

            xy -= new Vector2(_aaOffset); // Account for AA.
            Vector2 size1 = size + new Vector2(_aaOffset * 2f); // Account for AA.
            Vector2 half = size / 2f;
            Vector2 half1 = half + new Vector2(_aaOffset); // Account for AA.

            half -= new Vector2(rounded);

            var topLeft = xy;
            var topRight = xy + new Vector2(size1.X, 0);
            var bottomRight = xy + size1;
            var bottomLeft = xy + new Vector2(0, size1.Y);

            if (rotation != 0f) {
                Vector2 center = xy + half1;
                topLeft = Rotate(topLeft, center, rotation);
                topRight = Rotate(topRight, center, rotation);
                bottomRight = Rotate(bottomRight, center, rotation);
                bottomLeft = Rotate(bottomLeft, center, rotation);
            }

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-half1.X, -half1.Y), 1f, c1, c2, thickness, half.X, _pixelSize, half.Y, aaSize: _aaSize, rounded: rounded);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(half1.X, -half1.Y), 1f, c1, c2, thickness, half.X, _pixelSize, half.Y, aaSize: _aaSize, rounded: rounded);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(half1.X, half1.Y), 1f, c1, c2, thickness, half.X, _pixelSize, half.Y, aaSize: _aaSize, rounded: rounded);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-half1.X, half1.Y), 1f, c1, c2, thickness, half.X, _pixelSize, half.Y, aaSize: _aaSize, rounded: rounded);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillRectangle(Vector2 xy, Vector2 size, Color c, float rounded = 0f, float rotation = 0f) {
            DrawRectangle(xy, size, c, c, 0f, rounded, rotation);
        }
        public void BorderRectangle(Vector2 xy, Vector2 size, Color c, float thickness = 1f, float rounded = 0f, float rotation = 0f) {
            DrawRectangle(xy, size, Color.Transparent, c, thickness, rounded, rotation);
        }
        public void DrawLine(Vector2 a, Vector2 b, float radius, Color c1, Color c2, float thickness = 1f) {
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            var radius1 = radius + _aaOffset; // Account for AA.

            var c = Slide(a, b, radius1);
            var d = Slide(b, a, radius1);

            var topLeft = CounterClockwise(d, c, radius1);
            var topRight = Clockwise(d, c, radius1);
            var bottomRight = CounterClockwise(c, d, radius1);
            var bottomLeft = Clockwise(c, d, radius1);

            var width1 = radius + radius1;
            var height = Vector2.Distance(a, b) + radius;
            var height1 = Vector2.Distance(topLeft, bottomLeft) - _aaOffset; // Account for AA.

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-_aaOffset, -_aaOffset), 2f, c1, c2, thickness, radius, _pixelSize, height, aaSize: _aaSize);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(width1, -_aaOffset), 2f, c1, c2, thickness, radius, _pixelSize, height, aaSize: _aaSize);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(width1, height1), 2f, c1, c2, thickness, radius, _pixelSize, height, aaSize: _aaSize);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-_aaOffset, height1), 2f, c1, c2, thickness, radius, _pixelSize, height, aaSize: _aaSize);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillLine(Vector2 a, Vector2 b, float radius, Color c) {
            DrawLine(a, b, radius, c, c, 0f);
        }
        public void BorderLine(Vector2 a, Vector2 b, float radius, Color c, float thickness = 1f) {
            DrawLine(a, b, radius, Color.Transparent, c, thickness);
        }

        public void DrawHexagon(Vector2 center, float radius, Color c1, Color c2, float thickness = 1f, float rounded = 0, float rotation = 0f) {
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            rounded = MathF.Min(rounded, radius);

            float radius1 = radius + _aaOffset; // Account for AA.
            float width1 = 2f * radius / MathF.Sqrt(3f) + _aaOffset; // Account for AA.

            radius -= rounded;

            Vector2 size = new Vector2(width1, radius1);

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

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-size.X, -size.Y), 3f, c1, c2, thickness, radius, _pixelSize, aaSize: _aaSize, rounded: rounded);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(size.X, -size.Y), 3f, c1, c2, thickness, radius, _pixelSize, aaSize: _aaSize, rounded: rounded);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(size.X, size.Y), 3f, c1, c2, thickness, radius, _pixelSize, aaSize: _aaSize, rounded: rounded);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-size.X, size.Y), 3f, c1, c2, thickness, radius, _pixelSize, aaSize: _aaSize, rounded: rounded);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillHexagon(Vector2 center, float radius, Color c, float rounded = 0f, float rotation = 0f) {
            DrawHexagon(center, radius, c, c, 0f, rounded, rotation);
        }
        public void BorderHexagon(Vector2 center, float radius, Color c, float thickness = 1f, float rounded = 0f, float rotation = 0f) {
            DrawHexagon(center, radius, Color.Transparent, c, thickness, rounded, rotation);
        }

        public void DrawEquilateralTriangle(Vector2 center, float radius, Color c1, Color c2, float thickness = 1f, float rounded = 0f, float rotation = 0f) {
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            rounded = MathF.Min(rounded, radius);

            float height = radius * 3f;

            float halfWidth = height / MathF.Sqrt(3f);
            float incircle = height / 3f;
            float circumcircle = 2f * height / 3f;

            float halfWidth1 = halfWidth + _aaOffset; // Account for AA.
            float incircle1 = incircle + _aaOffset; // Account for AA.
            float circumcircle1 = circumcircle + _aaOffset; // Account for AA.

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

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-halfWidth1, -incircle1), 4f, c1, c2, thickness, halfWidth, _pixelSize, aaSize: _aaSize, rounded: rounded);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(halfWidth1, -incircle1), 4f, c1, c2, thickness, halfWidth, _pixelSize, aaSize: _aaSize, rounded: rounded);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(halfWidth1, circumcircle1), 4f, c1, c2, thickness, halfWidth, _pixelSize, aaSize: _aaSize, rounded: rounded);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-halfWidth1, circumcircle1), 4f, c1, c2, thickness, halfWidth, _pixelSize, aaSize: _aaSize, rounded: rounded);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillEquilateralTriangle(Vector2 center, float radius, Color c, float rounded = 0f, float rotation = 0f) {
            DrawEquilateralTriangle(center, radius, c, c, 0f, rounded, rotation);
        }
        public void BorderEquilateralTriangle(Vector2 center, float radius, Color c, float thickness = 1f, float rounded = 0f, float rotation = 0f) {
            DrawEquilateralTriangle(center, radius, Color.Transparent, c, thickness, rounded, rotation);
        }
        public void DrawEllipse(Vector2 center, float radius1, float radius2, Color c1, Color c2, float thickness = 1f, float rotation = 0f) {
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            _indicesChanged = EnsureSizeOrDouble(ref _indices, _indexCount + 6) || _indicesChanged;

            float radius3 = radius1 + _aaOffset; // Account for AA.
            float radius4 = radius2 + _aaOffset; // Account for AA.

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

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-radius3, -radius4), 5f, c1, c2, thickness, radius1, _pixelSize, radius2, aaSize: _aaSize);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(radius3, -radius4), 5f, c1, c2, thickness, radius1, _pixelSize, radius2, aaSize: _aaSize);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(radius3, radius4), 5f, c1, c2, thickness, radius1, _pixelSize, radius2, aaSize: _aaSize);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-radius3, radius4), 5f, c1, c2, thickness, radius1, _pixelSize, radius2, aaSize: _aaSize);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillEllipse(Vector2 center, float width, float height, Color c, float rotation = 0f) {
            DrawEllipse(center, width, height, c, c, 0f, rotation);
        }
        public void BorderEllipse(Vector2 center, float width, float height, Color c, float thickness = 1f, float rotation = 0f) {
            DrawEllipse(center, width, height, Color.Transparent, c, thickness, rotation);
        }

        public void End() {
            Flush();

            // TODO: Restore old states like rasterizer, depth stencil, blend state?
        }
        
        public RectangleF Bounds()
        {
            if (_vertexCount == 0) 
                return RectangleF.Empty;

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            for (var i = 0; i < _vertexCount; i++)
            {
                var v = _vertices[i].Position;
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }

            return new RectangleF(min.X, min.Y, max.X - min.X, max.Y - min.Y);
        }

        private void Flush() {
            if (_triangleCount == 0) return;

            _effect.Parameters["view_projection"].SetValue(_view * _projection);

            if (_indicesChanged) {
                _vertexBuffer.Dispose();
                _indexBuffer.Dispose();

                _vertexBuffer = new DynamicVertexBuffer(_graphicsDevice, typeof(VertexShape), _vertices.Length, BufferUsage.WriteOnly);

                GenerateIndexArray();

                _indexBuffer = new IndexBuffer(_graphicsDevice, typeof(uint), _indices.Length, BufferUsage.WriteOnly);
                _indexBuffer.SetData(_indices);

                _indicesChanged = false;
            }

            _vertexBuffer.SetData(_vertices);
            _graphicsDevice.SetVertexBuffer(_vertexBuffer);

            _graphicsDevice.Indices = _indexBuffer;

            _graphicsDevice.DepthStencilState = DepthStencilState.None;
            _graphicsDevice.BlendState = BlendState.AlphaBlend;

            foreach (EffectPass pass in _effect.CurrentTechnique.Passes) {
                pass.Apply();

                _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _triangleCount);
            }

            _triangleCount = 0;
            _vertexCount = 0;
            _indexCount = 0;
        }
        private float ScreenToWorldScale() {
            return Vector2.Distance(ScreenToWorld(0f, 0f), ScreenToWorld(1f, 0f));
        }
        private Vector2 ScreenToWorld(float x, float y) {
            return ScreenToWorld(new Vector2(x, y));
        }
        private Vector2 ScreenToWorld(Vector2 xy) {
            return Vector2.Transform(xy, Matrix.Invert(_view));
        }

        private Vector2 Slide(Vector2 a, Vector2 b, float distance) {
            var c = Vector2.Normalize(b - a) * distance;
            return b + c;
        }
        private Vector2 Clockwise(Vector2 a, Vector2 b, float distance) {
            var c = Vector2.Normalize(b - a) * distance;
            return new Vector2(c.Y, -c.X) + a;
        }
        private Vector2 CounterClockwise(Vector2 a, Vector2 b, float distance) {
            var c = Vector2.Normalize(b - a) * distance;
            return new Vector2(-c.Y, c.X) + a;
        }
        private Vector2 Rotate(Vector2 a, Vector2 origin, float rotation) {
            return new Vector2(origin.X + (a.X - origin.X) * MathF.Cos(rotation) - (a.Y - origin.Y) * MathF.Sin(rotation), origin.Y + (a.X - origin.X) * MathF.Sin(rotation) + (a.Y - origin.Y) * MathF.Cos(rotation));
        }

        private bool EnsureSizeOrDouble<T>(ref T[] array, int neededCapacity) {
            if (array.Length < neededCapacity) {
                Array.Resize(ref array, array.Length * 2);
                return true;
            }
            return false;
        }

        private void GenerateIndexArray() {
            uint i = Floor(_fromIndex, 6, 6);
            uint j = Floor(_fromIndex, 6, 4);
            for (; i < _indices.Length; i += 6, j += 4) {
                _indices[i + 0] = j + 0;
                _indices[i + 1] = j + 1;
                _indices[i + 2] = j + 3;
                _indices[i + 3] = j + 1;
                _indices[i + 4] = j + 2;
                _indices[i + 5] = j + 3;
            }
            _fromIndex = _indices.Length;
        }
        private uint Floor(int value, int div, uint mul) {
            return (uint)MathF.Floor((float)value / div) * mul;
        }

        private const int _initialSprites = 2048;
        private const int _initialTriangles = 2048 * 2;
        private const int _initialVertices = 2048 * 4;
        private const int _initialIndices = 2048 * 6;

        private GraphicsDevice _graphicsDevice;
        private VertexShape[] _vertices;
        private uint[] _indices;
        private int _triangleCount = 0;
        private int _vertexCount = 0;
        private int _indexCount = 0;

        private DynamicVertexBuffer _vertexBuffer;
        private IndexBuffer _indexBuffer;

        private Matrix _view;
        private Matrix _projection;
        private Effect _effect;

        private float _pixelSize = 1f;
        private float _aaSize = 2f;
        private float _aaOffset = 1f;

        private bool _indicesChanged = false;
        private int _fromIndex = 0;
    }
}
