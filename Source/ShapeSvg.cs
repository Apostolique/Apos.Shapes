using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Xml;
using Microsoft.Xna.Framework;

namespace Apos.Shapes {
    /// <summary>
    /// A vector drawing a <see cref="ShapeBatch"/> draws, loaded from an SVG file. The outlines
    /// come straight out of the file and the pixel shader solves the coverage of every curve, the
    /// same way text works, so the drawing is exact at any size and nothing goes blurry when you
    /// zoom in.
    ///
    /// Loading is the expensive part, so load a drawing once and keep it. One drawing can back
    /// any number of batches at the same time.
    ///
    /// What it reads: `path`, `rect`, `circle`, `ellipse`, `line`, `polyline`, `polygon` and `g`,
    /// with transforms, fills, strokes, and linear and radial gradients out of `defs`. Text,
    /// `use`, clip paths, masks, filters, patterns and CSS style blocks are ignored. Anything
    /// ignored is counted rather than reported, so a file always loads if it parses as XML.
    ///
    /// Sizes are in em units: one em is the height of the viewBox, so multiply by the height you
    /// draw at to get world units. Everything here is safe to call from any thread.
    /// </summary>
    public sealed class ShapeSvg {
        /// <summary>Loads a drawing from SVG markup.</summary>
        /// <param name="markup">The whole document as text.</param>
        /// <param name="tolerance">
        /// How far a curve may stray from the shape the file describes, as a fraction of the
        /// viewBox diagonal. Smaller costs more curves.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="markup"/> is null.</exception>
        /// <exception cref="ArgumentException">The text isn't an SVG document this can read.</exception>
        public ShapeSvg(string markup, float tolerance = DefaultTolerance) {
            ArgumentNullException.ThrowIfNull(markup);
            using var text = new StringReader(markup);
            Load(text, tolerance);
        }
        /// <summary>Loads a drawing from the bytes of an .svg file.</summary>
        /// <param name="svg">The whole file.</param>
        /// <param name="tolerance">
        /// How far a curve may stray from the shape the file describes, as a fraction of the
        /// viewBox diagonal. Smaller costs more curves.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="svg"/> is null.</exception>
        /// <exception cref="ArgumentException">The bytes aren't an SVG document this can read.</exception>
        public ShapeSvg(byte[] svg, float tolerance = DefaultTolerance) {
            ArgumentNullException.ThrowIfNull(svg);
            using var stream = new MemoryStream(svg, false);
            Load(stream, tolerance);
        }
        /// <summary>Loads a drawing by reading a stream to its end. The stream stays open.</summary>
        /// <param name="svg">A stream over a whole .svg file.</param>
        /// <param name="tolerance">
        /// How far a curve may stray from the shape the file describes, as a fraction of the
        /// viewBox diagonal. Smaller costs more curves.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="svg"/> is null.</exception>
        /// <exception cref="ArgumentException">The bytes aren't an SVG document this can read.</exception>
        public ShapeSvg(Stream svg, float tolerance = DefaultTolerance) {
            ArgumentNullException.ThrowIfNull(svg);
            Load(svg, tolerance);
        }

