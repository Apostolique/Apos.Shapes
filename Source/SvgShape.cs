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
        // Set when the whole fill had more curves across a row or column than a band holds, so
        // it is drawn as these instead: the same outline cut into tiles that each fit. Fill
        // stays the whole thing, which is what the element's box is measured from.
        internal BakedGlyph[]? FillTiles;
        // Where the fill's own frame sits in the document's em frame.
        internal Vector2 Origin;
        // How much larger than the document's em the fill's own em is drawn. 1 unless the element
        // was too wide to bake as is: it is baked shrunk to fit, and drawn at this scale so it
        // lands where it was.
        internal float FillScale = 1f;
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
        internal bool MiterClip;
        internal float MiterLimit;

        // The dash pattern in em units along the contour: one period, and where each dash in it
        // starts and how long it is, counted from the start of every subpath. DashStyle has one
        // dash per period, so each of these draws as a pass of its own.
        internal bool Dashed;
        internal float DashPeriod;
        internal float[] DashStarts = Array.Empty<float>();
        internal float[] DashLengths = Array.Empty<float>();
        // How far along its contour each subpath's polyline now starts, when its start was cut
        // short. Empty when none was.
        internal float[] DashShift = Array.Empty<float>();
        internal DashCap DashCap;

        // The element's box in the document's em frame, stroke included.
        internal Vector2 Min;
        internal Vector2 Max;
    }
}
