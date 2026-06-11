#!/usr/bin/env python3
"""append_hover_citations.py -- make [Algorithm] citations visible in GH hovers.

2026-06-02 reversal: the Grasshopper UI shows only the Description string
passed to the GH_Component base(...) constructor; the structured
[Algorithm("name", "citation", ...)] attributes are invisible on hover.
This script appends a SHORT citation (author-year) derived from the FIRST
[Algorithm] attribute of each GH component class INTO the final string
literal of the description argument of its base(...) call:

    "...existing description."  ->  "...existing description. [Baker et al. 1980]"

Conservative by design. A class is edited ONLY when ALL of these hold:
  * the class carries >= 1 [Algorithm(...)] attribute,
  * the class derives (textually) from GH_Component,
    GH_TaskCapableComponent<...> or AsyncScanComponent<...>,
  * its ctor has a `: base(...)` call with exactly 5 top-level arguments
    whose 3rd argument (the description) is built purely from plain
    "..." string literals joined by '+',
  * the citation string of the first [Algorithm] attribute parses to an
    author-year short form (a 19xx/20xx year is present),
  * the description does NOT already cite: no first-author surname, no
    bracketed [...] chunk containing a year, no "[refs" marker.

Everything else is skipped and reported with a reason. Files without any
[Algorithm(...)] attribute are never touched. GUIDs, input/output order,
and all other code are untouched; the edit is a pure in-literal append.

Usage:
    python append_hover_citations.py --dry-run   # list planned edits only
    python append_hover_citations.py             # apply edits in place
"""

import argparse
import os
import re
import sys

ROOTS = [
    r"D:/frahan-stonepack/src/Frahan.StonePack.GH",
    r"D:/frahan-stonepack/src/Frahan.RubblePack",
]
EXCLUDE_DIRS = {"bin", "obj", ".vs"}
COMPONENT_BASES = {"GH_Component", "GH_TaskCapableComponent", "AsyncScanComponent"}
YEAR_RE = re.compile(r"\b(19|20)\d{2}\b")
BRACKET_YEAR_RE = re.compile(r"\[[^\[\]]*\b(19|20)\d{2}\b[^\[\]]*\]")

# ---------------------------------------------------------------------------
# Lightweight C# lexing: build a per-char mask
#   c = code, s = string literal contents (incl. quotes), x = comment
# ---------------------------------------------------------------------------

def build_mask(text):
    n = len(text)
    mask = ["c"] * n
    i = 0
    while i < n:
        ch = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if ch == "/" and nxt == "/":
            j = text.find("\n", i)
            j = n if j == -1 else j
            for k in range(i, j):
                mask[k] = "x"
            i = j
        elif ch == "/" and nxt == "*":
            j = text.find("*/", i + 2)
            j = n if j == -1 else j + 2
            for k in range(i, j):
                mask[k] = "x"
            i = j
        elif ch == "@" and nxt == '"':            # verbatim string
            j = i + 2
            while j < n:
                if text[j] == '"':
                    if j + 1 < n and text[j + 1] == '"':
                        j += 2
                        continue
                    j += 1
                    break
                j += 1
            for k in range(i, j):
                mask[k] = "s"
            i = j
        elif ch == "$":                            # interpolated string ($" or $@")
            if nxt == '"' or (nxt == "@" and i + 2 < n and text[i + 2] == '"'):
                j = i + (2 if nxt == '"' else 3)
                while j < n:
                    if text[j] == "\\" and nxt == '"':
                        j += 2
                        continue
                    if text[j] == '"':
                        j += 1
                        break
                    j += 1
                for k in range(i, j):
                    mask[k] = "s"
                i = j
            else:
                i += 1
        elif ch == '"':                            # regular string
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == '"':
                    j += 1
                    break
                j += 1
            for k in range(i, j):
                mask[k] = "s"
            i = j
        elif ch == "'":                            # char literal
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == "'":
                    j += 1
                    break
                j += 1
            # unterminated / apostrophe in code: treat as 1 char
            if j - i > 6:
                j = i + 1
            for k in range(i, min(j, n)):
                mask[k] = "s"
            i = max(j, i + 1)
        else:
            i += 1
    return mask


