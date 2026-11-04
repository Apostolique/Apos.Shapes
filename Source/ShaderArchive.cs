using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Apos.Shapes {
    /// <summary>
    /// The versions of the shader for one backend, as Tools/ShaderPack packed them: one effect
    /// per version, in chunks that unpack the first time a version in them loads. Every batch in
    /// the process shares one, so a chunk unpacks once.
    /// </summary>
    internal sealed class ShaderArchive {
        private ShaderArchive(byte[] file) {
            using var r = new BinaryReader(new MemoryStream(file));
            if (r.ReadUInt32() != 0x50535041) throw new InvalidDataException("Not a packed shader.");
            int chunkCount = r.ReadInt32();
            _chunkStarts = new int[chunkCount];
            _chunkLengths = new int[chunkCount];
            _chunkSizes = new int[chunkCount];
            _chunks = new WeakReference<byte[]>?[chunkCount];
            for (int i = 0; i < chunkCount; i++) _chunkLengths[i] = r.ReadInt32();
            int entryCount = r.ReadInt32();
            for (int i = 0; i < entryCount; i++) {
                int name = r.ReadInt32();
                int chunk = r.ReadInt32();
                int offset = r.ReadInt32();
                int length = r.ReadInt32();
                _entries[name] = (chunk, offset, length);
                _chunkSizes[chunk] = Math.Max(_chunkSizes[chunk], offset + length);
            }
            int start = (int)r.BaseStream.Position;
            for (int i = 0; i < chunkCount; i++) {
                _chunkStarts[i] = start;
                start += _chunkLengths[i];
            }
            _file = file;
        }

        /// <summary>The technique names of the versions there are.</summary>
        public IEnumerable<int> Names => _entries.Keys;

        /// <summary>The archive in an embedded resource, read the first time any batch asks.</summary>
        public static ShaderArchive For(string resource) {
            lock (_loaded) {
                if (!_loaded.TryGetValue(resource, out ShaderArchive? archive)) {
                    using Stream stream = typeof(ShaderArchive).Assembly.GetManifestResourceStream(resource)
                        ?? throw new InvalidOperationException($"Missing embedded resource \"{resource}\".");
                    var bytes = new byte[stream.Length];
                    stream.ReadExactly(bytes);
                    archive = _loaded[resource] = new ShaderArchive(bytes);
                }
                return archive;
            }
        }

        /// <summary>The effect file of the version with this technique name.</summary>
        public byte[] Read(int name) {
            var (chunk, offset, length) = _entries[name];
            byte[]? unpacked;
            lock (_chunks) {
                // Held weakly: a game that has loaded the versions it draws with is done with the
                // chunk, and one that needs another later pays an unpack next to its compile.
                if (_chunks[chunk] is not { } held || !held.TryGetTarget(out unpacked)) {
                    unpacked = Unpack(_file.AsSpan(_chunkStarts[chunk], _chunkLengths[chunk]), _chunkSizes[chunk]);
                    _chunks[chunk] = new WeakReference<byte[]>(unpacked);
                }
            }
            byte[] effect = unpacked.AsSpan(offset, length).ToArray();
            UseHighpFloats(effect);
            return effect;
        }

        private static byte[] Unpack(ReadOnlySpan<byte> packed, int size) {
            var unpacked = new byte[size];
#if KNI
            // Gzip over long repeats turned into references; see Lz in Tools/ShaderPack.
            using var gzip = new GZipStream(new MemoryStream(packed.ToArray()), CompressionMode.Decompress);
            using var tokens = new MemoryStream();
            gzip.CopyTo(tokens);
            byte[] t = tokens.GetBuffer();
            int i = 0, o = 0;
            while (true) {
                int literals = Varint(t, ref i);
                Array.Copy(t, i, unpacked, o, literals);
                i += literals;
                o += literals;
                int length = Varint(t, ref i);
                if (length == 0) break;
                int from = o - Varint(t, ref i);
                // Byte by byte, since a reference can overlap what it writes.
                for (int k = 0; k < length; k++) unpacked[o + k] = unpacked[from + k];
                o += length;
            }
#else
            if (!BrotliDecoder.TryDecompress(packed, unpacked, out int written) || written != size) {
                throw new InvalidDataException("A packed shader chunk didn't unpack.");
            }
#endif
            return unpacked;
        }

#if KNI
        private static int Varint(byte[] data, ref int i) {
            int v = 0;
            for (int shift = 0; ; shift += 7) {
                byte b = data[i++];
                v |= (b & 0x7F) << shift;
                if (b < 0x80) return v;
            }
        }
#endif

        /// <summary>
        /// ShadowDusk declares the GL pixel shader mediump, which phones run as 16-bit floats that
        /// can't hold pixel distances, so nothing draws. Swaps in highp, padded to keep lengths intact.
        /// </summary>
        private static void UseHighpFloats(byte[] bytes) {
            ReadOnlySpan<byte> mediump = "precision mediump float;"u8;
            ReadOnlySpan<byte> highp = "precision highp float;  "u8;
            Span<byte> rest = bytes;
            int i;
            while ((i = rest.IndexOf(mediump)) >= 0) {
                highp.CopyTo(rest[i..]);
                rest = rest[(i + highp.Length)..];
            }
        }

        private static readonly Dictionary<string, ShaderArchive> _loaded = new();
        private readonly byte[] _file;
        private readonly int[] _chunkStarts;
        private readonly int[] _chunkLengths;
        private readonly int[] _chunkSizes;
        private readonly WeakReference<byte[]>?[] _chunks;
        private readonly Dictionary<int, (int Chunk, int Offset, int Length)> _entries = new();
    }
}
