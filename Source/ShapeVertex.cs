using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Apos.Shapes {
    /// <summary>
    /// One corner of the quad a shape is drawn on, packed the way the built-in shader reads it.
    /// <see cref="ShapeBatch"/> builds these itself, so you only touch this type when writing a
    /// replacement shader or feeding the vertex buffer by hand. The meaning of
    /// <c>a</c> through <c>d</c> changes per <see cref="Shape"/>, so treat the layout as internal
    /// and expect it to move between versions.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VertexShape : IVertexType {
        /// <param name="position">Where this corner sits, in world units.</param>
        /// <param name="textureCoordinate">The corner's position in the shape's own frame, which is what the distance field is evaluated at.</param>
        /// <param name="shape">Which distance field the pixel shader runs.</param>
        /// <param name="fill">Color or gradient inside the border.</param>
        /// <param name="border">Color or gradient of the border.</param>
        /// <param name="thickness">Size of the border in world units. 0 makes the border the fill.</param>
        /// <param name="sdfSize">The shape's main size, which each distance field reads its own way.</param>
        /// <param name="clip">This corner's view of the clip rectangle.</param>
        /// <param name="height">The shape's second size, where it has one.</param>
        /// <param name="aaSize">
        /// Size of the anti-aliasing edge in pixels. The sign carries the <see cref="AAStyle"/>:
        /// negative puts the band across the edge, positive outside it.
        /// </param>
        /// <param name="rounded">Corner rounding in world units, where the shape has corners.</param>
        /// <param name="a">Spare channel. Its meaning depends on <paramref name="shape"/> and on whether the shape is dashed.</param>
        /// <param name="b">Spare channel, same as <paramref name="a"/>.</param>
        /// <param name="c">Spare channel, same as <paramref name="a"/>.</param>
        /// <param name="d">Spare channel, same as <paramref name="a"/>.</param>
        /// <param name="colorSpace">Space the colors are interpolated in. Textures and glyphs force Rgb.</param>
        /// <param name="dash">0 draws solid, 1 dashes with flat ends, 2 with round ones.</param>
        /// <param name="blur">Standard deviation of the edge's Gaussian falloff in world units, or 0 for a hard edge.</param>
        /// <param name="ramps">
        /// The batch's ramp table, which is where a <see cref="Ramp"/> or <see cref="ColorRamp"/>
        /// gets its row. Without one those fall away and the gradient uses its stop colors.
        /// </param>
        public VertexShape(Vector3 position, Vector2 textureCoordinate, Shape shape, Gradient fill, Gradient border, float thickness, float sdfSize, ClipSpace clip, float height = 1.0f, float aaSize = 1.5f, float rounded = 0f, float a = 0f, float b = 0f, float c = 0f, float d = 0f, ColorSpace colorSpace = ColorSpace.Oklab, int dash = 0, float blur = 0f, RampTable? ramps = null) {
            if (thickness <= 0f) {
                border = fill;
                thickness = 0f;
            }

            if (shape == Shape.Texture || shape == Shape.Glyph) {
                // Texture masks are multiplied in RGBA space, everything else is blended in the chosen color space.
                colorSpace = ColorSpace.Rgb;
                // A palette, a ramp, or a color ramp can't mask a texture, so all three fall
                // back to the stop colors, which a palette or color ramp gradient carries as
                // white.
                fill.IsPalette = false;
                border.IsPalette = false;
                fill.R = null;
                border.R = null;
                fill.Colors = null;
                border.Colors = null;
            }

            Position = position;
            TextureCoordinate = new Vector4(textureCoordinate, rounded, PackMeta(shape, fill, border, colorSpace, dash, blur > 0f ? 1 : 0));

            if (ramps == null) {
                // Rows only exist inside a batch's table, so without one the ramps and color
                // ramps fall away the same way texture masks shed them.
                fill.R = null;
                border.R = null;
                fill.Colors = null;
                border.Colors = null;
            }

            if (fill.IsPalette || border.IsPalette || fill.R != null || border.R != null || fill.Colors != null || border.Colors != null) {
                (FillA, FillB) = PackSide(fill, colorSpace, ramps);
                (BorderA, BorderB) = PackSide(border, colorSpace, ramps);
            } else {
                PackBothStops(fill, border, colorSpace, out FillA, out FillB, out BorderA, out BorderB);
            }

            FillCoord = new Vector4(fill.AXY.X, fill.AXY.Y, fill.BXY.X, fill.BXY.Y);
            BorderCoord = new Vector4(border.AXY.X, border.AXY.Y, border.BXY.X, border.BXY.Y);

            // The AA width travels in pixels; the shader scales it by the per-pixel
            // world footprint it derives from screen-space derivatives. A blur replaces it
            // outright rather than adding to it: the Gaussian profile antialiases on its own,
            // so the slot carries world units instead and the packed flag says which it is.
            Meta1 = new Vector4(thickness, blur > 0f ? blur : aaSize, sdfSize, height);
            Meta2 = new Vector4(a, b, c, d);
            Meta3 = new Vector4(fill.AOffset, fill.BOffset, border.AOffset, border.BOffset);
            ClipDistances = clip.Distances;
            ClipRounding = clip.Rounding;
            ClipAaSize = clip.AaSize;
        }

        private static (ulong, ulong) PackSide(in Gradient g, ColorSpace colorSpace, RampTable? ramps) {
            if (g.Colors != null) {
                // A color ramp spends no lanes on colors, so the whole side is two row indices
                // and a flag. They ride the second slot's first lane pair, which the vertex
                // shader packs into one float: the color row lands in the high digit, the
                // integral row in the low one. The color row's lane travels negated, and that
                // sign without the ramp flag beside it is what marks a color ramp; no other
                // combination sets it alone.
                // The ctor strips ramps and color ramps when there is no table, so ramps is
                // never null on these three resolves.
                var (colorRow, intRow) = g.Colors.Rows(colorSpace, ramps!);
                return (0ul, NegateLane(Renorm(colorRow)) | Renorm(intRow) << 16);
            }
            if (g.IsPalette) {
                // PackPalette folded the ramp flag and a placeholder row in when the gradient
                // was built, since rows only exist per batch table. The real row resolves
                // here, where it also stamps for the flush, and patches the lanes it rides in.
                return g.R != null ? (g.PalA, PatchPaletteRow(g.PalB, g.R.Row(ramps!))) : (g.PalA, g.PalB);
            }
            var (a, b) = PackStops(g, colorSpace);
            return g.R != null ? EmbedRamp(a, b, g.R.Row(ramps!), g.AC.A, g.BC.A) : (a, b);
        }

        // The current row written over the stale one in the two lanes a ramped palette keeps it
        // in: 5 bits above ch6's payload, 3 above ch7's (see PackPalette). Both lanes ride
        // unsigned, so this is a plain decode, splice, re-renorm.
        private static ulong PatchPaletteRow(ulong b, int row) {
            int ch6 = Unrenorm((int)(b >> 32 & 0xFFFF));
            int ch7 = Unrenorm((int)(b >> 48));
            if ((ch6 >> 6) + 32 * (ch7 >> 6) == row) return b;
            ch6 = ch6 & 63 | (row & 31) << 6;
            ch7 = ch7 & 63 | row >> 5 << 6;
            return b & 0xFFFFFFFFul | Renorm(ch6) << 32 | Renorm(ch7) << 48;
        }
        private static int Unrenorm(int raw) {
            return (raw * 2047 + 16383) / 32767;
        }

        // A ramp on a pair of stops rides in the corners the colors leave free: each alpha is a
        // byte at heart, so its 11 bit lane carries the byte plus 3 row bits above it, renormed
        // to survive the vertex shader's repack exactly. The last 2 row bits and the ramp flag
        // travel as negated lanes, which the vertex shader moves onto the packed floats' signs.
        private static (ulong, ulong) EmbedRamp(ulong a, ulong b, int row, byte alphaA, byte alphaB) {
            a = a & 0x0000FFFFFFFFFFFFul | Renorm(alphaA + 256 * (row & 7)) << 48;
            b = b & 0x0000FFFFFFFFFFFFul | Renorm(alphaB + 256 * (row >> 3 & 7)) << 48;
            a = NegateLaneAt(a, 2);
            if ((row & 64) != 0) b = NegateLaneAt(b, 0);
            if ((row & 128) != 0) b = NegateLaneAt(b, 2);
            return (a, b);
        }

        private static (ulong, ulong) PackStops(in Gradient g, ColorSpace colorSpace) {
            if (colorSpace == ColorSpace.Oklch) {
                return PackOklchPair(g.AC, g.BC);
            }
            if (colorSpace == ColorSpace.Oklab) {
                ulong a = PackOklab(g.AC);
                return (a, g.BC == g.AC ? a : PackOklab(g.BC));
            }
            return (PackRgb(g.AC), PackRgb(g.BC));
        }

        private static void PackBothStops(in Gradient fill, in Gradient border, ColorSpace colorSpace, out ulong fillA, out ulong fillB, out ulong borderA, out ulong borderB) {
            // Fill and border are the same gradient on every Fill* and Border* call, and a flat
            // color repeats its one stop, so the conversions worth skipping are named outright
            // rather than left to the cache: a hit still costs a lookup, and this costs a compare.
            bool sameStops = border.AC == fill.AC && border.BC == fill.BC;
            if (colorSpace == ColorSpace.Oklch) {
                (fillA, fillB) = PackOklchPair(fill.AC, fill.BC);
                (borderA, borderB) = sameStops ? (fillA, fillB) : PackOklchPair(border.AC, border.BC);
            } else if (colorSpace == ColorSpace.Oklab) {
                fillA = PackOklab(fill.AC);
                fillB = fill.BC == fill.AC ? fillA : PackOklab(fill.BC);
                if (sameStops) {
                    borderA = fillA;
                    borderB = fillB;
                } else {
                    borderA = PackOklab(border.AC);
                    borderB = border.BC == border.AC ? borderA : PackOklab(border.BC);
                }
            } else {
                fillA = PackRgb(fill.AC);
                fillB = PackRgb(fill.BC);
                borderA = sameStops ? fillA : PackRgb(border.AC);
                borderB = sameStops ? fillB : PackRgb(border.BC);
            }
        }

        /// <summary>
        /// This vertex moved to another corner of the same quad. Everything a quad's four
        /// vertices share - the packed colors, the gradient coordinates and all the meta - is
        /// copied as it stands, so the packing and the color space conversion run once per
        /// quad instead of once per vertex.
        /// </summary>
        internal readonly void CopyTo(ref VertexShape dst, Vector3 position, Vector2 local, in ClipSpace clip) {
            dst = this;
            dst.Position = position;
            dst.TextureCoordinate.X = local.X;
            dst.TextureCoordinate.Y = local.Y;
            dst.ClipDistances = clip.Distances;
        }

        /// <summary>Where this corner sits, in world units. Shapes live on the z = 0 plane.</summary>
        public Vector3 Position;
        /// <summary>
        /// The corner in the shape's own frame in xy, the corner rounding in z, and the shape,
        /// gradient styles, color space, dash and blur flags packed into w.
        /// </summary>
        public Vector4 TextureCoordinate;
        /// <summary>First half of the fill's packed color stops, or its palette.</summary>
        public ulong FillA;
        /// <summary>Second half of the fill's packed color stops, or its palette and ramp rows.</summary>
        public ulong FillB;
        /// <summary>First half of the border's packed color stops, or its palette.</summary>
        public ulong BorderA;
        /// <summary>Second half of the border's packed color stops, or its palette and ramp rows.</summary>
        public ulong BorderB;
        /// <summary>The fill gradient's two points, A in xy and B in zw, in world units.</summary>
        public Vector4 FillCoord;
        /// <summary>The border gradient's two points, A in xy and B in zw, in world units.</summary>
        public Vector4 BorderCoord;
        /// <summary>
        /// Border thickness, then the anti-aliasing width in pixels or the blur's standard
        /// deviation in world units, then the shape's two sizes.
        /// </summary>
        public Vector4 Meta1;
        /// <summary>The four spare channels, whose meaning depends on the shape.</summary>
        public Vector4 Meta2;
        /// <summary>The fill's two gradient offsets, then the border's.</summary>
        public Vector4 Meta3;
        /// <summary>Distances to the left, top, right, bottom clip edges. Positive inside.</summary>
        public Vector4 ClipDistances;
        /// <summary>Corner radius of the clip rectangle.</summary>
        public float ClipRounding;
        /// <summary>Antialiasing band width of the clip edge in pixels. 0 gives a hard scissor edge.</summary>
        public float ClipAaSize;
        /// <summary>Layout of this vertex, for the vertex buffer.</summary>
        public static readonly VertexDeclaration VertexDeclaration;

        readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

        /// <summary>A hash over every packed field.</summary>
        /// <returns>The hash code.</returns>
        public override readonly int GetHashCode() {
            return HashCode.Combine(Position, TextureCoordinate, HashCode.Combine(FillA, FillB, BorderA, BorderB), HashCode.Combine(FillCoord, BorderCoord, Meta1, Meta2, Meta3, ClipDistances, ClipRounding, ClipAaSize));
        }

        /// <summary>The position, the packed color slots and a few of the meta channels, for debugging.</summary>
        /// <returns>A readable dump of the vertex.</returns>
        public override readonly string ToString() {
            return
                "{{Position:" + Position +
                " FillA:" + FillA +
                " FillB:" + FillB +
                " BorderA:" + BorderA +
                " BorderB:" + BorderB +
                " FillCoord:" + FillCoord +
                " BorderCoord:" + BorderCoord +
                " TextureCoordinate:" + TextureCoordinate +
                " Thickness:" + Meta1.X +
                " PixelSize:" + Meta1.Z +
                " Width:" + Meta1.W +
                "}}";
        }

        /// <summary>Whether every packed field of the two vertices matches.</summary>
        /// <param name="left">First vertex.</param>
        /// <param name="right">Second vertex.</param>
        /// <returns>True when the two are identical.</returns>
        public static bool operator ==(VertexShape left, VertexShape right) {
            return
                left.Position == right.Position &&
                left.TextureCoordinate == right.TextureCoordinate &&
                left.FillA == right.FillA &&
                left.FillB == right.FillB &&
                left.BorderA == right.BorderA &&
                left.BorderB == right.BorderB &&
                left.FillCoord == right.FillCoord &&
                left.BorderCoord == right.BorderCoord &&
                left.Meta1 == right.Meta1 &&
                left.Meta2 == right.Meta2 &&
                left.Meta3 == right.Meta3 &&
                left.ClipDistances == right.ClipDistances &&
                left.ClipRounding == right.ClipRounding &&
                left.ClipAaSize == right.ClipAaSize;
        }

        /// <summary>Whether any packed field of the two vertices differs.</summary>
        /// <param name="left">First vertex.</param>
        /// <param name="right">Second vertex.</param>
        /// <returns>True when the two differ.</returns>
        public static bool operator !=(VertexShape left, VertexShape right) {
            return !(left == right);
        }

        /// <summary>Whether the other object is a vertex with every packed field the same.</summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns>True when it is an identical vertex.</returns>
        public override readonly bool Equals(object? obj) {
            if (obj == null)
                return false;

            if (obj.GetType() != GetType())
                return false;

            return this == ((VertexShape)obj);
        }

        /// <summary>Which distance field the pixel shader runs for this quad.</summary>
        public enum Shape {
            /// <summary>A circle.</summary>
            Circle = 0,
            /// <summary>A rectangle, with a radius per corner.</summary>
            Rectangle = 1,
            /// <summary>A capsule, which is what a line and a path segment both are.</summary>
            Line = 2,
            /// <summary>A hexagon with flat top and bottom edges.</summary>
            Hexagon = 3,
            /// <summary>An equilateral triangle pointing down.</summary>
            EquilateralTriangle = 4,
            /// <summary>A triangle through three arbitrary points.</summary>
            Triangle = 5,
            /// <summary>An ellipse.</summary>
            Ellipse = 6,
            /// <summary>A stroke along a circle, with rounded ends.</summary>
            Arc = 7,
            /// <summary>A stroke along a circle, with flat ends.</summary>
            Ring = 8,
            /// <summary>A textured quad, masked in raw RGBA.</summary>
            Texture = 9,
            /// <summary>One quad of a path, which carries its own caps and joins.</summary>
            Path = 11,
            /// <summary>A rectangle with its corners cut straight across.</summary>
            Chamfer = 12,
            /// <summary>A glyph outline, solved from its curves on the GPU.</summary>
            Glyph = 13
        }

        static VertexShape() {
            int offset = 0;
            var elements = new VertexElement[] {
                GetVertexElement(ref offset, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 0),
                GetVertexElement(ref offset, VertexElementFormat.NormalizedShort4, VertexElementUsage.TextureCoordinate, 1),
                GetVertexElement(ref offset, VertexElementFormat.NormalizedShort4, VertexElementUsage.TextureCoordinate, 2),
                GetVertexElement(ref offset, VertexElementFormat.NormalizedShort4, VertexElementUsage.TextureCoordinate, 3),
                GetVertexElement(ref offset, VertexElementFormat.NormalizedShort4, VertexElementUsage.TextureCoordinate, 4),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 5),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 6),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 7),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 8),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 9),
                GetVertexElement(ref offset, VertexElementFormat.Vector4, VertexElementUsage.Position, 1),
                GetVertexElement(ref offset, VertexElementFormat.Vector2, VertexElementUsage.Normal, 0)
            };
            VertexDeclaration = new VertexDeclaration(elements);
        }
        private static VertexElement GetVertexElement(ref int offset, VertexElementFormat f, VertexElementUsage u, int usageIndex) {
            return new VertexElement(OffsetInline(ref offset, Offsets[f]), f, u, usageIndex);
        }
        private static int OffsetInline(ref int value, int offset) {
            int old = value;
            value += offset;
            return old;
        }
        private static readonly Dictionary<VertexElementFormat, int> Offsets = new() {
            [VertexElementFormat.Single] = 4,
            [VertexElementFormat.Vector2] = 8,
            [VertexElementFormat.Vector3] = 12,
            [VertexElementFormat.Vector4] = 16,
            [VertexElementFormat.Color] = 4,
            [VertexElementFormat.Byte4] = 4,
            [VertexElementFormat.Short2] = 4,
            [VertexElementFormat.Short4] = 8,
            [VertexElementFormat.NormalizedShort2] = 4,
            [VertexElementFormat.NormalizedShort4] = 8,
            [VertexElementFormat.HalfVector2] = 4,
            [VertexElementFormat.HalfVector4] = 8,
        };

        // The shape uses 4 bits, gradient shapes 4 bits each, repeat styles 2 bits each, the color
        // space 2 bits, the dash type 2 bits and the blur flag 1 bit. The total stays under 2^21 so
        // it survives the trip through a float exactly. Dash is 0 for solid, 1 for basic dashes,
        // 2 for rounded dashes. Blur is a flag rather than a shape of its own so it composes with
        // every shape at once without spending any of the four shape slots left.
        private static float PackMeta(Shape shape, Gradient fill, Gradient border, ColorSpace colorSpace, int dash, int blur) {
            return (int)shape + 16 * ((int)fill.S + 16 * ((int)fill.RS + 4 * ((int)border.S + 16 * ((int)border.RS + 4 * ((int)colorSpace + 4 * (dash + 4 * blur))))));
        }

        // A palette rides in the same two color slots as eight 11 bit channels, sized so the
        // vertex shader's 11 bit repack recovers them exactly. Channel layout, low digit first:
        // ch0..2 carry bias (7 bits) and frequency (4 bits) per color channel, ch3..5 carry
        // amplitude (7 bits) and the phase's top 4 bits, ch6 the x and y phases' low 5 bits,
        // ch7 the z phase's low 5 bits and alpha (6 bits). The first lane is stored negated and
        // pushed 2 raw units past zero: colors only ever use the positive snorm half, so the
        // sign is what tells the shaders a palette from a pair of stops, and the nudge keeps a
        // zero channel from losing it. A ramp row reshapes ch6 and ch7 and adds a second
        // negated lane; see the fork below.
        internal static (ulong, ulong) PackPalette(in Palette p, int rampRow) {
            int alpha = (int)Math.Clamp(MathF.Round(p.Alpha * 63f), 0f, 63f);
            int ch0 = Q7(p.Bias.X) + 128 * QFreq(p.Frequency.X);
            int ch1 = Q7(p.Bias.Y) + 128 * QFreq(p.Frequency.Y);
            int ch2 = Q7(p.Bias.Z) + 128 * QFreq(p.Frequency.Z);
            int ch3, ch4, ch5, ch6, ch7;
            if (rampRow < 0) {
                int dx = QPhase(p.Phase.X);
                int dy = QPhase(p.Phase.Y);
                int dz = QPhase(p.Phase.Z);
                ch3 = Q7(p.Amplitude.X) + 128 * (dx >> 5);
                ch4 = Q7(p.Amplitude.Y) + 128 * (dy >> 5);
                ch5 = Q7(p.Amplitude.Z) + 128 * (dz >> 5);
                ch6 = (dx & 31) + 32 * (dy & 31);
                ch7 = (dz & 31) + 32 * alpha;
            } else {
                // With a ramp aboard the phase drops to 6 bits per channel and the freed bits
                // carry the row: 2 low phase bits per channel plus 5 row bits in ch6, the other
                // 3 row bits above the alpha in ch7. Plain palettes keep the 9 bit phase, so
                // only a ramped palette pays the coarser phase step.
                int dx = QPhase64(p.Phase.X);
                int dy = QPhase64(p.Phase.Y);
                int dz = QPhase64(p.Phase.Z);
                ch3 = Q7(p.Amplitude.X) + 128 * (dx >> 2);
                ch4 = Q7(p.Amplitude.Y) + 128 * (dy >> 2);
                ch5 = Q7(p.Amplitude.Z) + 128 * (dz >> 2);
                ch6 = (dx & 3) + 4 * (dy & 3) + 16 * (dz & 3) + 64 * (rampRow & 31);
                ch7 = alpha + 64 * (rampRow >> 5);
            }

            ulong a = NegateLane(Renorm(ch0)) | Renorm(ch1) << 16 | Renorm(ch2) << 32 | Renorm(ch3) << 48;
            ulong b = Renorm(ch4) | Renorm(ch5) << 16 | Renorm(ch6) << 32 | Renorm(ch7) << 48;
            if (rampRow >= 0) {
                a = NegateLaneAt(a, 2);
            }
            return (a, b);
        }
        private static int Q7(float v) {
            return (int)Math.Clamp(MathF.Round(v * 127f), 0f, 127f);
        }
        private static int QFreq(float v) {
            return (int)Math.Clamp(MathF.Round(v), 0f, 15f);
        }
        private static int QPhase(float v) {
            return (int)MathF.Round((v - MathF.Floor(v)) * 512f) & 511;
        }
        private static int QPhase64(float v) {
            return (int)MathF.Round((v - MathF.Floor(v)) * 64f) & 63;
        }
        private static ulong Renorm(int ch) {
            return (ulong)((ch * 32767 + 1023) / 2047);
        }
        // The sign is a flag and the +2 nudge keeps it through a zero payload, landing far
        // enough from zero that the Vulkan SSCALED detection still trips (see FixSnorm in the
        // shader). The cap keeps the negated value inside a ushort; cap and nudge both stay
        // within the 8 raw units of slack an 11 bit bucket has in 15, so a renormed payload
        // decodes unchanged, and a plain color channel moves by at most 1/2047.
        private static ulong NegateLane(ulong raw) {
            return unchecked((ushort)(-((int)Math.Min(raw, 32765ul) + 2)));
        }
        private static ulong NegateLaneAt(ulong u, int lane) {
            int sh = lane * 16;
            return u & ~(0xFFFFul << sh) | NegateLane(u >> sh & 0xFFFF) << sh;
        }

        // Colors are stored as four 16 bit normalized shorts. Only the positive half of the snorm
        // range is used so every channel arrives in [0, 1] on the GPU.
        private static ulong PackColor(Vector4 v) {
            return PackChannel(v.X) | PackChannel(v.Y) << 16 | PackChannel(v.Z) << 32 | PackChannel(v.W) << 48;
        }
        private static ulong PackChannel(float v) {
            return (ulong)(int)(Math.Clamp(v, 0f, 1f) * 32767f + 0.5f);
        }

        private static ulong PackRgb(Color c) {
            // A byte only ever lands on one of 256 packed values, so the divide, the clamp and
            // the rounding are all folded into a table. Same result as PackColor of c / 255.
            ushort[] t = _byteToSnorm;
            return t[c.R] | (ulong)t[c.G] << 16 | (ulong)t[c.B] << 32 | (ulong)t[c.A] << 48;
        }
        private static readonly ushort[] _byteToSnorm = CreateByteToSnorm();
        private static ushort[] CreateByteToSnorm() {
            var table = new ushort[256];
            for (int i = 0; i < 256; i++) table[i] = (ushort)PackChannel(i / 255f);
            return table;
        }
        private static ulong PackOklab(Color c) {
            // Every vertex of a shape packs the same colors, and a batch usually draws long
            // runs in one of them, so the conversion is worth remembering: it costs three
            // cbrt and would otherwise run sixteen times per quad.
            ulong key = c.PackedValue;
            ulong[] cache = _oklabCache ??= NewOklabCache();
            int slot = Slot(c.PackedValue, OklabSlots);
            if (cache[slot * 2] == key) {
                return cache[slot * 2 + 1];
            }

            Vector3 lab = ToOklab(c);
            // a and b are remapped from [-0.4, 0.4] which covers the whole sRGB gamut.
            ulong packed = PackColor(new Vector4(lab.X, lab.Y * 1.25f + 0.5f, lab.Z * 1.25f + 0.5f, c.A / 255f));
            cache[slot * 2] = key;
            cache[slot * 2 + 1] = packed;
            return packed;
        }
        private static (ulong, ulong) PackOklchPair(Color a, Color b) {
            // Same idea as PackOklab, keyed on the stop pair since the hue fixup couples them.
            ulong key = (ulong)a.PackedValue << 32 | b.PackedValue;
            ulong[] cache = _oklchCache ??= new ulong[OklchSlots * 3];
            int slot = Slot(a.PackedValue ^ b.PackedValue, OklchSlots);
            if (cache[slot * 3] == key && (_oklchValid & 1ul << slot) != 0) {
                return (cache[slot * 3 + 1], cache[slot * 3 + 2]);
            }

            var packed = PackOklchPairCore(a, b);
            cache[slot * 3] = key;
            cache[slot * 3 + 1] = packed.Item1;
            cache[slot * 3 + 2] = packed.Item2;
            _oklchValid |= 1ul << slot;
            return packed;
        }

        // Direct mapped and per thread. Sized well past the palette a scene actually draws in:
        // at sixteen slots even eight colors collided more often than not, and a collision
        // between two colors that alternate misses every single time, for three cbrt each.
        // A single color key is a uint, so an all ones slot cannot be one and means empty. The
        // Oklch key is a pair and fills all 64 bits, so that table keeps a bit per slot instead,
        // which is what holds it to the 64 a ulong of them can mark.
        private const int OklabSlots = 256;
        private const int OklchSlots = 64;
        private const ulong EmptyKey = ulong.MaxValue;
        [ThreadStatic] private static ulong[]? _oklabCache;
        [ThreadStatic] private static ulong[]? _oklchCache;
        [ThreadStatic] private static ulong _oklchValid;

        private static ulong[] NewOklabCache() {
            var cache = new ulong[OklabSlots * 2];
            for (int i = 0; i < OklabSlots; i++) cache[i * 2] = EmptyKey;
            return cache;
        }

        private static int Slot(uint key, int slots) {
            return (int)((key ^ key >> 16) * 2654435761u >> 16) & slots - 1;
        }

        private static (ulong, ulong) PackOklchPairCore(Color a, Color b) {
            Vector3 labA = ToOklab(a);
            Vector3 labB = ToOklab(b);
            float chromaA = MathF.Sqrt(labA.Y * labA.Y + labA.Z * labA.Z);
            float chromaB = MathF.Sqrt(labB.Y * labB.Y + labB.Z * labB.Z);
            float hueA = MathF.Atan2(labA.Z, labA.Y);
            float hueB = MathF.Atan2(labB.Z, labB.Y);

            // Grays have no hue of their own, they take the other stop's hue so the lerp doesn't drift.
            const float achromatic = 1e-4f;
            if (chromaA < achromatic) hueA = chromaB < achromatic ? 0f : hueB;
            if (chromaB < achromatic) hueB = chromaA < achromatic ? 0f : hueA;

            return (PackOklch(labA.X, chromaA, hueA, a.A), PackOklch(labB.X, chromaB, hueB, b.A));
        }
        private static ulong PackOklch(float l, float chroma, float hue, byte alpha) {
            // Chroma is remapped from [0, 0.4], hue from [-pi, pi].
            return PackColor(new Vector4(l, chroma * 2.5f, hue / MathF.Tau + 0.5f, alpha / 255f));
        }

        internal static Vector3 ToOklab(Color c) {
            float r = _srgbToLinear[c.R];
            float g = _srgbToLinear[c.G];
            float b = _srgbToLinear[c.B];

            float l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
            float m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
            float s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);

            return new Vector3(
                0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
        }

        private static readonly float[] _srgbToLinear = CreateSrgbToLinear();
        private static float[] CreateSrgbToLinear() {
            var table = new float[256];
            for (int i = 0; i < 256; i++) {
                float c = i / 255f;
                table[i] = c >= 0.04045f ? MathF.Pow((c + 0.055f) / 1.055f, 2.4f) : c / 12.92f;
            }
            return table;
        }
    }
}
