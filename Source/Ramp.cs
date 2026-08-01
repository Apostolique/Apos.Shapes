using System;
using System.Collections.Generic;

namespace Apos.Shapes {
    /// <summary>
    /// Reshapes how a <see cref="Gradient"/> travels between its colors. The gradient value runs
    /// through this curve first, so stops land where the curve puts them instead of evenly.
    /// Stops are (position, value) pairs in [0, 1] with straight lines between them. Two stops
    /// on the same position make a hard edge. Positions snap to a 256 step grid when the curve
    /// bakes, so a stop lands within 1/512 of where it was asked for.
    /// Build ramps once and reuse them where possible: each distinct curve takes a row of a
    /// 256 row table in every batch that draws it. When a table fills, the row that has gone
    /// longest undrawn is recycled, so curves that rebake every frame displace stale ones
    /// instead of running the table out. Going past 256 distinct curves in a single batch
    /// makes the batch flush early to make room, which costs a draw call.
    /// </summary>
    public sealed class Ramp {
        /// <param name="stops">
        /// (position, value) pairs in [0, 1], in any order. Two stops on the same position make a
        /// hard edge, and the order they are given in decides which side is which.
        /// </param>
        /// <exception cref="ArgumentException">No stops were given.</exception>
        public Ramp(params (float Position, float Value)[] stops) {
            if (stops == null || stops.Length == 0) {
                throw new ArgumentException("A ramp needs at least one stop.", nameof(stops));
            }

            int n = stops.Length;
            var pos = new float[n];
            var val = new float[n];
            for (int i = 0; i < n; i++) {
                // Positions snap to the texel grid here rather than at eval time so a pair of
                // stops that lands on the same boundary reads back as the hard edge it becomes.
                pos[i] = MathF.Round(Math.Clamp(stops[i].Position, 0f, 1f) * Width) / Width;
                val[i] = Math.Clamp(stops[i].Value, 0f, 1f);
            }
            // Insertion sort, stable: stops sharing a position keep their given order, which is
            // what decides the two sides of a hard edge.
            for (int i = 1; i < n; i++) {
                float p = pos[i];
                float v = val[i];
                int j = i - 1;
                while (j >= 0 && pos[j] > p) {
                    pos[j + 1] = pos[j];
                    val[j + 1] = val[j];
                    j--;
                }
                pos[j + 1] = p;
                val[j + 1] = v;
            }

            // Each texel takes two RGBA8 slots, split into bytes the shader reads apart by
            // hand (hardware filtering would blend the low and high bytes independently, same
            // reason the elliptical arc table is read that way). The first slot keeps the
            // curve's value at the texel's two edges as unorm16 pairs, with one-sided limits so
            // a jump sits exactly between two texels and stays a jump. The second keeps the
            // curve's running integral at the texel's start as a 24 bit fraction of the whole
            // row, accumulated from the quantized edge values so the shader's own trapezoids
            // land on exactly the same numbers. The integral is what lets the shader box
            // filter a hard stop at any width in one subtraction instead of a texel walk.
            var row = new byte[Width * 8];
            double acc = 0.0;
            for (int i = 0; i < Width; i++) {
                int v0 = (int)MathF.Round(Eval(pos, val, i / (float)Width, fromLeft: false) * 65535f);
                int v1 = (int)MathF.Round(Eval(pos, val, (i + 1) / (float)Width, fromLeft: true) * 65535f);
                int f = (int)Math.Round(acc / Width * 16777215.0);
                row[i * 8] = (byte)(v0 & 255);
                row[i * 8 + 1] = (byte)(v0 >> 8);
                row[i * 8 + 2] = (byte)(v1 & 255);
                row[i * 8 + 3] = (byte)(v1 >> 8);
                row[i * 8 + 4] = (byte)(f & 255);
                row[i * 8 + 5] = (byte)(f >> 8 & 255);
                row[i * 8 + 6] = (byte)(f >> 16);
                acc += (v0 + v1) / 2.0 / 65535.0;
            }

            Baked = row;
            Hash = RampTable.Hash(row);
        }

        // The baked bytes and their content hash; each batch's table seats a row from these.
        internal readonly byte[] Baked;
        internal readonly ulong Hash;
        // Where the bytes last resolved, swapped whole; see RampSlot.
        private RampSlot? _at;

        internal const int Width = 256;

        // The row this curve occupies in the given table right now, stamped as about to pack.
        internal int Row(RampTable table) => table.Resolve(Baked, Hash, ref _at);

        // Whether the table can seat this curve without evicting a row an undrawn shape still
        // needs. Resolving is the check; a full table of pinned rows is the one way it fails.
        internal bool TryPin(RampTable table) => Row(table) >= 0;

        // The curve at x, approaching from one side. From the left the first stop on x ends the
        // incoming segment; from the right the last stop on x starts the outgoing one. Outside
        // the outermost stops the curve holds flat.
        private static float Eval(float[] pos, float[] val, float x, bool fromLeft) {
            int n = pos.Length;
            if (fromLeft) {
                int i = 0;
                while (i < n && pos[i] < x) i++;
                if (i == n) return val[n - 1];
                if (pos[i] == x || i == 0) return val[i];
                return val[i - 1] + (val[i] - val[i - 1]) * ((x - pos[i - 1]) / (pos[i] - pos[i - 1]));
            } else {
                int i = n - 1;
                while (i >= 0 && pos[i] > x) i--;
                if (i < 0) return val[0];
                if (pos[i] == x || i == n - 1) return val[i];
                return val[i] + (val[i + 1] - val[i]) * ((x - pos[i]) / (pos[i + 1] - pos[i]));
            }
        }
    }