        /// <summary>
        /// Loads a drawing and hands back whether it worked, for when a file comes from somewhere
        /// you don't control. Nothing throws.
        /// </summary>
        /// <param name="markup">The whole document as text.</param>
        /// <param name="svg">The loaded drawing, or null when this returns false.</param>
        /// <param name="tolerance">
        /// How far a curve may stray from the shape the file describes, as a fraction of the
        /// viewBox diagonal.
        /// </param>
        /// <returns>False when the text isn't an SVG document this can read.</returns>
        public static bool TryLoad(string markup, [NotNullWhen(true)] out ShapeSvg? svg, float tolerance = DefaultTolerance) {
            svg = null;
            if (markup == null) return false;
            try {
                svg = new ShapeSvg(markup, tolerance);
                return true;
            } catch (ArgumentException) {
                return false;
            }
        }
        /// <summary>Loads a drawing from bytes and hands back whether it worked. See the string overload.</summary>
        /// <param name="bytes">The whole file.</param>
        /// <param name="svg">The loaded drawing, or null when this returns false.</param>
        /// <param name="tolerance">
        /// How far a curve may stray from the shape the file describes, as a fraction of the
        /// viewBox diagonal.
        /// </param>
        /// <returns>False when the bytes aren't an SVG document this can read.</returns>
        public static bool TryLoad(byte[] bytes, [NotNullWhen(true)] out ShapeSvg? svg, float tolerance = DefaultTolerance) {
            svg = null;
            if (bytes == null || bytes.Length == 0) return false;
            try {
                svg = new ShapeSvg(bytes, tolerance);
                return true;
            } catch (ArgumentException) {
                return false;
            }
        }
        /// <summary>Loads a drawing from a stream and hands back whether it worked. See the string overload.</summary>
        /// <param name="stream">A stream over a whole .svg file.</param>
        /// <param name="svg">The loaded drawing, or null when this returns false.</param>
        /// <param name="tolerance">
        /// How far a curve may stray from the shape the file describes, as a fraction of the
        /// viewBox diagonal.
        /// </param>
        /// <returns>False when the stream can't be read, or isn't an SVG document this can read.</returns>
        public static bool TryLoad(Stream stream, [NotNullWhen(true)] out ShapeSvg? svg, float tolerance = DefaultTolerance) {
            svg = null;
            if (stream == null) return false;
            try {
                svg = new ShapeSvg(stream, tolerance);
                return true;
            } catch (ArgumentException) {
                return false;
            } catch (IOException) {
                return false;
            }
        }

        /// <summary>How far a curve may stray from the file's shape by default, as a fraction of the viewBox diagonal.</summary>
        public const float DefaultTolerance = 0.001f;

        /// <summary>The viewBox's width in the document's own units. One em is <see cref="Height"/>.</summary>
        public float Width { get; private set; }
        /// <summary>The viewBox's height in the document's own units, which is what one em means for this drawing.</summary>
        public float Height { get; private set; }

        /// <summary>
        /// The box <see cref="ShapeBatch.DrawSvg(ShapeSvg, Vector2, float, float, Vector2, float)"/>
        /// draws into, in world units, with its top left corner at the position the drawing is
        /// drawn at. It's the viewBox, so the height is the size given and the width follows the
        /// document's aspect ratio.
        ///
        /// A file may draw outside its own viewBox, and this doesn't clip, so ink can land past
        /// this box. It's where the file says the picture is, not a promise about the pixels.
        /// </summary>
        /// <param name="size">Em size in world units, the same one the drawing is drawn at.</param>
        public Vector2 Measure(float size) {
            return new Vector2(Width / Height * size, size);
        }

        // Everything drawable in the document, in paint order. See the em frame note on SvgShape.
        internal IReadOnlyList<SvgShape> Shapes => _shapes;
        // Features the file asked for that this doesn't draw. A quality statistic, not an error.
        internal int Skipped => _skipped;
        internal IReadOnlyList<string> SkippedNames => _skippedNames;
        // The viewBox's corner in document units, which is what the em frame's origin sits on.
        internal Vector2 ViewMin { get; private set; }
        // How far the whole drawing reaches in the em frame, which is what a bounds query needs.
        internal Vector2 Min { get; private set; }
        internal Vector2 Max { get; private set; }

        private readonly List<SvgShape> _shapes = new();
        private readonly List<string> _skippedNames = new();
        private int _skipped;

        // Design units the fill baker is fed. Any grid works, since the baker divides by the same
        // number to get em units back; 2048 is what a TrueType font uses and keeps the integer
        // band box from quantizing anything visible.
        private const int Grid = 2048;
        // How far out of the em square an element's own control points may reach. The curve
        // texture holds them as fixed point over [-2, 2] on the KNI targets and the pad curve
        // every short band list is filled out with sits at -1.5, so an element wider than this
        // is dropped rather than drawn wrong.
        private const float EmReach = 1.2f;

        private void Load(Stream stream, float tolerance) {
            try {
                using XmlReader reader = XmlReader.Create(stream, Settings());
                Parse(reader, tolerance);
            } catch (XmlException e) {
                throw new ArgumentException("The bytes could not be read as an SVG document.", nameof(stream), e);
            }
        }

