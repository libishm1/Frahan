#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Rhino.Geometry;

namespace Frahan.Core.Fabrication;

// =============================================================================
// CompasExporter -- a bridge from a Frahan masonry/vault assembly to the COMPAS
// ecosystem (compas, compas_assembly / compas_model, compas_fab). COMPAS is the
// ETH/BRG Python framework that owns TNA (compas_tna), rigid-block equilibrium
// (compas_cra / compas_masonry -- the same Kao 2022 CRA Frahan ports) and robot
// fabrication (compas_fab). Rather than compete, INTEROP: hand a user the blocks,
// placement/robot frames, and contact interfaces so they can run compas_cra's
// solver or compas_fab's robot stack. Interface, not reimplement.
//
// Output is a small, stable, well-documented JSON (blocks as vertex/face lists,
// frames as point + x-axis + y-axis, interfaces as contact polygons) plus a ~15-
// line COMPAS-side Python loader (LoaderPy). We deliberately emit a NEUTRAL
// schema + loader rather than COMPAS's version-specific internal serialization,
// so it is robust across compas 1.x / 2.x and verifiable without a Python env.
//
// Pure managed string + file IO (Rhino value types only), headless-testable.
// =============================================================================

public static class CompasExporter
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    /// <summary>Build the bridge JSON from blocks (meshes) + optional ids, placement frames, and contact interfaces.</summary>
    public static string BuildJson(
        IReadOnlyList<Mesh> blocks,
        IReadOnlyList<string> ids,
        IReadOnlyList<Plane> frames,
        IReadOnlyList<int[]> interfacePairs,
        IReadOnlyList<Polyline> interfacePolys,
        string units,
        out int blockCount, out int frameCount, out int ifaceCount)
    {
        blockCount = frameCount = ifaceCount = 0;
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"schema\": \"frahan-compas-bridge/1\",\n");
        sb.Append("  \"units\": \"").Append(Esc(string.IsNullOrEmpty(units) ? "m" : units)).Append("\",\n");

        // ---- blocks ----
        sb.Append("  \"blocks\": [");
        if (blocks != null)
        {
            bool first = true;
            for (int i = 0; i < blocks.Count; i++)
            {
                var m = blocks[i];
                if (m == null || m.Vertices.Count < 3 || m.Faces.Count < 1) continue;
                if (!first) sb.Append(",");
                first = false;
                string id = (ids != null && ids.Count > i && !string.IsNullOrWhiteSpace(ids[i]))
                    ? ids[i] : "block_" + (i + 1).ToString("D3", CI);
                sb.Append("\n    {\"id\": \"").Append(Esc(id)).Append("\", \"vertices\": [");
                for (int v = 0; v < m.Vertices.Count; v++)
                {
                    var p = m.Vertices[v];
                    if (v > 0) sb.Append(", ");
                    sb.Append("[").Append(F(p.X)).Append(", ").Append(F(p.Y)).Append(", ").Append(F(p.Z)).Append("]");
                }
                sb.Append("], \"faces\": [");
                for (int fi = 0; fi < m.Faces.Count; fi++)
                {
                    var mf = m.Faces[fi];
                    if (fi > 0) sb.Append(", ");
                    if (mf.IsQuad) sb.Append("[").Append(mf.A).Append(", ").Append(mf.B).Append(", ").Append(mf.C).Append(", ").Append(mf.D).Append("]");
                    else sb.Append("[").Append(mf.A).Append(", ").Append(mf.B).Append(", ").Append(mf.C).Append("]");
                }
                sb.Append("]}");
                blockCount++;
            }
        }
        sb.Append("\n  ],\n");

        // ---- frames (compas Frame: point + xaxis + yaxis) ----
        sb.Append("  \"frames\": [");
        if (frames != null)
        {
            bool first = true;
            foreach (var pl in frames)
            {
                if (!pl.IsValid) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append("\n    {\"point\": [").Append(F(pl.OriginX)).Append(", ").Append(F(pl.OriginY)).Append(", ").Append(F(pl.OriginZ))
                  .Append("], \"xaxis\": [").Append(F(pl.XAxis.X)).Append(", ").Append(F(pl.XAxis.Y)).Append(", ").Append(F(pl.XAxis.Z))
                  .Append("], \"yaxis\": [").Append(F(pl.YAxis.X)).Append(", ").Append(F(pl.YAxis.Y)).Append(", ").Append(F(pl.YAxis.Z)).Append("]}");
                frameCount++;
            }
        }
        sb.Append("\n  ],\n");

        // ---- interfaces (contact polygons between block pairs) ----
        sb.Append("  \"interfaces\": [");
        if (interfacePairs != null && interfacePolys != null)
        {
            bool first = true;
            int n = Math.Min(interfacePairs.Count, interfacePolys.Count);
            for (int i = 0; i < n; i++)
            {
                var pair = interfacePairs[i]; var poly = interfacePolys[i];
                if (pair == null || pair.Length < 2 || poly == null || poly.Count < 3) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append("\n    {\"a\": ").Append(pair[0]).Append(", \"b\": ").Append(pair[1]).Append(", \"points\": [");
                for (int v = 0; v < poly.Count; v++)
                { if (v > 0) sb.Append(", "); sb.Append("[").Append(F(poly[v].X)).Append(", ").Append(F(poly[v].Y)).Append(", ").Append(F(poly[v].Z)).Append("]"); }
                sb.Append("]}");
                ifaceCount++;
            }
        }
        sb.Append("\n  ]\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>Write the bridge JSON (and, if requested, the companion Python loader) to disk.</summary>
    public static bool Write(
        string path,
        IReadOnlyList<Mesh> blocks, IReadOnlyList<string> ids, IReadOnlyList<Plane> frames,
        IReadOnlyList<int[]> interfacePairs, IReadOnlyList<Polyline> interfacePolys,
        string units, bool writeLoader, out string report)
    {
        report = "";
        if (string.IsNullOrWhiteSpace(path)) { report = "No path."; return false; }
        string json = BuildJson(blocks, ids, frames, interfacePairs, interfacePolys, units,
            out int nb, out int nf, out int ni);
        try
        {
            File.WriteAllText(path, json);
            if (writeLoader)
            {
                string dir = Path.GetDirectoryName(path) ?? ".";
                File.WriteAllText(Path.Combine(dir, "frahan_compas_loader.py"), LoaderPy());
            }
        }
        catch (Exception ex) { report = "Write failed: " + ex.Message; return false; }
        report = $"Wrote {nb} block(s), {nf} frame(s), {ni} interface(s) to {Path.GetFileName(path)}"
               + (writeLoader ? " (+ frahan_compas_loader.py)." : ".");
        return true;
    }

    /// <summary>The COMPAS-side Python loader that reconstructs compas objects from the bridge JSON.</summary>
    public static string LoaderPy()
    {
        return
@"# Frahan -> COMPAS bridge loader.  pip install compas   (optional: compas_assembly, compas_fab)
import json
from compas.geometry import Frame, Point, Vector
from compas.datastructures import Mesh


def load_frahan(path):
    """"""Return {frames, blocks (compas Mesh), interfaces} from a Frahan bridge JSON.""""""
    d = json.load(open(path))
    frames = [Frame(Point(*f['point']), Vector(*f['xaxis']), Vector(*f['yaxis']))
              for f in d.get('frames', [])]
    blocks = [Mesh.from_vertices_and_faces(b['vertices'], b['faces'])
              for b in d.get('blocks', [])]
    return {'frames': frames, 'blocks': blocks, 'interfaces': d.get('interfaces', [])}


def to_assembly(path):
    """"""Build a compas_assembly.Assembly (pip install compas_assembly).""""""
    from compas_assembly.datastructures import Assembly, Block
    d = json.load(open(path))
    a = Assembly()
    for b in d.get('blocks', []):
        a.add_block(Block.from_vertices_and_faces(b['vertices'], b['faces']))
    return a  # feed to compas_cra for the equilibrium solve


if __name__ == '__main__':
    import sys
    data = load_frahan(sys.argv[1])
    print(len(data['blocks']), 'blocks,', len(data['frames']), 'frames,', len(data['interfaces']), 'interfaces')
";
    }

    private static string F(double x) => x.ToString("0.######", CI);
    private static string Esc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == '"' || ch == '\\') { sb.Append('\\'); sb.Append(ch); }
            else if (ch < 32) sb.Append(' ');
            else sb.Append(ch);
        }
        return sb.ToString();
    }
}
