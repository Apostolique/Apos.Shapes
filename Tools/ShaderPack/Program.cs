using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

// Builds every version of the shader that Apos.Shapes ships for one backend and packs them into
// one file for the assembly to embed.
//
// A version is ShapePixel with some of its features switched off, which is what keeps each one
// quick for the driver to compile. That matters on GL, where the driver compiles the shader from
// source, so the GL backends get the most versions; the others load bytecode that's quick to
// compile either way and keep a few. Each version is an effect of its own, since some backends
// create every shader in an effect as soon as it loads. ShadowDusk only reads the techniques
// written in the file it compiles, so each version gets a small file that includes the shader
// and spells its technique out.
//
// The versions are mostly the same code over again, which is what the packing is for: versions
// compressed together see the code each repeats from the others. MonoGame's are Brotli, all in one
// chunk, which unpacks fast enough to take whole. Brotli doesn't run in a browser though, so KNI's,
// which cover WebGL, go through a pass that turns long repeats into references and then through
// gzip; see Lz below. That unpacks slower, so the versions that share the costlier features go in
// chunks of their own, and a game only unpacks the ones it draws from.
//
// Usage: ShaderPack build <shader.fx> <output> <gl|bytecode> <brotli|lz> <vs model> <ps model> [ShadowDusk options...]
const string usage = "Usage: ShaderPack build <shader.fx> <output> <gl|bytecode> <brotli|lz> <vs model> <ps model> [ShadowDusk options...]";
if (args.Length < 7 || args[0] != "build" || args[3] is not ("gl" or "bytecode") || args[4] is not ("brotli" or "lz")) {
    Console.Error.WriteLine(usage);
    return 1;
}
string shader = Path.GetFullPath(args[1]);
string output = Path.GetFullPath(args[2]);
bool gl = args[3] == "gl";
bool brotli = args[4] == "brotli";
string vsModel = args[5];
string psModel = args[6];
string[] options = args[7..];

