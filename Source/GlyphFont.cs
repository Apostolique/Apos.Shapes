// Reads outlines and metrics out of a TrueType file and bakes a glyph the first time it is
// asked for.
// Derived from Forme (MIT, Christopher Whitley), itself derived from the Slug reference
// shaders (Eric Lengyel, MIT/Apache-2.0, patent dedicated to the public domain 2026-03-17).
// See THIRD_PARTY_NOTICES.md.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using StbTrueTypeSharp;

namespace Apos.Shapes {
    // A font file, its metrics, and the glyphs baked out of it so far. Baking is on demand and
    // permanent: any code point the file has an outline for works without being named up
    // front, and a glyph is only ever read out of the file once.
    //
    // Lookups off the draw path never throw. A code point with no glyph, a glyph with no
    // outline and an outline this baker cannot express all come back as a baked glyph with no
    // bands, which carries its advance so a line of text still measures right.
    internal sealed class GlyphFont : IDisposable {
        /// <exception cref="ArgumentException">The bytes are not a font this can read.</exception>
        internal GlyphFont(byte[] ttf, int maxCurves = GlyphBake.MaxCurves) {
            if (ttf == null || ttf.Length == 0) {
                throw new ArgumentException("A font needs its file bytes.", nameof(ttf));
            }
            MaxCurves = Math.Clamp(maxCurves, 1, 64);
            // stb keeps the pointer it is handed rather than copying, so the bytes stay pinned
            // for as long as this font does. A fixed block would only hold them still for the
            // length of the constructor.
            _pin = GCHandle.Alloc(ttf, GCHandleType.Pinned);
            _info = new StbTrueType.stbtt_fontinfo();
            unsafe {
                if (StbTrueType.stbtt_InitFont(_info, (byte*)_pin.AddrOfPinnedObject(), 0) == 0) {
                    _info.Dispose();
                    _pin.Free();
                    throw new ArgumentException("The bytes could not be read as a TrueType font.", nameof(ttf));
                }
                int ascent, descent, lineGap;
                StbTrueType.stbtt_GetFontVMetrics(_info, &ascent, &descent, &lineGap);
                Ascent = ascent;
                Descent = descent;
                LineGap = lineGap;
                // stb has no direct reader for the head table's unitsPerEm, but the scale that
                // maps one em to one pixel is its reciprocal.
                float scale = StbTrueType.stbtt_ScaleForMappingEmToPixels(_info, 1f);
                UnitsPerEm = scale > 0f ? (int)MathF.Round(1f / scale) : 1000;
            }
            Quadratic = HasGlyfTable(ttf);
        }

        // Whether the file describes its glyphs with the quadratic curves this baker takes. A
        // font with no glyf table keeps its outlines in a CFF table instead, which is what most
        // .otf files do and what every one of those glyphs bakes blank from.
        private static bool HasGlyfTable(byte[] ttf) {
            if (ttf.Length < 12) return false;
            int tables = (ttf[4] << 8) | ttf[5];
            if (12L + tables * 16L > ttf.Length) return false;
            for (int i = 0; i < tables; i++) {
                int at = 12 + i * 16;
                if (ttf[at] == (byte)'g' && ttf[at + 1] == (byte)'l'
                 && ttf[at + 2] == (byte)'y' && ttf[at + 3] == (byte)'f') {
                    return true;
                }
            }
            return false;
        }

        private readonly GCHandle _pin;
        private readonly StbTrueType.stbtt_fontinfo _info;
        private readonly Dictionary<int, int> _indices = new();
        private readonly Dictionary<int, BakedGlyph> _glyphs = new();
        private readonly Dictionary<int, BakedGlyph> _byCodePoint = new();
        private readonly Dictionary<long, int> _kerning = new();
        private readonly List<GlyphCurve> _scratch = new();
        // A font outlives any one batch and nothing stops two of them being on different
        // threads, the same way a ramp can be. Everything behind here mutates on first use, and
        // the font file's own reader is not reentrant either, so the whole lookup takes the
        // lock. It is uncontended in the ordinary case and a glyph is only ever read once.
        private readonly object _gate = new();
        private bool _disposed;

        // Design units per em, and the vertical metrics in the same units. A line of text
        // advances by Ascent - Descent + LineGap, with Descent negative.
        internal readonly int UnitsPerEm;
        internal readonly int Ascent;
        internal readonly int Descent;
        internal readonly int LineGap;
        // What every band list in this font is padded out to.
        internal readonly int MaxCurves;
        // Whether this file's outlines are the quadratics the baker takes.
        internal readonly bool Quadratic;