def find_matching(text, mask, open_pos, open_ch="(", close_ch=")"):
    """Index of the matching close char for the open char at open_pos."""
    depth = 0
    for i in range(open_pos, len(text)):
        if mask[i] != "c":
            continue
        if text[i] == open_ch:
            depth += 1
        elif text[i] == close_ch:
            depth -= 1
            if depth == 0:
                return i
    return -1


def split_top_level_args(text, mask, start, end):
    """Split text[start:end] (inside parens) on top-level commas.
    Returns list of (arg_start, arg_end) spans."""
    spans = []
    depth = 0
    a = start
    for i in range(start, end):
        if mask[i] != "c":
            continue
        ch = text[i]
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        elif ch == "," and depth == 0:
            spans.append((a, i))
            a = i + 1
    spans.append((a, end))
    return spans


STRING_LIT_RE = re.compile(r'"((?:\\.|[^"\\])*)"')


def literal_spans_in(text, mask, start, end):
    """Spans of regular '"..."' literals inside [start,end). Rejects the
    region if it contains anything except literals, '+' and whitespace."""
    spans = []
    i = start
    while i < end:
        ch = text[i]
        if mask[i] == "x":                # comment inside the arg: fine, skip
            i += 1
            continue
        if mask[i] == "s":
            if ch != '"':
                return None, "non-plain string token (verbatim/interpolated/char)"
            m = STRING_LIT_RE.match(text, i)
            if not m or m.end() > end:
                return None, "unparseable string literal"
            spans.append((m.start(), m.end()))
            i = m.end()
            continue
        if ch in " \t\r\n+":
            i += 1
            continue
        return None, "description argument is not a pure string-literal concatenation"
    if not spans:
        return None, "no string literal in description argument"
    return spans, None


def unescape(s):
    return (s.replace("\\\\", "\x00").replace('\\"', '"')
             .replace("\\n", "\n").replace("\\t", "\t").replace("\x00", "\\"))


def concat_value(text, spans):
    return "".join(unescape(text[a + 1:b - 1]) for a, b in spans)


# ---------------------------------------------------------------------------
# Short-citation derivation
# ---------------------------------------------------------------------------

CONNECTORS = {",", "&", ";", "and", "et", "al", "al."}
INITIAL_RE = re.compile(r"^(?:[A-Z]\.)+$|^[A-Z]\.?$")
NAME_BLACKLIST = {"The", "A", "An", "In", "Of", "On", "For", "And", "Or", "By",
                  "Via", "Proceedings", "Proc", "Conference", "Journal",
                  "Standard", "Methods", "Suggested", "Interim", "Section"}


