using Xunit;

namespace Apos.Shapes.Tests {
    // D3D12 refuses DXIL whose container hash is zero (Apostolique/Apos.Shapes#43), so creating a
    // ShapeBatch failed on DirectX 12. ShadowDusk signs the container, on Linux and macOS too, from
    // 0.25.1. This reads the shipped pack and needs no graphics device.
    public class Dx12ShaderSignatureTests {
        [Fact]
        public void Dx12Pack_EveryContainerHasAHash() {
            ShaderArchive archive = ShaderArchive.For("Apos.Shapes.apos-shapes.dx12.pack");
            int containers = 0;

            foreach (int name in archive.Names) {
                byte[] effect = archive.Read(name);
                foreach (int at in ContainerOffsets(effect)) {
                    containers++;
                    // The 16 bytes right after the "DXBC" magic.
                    Assert.False(effect.AsSpan(at + 4, 16).IndexOfAnyExcept((byte)0) < 0,
                        $"Version {name} has a DXIL container with a zeroed hash at offset {at}.");
                }
            }

            // Each version has a vertex and a pixel shader.
            Assert.True(containers >= 2 * archive.Names.Count(), $"Only {containers} DXIL containers found.");
        }

        private static IEnumerable<int> ContainerOffsets(byte[] b) {
            for (int i = 0; i + 32 <= b.Length; i++) {
                if (b[i] != 'D' || b[i + 1] != 'X' || b[i + 2] != 'B' || b[i + 3] != 'C') continue;
                uint size = BitConverter.ToUInt32(b, i + 24);
                uint parts = BitConverter.ToUInt32(b, i + 28);
                if (size < 32 || i + size > b.Length || parts == 0 || parts > 64) continue;
                yield return i;
                i += (int)size - 1;
            }
        }
    }
}
