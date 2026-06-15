#!/usr/bin/env python3
"""Corrected component catalog extractor. Fixes the one-component-per-file bug in
docs/components/extract_components.py by scoping EACH component class body and parsing
all of them. Emits components.json + COMPONENTS.md + ICON_LIBRARY.md into a staging dir."""
import os, re, json, html

REPO = r"D:\frahan-stonepack"
GH = os.path.join(REPO, "src", "Frahan.StonePack.GH")
RES = os.path.join(GH, "Resources")
OUT = r"D:\code_ws\outputs\2026-06-15\wiki_source_v2"
os.makedirs(OUT, exist_ok=True)

TYPE_RE = re.compile(r"Add(\w+?)Parameter\s*\(")
STR_RE = re.compile(r'"((?:[^"\\]|\\.)*)"')
ACCESS_RE = re.compile(r"GH_ParamAccess\.(\w+)")
GUID_RE = re.compile(r'ComponentGuid\s*=>\s*\n?\s*new\s+Guid\("([0-9A-Fa-f-]+)"\)')
ALGO_RE = re.compile(r'\[Algorithm\(\s*"((?:[^"\\]|\\.)*)"\s*,\s*"((?:[^"\\]|\\.)*)"')
REL_RE = re.compile(r'\[RelatedComponent\(\s*"((?:[^"\\]|\\.)*)"(?:\s*,\s*Reason\s*=\s*"((?:[^"\\]|\\.)*)")?')
ICON_RE = re.compile(r'IconProvider\.Load\("([^"]+)"\)')
EXPO_RE = re.compile(r'\bExposure\s*=>\s*GH_Exposure\.(\w+)')
CLASS_RE = re.compile(r'\bclass\s+(\w+)\s*:\s*([\w<>, .]+?)\s*[\{\n]')
COMPONENT_BASES = ("Component", "AsyncScanComponent", "FrahanComponentBase")


def strip_cs(s):
    return s.replace('\\"', '"').replace("\\\\", "\\")


def balanced(text, open_idx, oc="{", cc="}"):
    depth = 0; i = open_idx
    while i < len(text):
        ch = text[i]
        if ch == '"':
            i += 1
            while i < len(text) and text[i] != '"':
                if text[i] == '\\':
                    i += 1
                i += 1
        elif ch == oc:
            depth += 1
        elif ch == cc:
            depth -= 1
            if depth == 0:
                return text[open_idx:i+1]
        i += 1
    return text[open_idx:]


def grab_call(text, start):
    return balanced(text, start, "(", ")")


def parse_params(block):
    out = []
    for m in TYPE_RE.finditer(block):
        call = grab_call(block, m.end() - 1)
        strs = [strip_cs(s) for s in STR_RE.findall(call)]
        acc = ACCESS_RE.search(call)
        if len(strs) >= 2:
            out.append({"name": strs[0], "nick": strs[1], "type": m.group(1),
                        "access": acc.group(1) if acc else "item",
                        "desc": " ".join(strs[2:]).strip() if len(strs) > 2 else ""})
    return out


def method_body(text, sig):
    m = re.search(sig, text)
    if not m:
        return ""
    b = text.find("{", m.end())
    return balanced(text, b) if b >= 0 else ""


def is_component_base(bases):
    head = bases.split(",")[0].split("<")[0].strip()   # strip generic args
    return head.endswith(("Component", "ComponentBase"))


