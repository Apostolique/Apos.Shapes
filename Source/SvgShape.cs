// One drawable out of an SVG, baked and ready for a quad.

using System;
using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    // A single element's fill and stroke, in the em frame ShapeSvg normalizes the document into.
    // Kept in the document's own paint order, so drawing the list front to back is what stacks
    // the picture the way the file says.
    internal sealed class SvgShape {
        // The filled area, or null when the element has no fill or its outline could not be
        // baked. Its control points are relative to Origin, the same way a glyph's are relative
        // to the pen.
        internal BakedGlyph? Fill;
        // Where the fill's own frame sits in the document's em frame.
        internal Vector2 Origin;
        internal Gradient FillPaint;
        internal bool HasFill;
        internal bool EvenOdd;
        // Set when the paint came from currentColor, so a draw time color can replace it.
        internal bool FillCurrent;

        // The stroke's polylines in the document's em frame, one per subpath, already flattened
        // to the load tolerance. Each has its own closed flag, which is what tells the path
        // renderer whether to join the two ends or cap them.
        internal Vector2[][] Stroke = Array.Empty<Vector2[]>();
        internal bool[] StrokeClosed = Array.Empty<bool>();
        internal Gradient StrokePaint;
        internal bool HasStroke;
        internal bool StrokeCurrent;
        // Half the stroke width in em units, which is the radius the path renderer takes.
        internal float StrokeRadius;
        internal PathCap Cap;
        internal PathJoin Join;
        internal float MiterLimit;

        // The dash pattern in em units along the contour, and the offset in periods. Only a two
        // length pattern maps onto DashStyle; anything longer is counted and drawn solid.
        internal bool Dashed;
        internal float DashSize;
        internal float DashSpacing;
        internal float DashOffset;
        internal DashCap DashCap;

        // The element's box in the document's em frame, stroke included.
        internal Vector2 Min;
        internal Vector2 Max;
    }
}