        private void Load(TextReader text, float tolerance) {
            try {
                using XmlReader reader = XmlReader.Create(text, Settings());
                Parse(reader, tolerance);
            } catch (XmlException e) {
                throw new ArgumentException("The text could not be read as an SVG document.", nameof(text), e);
            }
        }

        // No DTD and no resolver: an SVG can name an external entity, and resolving one would
        // read a file or reach the network on behalf of whoever handed over the document.
        private static XmlReaderSettings Settings() {
            return new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                CloseInput = false,
            };
        }

        // What a drawable was, kept until the viewBox is known: the tolerance is a fraction of
        // the viewBox diagonal and a file is allowed not to have one, in which case the box is
        // whatever the geometry turns out to cover.
        private readonly struct Pending {
            internal Pending(string name, SvgAttrs attrs, SvgStyle style, in SvgMatrix m) {
                Name = name;
                Attrs = attrs;
                Style = style;
                M = m;
            }
            internal readonly string Name;
            internal readonly SvgAttrs Attrs;
            internal readonly SvgStyle Style;
            internal readonly SvgMatrix M;
        }

        private struct Frame {
            internal SvgStyle Style;
            internal SvgMatrix M;
            internal bool Ignore;
            internal bool Defs;
            internal SvgGradientDef? Grad;
        }

