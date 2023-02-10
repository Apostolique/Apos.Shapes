using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Apos.Shapes {
    public class ShapeBatch {
        public ShapeBatch(GraphicsDevice graphicsDevice, ContentManager content) {
            _graphicsDevice = graphicsDevice;

            _effect = content.Load<Effect>("apos-shapes-effect");

            _vertices = new VertexShape[MAX_VERTICES];
            _indices = GenerateIndexArray();

            _vertexBuffer = new DynamicVertexBuffer(_graphicsDevice, typeof(VertexShape), _vertices.Length, BufferUsage.WriteOnly);

            _indexBuffer = new IndexBuffer(_graphicsDevice, typeof(ushort), _indices.Length, BufferUsage.WriteOnly);
            _indexBuffer.SetData(_indices);

            // TODO: Allow batch to resize instead of capping at 2048 shapes per batch.
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

            if (_triangleCount >= MAX_TRIANGLES) {
                Flush();
            }
        }
        public void FillCircle(Vector2 center, float radius, Color c) {
            DrawCircle(center, radius, c, c, 0f);
        }
        public void BorderCircle(Vector2 center, float radius, Color c, float thickness = 1f) {
            DrawCircle(center, radius, Color.Transparent, c, thickness);
        }
        public void DrawRectangle(Vector2 xy, Vector2 size, Color c1, Color c2, float thickness = 1f) {
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

            if (_triangleCount >= MAX_TRIANGLES) {
                Flush();
            }
        }
        public void FillRectangle(Vector2 xy, Vector2 size, Color c) {
            DrawRectangle(xy, size, c, c, 0f);
        }
        public void BorderRectangle(Vector2 xy, Vector2 size, Color c, float thickness = 1f) {
            DrawRectangle(xy, size, Color.Transparent, c, thickness);
        }
        public void DrawLine(Vector2 a, Vector2 b, float radius, Color c1, Color c2, float thickness = 1f) {
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

            if (_triangleCount >= MAX_TRIANGLES) {
                Flush();
            }
        }
        public void FillLine(Vector2 a, Vector2 b, float radius, Color c) {
            DrawLine(a, b, radius, c, c, 0f);
        }
        public void BorderLine(Vector2 a, Vector2 b, float radius, Color c, float thickness = 1f) {
            DrawLine(a, b, radius, Color.Transparent, c, thickness);
        }

        public void DrawHexagon(Vector2 center, float radius, Color c1, Color c2, float thickness = 1f) {
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

            if (_triangleCount >= MAX_TRIANGLES) {
                Flush();
            }
        }
        public void FillHexagon(Vector2 center, float radius, Color c) {
            DrawCircle(center, radius, c, c, 0f);
        }
        public void BorderHexagon(Vector2 center, float radius, Color c, float thickness = 1f) {
            DrawCircle(center, radius, Color.Transparent, c, thickness);
        }

        public void End() {
            Flush();
        }

        private void Flush() {
            if (_triangleCount == 0) return;

            _effect.Parameters["view_projection"].SetValue(_view * _projection);

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

        private static ushort[] GenerateIndexArray() {
            ushort[] result = new ushort[MAX_INDICES];
            for (int i = 0, j = 0; i < MAX_INDICES; i += 6, j += 4) {
                result[i + 0] = (ushort) (j + 0);
                result[i + 1] = (ushort) (j + 1);
                result[i + 2] = (ushort) (j + 3);
                result[i + 3] = (ushort) (j + 1);
                result[i + 4] = (ushort) (j + 2);
                result[i + 5] = (ushort) (j + 3);
            }
            return result;
        }

        const int MAX_SPRITES = 2048;
        const int MAX_TRIANGLES = 2048 * 2;
        const int MAX_VERTICES = 2048 * 4;
        const int MAX_INDICES = 2048 * 6;

        GraphicsDevice _graphicsDevice;
        VertexShape[] _vertices;
        ushort[] _indices;
        int _triangleCount = 0;
        int _vertexCount = 0;
        int _indexCount = 0;

        DynamicVertexBuffer _vertexBuffer;
        IndexBuffer _indexBuffer;

        Matrix _view;
        Matrix _projection;
        Effect _effect;

        float _pixelSize = 1f;
    }
}