        // The baked glyph a code point maps to, baking it on first use. A line of text walks
        // over one of these per character, so the code point gets a map of its own rather than
        // paying for the index lookup and the glyph lookup apart.
        internal BakedGlyph Lookup(int codePoint) {
            lock (_gate) {
                if (_byCodePoint.TryGetValue(codePoint, out BakedGlyph? baked)) return baked;
                if (!_indices.TryGetValue(codePoint, out int index)) {
                    unsafe {
                        index = StbTrueType.stbtt_FindGlyphIndex(_info, codePoint);
                    }
                    _indices[codePoint] = index;
                }
                if (!_glyphs.TryGetValue(index, out baked)) {
                    baked = Bake(index);
                    _glyphs[index] = baked;
                }
                _byCodePoint[codePoint] = baked;
                return baked;
            }
        }

        // The font's glyph index for a code point, or 0 for the missing glyph.
        internal int Index(int codePoint) {
            lock (_gate) {
                if (_indices.TryGetValue(codePoint, out int index)) return index;
                unsafe {
                    index = StbTrueType.stbtt_FindGlyphIndex(_info, codePoint);
                }
                _indices[codePoint] = index;
                return index;
            }
        }

        // The baked glyph for a font glyph index, baking it on first use.
        internal BakedGlyph Glyph(int glyph) {
            lock (_gate) {
                if (_glyphs.TryGetValue(glyph, out BakedGlyph? baked)) return baked;
                baked = Bake(glyph);
                _glyphs[glyph] = baked;
                return baked;
            }
        }

        // How much closer the second glyph sits to the first than its advance alone would put
        // it, in design units. Usually zero, and always zero for a font with no kerning.
        internal int Kerning(int left, int right) {
            long key = ((long)left << 32) | (uint)right;
            lock (_gate) {
                if (_kerning.TryGetValue(key, out int amount)) return amount;
                unsafe {
                    amount = StbTrueType.stbtt_GetGlyphKernAdvance(_info, left, right);
                }
                _kerning[key] = amount;
                return amount;
            }
        }

        private unsafe BakedGlyph Bake(int glyph) {
            int advance, bearing;
            StbTrueType.stbtt_GetGlyphHMetrics(_info, glyph, &advance, &bearing);

            StbTrueType.stbtt_vertex* verts;
            int count = StbTrueType.stbtt_GetGlyphShape(_info, glyph, &verts);
            if (count == 0) return Blank(glyph, advance, bearing);

            // Cubics come from CFF outlines. The Slug solver is quadratic only, so an OTF glyph
            // bakes blank rather than baking something that is not the glyph.
            for (int v = 0; v < count; v++) {
                if (verts[v].type == StbTrueType.STBTT_vcubic) {
                    StbTrueType.stbtt_FreeShape(_info, verts);
                    return Blank(glyph, advance, bearing);
                }
            }

            List<GlyphCurve> curves = _scratch;
            curves.Clear();
            float x = 0f, y = 0f;
            for (int v = 0; v < count; v++) {
                ref StbTrueType.stbtt_vertex vert = ref verts[v];
                float nx = vert.x;
                float ny = vert.y;
                switch ((int)vert.type) {
                    case StbTrueType.STBTT_vmove:
                        break;
                    case StbTrueType.STBTT_vline:
                        curves.Add(new GlyphCurve {
                            P1 = new Vector2(x, y),
                            P2 = new Vector2((x + nx) * 0.5f, (y + ny) * 0.5f),
                            P3 = new Vector2(nx, ny),
                        });
                        break;
                    case StbTrueType.STBTT_vcurve:
                        curves.Add(new GlyphCurve {
                            P1 = new Vector2(x, y),
                            P2 = new Vector2(vert.cx, vert.cy),
                            P3 = new Vector2(nx, ny),
                        });
                        break;
                }
                x = nx;
                y = ny;
            }
            StbTrueType.stbtt_FreeShape(_info, verts);
            if (curves.Count == 0) return Blank(glyph, advance, bearing);

            int x1, y1, x2, y2;
            StbTrueType.stbtt_GetGlyphBox(_info, glyph, &x1, &y1, &x2, &y2);
            return GlyphBake.Bake(curves, glyph, advance, bearing, x1, y1, x2, y2, UnitsPerEm, MaxCurves);
        }

        // A glyph with nothing to draw still advances the cursor, so whitespace does not
        // collapse and an unsupported outline leaves a gap the right size.
        private static BakedGlyph Blank(int glyph, int advance, int bearing) {
            return new BakedGlyph(glyph) {
                Advance = advance,
                Bearing = bearing,
                MaxCurves = 0,
            };
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            _info.Dispose();
            _pin.Free();
        }
    }
}
