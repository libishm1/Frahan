#!/usr/bin/env python3
"""Pre-compute data-structure consistency facts from components.json.
Finds: (1) concepts (by normalized port name) that appear as MORE THAN ONE GH type
across components - the canonical-type inconsistencies; (2) Generic/untyped ports;
(3) overall type frequency; (4) custom/unusual types. Output: type_audit.json +
TYPE_AUDIT_FACTS.md (input for the audit). No judgement here, just the facts."""
import json, io, re, collections

SRC = r"D:\code_ws\outputs\2026-06-15\wiki_source\components.json"
OUT = r"D:\code_ws\outputs\2026-06-15\wiki_source"
comps = json.load(io.open(SRC, encoding="utf-8"))

def norm(name):
    n = name.lower().strip()
    n = re.sub(r"\(.*?\)", "", n)            # drop parentheticals
    n = re.sub(r"[^a-z0-9 ]", " ", n)
    n = re.sub(r"\s+", " ", n).strip()
    if n.endswith("es") and len(n) > 4: n = n[:-2]
    elif n.endswith("s") and len(n) > 3: n = n[:-1]   # naive singularize
    return n

# concept -> {type -> [(component, dir, original_name)]}
concept = collections.defaultdict(lambda: collections.defaultdict(list))
type_freq = collections.Counter()
generic = []
ports_total = 0
for c in comps:
    for d, ps in (("in", c["inputs"]), ("out", c["outputs"])):
        for p in ps:
            ports_total += 1
            t = p["type"]
            type_freq[t] += 1
            key = norm(p["name"])
            if key:
                concept[key][t].append((c["name"], d, p["name"]))
            if t in ("Generic",):
                generic.append({"component": c["name"], "dir": d, "name": p["name"], "nick": p["nick"], "desc": p["desc"][:80]})

STD = {"Number","Integer","Boolean","Text","Mesh","Brep","Curve","Surface","Point","Vector",
       "Plane","Box","Line","Circle","Rectangle","Colour","Color","Geometry","Transform",
       "Interval","Domain","GenericObject","Generic","Param","Field","Time","Matrix"}
custom = sorted(t for t in type_freq if t not in STD)

multi = []
for k, tm in concept.items():
    types = sorted(tm.keys())
    if len(types) > 1:
        occ = []
        for t, lst in tm.items():
            for comp, d, orig in lst:
                occ.append({"type": t, "component": comp, "dir": d, "port": orig})
        multi.append({"concept": k, "types": types, "n_types": len(types),
                       "n_occurrences": len(occ), "occurrences": occ})
multi.sort(key=lambda m: (-m["n_types"], -m["n_occurrences"]))

out = {"components": len(comps), "ports_total": ports_total,
       "type_frequency": dict(type_freq.most_common()),
       "custom_types": custom,
       "generic_ports": generic,
       "multi_type_concepts": multi}
json.dump(out, io.open(f"{OUT}\\type_audit.json", "w", encoding="utf-8"), indent=2, ensure_ascii=False)

md = ["# Data-structure facts (pre-computed from components.json)", "",
      f"{len(comps)} components, {ports_total} ports. Type frequency:", ""]
for t, n in type_freq.most_common():
    md.append(f"- {t}: {n}")
md += ["", f"## Concepts that appear as MORE THAN ONE type ({len(multi)})", "",
       "These are the canonical-type inconsistency candidates: the same named port modelled as different GH types across components.", ""]
for m in multi:
    md.append(f"### `{m['concept']}` -> {', '.join(m['types'])}  ({m['n_occurrences']} ports)")
    for o in m["occurrences"]:
        md.append(f"- {o['type']:8s} {o['dir']:3s}  {o['component']} :: {o['port']}")
    md.append("")
md += [f"## Generic / untyped ports ({len(generic)})", ""]
for g in generic:
    md.append(f"- {g['dir']:3s} {g['component']} :: {g['name']} ({g['nick']}) - {g['desc']}")
if custom:
    md += ["", f"## Custom / non-standard types ({len(custom)})", ""] + [f"- {t}" for t in custom]
io.open(f"{OUT}\\TYPE_AUDIT_FACTS.md", "w", encoding="utf-8").write("\n".join(md))
print(f"{len(comps)} comps, {ports_total} ports, {len(multi)} multi-type concepts, "
      f"{len(generic)} generic ports, {len(custom)} custom types")
print("types:", dict(type_freq.most_common()))