comps = []
for root, _, files in os.walk(GH):
    if os.sep + "obj" in root or os.sep + "bin" in root:
        continue
    for fn in files:
        if not fn.endswith(".cs"):
            continue
        path = os.path.join(root, fn)
        text = open(path, encoding="utf-8", errors="replace").read()
        if "ComponentGuid" not in text:
            continue
        # find every class declaration; scope its body
        for m in CLASS_RE.finditer(text):
            cls, bases = m.group(1), m.group(2)
            if not is_component_base(bases):
                continue
            bstart = text.find("{", m.end() - 1)
            if bstart < 0:
                continue
            body = balanced(text, bstart)
            gm = GUID_RE.search(body)
            if not gm:
                continue
            bm = re.search(r':\s*base\s*\(', body)
            name = nick = desc = category = subcat = ""
            if bm:
                call = grab_call(body, bm.end() - 1)
                bs = [strip_cs(s) for s in STR_RE.findall(call)]
                if len(bs) >= 5:
                    name, nick = bs[0], bs[1]
                    category, subcat = bs[-2], bs[-1]
                    desc = " ".join(bs[2:-2]).strip()
                elif len(bs) >= 2:
                    name, nick = bs[0], bs[1]
                    desc = " ".join(bs[2:]).strip()
            algo = ALGO_RE.search(text[max(0, m.start()-1200):m.start()] + body[:400])
            related = [{"target": strip_cs(t), "reason": strip_cs(r or "")}
                       for t, r in REL_RE.findall(body)]
            icon = ICON_RE.search(body)
            expo = EXPO_RE.search(body)
            comps.append({
                "guid": gm.group(1).upper(), "class": cls, "name": name, "nickname": nick,
                "category": category or "Frahan", "subcategory": subcat, "description": desc,
                "exposure": expo.group(1) if expo else "secondary",
                "algorithm": {"title": strip_cs(algo.group(1)), "citation": strip_cs(algo.group(2))} if algo else None,
                "inputs": parse_params(method_body(body, r"RegisterInputParams\s*\([^)]*\)")),
                "outputs": parse_params(method_body(body, r"RegisterOutputParams\s*\([^)]*\)")),
                "related": related, "icon": icon.group(1) if icon else None,
                "file": os.path.relpath(path, REPO).replace("\\", "/"),
            })

# dedupe by guid (keep first)
seen = {}
for c in comps:
    seen.setdefault(c["guid"], c)
comps = sorted(seen.values(), key=lambda c: (c["subcategory"], c["name"]))
json.dump(comps, open(os.path.join(OUT, "components.json"), "w", encoding="utf-8"), indent=2, ensure_ascii=False)

from collections import Counter
print(f"parsed {len(comps)} components (was 187)")
sub = Counter(c["subcategory"] for c in comps)
for s, n in sorted(sub.items(), key=lambda x: -x[1]):
    print(f"  {s:18s} {n}")
noicon = [c for c in comps if not c["icon"]]
print(f"components missing an icon ref: {len(noicon)}")

# ---- COMPONENTS.md ----
by_sub = {}
for c in comps:
    by_sub.setdefault(c["subcategory"] or "(uncategorized)", []).append(c)
md = ["# Frahan StonePack - component catalog (inputs / outputs)", "",
      f"Auto-generated from source by `extract_components.py`. {len(comps)} components on the `Frahan` ribbon tab.",
      "Each entry lists its GUID, algorithm citation, inputs, outputs, and related components.",
      "Source of truth = the component source; regenerate after any component change.", "", "## Subcategories", ""]
for s in sorted(by_sub):
    md.append(f"- [{s}](#{s.lower().replace(' ', '-')}) ({len(by_sub[s])})")
md.append("")
for s in sorted(by_sub):
    md.append(f"\n## {s}\n")
    for c in by_sub[s]:
        md.append(f"### {c['name']}  (`{c['nickname']}`)\n")
        md.append(f"- GUID: `{c['guid']}`  |  icon: `{c['icon'] or '(none)'}`  |  exposure: `{c['exposure']}`  |  source: `{c['file']}`")
        if c["algorithm"]:
            md.append(f"- Algorithm: **{c['algorithm']['title']}** - {c['algorithm']['citation']}")
        if c["description"]:
            md.append(f"- {c['description']}")
        md.append("")
        for lbl, key in (("in", "inputs"), ("out", "outputs")):
            if c[key]:
                md.append(f"| {lbl} | type | access | description |"); md.append("|---|---|---|---|")
                for p in c[key]:
                    md.append(f"| {p['name']} (`{p['nick']}`) | {p['type']} | {p['access']} | {p['desc']} |")
                md.append("")
        if c["related"]:
            md.append("Related:")
            for r in c["related"]:
                md.append(f"- {r['target']}" + (f" - {r['reason']}" if r['reason'] else ""))
            md.append("")
open(os.path.join(OUT, "COMPONENTS.md"), "w", encoding="utf-8").write("\n".join(md))

