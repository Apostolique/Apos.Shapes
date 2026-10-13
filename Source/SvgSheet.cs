// The rules out of a document's style elements, and which of them an element picks up.

using System;
using System.Collections.Generic;

namespace Apos.Shapes {
    // A stylesheet cut down to what drawing exports use: rules whose selectors are a tag, a class,
    // an id, or those written together like path.a#b. Combinators, attribute selectors,
    // pseudo-classes and at-rules are counted and not matched exactly.
    //
    // A selector that can't be matched exactly still stands in as its last compound, with the
    // specificity of the whole selector. Where that compound matches an element, the properties
    // it sets stop coming from any rule it outranks and fall back to the element's attributes.
    // Without that, a skipped rule would let a less specific one win in its place, so a file
    // never loads worse than it did without the sheet.
    //
    // Everything resolves while the file loads, into the same style an inline declaration would
    // have produced, so a drawing styled from a sheet draws exactly like one styled inline.
    internal sealed class SvgSheet {
        // Each rule sits in one bucket, picked by the part of it that's cheapest to look up, so an
        // element only tests the rules that share its id, one of its classes, or its tag.
        private readonly Dictionary<string, List<Rule>> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Rule>> _byClass = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Rule>> _byTag = new(StringComparer.Ordinal);
        private readonly List<Rule> _any = new();
        private readonly List<Rule> _hits = new();
        private int _count;

        internal bool IsEmpty => _count == 0;

        private sealed class Rule {
            internal string? Tag;
            internal string? Id;
            internal string[] Classes = Array.Empty<string>();
            internal int Specificity;
            internal int Order;
            // Not matched exactly: only the last compound is checked.
            internal bool Inexact;
            internal Dictionary<string, string> Declarations = null!;
        }

        private static readonly Comparison<Rule> ByPrecedence = (a, b) =>
            a.Specificity != b.Specificity ? a.Specificity.CompareTo(b.Specificity) : a.Order.CompareTo(b.Order);

