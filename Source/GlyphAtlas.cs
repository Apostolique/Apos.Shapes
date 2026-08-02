// The two textures a GlyphTable's arenas mirror into.

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Apos.Shapes {
    // Brings the band and curve textures up to date with a table's arenas. Everything above
    // this is device free, so a bake can be checked without a graphics device at all; this is
    // the only part that needs one.
    //
    // Both textures are RGBA32F. The band texture only carries two values per texel, but a
    // WebGL1 context that has OES_texture_float at all has it for RGBA, and not always for two
    // channel floats, so the pair rides in the first two channels of a four channel texel.
    //
    // KNI's GL family cannot upload a float texture through the browser at all, so there it
    // repacks to RGBA8 instead. See GlyphRepack for the encodings and apos-shapes.fx for the
    // matching decode.
    internal sealed class GlyphAtlas : IDisposable {
        internal Texture2D? Band;
        internal Texture2D? Curve;

        internal void Upload(GraphicsDevice graphicsDevice, GlyphTable table) {
#if KNI
            if (Repacks(graphicsDevice)) {
                Band = SyncBand8(graphicsDevice, Band, table.Band);
                Curve = SyncCurve8(graphicsDevice, Curve, table.Curve);
                return;
            }
#endif
            Band = Sync(graphicsDevice, Band, table.Band);
            Curve = Sync(graphicsDevice, Curve, table.Curve);
        }

        // Growth rebuilds the texture and refills it whole, which keeps every already seated
        // texel index valid. Otherwise only the rows written since the last upload go up.
        private static Texture2D? Sync(GraphicsDevice graphicsDevice, Texture2D? texture, TexelArena arena) {
            if (arena.Rows == 0) return texture;
            if (texture == null || arena.Grew) {
                texture?.Dispose();
                texture = new Texture2D(graphicsDevice, arena.Width, arena.Rows, false, SurfaceFormat.Vector4);
            }
            if (arena.DirtyTo > arena.DirtyFrom) {
                // A texture takes whole rows, so a write that starts or ends mid row rounds out
                // to the rows it touched.
                int y = arena.DirtyFrom / arena.Width;
                int rows = (arena.DirtyTo + arena.Width - 1) / arena.Width - y;
                texture.SetData(0, new Rectangle(0, y, arena.Width, rows), arena.Data,
                                y * arena.Width * 4, rows * arena.Width * 4);
            }
            arena.Uploaded();
            return texture;
        }

#if KNI
        // The DirectX backends of KNI load the standard mgfx shader and take a float texture the
        // same way the MonoGame ones do, so only the GL family repacks. This matches the pick in
        // ShapeBatch.LoadEmbeddedEffect: a custom effect compiled for a different backend than
        // the device is running would disagree with it, the same way it already disagrees about
        // which bytecode to load.
        private static bool Repacks(GraphicsDevice graphicsDevice) {
            GraphicsBackend backend = graphicsDevice.Adapter.Backend;
            return backend != GraphicsBackend.DirectX11 && backend != GraphicsBackend.DirectX12;
        }

        // Scratch for the rows going up. The interop hands WebGL a view over the whole managed
        // array and ignores both the start index and the element count it was given, so the array
        // has to hold exactly the rectangle's bytes and nothing else. It is kept between uploads
        // and only replaced when the size changes, which for a working set that has settled is
        // never, since a run with no new glyphs uploads nothing at all.
        private byte[] _bandBytes = Array.Empty<byte>();
        private byte[] _curveBytes = Array.Empty<byte>();

        private static byte[] Fit(ref byte[] scratch, int bytes) {
            if (scratch.Length != bytes) scratch = new byte[bytes];
            return scratch;
        }

        private Texture2D? SyncBand8(GraphicsDevice graphicsDevice, Texture2D? texture, TexelArena arena) {
            if (arena.Rows == 0) return texture;
            if (texture == null || arena.Grew) {
                texture?.Dispose();
                texture = new Texture2D(graphicsDevice, arena.Width, arena.Rows, false, SurfaceFormat.Color);
            }
            if (arena.DirtyTo > arena.DirtyFrom) {
                int y = arena.DirtyFrom / arena.Width;
                int rows = (arena.DirtyTo + arena.Width - 1) / arena.Width - y;
                byte[] bytes = Fit(ref _bandBytes, rows * arena.Width * 4);
                GlyphRepack.EncodeBandRows(arena.Data, arena.Width, y, rows, bytes);
                texture.SetData(0, new Rectangle(0, y, arena.Width, rows), bytes, 0, bytes.Length);
            }
            arena.Uploaded();
            return texture;
        }

        // Twice the rows of the arena: the low bytes of a logical texel's four values sit in row
        // 2y and the high bytes in row 2y + 1.
        private Texture2D? SyncCurve8(GraphicsDevice graphicsDevice, Texture2D? texture, TexelArena arena) {
            if (arena.Rows == 0) return texture;
            if (texture == null || arena.Grew) {
                texture?.Dispose();
                texture = new Texture2D(graphicsDevice, arena.Width, arena.Rows * 2, false, SurfaceFormat.Color);
            }
            if (arena.DirtyTo > arena.DirtyFrom) {
                int y = arena.DirtyFrom / arena.Width;
                int rows = (arena.DirtyTo + arena.Width - 1) / arena.Width - y;
                byte[] bytes = Fit(ref _curveBytes, rows * arena.Width * 8);
                GlyphRepack.EncodeCurveRows(arena.Data, arena.Width, y, rows, bytes);
                texture.SetData(0, new Rectangle(0, y * 2, arena.Width, rows * 2), bytes, 0, bytes.Length);
            }
            arena.Uploaded();
            return texture;
        }
#endif

        public void Dispose() {
            Band?.Dispose();
            Curve?.Dispose();
            Band = null;
            Curve = null;
        }
    }
}
