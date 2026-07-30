using System;
using System.Buffers;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace Apos.Shapes {
    /// <summary>
    /// Axis aligned bounds for every shape the batch draws. A measure takes the same geometry its
    /// draw call takes and answers with the world space rectangle that shape covers, which is what
    /// a camera needs to decide whether drawing it is worth the call.
    ///
    /// Nothing here needs a ShapeBatch or a view. These are the shape's own bounds, so they hold
    /// wherever it is drawn and at whatever zoom: you can work them out once at load and put them
    /// in a quadtree.
    ///
    /// The bounds are of the shape, not of the fringe the anti-aliasing puts around it. That fringe
    /// is aaSize pixels wide, 1.5 by default, so how far it reaches in world units depends on the
    /// zoom - which is exactly what a bound that travels with the shape can't depend on. It is
    /// under a pixel and a half either way, and smaller than any margin a camera wants.
    ///
    /// Blur is the other way around. It is authored as a world distance and it reaches far, so the
    /// blurred shapes have their own measures that take it in.
    ///
    /// Thickness is left out because a border is measured inward from the edge, so outlining a
    /// shape never makes it any bigger. A DashStyle is left out because dashes only take pixels
    /// away, so the solid shape's rectangle still holds.
    /// </summary>
    public static class Measure {
        /// <summary>Bounds of DrawCircle. Rotation can't move a circle, so it isn't asked for.</summary>
        public static RectangleF Circle(Vector2 center, float radius) {
            float r = MathF.Max(radius, 0f);
            return new RectangleF(center.X - r, center.Y - r, r * 2f, r * 2f);
        }

        /// <summary>Bounds of DrawRectangle, rotated about the rectangle's center.</summary>
        public static RectangleF Rectangle(Vector2 xy, Vector2 size, CornerRadii cornerRadii = default, float rotation = 0f) {
            return RectangleBounds(xy, size, cornerRadii, rotation, 0f);
        }

        /// <summary>Bounds of DrawChamfer, rotated about the rectangle's center.</summary>
        public static RectangleF Chamfer(Vector2 xy, Vector2 size, CornerChamfers chamfers = default, float rotation = 0f) {
            return ChamferBounds(xy, size, chamfers, rotation, 0f);
        }

        /// <summary>Bounds of DrawLine: the capsule between the two end circles.</summary>
        public static RectangleF Line(Vector2 a, Vector2 b, float radius) {
            float r = MathF.Max(radius, 0f);
            Bounds bb = default;
            bb.Add(a, r);
            bb.Add(b, r);
            return bb;
        }

        /// <summary>Bounds of DrawHexagon.</summary>
        public static RectangleF Hexagon(Vector2 center, float radius, float rounded = 0f, float rotation = 0f) {
            rounded = MathHelper.Clamp(rounded, 0f, radius);
            // Rounding shrinks the hexagon and puts the corners back as arcs, so the shape is the
            // hull of six discs. The apothem comes out where it went in; the corners come in a
            // little, which is exactly what a rounded corner does.
            float apothem = radius - rounded;
            float circum = 2f * apothem / MathF.Sqrt(3f);
            float half = apothem / MathF.Sqrt(3f);
            Span<Vector2> corners = stackalloc Vector2[] {
                new(circum, 0f), new(half, apothem), new(-half, apothem),
                new(-circum, 0f), new(-half, -apothem), new(half, -apothem),
            };
            return HullOfDiscs(corners, center, rotation, rounded);
        }

        /// <summary>Bounds of DrawEquilateralTriangle.</summary>
        public static RectangleF EquilateralTriangle(Vector2 center, float radius, float rounded = 0f, float rotation = 0f) {
            rounded = MathHelper.Clamp(rounded, 0f, radius);
            // Same story as the hexagon: the inradius the drawing shrinks to, dilated back by the
            // rounding. radius is the inradius, so the corners sit at twice it from the center.
            float inradius = radius - rounded;
            Span<Vector2> corners = stackalloc Vector2[] {
                new(-inradius * MathF.Sqrt(3f), -inradius),
                new(inradius * MathF.Sqrt(3f), -inradius),
                new(0f, 2f * inradius),
            };
            return HullOfDiscs(corners, center, rotation, rounded);
        }

        /// <summary>Bounds of DrawTriangle.</summary>
        public static RectangleF Triangle(Vector2 a, Vector2 b, Vector2 c, float rounded = 0f) {
            rounded = MathF.Max(rounded, 0f);

            // The same shrink DrawTriangle makes: every edge slides in by the rounding, which for a
            // triangle is a scale about the incenter, and the corner discs put it back.
            float sideA = Vector2.Distance(a, b);
            float sideB = Vector2.Distance(b, c);
            float sideC = Vector2.Distance(c, a);
            float perimeter = sideA + sideB + sideC;
            float inRadius = MathF.Sqrt((-sideB + sideC + sideA) * (sideB - sideC + sideA) * (sideB + sideC - sideA) / perimeter) / 2f;

            Bounds bb = default;
            if (!(inRadius > 0f)) {
                // Degenerate: the three points are collinear, so there is no incircle to shrink
                // toward and the triangle has no inside to draw.
                bb.Add(a);
                bb.Add(b);
                bb.Add(c);
                return bb;
            }

            Vector2 inCenter = new((sideB * a.X + sideC * b.X + sideA * c.X) / perimeter,
                                   (sideB * a.Y + sideC * b.Y + sideA * c.Y) / perimeter);
            float ratio = (inRadius - rounded) / inRadius;
            if (ratio < 0.001f) {
                ratio = 0.001f;
                rounded = inRadius - inRadius * ratio;
            }
            bb.Add(inCenter + (a - inCenter) * ratio, rounded);
            bb.Add(inCenter + (b - inCenter) * ratio, rounded);
            bb.Add(inCenter + (c - inCenter) * ratio, rounded);
            return bb;
        }

        /// <summary>Bounds of DrawEllipse.</summary>
        public static RectangleF Ellipse(Vector2 center, float radius1, float radius2, float rotation = 0f) {
            return EllipseBounds(center, radius1, radius2, rotation, 0f);
        }

        /// <summary>
        /// Bounds of DrawArc. Only the part of the ring the arc actually sweeps is measured, so a
        /// small arc of a large circle comes back small.
        /// </summary>
        public static RectangleF Arc(Vector2 center, float angle1, float angle2, float radius1, float radius2) {
            float halfSpan = ArcHalfSpan(angle1, angle2);
            float mid = (angle1 + angle2) * 0.5f;
            float band = MathF.Max(radius2, 0f);

            Bounds bb = default;
            // Round caps: a disc of the band's half thickness sits on each end of the centerline,
            // and it covers the band's own corners there too.
            bb.Add(center + Direction(mid - halfSpan) * radius1, band);
            bb.Add(center + Direction(mid + halfSpan) * radius1, band);
            AddSweptAxes(ref bb, center, mid, halfSpan, radius1 + band);
            return bb;
        }

        /// <summary>Bounds of DrawRing, which is an arc cut flat at both ends.</summary>
        public static RectangleF Ring(Vector2 center, float angle1, float angle2, float radius1, float radius2) {
            float halfSpan = ArcHalfSpan(angle1, angle2);
            float mid = (angle1 + angle2) * 0.5f;
            float outer = radius1 + MathF.Max(radius2, 0f);
            // A band thicker than its radius reaches the center, and there its inner corner is.
            float inner = MathF.Max(radius1 - MathF.Max(radius2, 0f), 0f);

            Bounds bb = default;
            // Flat caps: the band ends on four corners rather than on two discs.
            for (int s = -1; s <= 1; s += 2) {
                Vector2 dir = Direction(mid + s * halfSpan);
                bb.Add(center + dir * inner);
                bb.Add(center + dir * outer);
            }
            AddSweptAxes(ref bb, center, mid, halfSpan, outer);
            return bb;
        }

        /// <summary>
        /// Bounds of the DrawPath, FillPath and BorderPath family. The join style and the miter
        /// limit are asked for because a sharp miter runs a long way past the stroke.
        /// </summary>
        public static RectangleF Path(ReadOnlySpan<Vector2> points, float radius, PathJoin join = PathJoin.Round, PathCap cap = PathCap.Round, PathCap? capEnd = null, float miterLimit = 4f, bool closed = false) {
            return PathCore(points, default, default, radius, join, cap, capEnd ?? cap, miterLimit, closed);
        }

        /// <summary>Bounds of the DrawPath overload that takes a radius per point.</summary>
        public static RectangleF Path(ReadOnlySpan<Vector2> points, ReadOnlySpan<float> radii, PathJoin join = PathJoin.Round, PathCap cap = PathCap.Round, PathCap? capEnd = null, float miterLimit = 4f, bool closed = false) {
            return PathCore(points, default, radii, 0f, join, cap, capEnd ?? cap, miterLimit, closed);
        }

        /// <summary>
        /// Bounds of the DrawPath overload whose points carry join styles. Named apart from
        /// <see cref="Path(ReadOnlySpan{Vector2}, float, PathJoin, PathCap, PathCap?, float, bool)"/>
        /// because a Vector2 converts to a PathPoint, which would leave a list of plain points with
        /// two overloads to choose between.
        /// </summary>
        public static RectangleF StyledPath(ReadOnlySpan<PathPoint> points, float radius, PathJoin join = PathJoin.Round, PathCap cap = PathCap.Round, PathCap? capEnd = null, float miterLimit = 4f, bool closed = false) {
            return PathCore(default, points, default, radius, join, cap, capEnd ?? cap, miterLimit, closed);
        }

        /// <summary>Bounds of the styled path overload that also takes a radius per point.</summary>
        public static RectangleF StyledPath(ReadOnlySpan<PathPoint> points, ReadOnlySpan<float> radii, PathJoin join = PathJoin.Round, PathCap cap = PathCap.Round, PathCap? capEnd = null, float miterLimit = 4f, bool closed = false) {
            return PathCore(default, points, radii, 0f, join, cap, capEnd ?? cap, miterLimit, closed);
        }

        /// <summary>
        /// Bounds of FillCircleBlurred and BorderCircleBlurred. The falloff is symmetric about the
        /// edge, so the circle keeps its size and the bounds grow by the three standard deviations
        /// the falloff is drawn out to.
        /// </summary>
        public static RectangleF CircleBlurred(Vector2 center, float radius, float blur) {
            float r = MathF.Max(radius, 0f) + BlurReach(blur);
            return new RectangleF(center.X - r, center.Y - r, r * 2f, r * 2f);
        }

        /// <summary>Bounds of FillEllipseBlurred and BorderEllipseBlurred.</summary>
        public static RectangleF EllipseBlurred(Vector2 center, float width, float height, float blur, float rotation = 0f) {
            return EllipseBounds(center, width, height, rotation, BlurReach(blur));
        }

        /// <summary>Bounds of FillRectangleBlurred and BorderRectangleBlurred.</summary>
        public static RectangleF RectangleBlurred(Vector2 xy, Vector2 size, float blur, CornerRadii cornerRadii = default, float rotation = 0f) {
            return RectangleBounds(xy, size, cornerRadii, rotation, BlurReach(blur));
        }

        /// <summary>Bounds of FillChamferBlurred and BorderChamferBlurred.</summary>
        public static RectangleF ChamferBlurred(Vector2 xy, Vector2 size, float blur, CornerChamfers chamfers = default, float rotation = 0f) {
            return ChamferBounds(xy, size, chamfers, rotation, BlurReach(blur));
        }

        /// <summary>Bounds of FillLineBlurred and BorderLineBlurred.</summary>
        public static RectangleF LineBlurred(Vector2 a, Vector2 b, float radius, float blur) {
            return LineBlurred(a, b, radius, radius, blur);
        }

        /// <summary>Bounds of the tapering FillLineBlurred and BorderLineBlurred.</summary>
        public static RectangleF LineBlurred(Vector2 a, Vector2 b, float radiusA, float radiusB, float blur) {
            float reach = BlurReach(blur);
            Bounds bb = default;
            bb.Add(a, MathF.Max(radiusA, 0f) + reach);
            bb.Add(b, MathF.Max(radiusB, 0f) + reach);
            return bb;
        }

        // Three sigma is where the tail drops under half of an 8 bit alpha step, which is exactly
        // how far the shader follows it. A blur under half a pixel is floored at half a pixel when
        // it is drawn; that floor is left out here for the same reason the fringe is, being a pixel
        // distance and a sub-pixel one at that.
        private const float _blurReach = 3f;
        private static float BlurReach(float blur) {
            return _blurReach * MathF.Max(blur, 0f);
        }

        private static RectangleF RectangleBounds(Vector2 xy, Vector2 size, CornerRadii cornerRadii, float rotation, float margin) {
            Vector2 half = size / 2f;
            Vector2 center = xy + half;

            float maxR = MathF.Min(size.X, size.Y) / 2f;
            float rTL = MathHelper.Clamp(cornerRadii.TopLeft,     0f, maxR);
            float rTR = MathHelper.Clamp(cornerRadii.TopRight,    0f, maxR);
            float rBR = MathHelper.Clamp(cornerRadii.BottomRight, 0f, maxR);
            float rBL = MathHelper.Clamp(cornerRadii.BottomLeft,  0f, maxR);

            // A rounded rectangle is the convex hull of its four corner discs, whatever radius each
            // of them got: every side is a common tangent of the two discs it runs between. So the
            // box is however far those four discs reach once the rotation has turned their centers,
            // which stays exact instead of falling back on the un-rounded corners.
            (float sin, float cos) = SinCos(rotation);
            Bounds bb = default;
            bb.Add(center + Rotate(new Vector2(rTL - half.X, rTL - half.Y), sin, cos), rTL + margin);
            bb.Add(center + Rotate(new Vector2(half.X - rTR, rTR - half.Y), sin, cos), rTR + margin);
            bb.Add(center + Rotate(new Vector2(half.X - rBR, half.Y - rBR), sin, cos), rBR + margin);
            bb.Add(center + Rotate(new Vector2(rBL - half.X, half.Y - rBL), sin, cos), rBL + margin);
            return bb;
        }

        // A chamfer box is the hull of its eight corners, so the box is what those corners reach
        // once the rotation has turned them. The margin rides on each of them because the offset
        // of a convex shape by d is its own support plus d in every direction.
        private static RectangleF ChamferBounds(Vector2 xy, Vector2 size, CornerChamfers chamfers, float rotation, float margin) {
            Vector2 half = size / 2f;
            Vector2 center = xy + half;

            float maxC = MathF.Min(size.X, size.Y) / 2f;
            float cTL = MathHelper.Clamp(chamfers.TopLeft,     0f, maxC);
            float cTR = MathHelper.Clamp(chamfers.TopRight,    0f, maxC);
            float cBR = MathHelper.Clamp(chamfers.BottomRight, 0f, maxC);
            float cBL = MathHelper.Clamp(chamfers.BottomLeft,  0f, maxC);

            Span<Vector2> corners = stackalloc Vector2[] {
                new(-half.X + cTL, -half.Y), new(half.X - cTR, -half.Y),
                new(half.X, -half.Y + cTR), new(half.X, half.Y - cBR),
                new(half.X - cBR, half.Y), new(-half.X + cBL, half.Y),
                new(-half.X, half.Y - cBL), new(-half.X, -half.Y + cTL),
            };
            return HullOfDiscs(corners, center, rotation, margin);
        }

        // The offset of a convex shape by d has the shape's own support plus d in every direction,
        // so a blurred ellipse's box is its own plus the reach on each side rather than the box of
        // a fatter ellipse, which is what the quad settles for.
        private static RectangleF EllipseBounds(Vector2 center, float radius1, float radius2, float rotation, float margin) {
            (float sin, float cos) = SinCos(rotation);
            float x1 = radius1 * cos, x2 = radius2 * sin;
            float y1 = radius1 * sin, y2 = radius2 * cos;
            float halfX = MathF.Sqrt(x1 * x1 + x2 * x2) + margin;
            float halfY = MathF.Sqrt(y1 * y1 + y2 * y2) + margin;
            return new RectangleF(center.X - halfX, center.Y - halfY, halfX * 2f, halfY * 2f);
        }

        // Every path measure ends up here. A path is the union of its segments' hulls, so a disc at
        // every point covers all of it: the field carves the stroke back to the spine's own width,
        // and round joins, bevel joins, round caps and butt caps all sit inside that. Two things
        // reach past it, and both are what the mesh puts there rather than what the field carves. A
        // square cap stands its box a radius clear of the end, and a miter tip runs out along the
        // bisector as far as the corner asks for, which on a sharp turn is much further than the
        // stroke is wide.
        private static RectangleF PathCore(ReadOnlySpan<Vector2> points, ReadOnlySpan<PathPoint> styled, ReadOnlySpan<float> radii, float radius, PathJoin join, PathCap capStart, PathCap capEnd, float miterLimit, bool closed) {
            int take = styled.IsEmpty ? points.Length : styled.Length;
            if (!radii.IsEmpty) take = Math.Min(take, radii.Length);
            if (take == 0) return default;
            bool tapered = !radii.IsEmpty;
            bool anyJoins = !styled.IsEmpty;

            // Borrowed rather than stack allocated or freshly allocated: a path is any length, and
            // culling runs every frame.
            Vector2[] pts = ArrayPool<Vector2>.Shared.Rent(take);
            PathJoin[] joins = anyJoins ? ArrayPool<PathJoin>.Shared.Rent(take) : [];
            float[] rs = tapered ? ArrayPool<float>.Shared.Rent(take) : [];
            try {
                int n = Dedupe(points, styled, radii, take, pts,
                               anyJoins ? joins.AsSpan(0, take) : default,
                               tapered ? rs.AsSpan(0, take) : default, closed, join);
                return PathBounds(pts.AsSpan(0, n), anyJoins ? joins.AsSpan(0, n) : default, radius,
                                  tapered ? rs.AsSpan(0, n) : default, join, capStart, capEnd, miterLimit, closed);
            } finally {
                ArrayPool<Vector2>.Shared.Return(pts);
                if (anyJoins) ArrayPool<PathJoin>.Shared.Return(joins);
                if (tapered) ArrayPool<float>.Shared.Return(rs);
            }
        }

        // The same points DrawPath keeps: consecutive duplicates go, since a segment with no length
        // has no direction, and a closed path that already returns to its first point drops the
        // repeat. A dropped point still hands on its style and the widest radius read at that spot.
        // Measure has to agree with the draw here, because which points survive is what decides
        // where the joints are.
        private static int Dedupe(ReadOnlySpan<Vector2> points, ReadOnlySpan<PathPoint> styled, ReadOnlySpan<float> radii, int take, Span<Vector2> pts, Span<PathJoin> joins, Span<float> rs, bool closed, PathJoin seed) {
            int n = 0;
            PathJoin running = seed;
            for (int i = 0; i < take; i++) {
                Vector2 p;
                if (styled.IsEmpty) {
                    p = points[i];
                } else {
                    p = styled[i].Position;
                    if (styled[i].Join.HasValue) running = styled[i].Join!.Value;
                }
                if (n == 0 || Vector2.DistanceSquared(p, pts[n - 1]) > 1e-12f) {
                    pts[n] = p;
                    if (!joins.IsEmpty) joins[n] = running;
                    if (!radii.IsEmpty) rs[n] = radii[i];
                    n++;
                } else {
                    if (!joins.IsEmpty) joins[n - 1] = running;
                    if (!radii.IsEmpty) rs[n - 1] = MathF.Max(rs[n - 1], radii[i]);
                }
            }
            if (closed && n > 1 && Vector2.DistanceSquared(pts[n - 1], pts[0]) <= 1e-12f) {
                n--;
                if (!joins.IsEmpty) joins[0] = joins[n];
                if (!radii.IsEmpty) rs[0] = MathF.Max(rs[0], rs[n]);
            }
            return n;
        }

        private static RectangleF PathBounds(ReadOnlySpan<Vector2> pts, ReadOnlySpan<PathJoin> joins, float radius, ReadOnlySpan<float> radii, PathJoin join, PathCap capStart, PathCap capEnd, float miterLimit, bool closed) {
            int n = pts.Length;
            Bounds bb = default;
            if (n == 0) return bb;
            // A loop needs a triangle at least; anything shorter draws as the open stroke it is.
            if (closed && n < 3) closed = false;

            if (n == 1) {
                // A lone point draws as its start cap, or as nothing at all when that cap is butt.
                float r0 = PointRadius(radii, radius, 0);
                if (capStart == PathCap.Round) {
                    bb.Add(pts[0], r0);
                } else if (capStart == PathCap.Square) {
                    bb.Add(pts[0] - new Vector2(r0));
                    bb.Add(pts[0] + new Vector2(r0));
                }
                return bb;
            }

            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++) {
                Vector2 a = pts[i];
                Vector2 b = pts[(i + 1) % n];
                float radiusA = PointRadius(radii, radius, i);
                float radiusB = PointRadius(radii, radius, (i + 1) % n);
                bb.Add(a, radiusA);
                bb.Add(b, radiusB);

                float lenPrev = (b - a).Length();
                Vector2 u = (b - a) / lenPrev;
                // The joint at the far end of this segment, which needs the segment after it.
                if (closed || i < segs - 1) {
                    Vector2 c = pts[(i + 2) % n];
                    float radiusC = PointRadius(radii, radius, (i + 2) % n);
                    Vector2 d = c - b;
                    float len = d.Length();
                    PathJoin requested = joins.IsEmpty ? join : joins[(i + 1) % n];
                    if (MiterTip(u, lenPrev, d / len, len, b, radiusA, radiusB, radiusC, requested, miterLimit, out Vector2 tip)) {
                        bb.Add(tip);
                    }
                } else if (capEnd == PathCap.Square) {
                    AddSquareCap(ref bb, b, u, radiusB);
                }
                if (!closed && i == 0 && capStart == PathCap.Square) {
                    AddSquareCap(ref bb, a, -u, radiusA);
                }
            }
            return bb;
        }

        /// <summary>
        /// Where a joint's outer corner lands, when it lands outside the disc the joint point
        /// already contributed. Only a miter does: a round join's fan and a bevel's cut both stay
        /// within the stroke's own half width, and the field carves them back to it. A miter end
        /// leaves that field open, so the corner the mesh reaches for is the corner the shape has.
        ///
        /// Mirrors the joint classification in ShapeBatch.DrawPathCore. The half width here is the
        /// stroke's rather than the stroke's plus its fringe, which only ever moves the answer the
        /// safe way: every threshold below is one this narrower corner passes at least as easily,
        /// so a miter that gets drawn is a miter that gets measured.
        /// </summary>
        private static bool MiterTip(Vector2 uPrev, float lenPrev, Vector2 u, float len, Vector2 joint, float rPrev, float rJoint, float rNext, PathJoin requested, float miterLimit, out Vector2 tip) {
            tip = default;
            if (requested != PathJoin.Miter) return false;

            float c2 = Vector2.Dot(uPrev, u);
            float cHalf = MathF.Sqrt(MathF.Max((1f + c2) * 0.5f, 0f));
            // Near reversal the bisector degenerates and both sides fall back to round caps.
            if (cHalf < 0.05f) return false;
            // SVG semantics: the miter ratio is 1 / cos of the half turn. Past the limit the joint
            // is bevelled, and a bevel cuts the corner off rather than reaching for it.
            if (1f > cHalf * miterLimit) return false;

            float sHalf = MathF.Sqrt(MathF.Max((1f - c2) * 0.5f, 0f));
            // The corner is built from whichever of the two segments reaches further, so that half
            // width is what decides whether the inner miter fits, exactly as DrawPathCore has it.
            float h = MathF.Max(SegHalfWidth(rPrev, rJoint, lenPrev), SegHalfWidth(rJoint, rNext, len));
            // The inner miter outruns a short segment, so the joint overlaps instead of mitering.
            if (h * sHalf / cHalf > MathF.Min(lenPrev, len) * 0.5f) return false;
            // Straight through: there is no corner to speak of.
            if (2f * MathF.Atan2(sHalf, cHalf) <= 1e-4f) return false;

            // Both walls run tangent to the circle at the joint, so each stands the joint's own half
            // width off it however the rest of the stroke tapers, and the tip is where they cross:
            // rJoint / cos of half the angle between them. That angle is the turn while the width
            // holds. A taper leans each wall off its spine by asin of its slope, toward the narrow
            // end, so a joint the stroke bulges at has both walls leaning apart and opens out past
            // the turn, while one it tapers straight through has them leaning the same way and
            // partly cancelling.
            //
            // The mesh caps it either way: a miter's quad runs to h / cos of the half turn and
            // stops, so nothing past that is drawn whatever the walls do.
            float slopePrev = lenPrev > 0f ? (rPrev - rJoint) / lenPrev : 0f;
            float slopeNext = len > 0f ? (rJoint - rNext) / len : 0f;
            float reach;
            if (slopePrev == 0f && slopeNext == 0f) {
                reach = rJoint / cHalf;
            } else if (MathF.Abs(slopePrev) >= 1f || MathF.Abs(slopeNext) >= 1f) {
                // One end circle swallows the other, so that segment has no walls and no corner.
                // Whatever is drawn is inside the circle the point already contributed.
                return false;
            } else {
                float half = MathF.Atan2(sHalf, cHalf) + 0.5f * (MathF.Asin(slopeNext) - MathF.Asin(slopePrev));
                float cosHalf = MathF.Cos(half);
                float walls = cosHalf > 1e-3f ? rJoint / cosHalf : float.MaxValue;
                reach = MathF.Min(walls, h / cHalf);
            }

            float s2 = uPrev.X * u.Y - uPrev.Y * u.X;
            float sign = s2 >= 0f ? 1f : -1f;
            // The bisector, pointing at the inner miter; the outer tip is the other way along it.
            Vector2 m = (new Vector2(-uPrev.Y, uPrev.X) + new Vector2(-u.Y, u.X)) / (2f * cHalf);
            tip = joint - m * (sign * reach);
            return true;
        }

        // Half the stroke's width at a point. A uniform path answers with the one radius it was given.
        private static float PointRadius(ReadOnlySpan<float> radii, float radius, int i) {
            return radii.IsEmpty ? radius : radii[i];
        }

        // How far a tapered segment's wall stands off the spine. The wall is the two end circles'
        // common tangent, which leans, so it stands 1 / Cos further out than the perpendicular
        // offset; and once the radii differ by more than the segment is long, one circle swallows
        // the other and the hull is that circle alone. Equal radii give the radius back unchanged.
        private static float SegHalfWidth(float rA, float rB, float len) {
            float b = (rA - rB) / len;
            float h = MathF.Max(rA, rB);
            if (b == 0f) return h;
            float cos = MathF.Sqrt(MathF.Max(1f - b * b, 0f));
            return cos <= 0f ? h : h / cos;
        }

        // A square cap is the one cap that leaves the end disc: an exact box standing a radius past
        // the point, so its two far corners are what has to be reached. Round and butt both stop
        // inside the disc the point already contributed.
        private static void AddSquareCap(ref Bounds bb, Vector2 end, Vector2 outward, float radius) {
            Vector2 o = end + outward * radius;
            Vector2 nrm = new(-outward.Y * radius, outward.X * radius);
            bb.Add(o + nrm);
            bb.Add(o - nrm);
        }

        // Half the turn an arc or a ring sweeps, wrapped the way DrawArc and DrawRing wrap it. The
        // sweep is centered on the average of the two angles.
        private static float ArcHalfSpan(float angle1, float angle2) {
            return MathF.Abs(Mod((angle2 - angle1) * 0.5f + MathF.PI, MathF.PI * 2f) - MathF.PI);
        }

        private static Vector2 Direction(float angle) {
            (float sin, float cos) = SinCos(angle);
            return new Vector2(cos, sin);
        }

        // A swept band reaches its full outer radius along an axis only where the sweep covers that
        // axis. Everywhere else the ends decide it, and those the caller has already added.
        private static void AddSweptAxes(ref Bounds bb, Vector2 center, float mid, float halfSpan, float outer) {
            for (int k = 0; k < 4; k++) {
                float angle = k * MathF.PI * 0.5f;
                if (MathF.Abs(Mod(angle - mid + MathF.PI, MathF.PI * 2f) - MathF.PI) > halfSpan) continue;
                bb.Add(center + Direction(angle) * outer);
            }
        }

        private static RectangleF HullOfDiscs(ReadOnlySpan<Vector2> corners, Vector2 center, float rotation, float radius) {
            (float sin, float cos) = SinCos(rotation);
            Bounds bb = default;
            foreach (Vector2 corner in corners) {
                bb.Add(center + Rotate(corner, sin, cos), radius);
            }
            return bb;
        }

        private static Vector2 Rotate(Vector2 v, float sin, float cos) {
            return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        }
        private static (float Sin, float Cos) SinCos(float a) {
            return (MathF.Sin(a), MathF.Cos(a));
        }
        private static float Mod(float x, float m) {
            return (x % m + m) % m;
        }

        /// <summary>
        /// Grows to hold whatever it is given. Everything a measure knows about a shape arrives as
        /// a point or as a disc around one, since a bounding box of a union is the union of the
        /// boxes and the box of a convex hull is the box of what it was built from.
        /// </summary>
        private struct Bounds {
            private float _minX, _minY, _maxX, _maxY;
            private bool _any;

            public void Add(Vector2 p) {
                Add(p, 0f);
            }
            public void Add(Vector2 c, float radius) {
                if (!_any) {
                    _any = true;
                    _minX = c.X - radius;
                    _minY = c.Y - radius;
                    _maxX = c.X + radius;
                    _maxY = c.Y + radius;
                    return;
                }
                _minX = MathF.Min(_minX, c.X - radius);
                _minY = MathF.Min(_minY, c.Y - radius);
                _maxX = MathF.Max(_maxX, c.X + radius);
                _maxY = MathF.Max(_maxY, c.Y + radius);
            }

            public static implicit operator RectangleF(Bounds bb) {
                return bb._any ? new RectangleF(bb._minX, bb._minY, bb._maxX - bb._minX, bb._maxY - bb._minY) : default;
            }
        }
    }
}