def short_citation(citation):
    """Derive 'Surname YYYY' / 'A & B YYYY' / 'A et al. YYYY' from a free-text
    citation. Only the author-list run IMMEDIATELY before the first 19xx/20xx
    year is used (scanning backwards; venue acronyms like ACM/ACADIA are
    transparent, anything prose-like stops the scan). Returns (None, [],
    reason) when no confident author-year can be derived."""
    m = YEAR_RE.search(citation)
    if not m:
        return None, [], "no year in citation (likely Frahan-original / no published source)"
    year = m.group(0)
    pre = citation[:m.start()]
    if "Frahan" in pre:
        return None, [], "citation declares Frahan-original work"
    pre = pre.rstrip().rstrip("(").rstrip()
    # tokenize: unicode-aware name-ish words (optionally with trailing '.') or
    # single punctuation marks; scan BACKWARDS from the year
    tokens = re.findall(r"[^\W\d_][\w'\-]*\.?|[&,;:+().]", pre, re.UNICODE)
    surnames = []
    has_etal = False
    for t in reversed(tokens):
        low = t.lower().rstrip(".")
        if t in CONNECTORS or low in {"and", "et", "al"}:
            if low in {"et", "al"}:
                has_etal = True
            continue
        if INITIAL_RE.match(t):                       # author initials: 'B.S.', 'G.'
            continue
        if t.isupper() and len(t) >= 2:               # venue acronym: ACM, ACADIA, ICLR
            continue
        if t[0].isupper() and len(t) > 1 and not t.endswith("."):
            if t in NAME_BLACKLIST:
                break
            surnames.append(t)
            continue
        break                                          # lowercase / punctuation: prose
    surnames.reverse()
    if not surnames:
        return None, [], "no author-year parsed (likely Frahan-original / no published source)"
    surnames = surnames[:6]
    first = surnames[0]
    if has_etal or len(surnames) >= 3:
        short = "%s et al. %s" % (first, year)
    elif len(surnames) == 2:
        short = "%s & %s %s" % (surnames[0], surnames[1], year)
    else:
        short = "%s %s" % (first, year)
    return short, surnames, None


# ---------------------------------------------------------------------------
# Per-file processing
# ---------------------------------------------------------------------------

CLASS_RE = re.compile(r"\bclass\s+([A-Za-z_]\w*)")


def code_finditer(rx, text, mask):
    for m in rx.finditer(text):
        if mask[m.start()] == "c":
            yield m


