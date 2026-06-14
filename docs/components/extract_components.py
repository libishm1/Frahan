#!/usr/bin/env python3
"""Extract Frahan StonePack GH component metadata from source -> structured wiki source.

Walks src/Frahan.StonePack.GH/**/*.cs, parses each GH_Component subclass for:
  GUID, name, nickname, category, subcategory, description, [Algorithm] citation,
  inputs/outputs (name, nick, type, access, description), [RelatedComponent] edges, icon.

Emits (into OUT):
  components.json        - the full structured array
  COMPONENTS.md          - human-readable catalog grouped by subcategory, with I/O tables
  connection_map.mmd     - Mermaid graph of RelatedComponent edges
  connection_map.graphml - same graph in GraphML
  connections.json       - nodes + edges
  ICON_LIBRARY.md        - component->icon map + coverage (missing icons flagged)

Source-of-truth = the source, so this is always current. Style: no em dashes.
"""
import os, re, json, html

GH = r"D:\frahan-stonepack\src\Frahan.StonePack.GH"
RES = os.path.join(GH, "Resources")
OUT = r"D:\code_ws\outputs\2026-06-15\wiki_source"
os.makedirs(OUT, exist_ok=True)

# AddXParameter -> friendly type
TYPE_RE = re.compile(r"Add(\w+?)Parameter\s*\(")
STR_RE = re.compile(r'"((?:[^"\\]|\\.)*)"')
ACCESS_RE = re.compile(r"GH_ParamAccess\.(\w+)")

def strip_cs(s):
    return s.replace('\\"', '"').replace("\\\\", "\\")

def grab_call(text, start):
    """Return the substring of a (...) call starting at index of '(' = start, balanced."""
    depth = 0
    i = start
    while i < len(text):
        c = text[i]
        if c == '"':  # skip string
            i += 1
            while i < len(text) and text[i] != '"':
                if text[i] == '\\':
                    i += 1
                i += 1
        elif c == '(':
            depth += 1
        elif c == ')':
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
        i += 1
    return text[start:]

def parse_params(block):
    """block = body of RegisterInput/OutputParams. Return list of param dicts."""
    params = []
    for m in TYPE_RE.finditer(block):
        ptype = m.group(1)
        call = grab_call(block, m.end() - 1)
        strs = [strip_cs(s) for s in STR_RE.findall(call)]
        acc = ACCESS_RE.search(call)
        if len(strs) >= 2:
            params.append({
                "name": strs[0],
                "nick": strs[1],
                "type": ptype,
                "access": acc.group(1) if acc else "item",
                "desc": " ".join(strs[2:]).strip() if len(strs) > 2 else "",
            })
    return params

def find_method_body(text, signature_re):
    m = signature_re.search(text)
    if not m:
        return ""
    brace = text.find("{", m.end())
    if brace < 0:
        return ""
    depth = 0
    i = brace
    while i < len(text):
        if text[i] == '{':
            depth += 1
        elif text[i] == '}':
            depth -= 1
            if depth == 0:
                return text[brace:i + 1]
        i += 1
    return text[brace:]

CLASS_RE = re.compile(r"class\s+(\w+)\s*:\s*(GH_Component|GH_TaskCapableComponent|AsyncScanComponent|GH_GeometryComponent|\w*Component)")
GUID_RE = re.compile(r'ComponentGuid\s*=>\s*new\s+Guid\("([0-9A-Fa-f-]+)"\)')
ALGO_RE = re.compile(r'\[Algorithm\(\s*"((?:[^"\\]|\\.)*)"\s*,\s*"((?:[^"\\]|\\.)*)"')
REL_RE = re.compile(r'\[RelatedComponent\(\s*"((?:[^"\\]|\\.)*)"(?:\s*,\s*Reason\s*=\s*"((?:[^"\\]|\\.)*)")?')
ICON_RE = re.compile(r'IconProvider\.Load\("([^"]+)"\)')
BASE_RE = re.compile(r":\s*base\s*\(")
INPUT_SIG = re.compile(r"RegisterInputParams\s*\([^)]*\)")
OUTPUT_SIG = re.compile(r"RegisterOutputParams\s*\([^)]*\)")

