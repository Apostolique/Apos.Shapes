using System;
using System.IO;
using System.Linq;
using Apos.Shapes;
using Xunit;

namespace Apos.Shapes.Tests {
    public class GlyphFontTileTests {
        private static GlyphFont Font() {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Example", "Content", "source-code-pro-medium.ttf"))) {
                dir = dir.Parent;
            }
            Assert.NotNull(dir);
            return new GlyphFont(File.ReadAllBytes(Path.Combine(dir!.FullName, "Example", "Content", "source-code-pro-medium.ttf")));
        }

        [Fact]
        public void Lookup_OrdinaryLetter_IsOneQuad() {
            using GlyphFont font = Font();
            BakedGlyph g = font.Lookup('A');

            Assert.Null(g.Tiles);
            Assert.Equal(0, g.Clamped);
        }

        [Fact]
        public void Lookup_GlyphTooDenseForTheLongLists_IsCutIntoTilesThatFit() {
            using GlyphFont font = Font();
            BakedGlyph g = font.Lookup('▒');

            Assert.True(g.Clamped > 0, "the whole glyph should overflow, or this test proves nothing");
            Assert.NotNull(g.Tiles);
            Assert.True(g.Tiles!.Length > 1);
            foreach (BakedGlyph tile in g.Tiles) {
                Assert.Equal(0, tile.Clamped);
            }
        }

        [Fact]
        public void Lookup_TiledGlyph_TilesCoverItsWholeBox() {
            using GlyphFont font = Font();
            BakedGlyph g = font.Lookup('▒');

            BakedGlyph[] tiles = g.Tiles!;
            Assert.Equal(g.Min.X, tiles.Min(t => t.Min.X), 4);
            Assert.Equal(g.Min.Y, tiles.Min(t => t.Min.Y), 4);
            Assert.Equal(g.Max.X, tiles.Max(t => t.Max.X), 4);
            Assert.Equal(g.Max.Y, tiles.Max(t => t.Max.Y), 4);
            float area = tiles.Sum(t => (t.Max.X - t.Min.X) * (t.Max.Y - t.Min.Y));
            Assert.Equal((g.Max.X - g.Min.X) * (g.Max.Y - g.Min.Y), area, 4);
        }
    }
}
