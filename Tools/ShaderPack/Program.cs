using System;
using System.IO;
using System.IO.Compression;

// Compresses the compiled shaders before Apos.Shapes embeds them. The techniques in one shader
// file are mostly the same text over again, which Brotli's largest window can see across while
// gzip's 32 KB can't. Brotli doesn't run in a browser though, so the KNI build, which covers
// WebGL, ships gzip.
//
// Usage: ShaderPack <brotli|gzip> <file>... writes each file next to itself as .br or .gz.
if (args.Length < 2 || args[0] is not ("brotli" or "gzip")) {
    Console.Error.WriteLine("Usage: ShaderPack <brotli|gzip> <file>...");
    return 1;
}
bool brotli = args[0] == "brotli";
for (int i = 1; i < args.Length; i++) {
    byte[] raw = File.ReadAllBytes(args[i]);
    using var output = File.Create(args[i] + (brotli ? ".br" : ".gz"));
    if (brotli) {
        byte[] packed = new byte[BrotliEncoder.GetMaxCompressedLength(raw.Length)];
        using var encoder = new BrotliEncoder(11, 24);
        encoder.Compress(raw, packed, out _, out int written, true);
        output.Write(packed, 0, written);
    } else {
        using var gzip = new GZipStream(output, CompressionLevel.SmallestSize);
        gzip.Write(raw);
    }
}
return 0;
