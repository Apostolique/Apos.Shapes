using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Apos.Shapes {
    public class ShapeBatch {
        public ShapeBatch(GraphicsDevice graphicsDevice, ContentManager content) {
            _graphicsDevice = graphicsDevice;

            _effect = content.Load<Effect>("apos-shapes-effect");

            _vertices = new VertexShape[_initialVertices];
            _indices = new uint[_initialIndices];

            GenerateIndexArray(ref _indices, 0);

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
        }
        public void DrawCircle(Vector2 center, float radius, Color c1, Color c2, float thickness = 1f) {
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            if (EnsureSizeOrDouble(ref _indices, _indexCount + 6)) {
                _fromIndex = _indexCount;
                _indicesChanged = true;
            }

            radius += _pixelSize; // Account for AA.

            var topLeft = center + new Vector2(-radius);
            var topRight = center + new Vector2(radius, -radius);
            var bottomRight = center + new Vector2(radius);
            var bottomLeft = center + new Vector2(-radius, radius);

            float ps = _pixelSize / (radius * 2);

            float u = 1.0f;

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-u, -u), 0f, c1, c2, thickness, ps);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(u, -u), 0f, c1, c2, thickness, ps);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(u, u), 0f, c1, c2, thickness, ps);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-u, u), 0f, c1, c2, thickness, ps);

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
        public void DrawRectangle(Vector2 xy, Vector2 size, Color c1, Color c2, float thickness = 1f) {
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            if (EnsureSizeOrDouble(ref _indices, _indexCount + 6)) {
                _fromIndex = _indexCount;
                _indicesChanged = true;
            }

            xy -= new Vector2(_pixelSize); // Account for AA.
            size += new Vector2(_pixelSize * 2f); // Account for AA.

            Vector2 uv = size / size.Y;
            float ux = uv.X;
            float uy = uv.Y;

            float ps = _pixelSize / size.Y;

            var topLeft = xy;
            var topRight = xy + new Vector2(size.X, 0);
            var bottomRight = xy + size;
            var bottomLeft = xy + new Vector2(0, size.Y);

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-ux, -uy), 1f, c1, c2, thickness, ps, ux);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(ux, -uy), 1f, c1, c2, thickness, ps, ux);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(ux, uy), 1f, c1, c2, thickness, ps, ux);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-ux, uy), 1f, c1, c2, thickness, ps, ux);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillRectangle(Vector2 xy, Vector2 size, Color c) {
            DrawRectangle(xy, size, c, c, 0f);
        }
        public void BorderRectangle(Vector2 xy, Vector2 size, Color c, float thickness = 1f) {
            DrawRectangle(xy, size, Color.Transparent, c, thickness);
        }
        public void DrawLine(Vector2 a, Vector2 b, float radius, Color c1, Color c2, float thickness = 1f) {
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            if (EnsureSizeOrDouble(ref _indices, _indexCount + 6)) {
                _fromIndex = _indexCount;
                _indicesChanged = true;
            }

            var r = radius + _pixelSize; // Account for AA.

            var c = Slide(a, b, r);
            var d = Slide(b, a, r);

            var topLeft = Clockwise(d, c, r);
            var topRight = CounterClockwise(c, d, r);
            var bottomRight = Clockwise(c, d, r);
            var bottomLeft = CounterClockwise(d, c, r);

            var size = new Vector2(Vector2.Distance(topLeft, topRight), Vector2.Distance(topLeft, bottomLeft));

            var uv = size / size.Y;
            var ux = uv.X;
            var uy = uv.Y;

            var ur = (radius * 2f) / size.Y;

            var ps = _pixelSize / size.Y;

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-ux, -uy), 2f, c1, c2, thickness, ps, ux - ur, ur);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(ux, -uy), 2f, c1, c2, thickness, ps, ux - ur, ur);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(ux, uy), 2f, c1, c2, thickness, ps, ux - ur, ur);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-ux, uy), 2f, c1, c2, thickness, ps, ux - ur, ur);

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

        public void DrawHexagon(Vector2 center, float radius, Color c1, Color c2, float thickness = 1f) {
            EnsureSizeOrDouble(ref _vertices, _vertexCount + 4);
            if (EnsureSizeOrDouble(ref _indices, _indexCount + 6)) {
                _fromIndex = _indexCount;
                _indicesChanged = true;
            }

            radius += _pixelSize; // Account for AA.
            Vector2 size = new Vector2(radius / (float)Math.Cos(Math.PI / 6.0), radius);

            var topLeft = center - size;
            var topRight = center + new Vector2(size.X, -size.Y);
            var bottomRight = center + size;
            var bottomLeft = center + new Vector2(-size.X, size.Y);

            float ps = _pixelSize / (radius * 2);

            float ux = 1.0f / (float)Math.Cos(Math.PI / 6.0);
            float uy = 1.0f;

            _vertices[_vertexCount + 0] = new VertexShape(new Vector3(topLeft, 0), new Vector2(-ux, -uy), 3f, c1, c2, thickness, ps);
            _vertices[_vertexCount + 1] = new VertexShape(new Vector3(topRight, 0), new Vector2(ux, -uy), 3f, c1, c2, thickness, ps);
            _vertices[_vertexCount + 2] = new VertexShape(new Vector3(bottomRight, 0), new Vector2(ux, uy), 3f, c1, c2, thickness, ps);
            _vertices[_vertexCount + 3] = new VertexShape(new Vector3(bottomLeft, 0), new Vector2(-ux, uy), 3f, c1, c2, thickness, ps);

            _triangleCount += 2;
            _vertexCount += 4;
            _indexCount += 6;
        }
        public void FillHexagon(Vector2 center, float radius, Color c) {
            DrawCircle(center, radius, c, c, 0f);
        }
        public void BorderHexagon(Vector2 center, float radius, Color c, float thickness = 1f) {
            DrawCircle(center, radius, Color.Transparent, c, thickness);
        }

        public void End() {
            Flush();

            // TODO: Restore old states like rasterizer, depth stencil, blend state?
        }

        private void Flush() {
            if (_triangleCount == 0) return;

            _effect.Parameters["view_projection"].SetValue(_view * _projection);

            if (_indicesChanged) {
                _vertexBuffer.Dispose();
                _indexBuffer.Dispose();

                _vertexBuffer = new DynamicVertexBuffer(_graphicsDevice, typeof(VertexShape), _vertices.Length, BufferUsage.WriteOnly);

                GenerateIndexArray(ref _indices, _fromIndex);

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

        private bool EnsureSizeOrDouble<T>(ref T[] array, int neededCapacity) {
            if (array.Length < neededCapacity) {
                Array.Resize(ref array, array.Length * 2);
                return true;
            }
            return false;
        }

        private void GenerateIndexArray(ref uint[] array, int index = 0) {
            uint i = Floor(index, 6, 6);
            uint j = Floor(index, 6, 4);
            for (; i < array.Length; i += 6, j += 4) {
                array[i + 0] = j + 0;
                array[i + 1] = j + 1;
                array[i + 2] = j + 3;
                array[i + 3] = j + 1;
                array[i + 4] = j + 2;
                array[i + 5] = j + 3;
            }
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

        private bool _indicesChanged = false;
        private int _fromIndex = 0;
    }
}
