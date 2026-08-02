// Where baked glyphs live inside the two glyph textures, and what recycles the room when
// they stop being drawn.

using System;
using System.Collections.Generic;

namespace Apos.Shapes {
    // A growable run of RGBA32F texels addressed by linear index, the way the shader addresses
    // both glyph textures. Rows are added at the end, so a linear index stays valid for the
    // life of the block it points into no matter how far the arena grows.
    //
    // Every block is an even number of texels and the width is even, so every block starts on
    // an even column. That is what keeps a curve's second texel in the same row as its first,
    // which the shader's fetch of the texel right after a curve's own requires.
    internal sealed class TexelArena {
        internal TexelArena(int width, int maxRows) {
            Width = width;
            MaxRows = maxRows;
        }

        internal readonly int Width;
        internal readonly int MaxRows;

        internal float[] Data = Array.Empty<float>();
        internal int Rows;
        // Set when rows were added, which is the one thing a texture cannot absorb in place.
        internal bool Grew;
        // The linear texel range written since the last upload, empty when To is not past From.
        internal int DirtyFrom = int.MaxValue;
        internal int DirtyTo;

        // First texel past everything handed out, and the blocks handed back by eviction.
        private int _end;
        private readonly List<int> _freeAt = new();
        private readonly List<int> _freeLen = new();

        internal int Capacity => Width * Rows;

        // A block of the given length, or -1 when the arena is at its ceiling and only an
        // eviction can make room.
        internal int Allocate(int texels) {
            for (int i = 0; i < _freeLen.Count; i++) {
                if (_freeLen[i] < texels) continue;
                int at = _freeAt[i];
                if (_freeLen[i] == texels) {
                    _freeAt.RemoveAt(i);
                    _freeLen.RemoveAt(i);
                } else {
                    _freeAt[i] = at + texels;
                    _freeLen[i] -= texels;
                }
                return at;
            }
            while (_end + texels > Capacity) {
                if (Rows >= MaxRows) return -1;
                Grow();
            }
            int start = _end;
            _end += texels;
            return start;
        }

        // Gives a block back, coalescing with either neighbour so a run of evictions leaves one
        // block rather than a sieve. A block that reaches the end goes back to the bump pointer
        // instead of the free list, which is what keeps a table that fills and empties from
        // growing forever.
        internal void Free(int at, int texels) {
            int i = 0;
            while (i < _freeAt.Count && _freeAt[i] < at) i++;
            _freeAt.Insert(i, at);
            _freeLen.Insert(i, texels);
            if (i + 1 < _freeAt.Count && _freeAt[i] + _freeLen[i] == _freeAt[i + 1]) {
                _freeLen[i] += _freeLen[i + 1];
                _freeAt.RemoveAt(i + 1);
                _freeLen.RemoveAt(i + 1);
            }
            if (i > 0 && _freeAt[i - 1] + _freeLen[i - 1] == _freeAt[i]) {
                _freeLen[i - 1] += _freeLen[i];
                _freeAt.RemoveAt(i);
                _freeLen.RemoveAt(i);
                i--;
            }
            if (_freeAt[i] + _freeLen[i] == _end) {
                _end = _freeAt[i];
                _freeAt.RemoveAt(i);
                _freeLen.RemoveAt(i);
            }
        }

        internal void Mark(int at, int texels) {
            if (at < DirtyFrom) DirtyFrom = at;
            if (at + texels > DirtyTo) DirtyTo = at + texels;
        }

        internal void Uploaded() {
            DirtyFrom = int.MaxValue;
            DirtyTo = 0;
            Grew = false;
        }

        private void Grow() {
            int rows = Rows == 0 ? 1 : Math.Min(MaxRows, Rows * 2);
            var data = new float[Width * rows * 4];
            Data.CopyTo(data, 0);
            Data = data;
            Rows = rows;
            Grew = true;
            // The texture behind this is rebuilt empty, so everything live goes up again.
            DirtyFrom = 0;
            DirtyTo = _end;
        }
    }

    // A batch's record of which glyphs are resident in its two glyph textures. Each ShapeBatch
    // owns its own, so batches never contend for room; the batch mirrors the arenas into its
    // textures at flush time.
    internal sealed class GlyphTable {
        internal GlyphTable() { }

        // Even, so a curve's two texels always share a row. 2048 is the widest a texture is
        // guaranteed to be able to be everywhere the library runs.
        internal const int Width = 2048;
        // The ceiling on each texture, which is what makes eviction rather than growth the
        // answer for a working set that keeps climbing. A linear index stays well inside the
        // 2^22 an interpolator carries exactly.
        internal const int MaxRows = 256;

        internal readonly TexelArena Band = new(Width, MaxRows);
        internal readonly TexelArena Curve = new(Width, MaxRows);

        private readonly List<BakedGlyph?> _glyphs = new();
        private readonly List<int> _bandAt = new();
        private readonly List<int> _bandLen = new();
        private readonly List<int> _curveAt = new();
        private readonly List<int> _curveLen = new();
        // One stamp per entry: an entry is only touched when a quad is about to pack it, so use
        // recency and pack recency are the same clock. An entry is pinned while its stamp is
        // newer than the last flush, meaning an undrawn quad references it.
        private readonly List<long> _stamps = new();
        private readonly Dictionary<BakedGlyph, int> _byGlyph = new();
        private readonly List<int> _vacant = new();
        private long _clock;
        private long _flushClock;

