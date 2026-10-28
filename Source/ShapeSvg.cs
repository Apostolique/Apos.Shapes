using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
    /// with transforms, fills, strokes, and linear and radial gradients out of `defs`, plus
    /// `use`, `symbol`, nested `svg` and `switch`. Styles come from presentation attributes,
    /// `style` attributes and `style` elements, where a rule may select by tag, class and id.
    /// Text, clip paths, masks, filters and patterns are ignored. Anything ignored is counted
    /// rather than reported, so a file always loads if it parses as XML.
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
            Load(() => XmlReader.Create(new StringReader(markup), Settings()), tolerance,
                 "The text could not be read as an SVG document.", nameof(markup));
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
            Load(() => XmlReader.Create(new MemoryStream(svg, false), Settings()), tolerance,
                 "The bytes could not be read as an SVG document.", nameof(svg));
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
            // The document is read twice, so the stream is taken in whole first.
            var copy = new MemoryStream();
            svg.CopyTo(copy);
            Load(() => XmlReader.Create(new MemoryStream(copy.GetBuffer(), 0, (int)copy.Length, false), Settings()), tolerance,
                 "The bytes could not be read as an SVG document.", nameof(svg));
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
        // is baked shrunk to fit and drawn enlarged.
        private const float EmReach = 1.2f;
        // How far past a tile, in ems, the outline it is cut from is kept.
        private const float TileMargin = 0.05f;

        // A style element may come after the elements it styles, so its rules are gathered in a
        // pass of their own before anything is resolved.
        private void Load(Func<XmlReader> open, float tolerance, string message, string param) {
            try {
                SvgSheet sheet;
                using (XmlReader reader = open()) sheet = Sheet(reader);
                using (XmlReader reader = open()) Parse(reader, sheet.IsEmpty ? null : sheet, tolerance);
            } catch (XmlException e) {
                throw new ArgumentException(message, param, e);
            }
        }

        private SvgSheet Sheet(XmlReader r) {
            var sheet = new SvgSheet();
            var text = new System.Text.StringBuilder();
            int depth = -1;
            while (r.Read()) {
                if (depth < 0) {
                    if (r.NodeType == XmlNodeType.Element && r.LocalName == "style" && !r.IsEmptyElement
                        && Applies(r.GetAttribute("type"), r.GetAttribute("media"))) {
                        depth = r.Depth;
                    }
                } else if (r.NodeType == XmlNodeType.Text || r.NodeType == XmlNodeType.CDATA) {
                    text.Append(r.Value);
                } else if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth) {
                    sheet.Add(text.ToString(), Drop);
                    text.Clear();
                    depth = -1;
                }
            }
            return sheet;
        }

        // A style element applies when it's CSS, which is what no type means, and it isn't for
        // some other medium. This draws to a screen.
        private static bool Applies(string? type, string? media) {
            return IsCss(type) && IsScreen(media);
        }

        private static bool IsCss(string? type) {
            return type == null || type.Trim().Length == 0
                || string.Equals(type.Trim(), "text/css", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsScreen(string? media) {
            return media == null || media.Trim().Length == 0
                || string.Equals(media.Trim(), "all", StringComparison.OrdinalIgnoreCase)
                || string.Equals(media.Trim(), "screen", StringComparison.OrdinalIgnoreCase);
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
            internal bool Defs;
        }

        // One element, held until the whole document is read so a use can point at an element
        // anywhere in the file, before or after it.
        private sealed class Node {
            internal Node(string name, SvgAttrs attrs) {
                Name = name;
                Attrs = attrs;
            }
            internal readonly string Name;
            internal readonly SvgAttrs Attrs;
            internal List<Node>? Children;
        }

        // How deep elements may nest, uses included, before the rest is dropped. The walk is
        // recursive, so this is what keeps a hostile file from running out of stack.
        private const int MaxDepth = 256;
        // How many elements uses may stamp out in all. A use can point at a group full of uses,
        // so without a cap a small file could ask for billions of copies.
        private const int MaxUsed = 100_000;

        // Gradients are gathered here, wherever they are in the tree, since a paint can name one
        // from anywhere. An id goes to the first element that has it.
        private static Node? Read(XmlReader r, SvgSheet? sheet, Dictionary<string, Node> ids, SvgGradients gradients) {
            Node? root = null;
            var stack = new List<(Node Node, SvgGradientDef? Grad)>();
            while (r.Read()) {
                if (r.NodeType == XmlNodeType.EndElement) {
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    continue;
                }
                if (r.NodeType != XmlNodeType.Element) continue;

                bool empty = r.IsEmptyElement;
                string name = r.LocalName;
                var node = new Node(name, new SvgAttrs(r, sheet));
                SvgGradientDef? grad = stack.Count > 0 ? stack[stack.Count - 1].Grad : null;
                if (stack.Count > 0) {
                    Node parent = stack[stack.Count - 1].Node;
                    (parent.Children ??= new List<Node>()).Add(node);
                } else {
                    root = node;
                }

                string? id = node.Attrs.Raw("id");
                if (id != null && id.Length > 0) ids.TryAdd(id, node);

                if (name == "linearGradient" || name == "radialGradient") {
                    var def = new SvgGradientDef(id ?? string.Empty, name[0] == 'r');
                    Copy(r, def);
                    def.Href = Reference(node.Attrs);
                    gradients.Add(def);
                    grad = def;
                } else if (name == "stop" && grad != null) {
                    Stop(node.Attrs, grad);
                }

                if (!empty) stack.Add((node, grad));
            }
            return root;
        }

        private void Parse(XmlReader r, SvgSheet? sheet, float tolerance) {
            var pending = new List<Pending>();
            var gradients = new SvgGradients();
            var ids = new Dictionary<string, Node>(StringComparer.Ordinal);
            var stamping = new HashSet<Node>();
            int used = 0;

            bool sawRoot = false;
            bool hasBox = false;
            float vbX = 0f, vbY = 0f, vbW = 0f, vbH = 0f;
            float attrW = 0f, attrH = 0f;

            Node? top = Read(r, sheet, ids, gradients);
            if (top != null) {
                Walk(top, new Frame { Style = SvgStyle.Root(), M = SvgMatrix.Identity }, 0, null);
            }

            // use is the element that stamped this one out, when one did, which is what sizes a
            // symbol or an svg it points at.
            void Walk(Node n, Frame parent, int depth, SvgAttrs? use) {
                if (depth > MaxDepth) {
                    Drop("deep nesting");
                    return;
                }
                SvgAttrs attrs = n.Attrs;
                if (depth > 0 && !Conditions(attrs)) return;

                Frame frame = parent;
                switch (n.Name) {
                    case "svg":
                        frame.Style = frame.Style.With(attrs, ref _skipped);
                        frame.M = SvgMatrix.Mul(frame.M, SvgMatrix.Parse(attrs.Raw("transform"), ref _skipped));
                        if (!sawRoot) {
                            sawRoot = true;
                            hasBox = ViewBox(attrs.Raw("viewBox"), ref vbX, ref vbY, ref vbW, ref vbH);
                            SvgColor.TryLength(attrs.Raw("width"), 0f, out attrW);
                            SvgColor.TryLength(attrs.Raw("height"), 0f, out attrH);
                        } else {
                            // A nested svg is its own viewport with its own scaling.
                            frame.M = SvgMatrix.Mul(frame.M, Viewport(attrs, use));
                        }
                        break;
                    case "symbol":
                        // A symbol only draws where a use stamps it out.
                        if (use == null) return;
                        frame.Style = frame.Style.With(attrs, ref _skipped);
                        frame.M = SvgMatrix.Mul(frame.M, Viewport(attrs, use));
                        break;
                    case "g":
                    case "a":
                        frame.Style = frame.Style.With(attrs, ref _skipped);
                        frame.M = SvgMatrix.Mul(frame.M, SvgMatrix.Parse(attrs.Raw("transform"), ref _skipped));
                        break;
                    case "switch": {
                        // Only the first child whose conditions hold draws.
                        frame.Style = frame.Style.With(attrs, ref _skipped);
                        frame.M = SvgMatrix.Mul(frame.M, SvgMatrix.Parse(attrs.Raw("transform"), ref _skipped));
                        if (n.Children == null) return;
                        foreach (Node c in n.Children) {
                            if (c.Name is "title" or "desc" or "metadata" || !Conditions(c.Attrs)) continue;
                            Walk(c, frame, depth + 1, null);
                            break;
                        }
                        return;
                    }
                    case "defs":
                        frame.Defs = true;
                        frame.Style = frame.Style.With(attrs, ref _skipped);
                        frame.M = SvgMatrix.Mul(frame.M, SvgMatrix.Parse(attrs.Raw("transform"), ref _skipped));
                        break;
                    case "use": {
                        if (frame.Defs) return;
                        string? id = Reference(attrs);
                        if (id == null || !ids.TryGetValue(id, out Node? target)) {
                            Drop("use");
                            return;
                        }
                        // A use that points at one of its own ancestors would stamp itself out
                        // forever.
                        if (stamping.Contains(target) || used >= MaxUsed) {
                            Drop("use");
                            return;
                        }
                        used += Count(target, MaxUsed - used, depth);
                        frame.Style = frame.Style.With(attrs, ref _skipped);
                        frame.M = SvgMatrix.Mul(frame.M, SvgMatrix.Parse(attrs.Raw("transform"), ref _skipped));
                        frame.M = SvgMatrix.Mul(frame.M, Translate(Len(attrs, "x", vbW), Len(attrs, "y", vbH)));
                        stamping.Add(target);
                        Walk(target, frame, depth + 1, attrs);
                        stamping.Remove(target);
                        return;
                    }
                    case "linearGradient":
                    case "radialGradient":
                    case "stop":
                    case "title":
                    case "desc":
                    case "metadata":
                        return;
                    case "path":
                    case "rect":
                    case "circle":
                    case "ellipse":
                    case "line":
                    case "polyline":
                    case "polygon": {
                        SvgStyle style = frame.Style.With(attrs, ref _skipped);
                        SvgMatrix m = SvgMatrix.Mul(frame.M, SvgMatrix.Parse(attrs.Raw("transform"), ref _skipped));
                        if (!frame.Defs) pending.Add(new Pending(n.Name, attrs, style, m));
                        return;
                    }
                    case "style":
                        // Read ahead of this pass. A style that isn't used is dropped here.
                        if (!Applies(attrs.Raw("type"), attrs.Raw("media"))) Drop("style");
                        return;
                    default:
                        Drop(n.Name);
                        return;
                }

                if (n.Children == null) return;
                foreach (Node c in n.Children) Walk(c, frame, depth + 1, null);
            }

            // A nested svg or a symbol maps its viewBox onto the box it's given: its own x, y,
            // width and height, with the use's width and height winning over its own. Percentages
            // are of the root viewBox.
            SvgMatrix Viewport(SvgAttrs own, SvgAttrs? use) {
                float x = Len(own, "x", vbW);
                float y = Len(own, "y", vbH);
                float w = vbW;
                float h = vbH;
                if (use != null && SvgColor.TryLength(use.Raw("width"), vbW, out float uw)) w = uw;
                else if (SvgColor.TryLength(own.Raw("width"), vbW, out float ow)) w = ow;
                if (use != null && SvgColor.TryLength(use.Raw("height"), vbH, out float uh)) h = uh;
                else if (SvgColor.TryLength(own.Raw("height"), vbH, out float oh)) h = oh;

                SvgMatrix m = Translate(x, y);
                float bx = 0f, by = 0f, bw = 0f, bh = 0f;
                if (!ViewBox(own.Raw("viewBox"), ref bx, ref by, ref bw, ref bh) || !(w > 0f) || !(h > 0f)) {
                    return m;
                }
                return SvgMatrix.Mul(m, Fit(own.Raw("preserveAspectRatio"), bx, by, bw, bh, w, h));
            }

            // How many elements stamping n out adds, counted no further than limit or than the walk
            // would go.
            int Count(Node n, int limit, int depth) {
                int total = 1;
                if (n.Children == null || depth >= MaxDepth) return total;
                foreach (Node c in n.Children) {
                    if (total >= limit) break;
                    total += Count(c, limit - total, depth + 1);
                }
                return total;
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

        // Whether an element's conditional attributes let it draw. No extension is supported, and
        // a language matches the one the app runs in, or the language that one is a dialect of.
        private static bool Conditions(SvgAttrs a) {
            if (a.Has("requiredExtensions")) return false;
            string? languages = a.Raw("systemLanguage");
            if (languages == null) return true;
            CultureInfo ui = CultureInfo.CurrentUICulture;
            string name = ui.Name.Length > 0 ? ui.Name : "en";
            string two = ui.Name.Length > 0 ? ui.TwoLetterISOLanguageName : "en";
            foreach (string part in languages.Split(',')) {
                string tag = part.Trim();
                if (tag.Length == 0) continue;
                if (string.Equals(tag, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tag, two, StringComparison.OrdinalIgnoreCase)
                    || tag.StartsWith(two + "-", StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }

        private static SvgMatrix Translate(float x, float y) => new(1f, 0f, 0f, 1f, x, y);

        // A viewBox fitted into a w by h box the way preserveAspectRatio says, which by default
        // is scaled to fit whole and centered.
        private static SvgMatrix Fit(string? aspect, float bx, float by, float bw, float bh, float w, float h) {
            float sx = w / bw;
            float sy = h / bh;
            string align = "xMidYMid";
            bool slice = false;
            if (aspect != null) {
                string[] parts = aspect.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                int i = 0;
                if (i < parts.Length && parts[i] == "defer") i++;
                if (i < parts.Length) align = parts[i++];
                if (i < parts.Length) slice = parts[i] == "slice";
            }
            if (align == "none") return new SvgMatrix(sx, 0f, 0f, sy, -bx * sx, -by * sy);

            float s = slice ? MathF.Max(sx, sy) : MathF.Min(sx, sy);
            float ax = align.StartsWith("xMin", StringComparison.Ordinal) ? 0f
                : align.StartsWith("xMax", StringComparison.Ordinal) ? 1f : 0.5f;
            float ay = align.EndsWith("YMin", StringComparison.Ordinal) ? 0f
                : align.EndsWith("YMax", StringComparison.Ordinal) ? 1f : 0.5f;
            float tx = (w - bw * s) * ax - bx * s;
            float ty = (h - bh * s) * ay - by * s;
            return new SvgMatrix(s, 0f, 0f, s, tx, ty);
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

            // currentColor is the color property of the element that draws, the way CSS
            // inherits it, rather than of the one that set the paint.
            if (style.Fill.Current) style.Fill = new SvgPaint(SvgPaintKind.Flat, style.Current, null, true);
            if (style.Stroke.Current) style.Stroke = new SvgPaint(SvgPaintKind.Flat, style.Current, null, true);

            if (style.Fill.Kind != SvgPaintKind.None && fillAlpha > 0f) {
                if (Paint(style.Fill, gradients, o, p.M, ToEm, viewW, viewH, fillAlpha, out Gradient g)) {
                    shape.Fill = Bake(o, shape.Origin, u, vbMin, index, out float fillScale, out BakedGlyph[]? fillTiles);
                    if (shape.Fill != null) {
                        shape.FillScale = fillScale;
                        shape.FillTiles = fillTiles;
                        shape.HasFill = true;
                        shape.FillPaint = g;
                        shape.EvenOdd = style.EvenOdd;
                        shape.FillCurrent = style.Fill.Current && !style.CurrentSet;
                        shape.FillAlpha = fillAlpha;
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
                        shape.StrokeCurrent = style.Stroke.Current && !style.CurrentSet;
                        shape.StrokeAlpha = strokeAlpha;
                        shape.Stroke = lines.ToArray();
                        shape.StrokeClosed = closed.ToArray();
                        shape.StrokeRadius = radius;
                        shape.Cap = style.Cap;
                        shape.Join = style.Join;
                        shape.MiterClip = style.MiterClip;
                        shape.MiterLimit = style.MiterLimit;
                        Dash(shape, style, pen * inv);
                        TrimDashedEnds(shape);
                    }
                }
            }

            if (!shape.HasFill && !shape.HasStroke) return null;

            Vector2 a = ToEm(docMin);
            Vector2 b = ToEm(docMax);
            Vector2 lo = Vector2.Min(a, b);
            Vector2 hi = Vector2.Max(a, b);
            if (shape.Fill != null) {
                lo = Vector2.Min(lo, shape.Origin + shape.Fill.Min * shape.FillScale);
                hi = Vector2.Max(hi, shape.Origin + shape.Fill.Max * shape.FillScale);
            }
            float pad = shape.HasStroke ? shape.StrokeRadius : 0f;
            shape.Min = lo - new Vector2(pad);
            shape.Max = hi + new Vector2(pad);
            return shape;
        }

        // The dash pattern as a period and the dashes in it. An odd list repeats to an even one,
        // which is what the spec says. Square caps are butt dashes grown by the stroke's radius
        // on both ends, and round caps reach that far too, so dashes whose caps would meet are
        // merged into one: the union of the two is the longer dash, and drawing them as two
        // would blend the overlap twice on a translucent stroke.
        private void Dash(SvgShape shape, in SvgStyle style, float toEm) {
            float[]? list = style.Dash;
            if (list == null) return;
            int n = list.Length % 2 == 0 ? list.Length : list.Length * 2;
            float period = 0f;
            for (int i = 0; i < n; i++) period += list[i % list.Length];
            if (!(period > 0f) || !float.IsFinite(period)) return;

            float r = style.Cap == PathCap.Butt ? 0f : shape.StrokeRadius / toEm;
            var spans = new List<(float Start, float End)>();
            float at = -style.DashOffset;
            for (int i = 0; i < n; i += 2) {
                float length = list[i % list.Length];
                // A butt dash of no length has nothing to draw.
                if (length > 0f || r > 0f) {
                    float start = at - r;
                    start -= MathF.Floor(start / period) * period;
                    spans.Add((start, start + length + 2f * r));
                }
                at += length + list[(i + 1) % list.Length];
            }
            if (spans.Count == 0) {
                shape.HasStroke = false;
                return;
            }
            spans.Sort((a, b) => a.Start.CompareTo(b.Start));

            var merged = new List<(float Start, float End)>();
            foreach ((float Start, float End) s in spans) {
                if (merged.Count > 0 && s.Start <= merged[merged.Count - 1].End) {
                    (float Start, float End) last = merged[merged.Count - 1];
                    merged[merged.Count - 1] = (last.Start, MathF.Max(last.End, s.End));
                } else {
                    merged.Add(s);
                }
            }
            // The last one can wrap around into the first period's start.
            while (merged.Count > 1 && merged[merged.Count - 1].End - period >= merged[0].Start) {
                (float Start, float End) last = merged[merged.Count - 1];
                merged.RemoveAt(merged.Count - 1);
                merged[0] = (last.Start - period, MathF.Max(merged[0].End, last.End - period));
            }
            foreach ((float Start, float End) s in merged) {
                // Dashes that cover the whole period with no space left are a solid stroke.
                if (s.End - s.Start >= period) return;
            }

            shape.Dashed = true;
            shape.DashPeriod = period * toEm;
            shape.DashStarts = new float[merged.Count];
            shape.DashLengths = new float[merged.Count];
            // A round dash's length doesn't count its caps, while a square one is drawn as a
            // butt dash whose ends are where the caps end.
            float keep = style.Cap == PathCap.Round ? r : 0f;
            for (int i = 0; i < merged.Count; i++) {
                shape.DashStarts[i] = (merged[i].Start + keep) * toEm;
                shape.DashLengths[i] = (merged[i].End - merged[i].Start - 2f * keep) * toEm;
            }
            shape.DashCap = style.Cap == PathCap.Round ? DashCap.Round : DashCap.Butt;
        }

        // A cap reaches a stroke's radius past its dash, and the path renderer draws the pattern
        // a cap's length past each open end. So a dash that the spec says starts after the end,
        // or stops before the start, would still show the part of its cap that reaches back in,
        // and one that starts right where an end's cap stops leaves a sliver of anti aliasing.
        // The subpath is cut short so its cap ends halfway across the space before such a dash,
        // and DashShift keeps the pattern where it was when the start moves.
        private static void TrimDashedEnds(SvgShape shape) {
            float r = shape.StrokeRadius;
            if (!shape.Dashed || shape.DashCap == DashCap.Butt && shape.Cap == PathCap.Butt || !(r > 0f)) return;
            float period = shape.DashPeriod;
            // Each dash with its caps on is a span along the contour, repeating every period.
            float grow = shape.DashCap == DashCap.Round ? r : 0f;
            int n = shape.DashStarts.Length;
            float Start(int k) => shape.DashStarts[k] - grow;
            float End(int k) => shape.DashStarts[k] + shape.DashLengths[k] + grow;
            // The first repeat of at past x, and the last one short of it.
            float Above(float x, float at) => at + MathF.Floor((x - at) / period + 1f) * period;
            float Below(float x, float at) => at + MathF.Ceiling((x - at) / period - 1f) * period;

            for (int i = 0; i < shape.Stroke.Length; i++) {
                if (shape.StrokeClosed[i]) continue;
                Vector2[] line = shape.Stroke[i];
                float length = 0f;
                for (int j = 1; j < line.Length; j++) length += Vector2.Distance(line[j - 1], line[j]);

                // The first dash that starts past the end, and whether its cap reaches back in or
                // stops within a radius of the end's.
                float to = length;
                float next = float.MaxValue;
                for (int k = 0; k < n; k++) next = MathF.Min(next, Above(length - r, Start(k)));
                if (next < length + 2f * r) {
                    float before = float.MinValue;
                    for (int k = 0; k < n; k++) before = MathF.Max(before, Below(next, End(k)));
                    to = MathF.Min((before + next) * 0.5f, length + r) - r;
                }

                float from = 0f;
                float last = float.MinValue;
                for (int k = 0; k < n; k++) last = MathF.Max(last, Below(r, End(k)));
                if (last > -2f * r) {
                    float after = float.MaxValue;
                    for (int k = 0; k < n; k++) after = MathF.Min(after, Above(last, Start(k)));
                    from = MathF.Max((last + after) * 0.5f, -r) + r;
                }

                if (from <= 0f && to >= length) continue;
                if (shape.DashShift.Length == 0) shape.DashShift = new float[shape.Stroke.Length];
                shape.Stroke[i] = to - from > 1e-6f ? Cut(line, from, to) : Array.Empty<Vector2>();
                shape.DashShift[i] = from;
            }
        }

        // The stretch of a polyline between two distances along it.
        private static Vector2[] Cut(Vector2[] line, float from, float to) {
            var points = new List<Vector2>();
            float at = 0f;
            for (int j = 1; j < line.Length; j++) {
                Vector2 a = line[j - 1];
                Vector2 b = line[j];
                float d = Vector2.Distance(a, b);
                float next = at + d;
                if (next > from && at < to && d > 0f) {
                    if (points.Count == 0) points.Add(Vector2.Lerp(a, b, MathF.Max(from - at, 0f) / d));
                    if (next < to) points.Add(b);
                    else points.Add(Vector2.Lerp(a, b, (to - at) / d));
                }
                at = next;
            }
            return points.ToArray();
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
        private BakedGlyph? Bake(SvgOutline o, Vector2 origin, float u, Vector2 vbMin, int index,
                                 out float fillScale, out BakedGlyph[]? tiles) {
            fillScale = 1f;
            tiles = null;
            float k = Grid / u;
            float ox = (vbMin.X * (1f / u) + origin.X) * Grid;
            float oy = (vbMin.Y * (1f / u) - origin.Y) * Grid;
            Vector2 Design(Vector2 q) => new(q.X * k - ox, oy - q.Y * k);

            var contours = new List<List<GlyphCurve>>();
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
                var contour = new List<GlyphCurve>(count + 1);
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
                    contour.Add(new GlyphCurve { P1 = p1, P2 = p2, P3 = p3 });
                }
                // Every subpath is closed for filling, whether or not a Z said so.
                if (last != first) {
                    contour.Add(new GlyphCurve { P1 = last, P2 = (last + first) * 0.5f, P3 = first });
                }
                contours.Add(contour);
            }
            if (contours.Count == 0) return null;

            // An element reaching past the limit is baked shrunk about its center, which is the
            // origin, and drawn enlarged by the same factor, so a wide drawing loses nothing.
            float reach = Grid * EmReach;
            float farthest = MathF.Max(MathF.Max(-lox, hix), MathF.Max(-lo, hi));
            if (!float.IsFinite(farthest)) {
                Drop("oversized element");
                return null;
            }
            if (farthest > reach) {
                float shrink = reach / farthest;
                foreach (List<GlyphCurve> contour in contours) {
                    for (int i = 0; i < contour.Count; i++) {
                        GlyphCurve c = contour[i];
                        c.P1 *= shrink;
                        c.P2 *= shrink;
                        c.P3 *= shrink;
                        contour[i] = c;
                    }
                }
                lox *= shrink;
                hix *= shrink;
                lo *= shrink;
                hi *= shrink;
                fillScale = 1f / shrink;
            }

            int x1 = (int)MathF.Floor(lox);
            int y1 = (int)MathF.Floor(lo);
            int x2 = (int)MathF.Ceiling(hix);
            int y2 = (int)MathF.Ceiling(hi);

            BakedGlyph whole = GlyphBake.Bake(Flatten(contours), index, 0, 0, x1, y1, x2, y2, Grid, GlyphBake.MaxCurves);
            if (whole.Clamped > 0) {
                tiles = Tile(contours, index, x1, y1, x2, y2, whole.Clamped);
            }
            return whole;
        }

        private static List<GlyphCurve> Flatten(List<List<GlyphCurve>> contours) {
            var all = new List<GlyphCurve>();
            foreach (List<GlyphCurve> contour in contours) all.AddRange(contour);
            return all;
        }

        // A band holds a fixed number of curves and drops the rest, which leaves streaks across
        // an element with more curves than that along one row or column, such as a line of text
        // outlined into a single path. Cutting the outline into a grid of tiles gives each its
        // own bands. The smallest grid in which none overflow is used, or failing that the one
        // with the fewest overflowing bands, as long as that beats the whole element. Null when
        // tiling doesn't help, in which case the whole thing draws as before.
        private static BakedGlyph[]? Tile(List<List<GlyphCurve>> contours, int index, int x1, int y1, int x2, int y2, int wholeClamped) {
            // How far past its tile each clipped outline reaches, so the anti aliasing at a seam
            // sees the real edge rather than the chord that cut it.
            float margin = Grid * TileMargin;
            float width = Math.Max(1, x2 - x1);
            float height = Math.Max(1, y2 - y1);
            int[] steps = { 2, 3, 4, 6, 8, 12, 16 };
            BakedGlyph[]? best = null;
            int bestClamped = wholeClamped;
            foreach (int n in steps) {
                int nx, ny;
                if (width >= height) {
                    nx = n;
                    ny = Math.Max(1, (int)MathF.Round(n * height / width));
                } else {
                    ny = n;
                    nx = Math.Max(1, (int)MathF.Round(n * width / height));
                }
                var baked = new List<BakedGlyph>();
                int clamped = 0;
                for (int iy = 0; iy < ny; iy++) {
                    int ty1 = y1 + (int)((long)(y2 - y1) * iy / ny);
                    int ty2 = y1 + (int)((long)(y2 - y1) * (iy + 1) / ny);
                    for (int ix = 0; ix < nx; ix++) {
                        int tx1 = x1 + (int)((long)(x2 - x1) * ix / nx);
                        int tx2 = x1 + (int)((long)(x2 - x1) * (ix + 1) / nx);
                        List<GlyphCurve> clipped = SvgTileClip.Clip(contours,
                            ix == 0 ? float.NegativeInfinity : tx1 - margin,
                            ix == nx - 1 ? float.PositiveInfinity : tx2 + margin,
                            iy == 0 ? float.NegativeInfinity : ty1 - margin,
                            iy == ny - 1 ? float.PositiveInfinity : ty2 + margin);
                        if (clipped.Count == 0) continue;
                        BakedGlyph tile = GlyphBake.Bake(clipped, index, 0, 0, tx1, ty1, tx2, ty2, Grid, GlyphBake.MaxCurves);
                        clamped += tile.Clamped;
                        tile.SeamMinX = ix > 0;
                        tile.SeamMaxX = ix < nx - 1;
                        tile.SeamMinY = iy > 0;
                        tile.SeamMaxY = iy < ny - 1;
                        baked.Add(tile);
                    }
                }
                if (baked.Count == 0 || clamped >= bestClamped) continue;
                best = baked.ToArray();
                bestClamped = clamped;
                if (clamped == 0) break;
            }
            return best;
        }
    }
}