    // An immutable (table, row, generation) fact recording where some baked bytes last
    // resolved. Swapped as one reference so two batches on different threads sharing a curve
    // can't tear each other's hint: a stale or foreign hint misses and re-resolves, it can
    // never validate against the wrong table's row.
    internal sealed class RampSlot {
        internal RampSlot(RampTable table, int index, int gen) {
            Table = table;
            Index = index;
            Gen = gen;
        }
        internal readonly RampTable Table;
        internal readonly int Index;
        internal readonly int Gen;
    }

    /// <summary>
    /// A batch's table of the ramp and color ramp rows its shapes reference, up to 256. Each
    /// <see cref="ShapeBatch"/> owns its own, so batches never contend for rows; the batch
    /// mirrors it into its ramp atlas at flush time.
    /// </summary>
    public sealed class RampTable {
        internal RampTable() { }

        internal const int MaxRows = 256; // The row index travels in 8 bits.

        private readonly List<byte[]> _rows = new();
        private readonly List<int> _gens = new();
        private readonly List<ulong> _hashes = new();
        // One stamp per row: a row is only touched when a quad is about to pack it, so use
        // recency and pack recency are the same clock. A row is pinned while its stamp is
        // newer than the last flush, meaning an undrawn quad references it.
        private readonly List<long> _stamps = new();
        private readonly Dictionary<ulong, List<int>> _byHash = new();
        private long _clock;
        private long _flushClock;

        internal int Count => _rows.Count;

        // Everything packed so far is drawn, so every row is free to recycle.
        internal void Flushed() => _flushClock = _clock;

        // The row seating these bytes, stamped as about to pack; adds and recycles as needed.
        // The hint skips the content lookup while its generation holds, and a recycled row is
        // exactly what a generation mismatch detects. Returns -1 when every row is pinned,
        // which the batch's pre-pin turns into a flush; a flush unpins everything, so the
        // repin can't fail.
        internal int Resolve(byte[] row, ulong hash, ref RampSlot? at) {
            RampSlot? s = at;
            if (s != null && s.Table == this && s.Gen == _gens[s.Index]) {
                _stamps[s.Index] = ++_clock;
                return s.Index;
            }
            int i = AddOrFind(row, hash);
            if (i >= 0) {
                at = new RampSlot(this, i, _gens[i]);
                _stamps[i] = ++_clock;
            }
            return i;
        }

        private int AddOrFind(byte[] row, ulong hash) {
            if (_byHash.TryGetValue(hash, out var bucket)) {
                // Identical bytes collapse to one row, so rebuilding the same curve every
                // frame costs a bake but never a row. The reference compare catches the same
                // array coming back before the bytes have to be walked.
                foreach (int i in bucket) {
                    if (_rows[i] == row || row.AsSpan().SequenceEqual(_rows[i])) return i;
                }
            } else {
                bucket = new List<int>();
                _byHash[hash] = bucket;
            }
            if (_rows.Count < MaxRows) {
                _rows.Add(row);
                _gens.Add(1);
                _hashes.Add(hash);
                _stamps.Add(0);
                bucket.Add(_rows.Count - 1);
                return _rows.Count - 1;
            }
            int victim = -1;
            long oldest = long.MaxValue;
            for (int i = 0; i < MaxRows; i++) {
                if (_stamps[i] <= _flushClock && _stamps[i] < oldest) {
                    oldest = _stamps[i];
                    victim = i;
                }
            }
            if (victim < 0) return -1;
            var oldBucket = _byHash[_hashes[victim]];
            oldBucket.Remove(victim);
            if (oldBucket.Count == 0 && _hashes[victim] != hash) {
                _byHash.Remove(_hashes[victim]);
            }
            _rows[victim] = row;
            _gens[victim]++;
            _hashes[victim] = hash;
            _stamps[victim] = 0;
            bucket.Add(victim);
            return victim;
        }

        // The rows whose generation the batch's atlas hasn't seen, in one pass: the indices
        // land in dirty sorted, their bytes packed tight into buffer in the same order, and
        // uploadedGen is stamped to match. Returns the table's row count.
        internal int CollectDirty(int[] uploadedGen, List<int> dirty, ref byte[] buffer) {
            dirty.Clear();
            for (int i = 0; i < _rows.Count; i++) {
                if (uploadedGen[i] != _gens[i]) dirty.Add(i);
            }
            int size = dirty.Count * Ramp.Width * 8;
            if (buffer.Length < size) buffer = new byte[size];
            for (int k = 0; k < dirty.Count; k++) {
                int i = dirty[k];
                _rows[i].CopyTo(buffer, k * Ramp.Width * 8);
                uploadedGen[i] = _gens[i];
            }
            return _rows.Count;
        }

        internal static ulong Hash(byte[] data) {
            ulong h = 14695981039346656037ul;
            foreach (byte b in data) h = (h ^ b) * 1099511628211ul;
            return h;
        }
    }
}