# ---- connection map (RelatedComponent edges) ----
name_to_id = {c["name"]: f"n{i}" for i, c in enumerate(comps)}
edges = []
for c in comps:
    for r in c["related"]:
        tgt = [p.strip() for p in r["target"].split(">")][-1]
        if tgt in name_to_id:
            edges.append({"from": c["name"], "to": tgt, "reason": r["reason"]})
conn = {"nodes": [{"id": name_to_id[c["name"]], "name": c["name"], "subcategory": c["subcategory"], "guid": c["guid"]} for c in comps],
        "edges": [{"from": name_to_id[e["from"]], "to": name_to_id[e["to"]], "fromName": e["from"], "toName": e["to"], "reason": e["reason"]} for e in edges]}
json.dump(conn, open(os.path.join(OUT, "connections.json"), "w", encoding="utf-8"), indent=2, ensure_ascii=False)
mm = ["```mermaid", "graph LR"]
subs = {}
for c in comps:
    subs.setdefault(c["subcategory"] or "misc", []).append(c)
for s, cs in sorted(subs.items()):
    mm.append(f"  subgraph {re.sub(chr(92)+'W', '_', s) or 'misc'}[{s}]")
    for c in cs:
        mm.append(f'    {name_to_id[c["name"]]}["{c["name"].replace(chr(34), chr(39))}"]')
    mm.append("  end")
for e in edges:
    mm.append(f'  {name_to_id[e["from"]]} --> {name_to_id[e["to"]]}')
mm.append("```")
open(os.path.join(OUT, "connection_map.mmd"), "w", encoding="utf-8").write("\n".join(mm))
gx = ['<?xml version="1.0" encoding="UTF-8"?>', '<graphml xmlns="http://graphml.graphdrawing.org/xmlns">',
      '<key id="name" for="node" attr.name="name" attr.type="string"/>',
      '<key id="sub" for="node" attr.name="subcategory" attr.type="string"/>',
      '<key id="reason" for="edge" attr.name="reason" attr.type="string"/>', '<graph edgedefault="directed">']
for c in comps:
    gx.append(f'<node id="{name_to_id[c["name"]]}"><data key="name">{html.escape(c["name"])}</data><data key="sub">{html.escape(c["subcategory"])}</data></node>')
for i, e in enumerate(edges):
    gx.append(f'<edge id="e{i}" source="{name_to_id[e["from"]]}" target="{name_to_id[e["to"]]}"><data key="reason">{html.escape(e["reason"])}</data></edge>')
gx += ['</graph>', '</graphml>']
open(os.path.join(OUT, "connection_map.graphml"), "w", encoding="utf-8").write("\n".join(gx))

# ---- ICON_LIBRARY.md ----
icons_on_disk = sorted(f for f in os.listdir(RES) if f.lower().endswith(".png")) if os.path.isdir(RES) else []
used = {c["icon"] for c in comps if c["icon"]}
diskset = set(icons_on_disk)
missing_file = [c for c in comps if c["icon"] and c["icon"] not in diskset]
il = ["# Frahan StonePack - icon library", "",
      f"Resources dir: `src/Frahan.StonePack.GH/Resources` ({len(icons_on_disk)} PNGs). "
      f"{len(used)} distinct icons referenced by {len(comps)} components.", "", "## Coverage", "",
      f"- Components WITHOUT an explicit icon: **{len([c for c in comps if not c['icon']])}**.",
      f"- Referenced icons with NO file on disk: **{len(missing_file)}**.",
      f"- Icons on disk NOT referenced: **{len([i for i in icons_on_disk if i not in used])}**.", "",
      "## Component -> icon", "", "| component | subcategory | icon | on disk |", "|---|---|---|---|"]
for c in comps:
    od = "yes" if c["icon"] in diskset else ("-" if not c["icon"] else "MISSING FILE")
    il.append(f"| {c['name']} | {c['subcategory']} | {c['icon'] or '(none)'} | {od} |")
if missing_file:
    il += ["", "## Referenced icons with NO file (canvas shows default)", ""]
    il += [f"- {c['name']} ({c['subcategory']}) -> `{c['icon']}`" for c in missing_file]
open(os.path.join(OUT, "ICON_LIBRARY.md"), "w", encoding="utf-8").write("\n".join(il))
print(f"wrote COMPONENTS.md, ICON_LIBRARY.md, connection maps -> {OUT}")