        internal int Count => _byGlyph.Count;

        // Everything packed so far is drawn, so every entry is free to recycle.
        internal void Flushed() => _flushClock = _clock;

        // Where a seated glyph's band block starts, which is what the quad carries as its band
        // texel base.
        internal int BandBase(int index) => _bandAt[index];
        internal int CurveBase(int index) => _curveAt[index];

        // The entry seating this glyph, stamped as about to pack; seats and recycles as needed.
        // Returns -1 when every resident glyph is pinned, which the batch's pre-pin turns into a
        // flush; a flush unpins everything, so the reseat can't fail.
        //
        // The residency map is the whole lookup. A glyph carries no hint of its own, which is
        // what makes one font shareable across any number of batches: each table answers out of
        // its own map, so two batches can't thrash a single cached answer between them, and the
        // lookup itself allocates nothing.
        internal int Resolve(BakedGlyph glyph) {
            if (_byGlyph.TryGetValue(glyph, out int found)) {
                _stamps[found] = ++_clock;
                return found;
            }
            int i = Add(glyph);
            if (i >= 0) _stamps[i] = ++_clock;
            return i;
        }

        private int Add(BakedGlyph glyph) {
            int bandLen = glyph.BandTexelCount;
            int curveLen = glyph.CurveTexelCount;
            int bandAt = 0;
            int curveAt = 0;
            // A glyph with no outline takes no room. It still gets an entry so the lookup and
            // the pin behave the same for every glyph a line of text walks over.
            if (bandLen > 0) {
                bandAt = Band.Allocate(bandLen);
                while (bandAt < 0) {
                    if (!Evict()) return -1;
                    bandAt = Band.Allocate(bandLen);
                }
                curveAt = Curve.Allocate(curveLen);
                while (curveAt < 0) {
                    if (!Evict()) {
                        Band.Free(bandAt, bandLen);
                        return -1;
                    }
                    curveAt = Curve.Allocate(curveLen);
                }
                Seat(glyph, bandAt, curveAt);
            }

            int index;
            if (_vacant.Count > 0) {
                index = _vacant[_vacant.Count - 1];
                _vacant.RemoveAt(_vacant.Count - 1);
                _glyphs[index] = glyph;
                _bandAt[index] = bandAt;
                _bandLen[index] = bandLen;
                _curveAt[index] = curveAt;
                _curveLen[index] = curveLen;
                _stamps[index] = 0;
            } else {
                index = _glyphs.Count;
                _glyphs.Add(glyph);
                _bandAt.Add(bandAt);
                _bandLen.Add(bandLen);
                _curveAt.Add(curveAt);
                _curveLen.Add(curveLen);
                _stamps.Add(0);
            }
            _byGlyph[glyph] = index;
            return index;
        }

        // Copies a baked glyph's two blocks into the arenas at the places it just took, turning
        // the block relative addresses the bake produced into the absolute ones the shader
        // reads: a header's curve list offset becomes a linear texel index into the band
        // texture, and a list entry's relative curve index becomes the 2D texel coordinate the
        // curve's first texel actually sits at.
        private void Seat(BakedGlyph glyph, int bandAt, int curveAt) {
            float[] src = glyph.CurveTexels;
            float[] dst = Curve.Data;
            Array.Copy(src, 0, dst, curveAt * 4, src.Length);
            Curve.Mark(curveAt, glyph.CurveTexelCount);

            src = glyph.BandTexels;
            dst = Band.Data;
            int texels = glyph.BandTexelCount;
            int heads = glyph.Bands * 2;
            for (int t = 0; t < texels; t++) {
                int s = t * 4;
                int d = (bandAt + t) * 4;
                if (t < heads) {
                    dst[d] = src[s];
                    dst[d + 1] = src[s + 1] + bandAt;
                } else {
                    int texel = curveAt + (int)src[s];
                    dst[d] = texel % Curve.Width;
                    dst[d + 1] = texel / Curve.Width;
                }
                dst[d + 2] = 0f;
                dst[d + 3] = 0f;
            }
            Band.Mark(bandAt, texels);
        }

        // Drops the resident glyph that has gone longest undrawn, giving both its blocks back.
        // A pinned glyph is one an undrawn quad still points at, so it is never a candidate.
        private bool Evict() {
            int victim = -1;
            long oldest = long.MaxValue;
            for (int i = 0; i < _glyphs.Count; i++) {
                if (_glyphs[i] == null) continue;
                if (_stamps[i] <= _flushClock && _stamps[i] < oldest) {
                    oldest = _stamps[i];
                    victim = i;
                }
            }
            if (victim < 0) return false;
            if (_bandLen[victim] > 0) {
                Band.Free(_bandAt[victim], _bandLen[victim]);
                Curve.Free(_curveAt[victim], _curveLen[victim]);
            }
            _byGlyph.Remove(_glyphs[victim]!);
            _glyphs[victim] = null;
            _stamps[victim] = 0;
            _vacant.Add(victim);
            return true;
        }
    }
}