        private void Parse(XmlReader r, float tolerance) {
            var pending = new List<Pending>();
            var gradients = new SvgGradients();
            var stack = new List<Frame>();
            SvgStyle rootStyle = SvgStyle.Root();
            SvgMatrix rootM = SvgMatrix.Identity;

            bool sawRoot = false;
            bool hasBox = false;
            float vbX = 0f, vbY = 0f, vbW = 0f, vbH = 0f;
            float attrW = 0f, attrH = 0f;

            while (r.Read()) {
                if (r.NodeType == XmlNodeType.EndElement) {
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    continue;
                }
                if (r.NodeType != XmlNodeType.Element) continue;

                bool empty = r.IsEmptyElement;
                Frame parent = stack.Count > 0 ? stack[stack.Count - 1] : new Frame { Style = rootStyle, M = rootM };
                var frame = new Frame {
                    Style = parent.Style,
                    M = parent.M,
                    Ignore = parent.Ignore,
                    Defs = parent.Defs,
                    Grad = parent.Grad,
                };

                if (!parent.Ignore) {
                    string name = r.LocalName;
                    var attrs = new SvgAttrs(r);
                    switch (name) {
                        case "svg":
                            if (!sawRoot) {
                                sawRoot = true;
                                hasBox = ViewBox(attrs.Raw("viewBox"), ref vbX, ref vbY, ref vbW, ref vbH);
                                SvgColor.TryLength(attrs.Raw("width"), 0f, out attrW);
                                SvgColor.TryLength(attrs.Raw("height"), 0f, out attrH);
                            } else {
                                // A nested svg is its own viewport with its own scaling.
                                Drop("svg");
                            }
                            frame.Style = frame.Style.With(attrs, ref _skipped);
                            frame.M = SvgMatrix.Mul(frame.M, SvgMatrix.Parse(attrs.Raw("transform"), ref _skipped));
                            break;
                        case "g":
                        case "a":
                            frame.Style = frame.Style.With(attrs, ref _skipped);
                            frame.M = SvgMatrix.Mul(frame.M, SvgMatrix.Parse(attrs.Raw("transform"), ref _skipped));
                            break;
                        case "switch":
                            // Only the first child that a renderer accepts should draw; drawing
                            // all of them is what happens instead.
                            Drop("switch");
                            frame.Style = frame.Style.With(attrs, ref _skipped);
                            frame.M = SvgMatrix.Mul(frame.M, SvgMatrix.Parse(attrs.Raw("transform"), ref _skipped));
                            break;
                        case "defs":
                            frame.Defs = true;
                            frame.Style = frame.Style.With(attrs, ref _skipped);
                            frame.M = SvgMatrix.Mul(frame.M, SvgMatrix.Parse(attrs.Raw("transform"), ref _skipped));
                            break;
                        case "linearGradient":
                        case "radialGradient": {
                            string id = attrs.Raw("id") ?? string.Empty;
                            var def = new SvgGradientDef(id, name[0] == 'r');
                            Copy(r, def);
                            def.Href = Reference(attrs);
                            gradients.Add(def);
                            frame.Grad = def;
                            break;
                        }
                        case "stop":
                            if (frame.Grad != null) Stop(attrs, frame.Grad);
                            break;
                        case "path":
                        case "rect":
                        case "circle":
                        case "ellipse":
                        case "line":
                        case "polyline":
                        case "polygon": {
                            SvgStyle style = frame.Style.With(attrs, ref _skipped);
                            SvgMatrix m = SvgMatrix.Mul(frame.M, SvgMatrix.Parse(attrs.Raw("transform"), ref _skipped));
                            if (!frame.Defs) pending.Add(new Pending(name, attrs, style, m));
                            frame.Ignore = true;
                            break;
                        }
                        case "title":
                        case "desc":
                        case "metadata":
                            frame.Ignore = true;
                            break;
                        default:
                            Drop(name);
                            frame.Ignore = true;
                            break;
                    }
                }

                if (!empty) stack.Add(frame);
            }

            if (!sawRoot) {
                throw new ArgumentException("The document has no svg element.");
            }

            float tol = MathF.Max(tolerance, 1e-6f);
            if (!hasBox) {
                if (attrW > 0f && attrH > 0f) {
                    vbX = 0f;
                    vbY = 0f;
                    vbW = attrW;
                    vbH = attrH;
                    hasBox = true;
                } else {
                    // No box of its own, so the geometry's is the box. A coarse pass is enough to
                    // find it: the tolerance it picks only moves the box by the tolerance itself.
                    Content(pending, out vbX, out vbY, out vbW, out vbH);
                    hasBox = vbW > 0f && vbH > 0f;
                }
            }
            if (!hasBox || !(vbW > 0f) || !(vbH > 0f)
                || !float.IsFinite(vbX) || !float.IsFinite(vbY)
                || !float.IsFinite(vbW) || !float.IsFinite(vbH)) {
                vbX = 0f;
                vbY = 0f;
                vbW = 1f;
                vbH = 1f;
            }

            Width = vbW;
            Height = vbH;
            ViewMin = new Vector2(vbX, vbY);
            float u = vbH;
            float doc = MathF.Sqrt(vbW * vbW + vbH * vbH) * tol;

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < pending.Count; i++) {
                SvgShape? shape = Build(pending[i], gradients, doc, u, new Vector2(vbX, vbY), vbW, vbH, i);
                if (shape == null) continue;
                _shapes.Add(shape);
                min = Vector2.Min(min, shape.Min);
                max = Vector2.Max(max, shape.Max);
            }
            if (_shapes.Count == 0) {
                min = Vector2.Zero;
                max = Vector2.Zero;
            }
            Min = min;
            Max = max;
        }

        private void Drop(string what) {
            _skipped++;
            if (_skippedNames.Count < 64 && !_skippedNames.Contains(what)) _skippedNames.Add(what);
        }

        private static void Copy(XmlReader r, SvgGradientDef def) {
            if (!r.HasAttributes) return;
            r.MoveToFirstAttribute();
            do {
                def.Attrs[r.LocalName] = r.Value;
            } while (r.MoveToNextAttribute());
            r.MoveToElement();
        }

        // href or the older xlink:href, without the leading hash.
        private static string? Reference(SvgAttrs a) {
            string? v = a.Raw("href") ?? a.Raw("xlink:href");
            if (v == null) return null;
            v = v.Trim();
            if (!v.StartsWith("#", StringComparison.Ordinal) || v.Length < 2) return null;
            return v.Substring(1);
        }

        private static void Stop(SvgAttrs a, SvgGradientDef def) {
            def.Stops ??= new List<SvgStop>();
            float offset = 0f;
            if (SvgColor.TryLength(a.Get("offset"), 1f, out float o)) offset = Math.Clamp(o, 0f, 1f);
            // Offsets never go backwards; one that does is pulled up to the one before it.
            if (def.Stops.Count > 0) offset = MathF.Max(offset, def.Stops[def.Stops.Count - 1].Offset);
            if (!SvgColor.TryColor(a.Get("stop-color"), out Color c)) c = Color.Black;
            float alpha = 1f;
            if (SvgColor.TryLength(a.Get("stop-opacity"), 1f, out float sa)) alpha = Math.Clamp(sa, 0f, 1f);
            def.Stops.Add(new SvgStop(offset, SvgGradients.Fade(c, alpha)));
        }

        private static bool ViewBox(string? value, ref float x, ref float y, ref float w, ref float h) {
            if (value == null) return false;
            var s = new SvgScan(value);
            if (!s.TryNumber(out float a) || !s.TryNumber(out float b)
                || !s.TryNumber(out float c) || !s.TryNumber(out float d)) {
                return false;
            }
            if (!(c > 0f) || !(d > 0f)) return false;
            x = a;
            y = b;
            w = c;
            h = d;
            return true;
        }

        // The geometry's own box, for a document that never said what its box was.
        private void Content(List<Pending> pending, out float x, out float y, out float w, out float h) {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            int keep = _skipped;
            int keepNames = _skippedNames.Count;
            foreach (Pending p in pending) {
                SvgOutline o = Outline(p, 1f, 0f, 0f);
                if (o.Quads.Count == 0) continue;
                o.DocBox(out Vector2 a, out Vector2 b);
                min = Vector2.Min(min, a);
                max = Vector2.Max(max, b);
            }
            // The coarse pass is a measurement, so nothing it drops counts twice.
            _skipped = keep;
            _skippedNames.RemoveRange(keepNames, _skippedNames.Count - keepNames);
            if (min.X > max.X) {
                x = 0f;
                y = 0f;
                w = 0f;
                h = 0f;
                return;
            }
            x = min.X;
            y = min.Y;
            w = max.X - min.X;
            h = max.Y - min.Y;
        }

        private SvgOutline Outline(in Pending p, float tol, float viewW, float viewH) {
            var o = new SvgOutline(p.M, tol);
            SvgAttrs a = p.Attrs;
            switch (p.Name) {
                case "path":
                    if (!SvgPathData.Parse(a.Get("d"), o)) Drop("path data");
                    break;
                case "rect": {
                    float x = Len(a, "x", viewW);
                    float y = Len(a, "y", viewH);
                    float w = Len(a, "width", viewW);
                    float h = Len(a, "height", viewH);
                    bool hasRx = SvgColor.TryLength(a.Get("rx"), viewW, out float rx);
                    bool hasRy = SvgColor.TryLength(a.Get("ry"), viewH, out float ry);
                    if (!hasRx && hasRy) rx = ry;
                    if (hasRx && !hasRy) ry = rx;
                    SvgPathData.Rect(o, x, y, w, h, MathF.Max(rx, 0f), MathF.Max(ry, 0f));
                    break;
                }
                case "circle": {
                    float r = Len(a, "r", MathF.Sqrt((viewW * viewW + viewH * viewH) * 0.5f));
                    SvgPathData.Ellipse(o, Len(a, "cx", viewW), Len(a, "cy", viewH), r, r);
                    break;
                }
                case "ellipse":
                    SvgPathData.Ellipse(o, Len(a, "cx", viewW), Len(a, "cy", viewH),
                                        Len(a, "rx", viewW), Len(a, "ry", viewH));
                    break;
                case "line":
                    SvgPathData.Line(o, Len(a, "x1", viewW), Len(a, "y1", viewH),
                                     Len(a, "x2", viewW), Len(a, "y2", viewH));
                    break;
                case "polyline":
                    if (!SvgPathData.Points(a.Get("points"), o, false)) Drop("points");
                    break;
                case "polygon":
                    if (!SvgPathData.Points(a.Get("points"), o, true)) Drop("points");
                    break;
            }
            o.Finish();
            return o;
        }

        private static float Len(SvgAttrs a, string name, float percentOf) {
            return SvgColor.TryLength(a.Get(name), percentOf, out float v) ? v : 0f;
        }

        private SvgShape? Build(in Pending p, SvgGradients gradients, float tol, float u, Vector2 vbMin,
                                float viewW, float viewH, int index) {
            SvgOutline o = Outline(p, tol, viewW, viewH);
            if (o.Quads.Count == 0) return null;
            // Nothing downstream can be handed a coordinate that is not a number.
            if (!o.Finite) {
                Drop("unbounded element");
                return null;
            }
            o.DocBox(out Vector2 docMin, out Vector2 docMax);

            float inv = 1f / u;
            Vector2 ToEm(Vector2 q) => new((q.X - vbMin.X) * inv, -(q.Y - vbMin.Y) * inv);

            var shape = new SvgShape();
            Vector2 center = (docMin + docMax) * 0.5f;
            shape.Origin = ToEm(center);

            SvgStyle style = p.Style;
            float fillAlpha = style.FillOpacity * style.Opacity;
            float strokeAlpha = style.StrokeOpacity * style.Opacity;

            if (style.Fill.Kind != SvgPaintKind.None && fillAlpha > 0f) {
                if (Paint(style.Fill, gradients, o, p.M, ToEm, viewW, viewH, fillAlpha, out Gradient g)) {
                    shape.Fill = Bake(o, shape.Origin, u, vbMin, index);
                    if (shape.Fill != null) {
                        shape.HasFill = true;
                        shape.FillPaint = g;
                        shape.EvenOdd = style.EvenOdd;
                        shape.FillCurrent = style.Fill.Current;
                    }
                }
            }

            float pen = p.M.PenScale;
            float radius = style.StrokeWidth * pen * inv * 0.5f;
            if (style.Stroke.Kind != SvgPaintKind.None && strokeAlpha > 0f && radius > 0f) {
                if (Paint(style.Stroke, gradients, o, p.M, ToEm, viewW, viewH, strokeAlpha, out Gradient g)) {
                    if (p.M.Anisotropic) Drop("anisotropic stroke");
                    var lines = new List<Vector2[]>();
                    var closed = new List<bool>();
                    var buffer = new List<Vector2>();
                    for (int i = 0; i < o.SubpathCount; i++) {
                        o.Flatten(i, buffer, tol);
                        if (buffer.Count < 2) continue;
                        var points = new Vector2[buffer.Count];
                        for (int j = 0; j < buffer.Count; j++) points[j] = ToEm(buffer[j]);
                        lines.Add(points);
                        closed.Add(o.Closed[i]);
                    }
                    if (lines.Count > 0) {
                        shape.HasStroke = true;
                        shape.StrokePaint = g;
                        shape.StrokeCurrent = style.Stroke.Current;
                        shape.Stroke = lines.ToArray();
                        shape.StrokeClosed = closed.ToArray();
                        shape.StrokeRadius = radius;
                        shape.Cap = style.Cap;
                        shape.Join = style.Join;
                        shape.MiterLimit = style.MiterLimit;
                        Dash(shape, style, pen * inv);
                    }
                }
            }

            if (!shape.HasFill && !shape.HasStroke) return null;

            Vector2 a = ToEm(docMin);
            Vector2 b = ToEm(docMax);
            Vector2 lo = Vector2.Min(a, b);
            Vector2 hi = Vector2.Max(a, b);
            if (shape.Fill != null) {
                lo = Vector2.Min(lo, shape.Origin + shape.Fill.Min);
                hi = Vector2.Max(hi, shape.Origin + shape.Fill.Max);
            }
            float pad = shape.HasStroke ? shape.StrokeRadius : 0f;
            shape.Min = lo - new Vector2(pad);
            shape.Max = hi + new Vector2(pad);
            return shape;
        }

        // A dasharray of two lengths is the pattern DashStyle takes. One length is a dash and a
        // space of the same size, which is what the spec says an odd list repeats to. Anything
        // longer has more than one dash length in it and draws solid instead.
        private void Dash(SvgShape shape, in SvgStyle style, float toEm) {
            float[]? dash = style.Dash;
            if (dash == null) return;
            float size;
            float space;
            if (dash.Length == 1) {
                size = dash[0];
                space = dash[0];
            } else if (dash.Length == 2) {
                size = dash[0];
                space = dash[1];
            } else {
                Drop("stroke-dasharray");
                return;
            }
            float period = size + space;
            if (!(period > 0f) || !(space > 0f)) return;
            shape.Dashed = true;
            shape.DashSize = size * toEm;
            shape.DashSpacing = space * toEm;
            // DashStyle counts the offset in periods and puts a dash's center on the phase, so
            // the SVG offset comes in negated against a pattern that already starts half a dash
            // in. Square caps have no dash equivalent, so a dash keeps the flat end.
            shape.DashOffset = -style.DashOffset / period;
            shape.DashCap = style.Cap == PathCap.Round ? DashCap.Round : DashCap.Butt;
            if (style.Cap == PathCap.Square) Drop("square dash cap");
        }

        private bool Paint(in SvgPaint paint, SvgGradients gradients, SvgOutline o, in SvgMatrix m,
                           Func<Vector2, Vector2> toEm, float viewW, float viewH, float alpha, out Gradient g) {
            if (paint.Kind == SvgPaintKind.Flat) {
                g = SvgGradients.Fade(paint.Color, alpha);
                return g.AC.A > 0;
            }
            g = default;
            if (paint.Kind != SvgPaintKind.Ref || paint.Id == null) return false;
            Vector2 boxMin = o.HasBox ? o.LocalMin : Vector2.Zero;
            Vector2 boxMax = o.HasBox ? o.LocalMax : Vector2.One;
            if (gradients.TryResolve(paint.Id, boxMin, boxMax, m, toEm, viewW, viewH, alpha, out g, ref _skipped)) {
                return true;
            }
            // A reference that misses falls back to the color written after it, and to nothing
            // when there wasn't one.
            if (paint.Color.A == 0) return false;
            g = SvgGradients.Fade(paint.Color, alpha);
            return true;
        }

        // The element's fill through the glyph baker. Document units go to a pseudo design unit
        // grid: divided by the viewBox height so one em is that height, y negated so the outline
        // lands in the y up frame a glyph's does, moved so the element's own box is centered on
        // its origin, and multiplied by the grid.
        private BakedGlyph? Bake(SvgOutline o, Vector2 origin, float u, Vector2 vbMin, int index) {
            float k = Grid / u;
            float ox = (vbMin.X * (1f / u) + origin.X) * Grid;
            float oy = (vbMin.Y * (1f / u) - origin.Y) * Grid;
            Vector2 Design(Vector2 q) => new(q.X * k - ox, oy - q.Y * k);

            var curves = new List<GlyphCurve>();
            float lo = float.MaxValue, hi = float.MinValue, lox = float.MaxValue, hix = float.MinValue;
            void Grow(Vector2 d) {
                if (d.X < lox) lox = d.X;
                if (d.X > hix) hix = d.X;
                if (d.Y < lo) lo = d.Y;
                if (d.Y > hi) hi = d.Y;
            }

            for (int s = 0; s < o.SubpathCount; s++) {
                int start = o.Starts[s];
                int count = o.Counts[s];
                if (count == 0) continue;
                Vector2 first = default, last = default;
                for (int i = 0; i < count; i++) {
                    SvgQuad q = o.Quads[start + i];
                    Vector2 p1 = Design(q.P1);
                    Vector2 p2 = Design(q.P2);
                    Vector2 p3 = Design(q.P3);
                    if (i == 0) first = p1;
                    last = p3;
                    Grow(p1);
                    Grow(p2);
                    Grow(p3);
                    curves.Add(new GlyphCurve { P1 = p1, P2 = p2, P3 = p3 });
                }
                // Every subpath is closed for filling, whether or not a Z said so.
                if (last != first) {
                    curves.Add(new GlyphCurve { P1 = last, P2 = (last + first) * 0.5f, P3 = first });
                }
            }
            if (curves.Count == 0) return null;

            float reach = Grid * EmReach;
            if (!(lox >= -reach) || !(hix <= reach) || !(lo >= -reach) || !(hi <= reach)) {
                Drop("oversized element");
                return null;
            }

            int x1 = (int)MathF.Floor(lox);
            int y1 = (int)MathF.Floor(lo);
            int x2 = (int)MathF.Ceiling(hix);
            int y2 = (int)MathF.Ceiling(hi);
            return GlyphBake.Bake(curves, index, 0, 0, x1, y1, x2, y2, Grid, GlyphBake.MaxCurves);
        }
    }
}
