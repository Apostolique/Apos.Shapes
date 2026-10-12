// The rules out of a document's style elements, and which of them an element picks up.

using System;
using System.Collections.Generic;

namespace Apos.Shapes {
    // A stylesheet cut down to what drawing exports use: rules whose selectors are a tag, a class,
    // an id, or those written together like path.a#b. Combinators, attribute selectors,
    // pseudo-classes and at-rules are counted and left out, so a file never loads worse than it
    // did without the sheet.
    //
    // Everything resolves while the file loads, into the same style an inline declaration would
    // have produced, so a drawing styled from a sheet draws exactly like one styled inline.
    internal sealed class SvgSheet {
        private readonly List<Rule> _rules = new();
        // What was left out, the same statistic ShapeSvg keeps for elements.
        internal int Skipped;
        internal readonly List<string> SkippedNames = new();

        internal bool IsEmpty => _rules.Count == 0;

        private sealed class Rule {
            internal string? Tag;
            internal string? Id;
            internal string[] Classes = Array.Empty<string>();
            internal int Specificity;
            internal int Order;
            internal Dictionary<string, string> Declarations = null!;
        }

        internal void Add(string css) {
            css = Uncomment(css);
            int at = 0;
            while (at < css.Length) {
                char c = css[at];
                if (char.IsWhiteSpace(c)) {
                    at++;
                    continue;
                }
                if (c == '@') {
                    // An at-rule ends at its semicolon, or with its block when it has one.
                    int semi = css.IndexOf(';', at);
                    int open = css.IndexOf('{', at);
                    // Some exporters start with @charset, which says nothing about the drawing.
                    bool charset = string.CompareOrdinal(css, at, "@charset", 0, 8) == 0;
                    if (open >= 0 && (semi < 0 || open < semi)) {
                        at = BlockEnd(css, open) + 1;
                    } else {
                        at = semi < 0 ? css.Length : semi + 1;
                    }
                    if (!charset) Skip("@rule");
                    continue;
                }
                int brace = css.IndexOf('{', at);
                if (brace < 0) break;
                int end = BlockEnd(css, brace);
                string prelude = css.Substring(at, brace - at);
                var decls = new Dictionary<string, string>(StringComparer.Ordinal);
                Declarations(css.Substring(brace + 1, Math.Max(0, end - brace - 1)), decls);
                if (decls.Count > 0) {
                    foreach (string selector in prelude.Split(',')) {
                        Rule? rule = Selector(selector.Trim());
                        if (rule == null) {
                            Skip("css selector");
                            continue;
                        }
                        rule.Order = _rules.Count;
                        rule.Declarations = decls;
                        _rules.Add(rule);
                    }
                }
                at = end + 1;
            }
        }

        // What the sheet says for one element, with the more specific rule winning and the later
        // one winning a tie. Null when nothing matches.
        internal Dictionary<string, string>? Match(string tag, string? id, string? classes) {
            if (_rules.Count == 0) return null;
            string[] own = classes == null
                ? Array.Empty<string>()
                : classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            List<Rule>? hits = null;
            foreach (Rule rule in _rules) {
                if (!Matches(rule, tag, id, own)) continue;
                hits ??= new List<Rule>();
                hits.Add(rule);
            }
            if (hits == null) return null;
            hits.Sort((a, b) => a.Specificity != b.Specificity
                ? a.Specificity.CompareTo(b.Specificity)
                : a.Order.CompareTo(b.Order));
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Rule rule in hits) {
                foreach (KeyValuePair<string, string> d in rule.Declarations) result[d.Key] = d.Value;
            }
            return result;
        }

        // A declaration list, the body of a rule or a style attribute. !important is dropped: it
        // only matters when two sources disagree, and the order they're layered in already
        // settles that for anything a drawing tool writes.
        internal static void Declarations(string text, Dictionary<string, string> into) {
            int at = 0;
            while (at < text.Length) {
                int end = text.IndexOf(';', at);
                if (end < 0) end = text.Length;
                int colon = text.IndexOf(':', at);
                if (colon > at && colon < end) {
                    string name = text.Substring(at, colon - at).Trim();
                    string value = text.Substring(colon + 1, end - colon - 1).Trim();
                    int bang = value.IndexOf('!');
                    if (bang >= 0) value = value.Substring(0, bang).TrimEnd();
                    if (name.Length > 0) into[name] = value;
                }
                at = end + 1;
            }
        }

        private static bool Matches(Rule rule, string tag, string? id, string[] own) {
            if (rule.Tag != null && !string.Equals(rule.Tag, tag, StringComparison.Ordinal)) return false;
            if (rule.Id != null && !string.Equals(rule.Id, id, StringComparison.Ordinal)) return false;
            foreach (string c in rule.Classes) {
                if (Array.IndexOf(own, c) < 0) return false;
            }
            return true;
        }

        // One compound selector, or null for anything past a tag, classes and an id.
        private static Rule? Selector(string s) {
            if (s.Length == 0) return null;
            var rule = new Rule();
            var classes = new List<string>();
            int at = 0;
            if (s[0] == '*') {
                at = 1;
            } else if (IsName(s[0])) {
                rule.Tag = Name(s, ref at);
            }
            while (at < s.Length) {
                char c = s[at++];
                string name = Name(s, ref at);
                if (name.Length == 0) return null;
                if (c == '.') {
                    classes.Add(name);
                } else if (c == '#' && rule.Id == null) {
                    rule.Id = name;
                } else {
                    return null;
                }
            }
            rule.Classes = classes.ToArray();
            rule.Specificity = (rule.Id != null ? 10000 : 0) + classes.Count * 100 + (rule.Tag != null ? 1 : 0);
            return rule;
        }

        private static string Name(string s, ref int at) {
            int start = at;
            while (at < s.Length && IsName(s[at])) at++;
            return s.Substring(start, at - start);
        }

        private static bool IsName(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c > 0x7f;

        private static int BlockEnd(string css, int open) {
            int depth = 0;
            for (int i = open; i < css.Length; i++) {
                if (css[i] == '{') depth++;
                else if (css[i] == '}' && --depth == 0) return i;
            }
            return css.Length;
        }

        private static string Uncomment(string css) {
            int open = css.IndexOf("/*", StringComparison.Ordinal);
            if (open < 0) return css;
            var sb = new System.Text.StringBuilder(css.Length);
            int at = 0;
            while (open >= 0) {
                sb.Append(css, at, open - at);
                int close = css.IndexOf("*/", open + 2, StringComparison.Ordinal);
                at = close < 0 ? css.Length : close + 2;
                open = at < css.Length ? css.IndexOf("/*", at, StringComparison.Ordinal) : -1;
            }
            if (at < css.Length) sb.Append(css, at, css.Length - at);
            return sb.ToString();
        }

        private void Skip(string what) {
            Skipped++;
            if (!SkippedNames.Contains(what)) SkippedNames.Add(what);
        }
    }
}