comps = []
for root, _, files in os.walk(GH):
    if os.sep + "obj" in root or os.sep + "bin" in root:
        continue
    for fn in files:
        if not fn.endswith(".cs"):
            continue
        path = os.path.join(root, fn)
        text = open(path, encoding="utf-8", errors="replace").read()
        if "ComponentGuid" not in text or "RegisterInputParams" not in text:
            continue
        guid = GUID_RE.search(text)
        cls = CLASS_RE.search(text)
        if not guid or not cls:
            continue
        # base ctor strings (name, nick, desc..., category, subcategory)
        bm = BASE_RE.search(text)
        name = nick = desc = category = subcat = ""
        if bm:
            call = grab_call(text, bm.end() - 1)
            bstrs = [strip_cs(s) for s in STR_RE.findall(call)]
            if len(bstrs) >= 5:
                name, nick = bstrs[0], bstrs[1]
                category, subcat = bstrs[-2], bstrs[-1]
                desc = " ".join(bstrs[2:-2]).strip()
            elif len(bstrs) >= 2:
                name, nick = bstrs[0], bstrs[1]
                desc = " ".join(bstrs[2:]).strip()
        algo = ALGO_RE.search(text)
        related = [{"target": strip_cs(t), "reason": strip_cs(r or "")} for t, r in REL_RE.findall(text)]
        icon = ICON_RE.search(text)
        comps.append({
            "guid": guid.group(1).upper(),
            "class": cls.group(1),
            "name": name,
            "nickname": nick,
            "category": category or "Frahan",
            "subcategory": subcat,
            "description": desc,
            "algorithm": {"title": strip_cs(algo.group(1)), "citation": strip_cs(algo.group(2))} if algo else None,
            "inputs": parse_params(find_method_body(text, INPUT_SIG)),
            "outputs": parse_params(find_method_body(text, OUTPUT_SIG)),
            "related": related,
            "icon": icon.group(1) if icon else None,
            "file": os.path.relpath(path, r"D:\frahan-stonepack").replace("\\", "/"),
        })

comps.sort(key=lambda c: (c["subcategory"], c["name"]))
json.dump(comps, open(os.path.join(OUT, "components.json"), "w", encoding="utf-8"), indent=2, ensure_ascii=False)
print(f"parsed {len(comps)} components")

# ---- COMPONENTS.md (I/O catalog grouped by subcategory) ----
by_sub = {}
for c in comps:
    by_sub.setdefault(c["subcategory"] or "(uncategorized)", []).append(c)
md = ["# Frahan StonePack - component catalog (inputs / outputs)",
      "",
      f"Auto-generated from source by `extract_components.py`. {len(comps)} components on the `Frahan` ribbon tab.",
      "Each entry lists its GUID, algorithm citation, inputs, outputs, and related components.",
      "Source of truth = the component source; regenerate after any component change.", ""]
md.append("## Subcategories\n")
for sub in sorted(by_sub):
    md.append(f"- [{sub}](#{sub.lower().replace(' ', '-')}) ({len(by_sub[sub])})")
md.append("")
for sub in sorted(by_sub):
    md.append(f"\n## {sub}\n")
    for c in by_sub[sub]:
        md.append(f"### {c['name']}  (`{c['nickname']}`)")
        md.append("")
        md.append(f"- GUID: `{c['guid']}`  |  icon: `{c['icon'] or '(none)'}`  |  source: `{c['file']}`")
        if c["algorithm"]:
            md.append(f"- Algorithm: **{c['algorithm']['title']}** - {c['algorithm']['citation']}")
        if c["description"]:
            md.append(f"- {c['description']}")
        md.append("")
        if c["inputs"]:
            md.append("| in | type | access | description |")
            md.append("|---|---|---|---|")
            for p in c["inputs"]:
                md.append(f"| {p['name']} (`{p['nick']}`) | {p['type']} | {p['access']} | {p['desc']} |")
            md.append("")
        if c["outputs"]:
            md.append("| out | type | access | description |")
            md.append("|---|---|---|---|")
            for p in c["outputs"]:
                md.append(f"| {p['name']} (`{p['nick']}`) | {p['type']} | {p['access']} | {p['desc']} |")
            md.append("")
        if c["related"]:
            md.append("Related:")
            for r in c["related"]:
                md.append(f"- {r['target']}" + (f" - {r['reason']}" if r['reason'] else ""))
            md.append("")
open(os.path.join(OUT, "COMPONENTS.md"), "w", encoding="utf-8").write("\n".join(md))
print("wrote COMPONENTS.md")

# ---- connection map (RelatedComponent edges) ----
# node key = "Subcat > Name"; resolve related targets "Frahan > Sub > Name" to names.
def short(target):
    parts = [p.strip() for p in target.split(">")]
    return parts[-1] if parts else target
