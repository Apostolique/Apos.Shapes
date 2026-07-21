using System;

namespace Apos.Shapes {
    /// <summary>
    /// The shape of a dash's ends, named like the line cap styles.
    /// </summary>
    public enum DashCap {
        /// <summary>Dashes are cut flat at both ends.</summary>
        Butt = 0,
        /// <summary>Dashes end in half circles. With a dash length of 0 they become dots.</summary>
        Round = 1
    }

    /// <summary>
    /// How the dash pattern is fitted to the contour so it doesn't end on a partial dash.
    /// </summary>
    public enum DashSnap {
        /// <summary><see cref="Tiling"/> on closed outlines, <see cref="EndToEnd"/> on open strokes.</summary>
        Auto = 0,
        /// <summary>The pattern is used as given. It may end mid dash, and on closed outlines the seam shows.</summary>
        Off = 1,
        /// <summary>
        /// The period is scaled so a whole number of dash + space repeats fits the contour. Closed
        /// outlines wrap seamlessly; open strokes start on a dash and end on a space.
        /// </summary>
        Tiling = 2,
        /// <summary>
        /// The period is scaled so a dash is centered on each end of the stroke, merging with the caps.
        /// Falls back to <see cref="Tiling"/> on closed outlines.
        /// </summary>
        EndToEnd = 3
    }