string work = output + ".parts";
Directory.CreateDirectory(work);
int[] versions = (gl ? Variants.Gl() : Variants.Bytecode()).ToArray();
var compiled = new byte[versions.Length][];
string? failure = null;
Parallel.For(0, versions.Length, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i => {
    int features = versions[i];
    int name = Variants.Name(features);
    string source = Path.Combine(work, $"shapes{name}.fx");
    string compiledPath = Path.Combine(work, $"shapes{name}.bin");
    File.WriteAllText(source, Variants.Wrapper(Path.GetFileName(shader), features, vsModel, psModel));
    var start = new ProcessStartInfo("dotnet") {
        WorkingDirectory = Path.GetDirectoryName(shader)!,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    foreach (string a in new[] { "tool", "run", "ShadowDuskCLI", source, compiledPath, "/I", Path.GetDirectoryName(shader)! }.Concat(options)) {
        start.ArgumentList.Add(a);
    }
    // Roll forward so the net8.0 tool still runs when only a newer runtime is installed, like in CI.
    start.Environment["DOTNET_ROLL_FORWARD"] = "Major";
    using var process = Process.Start(start)!;
    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    if (process.ExitCode != 0) {
        failure ??= $"ShadowDusk failed on version {name}:\n{stdout.Result}{stderr.Result}";
        return;
    }
    compiled[i] = File.ReadAllBytes(compiledPath);
});
if (failure != null) {
    Console.Error.WriteLine(failure);
    return 1;
}

Func<int, int> chunkKey = f => brotli ? 0 : Variants.ChunkKey(f);
var chunkKeys = versions.Select(chunkKey).Distinct().OrderBy(k => k).ToList();
var chunks = new List<byte[]>();
var entries = new List<(int Name, int Chunk, int Offset, int Length)>();
foreach (int key in chunkKeys) {
    using var raw = new MemoryStream();
    for (int i = 0; i < versions.Length; i++) {
        if (chunkKey(versions[i]) != key) continue;
        entries.Add((Variants.Name(versions[i]), chunks.Count, (int)raw.Length, compiled[i].Length));
        raw.Write(compiled[i]);
    }
    chunks.Add(brotli ? Brotli(raw.ToArray()) : Gzip(Lz.Encode(raw.ToArray())));
}

// Layout, all integers little endian: "APSP", the chunk count and each chunk's packed length,
// the version count and each version's technique name, chunk, offset and length in its unpacked
// chunk, then the chunks.
using (var file = File.Create(output))
using (var w = new BinaryWriter(file)) {
    w.Write("APSP"u8);
    w.Write(chunks.Count);
    foreach (byte[] c in chunks) w.Write(c.Length);
    w.Write(entries.Count);
    foreach (var e in entries) {
        w.Write(e.Name);
        w.Write(e.Chunk);
        w.Write(e.Offset);
        w.Write(e.Length);
    }
    foreach (byte[] c in chunks) w.Write(c);
}
Directory.Delete(work, true);
return 0;

static byte[] Brotli(byte[] raw) {
    byte[] packed = new byte[BrotliEncoder.GetMaxCompressedLength(raw.Length)];
    using var encoder = new BrotliEncoder(11, 24);
    encoder.Compress(raw, packed, out _, out int written, true);
    return packed[..written];
}

static byte[] Gzip(byte[] raw) {
    using var packed = new MemoryStream();
    using (var gzip = new GZipStream(packed, CompressionLevel.SmallestSize, true)) gzip.Write(raw);
    return packed.ToArray();
}

static class Variants {
    // The feature bits, the same as ShapeFeatures.
    const int Dashes = 1, Gradients = 2, BorderGradients = 4, Text = 8, Shapes = 16, Clips = 32, Ramps = 64, DenseText = 128;
    // Technique names carry these inverted, so the names a shader had before they were
    // switches still mean what they did: Shapes0 to Shapes15 have ellipses, paths, clips, ramps
    // and the long glyph curve lists. ShapeBatch undoes it the same way.
    const int Inverted = Shapes | Clips | Ramps | DenseText;

    // Every combination of dashes, gradients, text, ellipses and paths, and clips. Border
    // gradients and ramps are rarer, so they come as one more version for each one with
    // gradients rather than doubling the count twice over. Each version with text but no dashes
    // has a twin with the long glyph curve lists, which only rare glyphs need; dashed text with
    // those draws with the version that has everything.
    public static IEnumerable<int> Gl() {
        for (int f = 0; f < 256; f++) {
            int rare = f & (BorderGradients | Ramps);
            if (rare != 0 && (rare != (BorderGradients | Ramps) || (f & Gradients) == 0)) continue;
            if ((f & DenseText) != 0 && f != 255 && ((f & Text) == 0 || (f & Dashes) != 0)) continue;
            yield return f;
        }
    }

    // Every combination of dashes, gradients, border gradients and text, each with the rest,
    // which are the techniques Shapes0 to Shapes15.
    public static IEnumerable<int> Bytecode() {
        for (int name = 0; name < 16; name++) yield return Name(name);
    }

    public static int Name(int features) => features ^ Inverted;

    public static int ChunkKey(int features) => features & (Text | Shapes | Clips);

    public static string Wrapper(string shader, int features, string vsModel, string psModel) {
        string Flag(int bit) => (features & bit) != 0 ? "true" : "false";
        int name = Name(features);
        return $"#define ONE_TECHNIQUE\n#include \"{shader}\"\n" +
            $"float4 ShapePixel{name}(PixelInput p) : SV_TARGET {{ return ShapePixel(p, {Flag(Dashes)}, {Flag(Gradients)}, {Flag(BorderGradients)}, {Flag(Text)}, {Flag(Shapes)}, {Flag(Clips)}, {Flag(Ramps)}, {Flag(DenseText)}); }}\n" +
            $"technique Shapes{name} {{ pass {{ VertexShader = compile {vsModel} SpriteVertexShader(); PixelShader = compile {psModel} ShapePixel{name}(); }} }}\n";
    }
}

// Long repeats turned into references to earlier output, ahead of gzip, whose 32 KB window can't
// reach the copy of a function in the version before. A stream of literal runs, each followed by
// one reference: the run's length, its bytes, then the reference's length and how far back it
// starts, all as 7 bit varints. A reference of length 0 ends the stream. ShapeBatch has the
// decoder.
static class Lz {
    const int MinMatch = 32;
    const int MaxChain = 32;

    public static byte[] Encode(byte[] data) {
        var output = new MemoryStream();
        var head = new Dictionary<ulong, int>();
        var previous = new int[data.Length];
        int literalStart = 0;
        int i = 0;
        while (i < data.Length) {
            int bestLength = 0, bestDistance = 0;
            if (i + 8 <= data.Length) {
                ulong key = BitConverter.ToUInt64(data, i);
                int candidate = head.TryGetValue(key, out int h) ? h : -1;
                for (int chain = 0; candidate >= 0 && chain < MaxChain; chain++) {
                    int length = 0;
                    while (i + length < data.Length && data[candidate + length] == data[i + length]) length++;
                    if (length > bestLength) {
                        bestLength = length;
                        bestDistance = i - candidate;
                    }
                    candidate = previous[candidate];
                }
            }
            if (bestLength >= MinMatch) {
                WriteVarint(output, i - literalStart);
                output.Write(data, literalStart, i - literalStart);
                WriteVarint(output, bestLength);
                WriteVarint(output, bestDistance);
                for (int end = i + bestLength; i < end; i++) Insert(data, i, head, previous);
                literalStart = i;
            } else {
                Insert(data, i, head, previous);
                i++;
            }
        }
        WriteVarint(output, data.Length - literalStart);
        output.Write(data, literalStart, data.Length - literalStart);
        WriteVarint(output, 0);
        return output.ToArray();
    }

    static void Insert(byte[] data, int i, Dictionary<ulong, int> head, int[] previous) {
        if (i + 8 > data.Length) return;
        ulong key = BitConverter.ToUInt64(data, i);
        previous[i] = head.TryGetValue(key, out int h) ? h : -1;
        head[key] = i;
    }

    static void WriteVarint(Stream s, int v) {
        while (v >= 0x80) {
            s.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }
        s.WriteByte((byte)v);
    }
}