def process_file(path):
    """Returns (edits, skips, new_text). edits = [(class, short, before, after)]"""
    with open(path, "rb") as f:
        raw = f.read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    try:
        text = raw.decode("utf-8-sig" if bom else "utf-8")
    except UnicodeDecodeError:
        return [], [(os.path.basename(path), "<file>", "not valid UTF-8, skipped whole file")], None

    mask = build_mask(text)

    if not any(True for _ in code_finditer(re.compile(r"\[\s*Algorithm\s*\("), text, mask)):
        return [], [], None   # no [Algorithm] in code: never touch

    classes = [(m.start(), m.group(1)) for m in code_finditer(CLASS_RE, text, mask)]
    algo_positions = [m.start() for m in
                      code_finditer(re.compile(r"\[\s*Algorithm\s*\("), text, mask)]

    # owning class of each [Algorithm( = first class declared after it
    per_class = {}
    for pos in algo_positions:
        owner = next((c for c in classes if c[0] > pos), None)
        if owner is None:
            continue
        per_class.setdefault(owner, []).append(pos)

    edits, skips = [], []
    insertions = []   # (insert_at, inserted_text)

    for (cls_pos, cls_name), attr_positions in sorted(per_class.items()):
        # ---- base type check -------------------------------------------------
        brace = text.find("{", cls_pos)
        header = text[cls_pos:brace] if brace != -1 else text[cls_pos:cls_pos + 200]
        bm = re.search(r":\s*([A-Za-z_]\w*)", header)
        base_name = bm.group(1) if bm else None
        if base_name not in COMPONENT_BASES:
            skips.append((cls_name, "?", "base type '%s' not a known GH_Component base" % base_name))
            continue

        # ---- first [Algorithm] -> short citation -----------------------------
        first_attr = min(attr_positions)
        op = text.find("(", first_attr)
        cp = find_matching(text, mask, op)
        if cp == -1:
            skips.append((cls_name, "?", "unbalanced [Algorithm(...)]"))
            continue
        arg_spans = split_top_level_args(text, mask, op + 1, cp)
        if len(arg_spans) < 2:
            skips.append((cls_name, "?", "[Algorithm] has no citation argument"))
            continue
        cit_spans, err = literal_spans_in(text, mask, *arg_spans[1])
        if err:
            skips.append((cls_name, "?", "citation arg: " + err))
            continue
        citation = concat_value(text, cit_spans)
        short, surnames, why = short_citation(citation)
        if not short:
            skips.append((cls_name, citation[:60], why))
            continue
        if '"' in short or "\\" in short:
            skips.append((cls_name, short, "short citation needs escaping, skipped"))
            continue

        # ---- locate ctor base(...) with 5 args -------------------------------
        cls_end = find_matching(text, mask, brace, "{", "}") if brace != -1 else -1
        if cls_end == -1:
            skips.append((cls_name, short, "could not bound class body"))
            continue
        base_call = None
        for m in code_finditer(re.compile(r":\s*base\s*\("), text, mask):
            if not (brace < m.start() < cls_end):
                continue
            bop = text.find("(", m.start())
            bcp = find_matching(text, mask, bop)
            if bcp == -1:
                continue
            spans5 = split_top_level_args(text, mask, bop + 1, bcp)
            if len(spans5) == 5:
                base_call = spans5
                break
        if base_call is None:
            skips.append((cls_name, short, "no 5-argument ': base(...)' call found"))
            continue

        desc_spans, err = literal_spans_in(text, mask, *base_call[2])
        if err:
            skips.append((cls_name, short, err))
            continue
        desc = concat_value(text, desc_spans)

        # ---- already-cited checks --------------------------------------------
        if surnames and surnames[0] in desc:
            skips.append((cls_name, short, "description already names author '%s'" % surnames[0]))
            continue
        if BRACKET_YEAR_RE.search(desc) or "[refs" in desc.lower():
            skips.append((cls_name, short, "description already has a [ citation ] suffix"))
            continue
        if short in desc:
            skips.append((cls_name, short, "short citation already present"))
            continue

        # ---- plan the edit -----------------------------------------------------
        last_a, last_b = desc_spans[-1]
        insert_at = last_b - 1                      # before the closing quote
        inserted = " [" + short + "]"
        tail = text[max(last_a, last_b - 50):last_b]
        insertions.append((insert_at, inserted))
        edits.append((cls_name, short, tail, tail[:-1] + inserted + '"'))

    if not insertions:
        return edits, skips, None

    new_text = text
    for insert_at, inserted in sorted(insertions, reverse=True):
        new_text = new_text[:insert_at] + inserted + new_text[insert_at:]
    out = new_text.encode("utf-8")
    if bom:
        out = b"\xef\xbb\xbf" + out
    return edits, skips, out


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--dry-run", action="store_true", help="list planned edits, change nothing")
    args = ap.parse_args()

    all_edits, all_skips, files_edited = [], [], 0
    for root in ROOTS:
        for dirpath, dirnames, filenames in os.walk(root):
            dirnames[:] = [d for d in dirnames if d not in EXCLUDE_DIRS]
            for fn in sorted(filenames):
                if not fn.endswith(".cs"):
                    continue
                path = os.path.join(dirpath, fn)
                edits, skips, out = process_file(path)
                rel = os.path.relpath(path, r"D:/frahan-stonepack")
                for cls, short, before, after in edits:
                    all_edits.append((rel, cls, short, before, after))
                for cls, short, reason in skips:
                    all_skips.append((rel, cls, short, reason))
                if out is not None and not args.dry_run:
                    with open(path, "wb") as f:
                        f.write(out)
                if out is not None:
                    files_edited += 1

    mode = "DRY-RUN (no files written)" if args.dry_run else "APPLIED"
    print("== %s ==" % mode)
    print("\n-- planned edits (%d classes in %d files) --" % (len(all_edits), files_edited))
    for rel, cls, short, before, after in all_edits:
        print("EDIT  %-72s %-44s + [%s]" % (rel, cls, short))
    print("\n-- skipped (%d classes) --" % len(all_skips))
    for rel, cls, short, reason in all_skips:
        print("SKIP  %-72s %-44s %s" % (rel, cls, reason))
    print("\nSummary: %d classes edited across %d files, %d skipped."
          % (len(all_edits), files_edited, len(all_skips)))


if __name__ == "__main__":
    sys.exit(main())