    /// <summary>
    /// Dashes a shape. On closed outlines (circle, rectangle, hexagon, equilateral triangle, triangle)
    /// the border is dashed along the perimeter and the fill is untouched. On strokes (line, arc, ring,
    /// path) the whole stroke is cut into dashes and each dash keeps its own fill, border and caps.
    /// The pattern rounds every corner it walks, so dashes bend around joints and corners at full
    /// width and slide off the end caps.
    /// The pattern comes in world units from the constructor, or as a repeat count from
    /// <see cref="FromCount"/>, which never pops as the shape changes size.
    /// The default value draws solid.
    /// </summary>
    public readonly struct DashStyle {
        /// <param name="size">Length of a dash in world units along the contour. 0 with <see cref="DashCap.Round"/> gives dots.</param>
        /// <param name="spacing">Length of the space between dashes in world units. 0 or less disables dashing.</param>
        /// <param name="offset">Offset of the pattern in periods, where 1 is one dash + space. Integer values look the same.</param>
        /// <param name="cap">The shape of each dash's ends.</param>
        /// <param name="snap">How the pattern is fitted to the contour.</param>
        public DashStyle(float size, float spacing, float offset = 0f, DashCap cap = DashCap.Butt, DashSnap snap = DashSnap.Auto) {
            Size = size;
            Spacing = spacing;
            Offset = offset;
            Cap = cap;
            Snap = snap;
            _count = 0f;
            _fill = 0f;
        }

        private DashStyle(float count, float fill, float offset, DashCap cap) {
            Size = 0f;
            Spacing = 0f;
            Offset = offset;
            Cap = cap;
            Snap = DashSnap.Auto;
            _count = count;
            _fill = fill;
        }

        /// <summary>
        /// Dashes with a whole number of repeats instead of world unit lengths: the period is always
        /// the contour length divided by count, so there is no rounding to snap. The pattern wraps
        /// closed outlines seamlessly, centers a dash on each end of an open stroke, and stretches
        /// continuously as the shape changes size, where a world unit pattern pops whenever the
        /// repeat count it snaps to changes. This is the style to use when the shape itself animates.
        /// </summary>
        /// <param name="count">Number of dash + space repeats laid along the contour. At least 1.</param>
        /// <param name="fill">Fraction of each period the dash covers, clamped to [0, 1]. 1 draws solid; 0 with <see cref="DashCap.Round"/> gives dots.</param>
        /// <param name="offset">Offset of the pattern in periods, same as the constructor's.</param>
        /// <param name="cap">The shape of each dash's ends.</param>
        public static DashStyle FromCount(int count, float fill = 0.5f, float offset = 0f, DashCap cap = DashCap.Butt) {
            return new DashStyle(Math.Max(count, 1), Math.Clamp(fill, 0f, 1f), offset, cap);
        }

        /// <summary>Length of a dash in world units along the contour. Unused by <see cref="FromCount"/> styles.</summary>
        public readonly float Size;
        /// <summary>Length of the space between dashes in world units. Unused by <see cref="FromCount"/> styles.</summary>
        public readonly float Spacing;
        /// <summary>Offset of the pattern in periods, where 1 is one dash + space.</summary>
        public readonly float Offset;
        /// <summary>The shape of each dash's ends.</summary>
        public readonly DashCap Cap;
        /// <summary>How the pattern is fitted to the contour. Unused by <see cref="FromCount"/> styles, which always fit exactly.</summary>
        public readonly DashSnap Snap;

        // A count of at least 1 selects count mode and Size/Spacing/Snap go unused; _fill is the
        // dash's fraction of the period. Both stay 0 for world unit styles.
        private readonly float _count;
        private readonly float _fill;

        /// <summary>Draws solid, same as the default value.</summary>
        public static DashStyle None => default;

        internal bool IsEnabled => _count >= 1f ? _fill < 1f : Spacing > 0f && Size >= 0f;

        // Fits the pattern to a contour and packs it for the shader: the snapped period rides as a
        // full float while the dash fraction and phase, both period-relative, share one float as two
        // 11 bit values. The phase convention puts a dash's center at u = phase * period.
        internal ResolvedDash Resolve(float length, bool closed) {
            if (!IsEnabled || !(length > 0f)) return default;

            float period;
            float frac;
            float phase;

            if (_count >= 1f) {
                // The period divides the contour exactly, so nothing snaps and nothing pops. Phase 0
                // centers a dash on both endpoints of an open stroke, since both sit on a multiple of
                // the period; on a closed outline the origin is arbitrary anyway.
                period = length / _count;
                frac = _fill;
                phase = 0f;
            } else {
                period = Size + Spacing;
                frac = Size / period;

                DashSnap snap = Snap == DashSnap.Auto ? (closed ? DashSnap.Tiling : DashSnap.EndToEnd) : Snap;
                if (closed && snap == DashSnap.EndToEnd) snap = DashSnap.Tiling;

                if (snap == DashSnap.Off) {
                    phase = closed ? 0f : frac * 0.5f;
                } else {
                    float n = MathF.Max(MathF.Round(length / period), 1f);
                    if (snap == DashSnap.Tiling && !closed) {
                        // Tiling starts flush on a dash. Fitting whole periods would start the next
                        // dash exactly at u = length, and on a butt capped stroke that coincidence
                        // still paints a sliver at the cap: the dash edge and the cap face are the
                        // same plane with opposing interiors, so the zero width overlap keeps a
                        // full AA ramp. End mid gap instead, half a space clear of the nearest
                        // dash edge. The start has no such degeneracy: the first dash and the
                        // stroke agree on which side of the cap face is inside.
                        period = length / (n - (1f - frac) * 0.5f);
                        phase = frac * 0.5f;
                    } else {
                        // EndToEnd centers a dash on both endpoints so the caps complete the end
                        // dashes. On closed outlines the origin is arbitrary anyway.
                        period = length / n;
                        phase = 0f;
                    }
                }
            }
            phase += Offset;
            phase -= MathF.Floor(phase);

            return new ResolvedDash(period, Pack11(frac, phase), Cap == DashCap.Round ? 2 : 1);
        }

        // Mirrors the shader's Pack11: two [0, 1] values as 11 bit integers in one exact float.
        internal static float Pack11(float a, float b) {
            return MathF.Floor(Math.Clamp(a, 0f, 1f) * 2047f + 0.5f) * 2048f + MathF.Floor(Math.Clamp(b, 0f, 1f) * 2047f + 0.5f);
        }
    }

    // A dash pattern fitted to one shape's contour. TypeDigit 0 means not dashed; Period and
    // FracPhase travel in shape-specific spare vertex channels.
    internal readonly struct ResolvedDash {
        public ResolvedDash(float period, float fracPhase, int typeDigit) {
            Period = period;
            FracPhase = fracPhase;
            TypeDigit = typeDigit;
        }

        public readonly float Period;
        public readonly float FracPhase;
        public readonly int TypeDigit;
    }
}