        // What can't be used is reported through drop, one call per occurrence.
        internal void Add(string css, Action<string> drop) {
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
                    if (!charset) drop("@rule");
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
                        if (rule == null || rule.Inexact) drop("css selector");
                        if (rule == null) continue;
                        rule.Order = _count++;
                        rule.Declarations = decls;
                        Bucket(rule).Add(rule);
                    }
                }
                at = end + 1;
            }
        }

        private List<Rule> Bucket(Rule rule) {
            if (rule.Id != null) return Slot(_byId, rule.Id);
            if (rule.Classes.Length > 0) return Slot(_byClass, rule.Classes[0]);
            if (rule.Tag != null) return Slot(_byTag, rule.Tag);
            return _any;
        }

        private static List<Rule> Slot(Dictionary<string, List<Rule>> map, string key) {
            if (!map.TryGetValue(key, out List<Rule>? list)) map[key] = list = new List<Rule>();
            return list;
        }

        // What the sheet says for one element, with the more specific rule winning and the later
        // one winning a tie. Null when nothing matches.
        internal Dictionary<string, string>? Match(string tag, string? id, string? classes) {
            if (_count == 0) return null;
            _hits.Clear();
            List<Rule>? list;
            if (id != null && _byId.TryGetValue(id, out list)) Collect(list, tag, id, classes);
            if (classes != null && _byClass.Count > 0) {
                int at = 0;
                while (at < classes.Length) {
                    while (at < classes.Length && char.IsWhiteSpace(classes[at])) at++;
                    int start = at;
                    while (at < classes.Length && !char.IsWhiteSpace(classes[at])) at++;
                    if (at > start && _byClass.TryGetValue(classes.Substring(start, at - start), out list)) {
                        Collect(list, tag, id, classes);
                    }
                }
            }
            if (_byTag.TryGetValue(tag, out list)) Collect(list, tag, id, classes);
            Collect(_any, tag, id, classes);
            if (_hits.Count == 0) return null;

            _hits.Sort(ByPrecedence);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Rule rule in _hits) {
                foreach (KeyValuePair<string, string> d in rule.Declarations) {
                    if (rule.Inexact) result.Remove(d.Key);
                    else result[d.Key] = d.Value;
                }
            }
            return result.Count == 0 ? null : result;
        }

        private void Collect(List<Rule> rules, string tag, string? id, string? classes) {
            foreach (Rule rule in rules) {
                if (Matches(rule, tag, id, classes)) _hits.Add(rule);
            }
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
                    int bang = value.LastIndexOf('!');
                    if (bang >= 0 && value.AsSpan(bang + 1).Trim().Equals("important", StringComparison.OrdinalIgnoreCase)) {
                        value = value.Substring(0, bang).TrimEnd();
                    }
                    if (name.Length > 0) into[name] = value;
                }
                at = end + 1;
            }
        }

        private static bool Matches(Rule rule, string tag, string? id, string? classes) {
            if (rule.Tag != null && !string.Equals(rule.Tag, tag, StringComparison.Ordinal)) return false;
            if (rule.Id != null && !string.Equals(rule.Id, id, StringComparison.Ordinal)) return false;
            foreach (string c in rule.Classes) {
                if (!HasClass(classes, c)) return false;
            }
            return true;
        }

        private static bool HasClass(string? classes, string name) {
            if (classes == null) return false;
            int at = 0;
            while ((at = classes.IndexOf(name, at, StringComparison.Ordinal)) >= 0) {
                int end = at + name.Length;
                if ((at == 0 || char.IsWhiteSpace(classes[at - 1]))
                    && (end == classes.Length || char.IsWhiteSpace(classes[end]))) {
                    return true;
                }
                at = end;
            }
            return false;
        }

        // One selector. The tag, classes and id are those of its last compound, which is all that
        // gets matched, and the specificity counts every compound. Null when there's nothing to
        // match on, such as an empty selector or one that ends in a combinator.
        private static Rule? Selector(string s) {
            if (s.Length == 0) return null;
            string? tag = null;
            string? id = null;
            var classes = new List<string>();
            bool inexact = false;
            bool started = false;
            int specificity = 0;
            int at = 0;
            while (at < s.Length) {
                char c = s[at];
                if (char.IsWhiteSpace(c) || c == '>' || c == '+' || c == '~') {
                    while (at < s.Length && (char.IsWhiteSpace(s[at]) || s[at] == '>' || s[at] == '+' || s[at] == '~')) at++;
                    if (!started || at >= s.Length) return null;
                    // The next compound is the one that matters.
                    inexact = true;
                    started = false;
                    tag = null;
                    id = null;
                    classes.Clear();
                    continue;
                }
                started = true;
                if (c == '*') {
                    at++;
                } else if (c == '.' || c == '#') {
                    at++;
                    string name = Name(s, ref at);
                    if (name.Length == 0) return null;
                    if (c == '.') {
                        classes.Add(name);
                        specificity += 100;
                    } else {
                        // A second id in one compound never matches an element that has one id.
                        if (id != null) inexact = true;
                        else id = name;
                        specificity += 10000;
                    }
                } else if (c == '[') {
                    int close = s.IndexOf(']', at);
                    at = close < 0 ? s.Length : close + 1;
                    inexact = true;
                    specificity += 100;
                } else if (c == ':') {
                    at++;
                    bool element = at < s.Length && s[at] == ':';
                    if (element) at++;
                    Name(s, ref at);
                    if (at < s.Length && s[at] == '(') at = ParenEnd(s, at);
                    inexact = true;
                    specificity += element ? 1 : 100;
                } else if (IsName(c)) {
                    string name = Name(s, ref at);
                    tag ??= name;
                    specificity++;
                } else {
                    return null;
                }
            }
            return new Rule {
                Tag = tag,
                Id = id,
                Classes = classes.ToArray(),
                Specificity = specificity,
                Inexact = inexact,
            };
        }

        private static string Name(string s, ref int at) {
            int start = at;
            while (at < s.Length && IsName(s[at])) at++;
            return s.Substring(start, at - start);
        }

        private static bool IsName(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c > 0x7f;

        // The index after the parenthesis that closes the one at open.
        private static int ParenEnd(string s, int open) {
            int depth = 0;
            for (int i = open; i < s.Length; i++) {
                if (s[i] == '(') depth++;
                else if (s[i] == ')' && --depth == 0) return i + 1;
            }
            return s.Length;
        }

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
    }
}