name_to_id = {c["name"]: f"n{i}" for i, c in enumerate(comps)}
edges = []
for c in comps:
    for r in c["related"]:
        tgt = short(r["target"])
        if tgt in name_to_id:
            edges.append({"from": c["name"], "to": tgt, "reason": r["reason"]})
conn = {"nodes": [{"id": name_to_id[c["name"]], "name": c["name"], "subcategory": c["subcategory"],
                    "guid": c["guid"]} for c in comps],
        "edges": [{"from": name_to_id[e["from"]], "to": name_to_id[e["to"]],
                    "fromName": e["from"], "toName": e["to"], "reason": e["reason"]} for e in edges]}
json.dump(conn, open(os.path.join(OUT, "connections.json"), "w", encoding="utf-8"), indent=2, ensure_ascii=False)

# Mermaid (grouped by subcategory as subgraphs)
mm = ["```mermaid", "graph LR"]
subs = {}
for c in comps:
    subs.setdefault(c["subcategory"] or "misc", []).append(c)
for sub, cs in sorted(subs.items()):
    safe = re.sub(r"\W", "_", sub) or "misc"
    mm.append(f"  subgraph {safe}[{sub}]")
    for c in cs:
        label = c["name"].replace('"', "'")
        mm.append(f'    {name_to_id[c["name"]]}["{label}"]')
    mm.append("  end")
for e in edges:
    mm.append(f'  {name_to_id[e["from"]]} --> {name_to_id[e["to"]]}')
mm.append("```")
open(os.path.join(OUT, "connection_map.mmd"), "w", encoding="utf-8").write("\n".join(mm))

# GraphML
gx = ['<?xml version="1.0" encoding="UTF-8"?>',
      '<graphml xmlns="http://graphml.graphdrawing.org/xmlns">',
      '<key id="name" for="node" attr.name="name" attr.type="string"/>',
      '<key id="sub" for="node" attr.name="subcategory" attr.type="string"/>',
      '<key id="reason" for="edge" attr.name="reason" attr.type="string"/>',
      '<graph edgedefault="directed">']
for c in comps:
    gx.append(f'<node id="{name_to_id[c["name"]]}"><data key="name">{html.escape(c["name"])}</data>'
              f'<data key="sub">{html.escape(c["subcategory"])}</data></node>')
for i, e in enumerate(edges):
    gx.append(f'<edge id="e{i}" source="{name_to_id[e["from"]]}" target="{name_to_id[e["to"]]}">'
              f'<data key="reason">{html.escape(e["reason"])}</data></edge>')
gx += ['</graph>', '</graphml>']
open(os.path.join(OUT, "connection_map.graphml"), "w", encoding="utf-8").write("\n".join(gx))
print(f"wrote connection map: {len(comps)} nodes, {len(edges)} edges")

# ---- icon library ----
icons_on_disk = sorted(f for f in os.listdir(RES) if f.lower().endswith(".png")) if os.path.isdir(RES) else []
used = {c["icon"] for c in comps if c["icon"]}
missing = [c for c in comps if not c["icon"]]
unused = [i for i in icons_on_disk if i not in used]
il = ["# Frahan StonePack - icon library",
      "",
      f"Resources dir: `src/Frahan.StonePack.GH/Resources` ({len(icons_on_disk)} PNGs). "
      f"{len(used)} distinct icons referenced by {len(comps)} components.", "",
      "## Coverage", "",
      f"- Components WITHOUT an explicit icon: **{len(missing)}** (fall back to the default GH icon).",
      f"- Icons on disk NOT referenced by any component: **{len(unused)}**.", "",
      "## Component -> icon", "",
      "| component | subcategory | icon | on disk |",
      "|---|---|---|---|"]
diskset = set(icons_on_disk)
for c in comps:
    ic = c["icon"] or "(none)"
    od = "yes" if c["icon"] in diskset else ("-" if not c["icon"] else "MISSING FILE")
    il.append(f"| {c['name']} | {c['subcategory']} | {ic} | {od} |")
if missing:
    il += ["", "## Components needing an icon", ""]
    il += [f"- {c['name']} ({c['subcategory']}) - `{c['file']}`" for c in missing]
if unused:
    il += ["", "## Unreferenced icons on disk (candidates to retire or wire up)", ""]
    il += [f"- {i}" for i in unused]
open(os.path.join(OUT, "ICON_LIBRARY.md"), "w", encoding="utf-8").write("\n".join(il))
print(f"icons: {len(icons_on_disk)} on disk, {len(used)} used, {len(missing)} components missing an icon")
print("done ->", OUT)
