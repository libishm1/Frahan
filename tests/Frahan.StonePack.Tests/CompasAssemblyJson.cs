#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Frahan.Tests;

// =============================================================================
// TEST HELPER (no production code) — minimal tolerant loader for the compas
// Assembly JSON sample files shipped with BlockResearchGroup/compas_cra (MIT,
// ETH Zurich BRG, Kao et al.). Fixtures live in tests/data/compas_cra/ with
// licence notes in the README.md beside them.
//
// Schema (compas json_dump of a compas_assembly Assembly):
//   { "dtype": "compas_assembly.datastructures/Assembly",
//     "data": {
//       "attributes": {...},
//       "graph": { "dtype": "compas.datastructures/Graph",
//         "data": {
//           "node": { "<id>": {
//               "block": { "dtype": "compas_assembly.datastructures/Block",
//                          "data": { "vertex": { "<vkey>": {"x":..,"y":..,"z":..}, ... },
//                                    "face":   { "<fkey>": [vkey, vkey, ...], ... } } },
//               "is_support": <bool, optional/absent in the samples> }, ... },
//           "edge": {...}, "adjacency": {...}, ... } } } }
//
// Node / vertex / face dicts are keyed by STRINGIFIED INTS in arbitrary JSON
// order -> always order by int key. Face polygons reference vertex KEYS (not
// positions) -> remap through the sorted-key index. Faces wind outward in the
// compas block convention; fan triangulation preserves that winding.
// =============================================================================

static class CompasAssemblyJson
{
    /// <summary>
    /// Load a compas_cra sample Assembly JSON. Returns per-node triangulated
    /// meshes in the Frahan flat convention (parallel lists, node id == list
    /// index — enforced) plus the node ids flagged is_support in the file
    /// (empty for the shipped samples; their example scripts set supports
    /// explicitly via set_boundary_conditions).
    /// </summary>
    public static (List<IReadOnlyList<double>> Coords,
                   List<IReadOnlyList<int>> Tris,
                   List<int> SupportNodeIndices) Load(string path)
    {
        var root = JObject.Parse(File.ReadAllText(path));
        var graphData = (JObject)root["data"]["graph"]["data"];
        var nodeObj = (JObject)graphData["node"];
        if (nodeObj == null)
            throw new InvalidDataException($"{path}: data.graph.data.node missing");

        var ids = new List<int>();
        foreach (var p in nodeObj.Properties())
            ids.Add(int.Parse(p.Name, CultureInfo.InvariantCulture));
        ids.Sort();
        // The tests address nodes by compas node id (e.g. supports [0, 1]).
        // Guarantee id == index so that addressing cannot silently shift.
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] != i)
                throw new InvalidDataException(
                    $"{path}: node ids not contiguous from 0 (found {ids[i]} at position {i}); " +
                    "id-as-index addressing would be wrong");
        }

        var coords = new List<IReadOnlyList<double>>(ids.Count);
        var tris = new List<IReadOnlyList<int>>(ids.Count);
        var supports = new List<int>();

        foreach (int id in ids)
        {
            var node = (JObject)nodeObj[id.ToString(CultureInfo.InvariantCulture)];

            var isSupport = node["is_support"];
            if (isSupport != null &&
                ((isSupport.Type == JTokenType.Boolean && (bool)isSupport) ||
                 (isSupport.Type == JTokenType.Integer && (long)isSupport != 0)))
            {
                supports.Add(id);
            }

            var blockData = (JObject)node["block"]["data"];
            var vertObj = (JObject)blockData["vertex"];
            var faceObj = (JObject)blockData["face"];

            // vertices: order by int key, remember key -> position
            var vkeys = new List<int>();
            foreach (var p in vertObj.Properties())
                vkeys.Add(int.Parse(p.Name, CultureInfo.InvariantCulture));
            vkeys.Sort();
            var keyToIndex = new Dictionary<int, int>(vkeys.Count);
            var flat = new List<double>(vkeys.Count * 3);
            for (int i = 0; i < vkeys.Count; i++)
            {
                keyToIndex[vkeys[i]] = i;
                var v = vertObj[vkeys[i].ToString(CultureInfo.InvariantCulture)];
                flat.Add((double)v["x"]);
                flat.Add((double)v["y"]);
                flat.Add((double)v["z"]);
            }

            // faces: order by int key (determinism), fan-triangulate each
            // polygon preserving winding
            var fkeys = new List<int>();
            foreach (var p in faceObj.Properties())
                fkeys.Add(int.Parse(p.Name, CultureInfo.InvariantCulture));
            fkeys.Sort();
            var t = new List<int>();
            foreach (int fk in fkeys)
            {
                var poly = (JArray)faceObj[fk.ToString(CultureInfo.InvariantCulture)];
                if (poly.Count < 3)
                    throw new InvalidDataException(
                        $"{path}: node {id} face {fk} has {poly.Count} vertices (< 3)");
                int v0 = keyToIndex[(int)poly[0]];
                for (int j = 1; j + 1 < poly.Count; j++)
                {
                    t.Add(v0);
                    t.Add(keyToIndex[(int)poly[j]]);
                    t.Add(keyToIndex[(int)poly[j + 1]]);
                }
            }

            coords.Add(flat);
            tris.Add(t);
        }

        return (coords, tris, supports);
    }
}
