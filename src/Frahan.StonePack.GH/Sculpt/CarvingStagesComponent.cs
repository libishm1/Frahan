#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Frahan.Core.Sculpt;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Sculpt;

// =============================================================================
// CarvingStagesComponent — "Carving Stages" (digital pointing-machine roughing).
//
// RESTORED 2026-06-05 to the proven SYNCHRONOUS + CACHED + Run-gated design
// (commit 098d041, 2026-05-29). The 2026-05-30 "v2" rewrite (dfff18d) dropped the
// input cache and the real block-MESH clamp and reordered the inputs, which made
// every canvas edit recompute (a 2 M-vertex scan -> ~11 s per change = a frozen
// canvas) and broke the wiring of files saved before it. This version brings the
// good architecture back AND keeps v2's genuine Radial fold-fix (smoothed normals
// + per-vertex offset caps). Input order matches files saved under 098d041, so
// those canvases re-wire correctly.
//
// Roughing-pass shells from a rough block / flat top down to the finished
// sculpture. Modes:
//   0 Radial   — offset along (smoothed) surface normals; feature-aware.
//   1 Push-In  — offset along Front Direction.
//   2 Flat Top — push each vertex up to the flat bounding-box face along Front
//                Direction (clean flat top; no spikes; no Block; best for reliefs).
//   + a Block input (any mode): clamp each stage to the block via per-vertex rays
//     against an RTree of the block faces (works on an arbitrary scanned block,
//     not just an AABB).
//
// CACHED + Run-gated: the component RECOMPUTES only when its own inputs change (an
// input hash). On any other canvas solution — picking a List Item index, editing
// an unrelated component — it re-emits the CACHED stage meshes WITHOUT recomputing,
// so the canvas does not freeze. Run = false keeps showing the last result.
// Caching (not threading) is the fix: an always-on background re-solve would
// recompute every pass, which is exactly what we are avoiding.
//
// Preview is OFF (Hidden = true): the intermediate shells are picked downstream
// (List Item); redrawing all N dense shells every viewport refresh is what bogged
// the canvas / drove the display crash. Right-click -> Preview for a quick look.
// =============================================================================

[DesignApplication(
    "Staged roughing shells from a rough top down to a sculpture or relief",
    DesignFlow.TopDown,
    Precedent = "Quarra Two Horse Relief (Met) multi-pass machining; Borrowed Earth Wood Ridge contour sculpture",
    Tolerance = "final-stage Hausdorff <= 0.5 mm flat; <= 2 mm high-curvature; per-vertex offset <= 0.5x shortest edge",
    CardSet = "wiki/research/hitl_cards/td_carve_stages/")]
[Algorithm("Staged offset-shell roughing", "Frahan-original",
    Note = "pure per-vertex offset math, O(vertices x stages), cached + Run-gated; no published roughing-strategy paper implemented")]
public sealed class CarvingStagesComponent : FrahanComponentBase
{
    public CarvingStagesComponent()
        : base("Carving Stages", "CarveStages",
            "Roughing-pass shells from a rough block / flat top down to the "
            + "finished sculpture (digital pointing machine). Mode 0 Radial "
            + "(smoothed normals), 1 Push-In (Front Direction), 2 Flat Top (bbox "
            + "face; best for reliefs, no Block needed); a Block input clamps stages "
            + "to an arbitrary block mesh. CACHED + Run-gated: recomputes only when "
            + "its inputs change and re-emits the cached result otherwise, so editing "
            + "a List Item index or other components never re-runs it or freezes the "
            + "canvas. Synchronous; preview off (pick a stage downstream). "
            + "Frahan-original method.",
            "Frahan", "Sculpt")
    {
        Hidden = true;
    }

    public override Guid ComponentGuid => new Guid("F2D06A03-1A2B-4C3D-9E4F-5A6B7C8D9E03");
    protected override Bitmap Icon => IconProvider.Load("CncRoughing.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    // -- cache (re-emitted on solutions where inputs are unchanged) -------------
    private string _lastHash;
    private List<Mesh> _cachedMeshes;
    private List<double> _cachedOffsets;
    private List<double> _cachedWeight;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Target", "M", "Finished sculpture / relief mesh (the final surface).", GH_ParamAccess.item);
        p.AddIntegerParameter("Stages", "N", "Number of roughing passes (>= 1).", GH_ParamAccess.item, 4);
        p.AddNumberParameter("Max Offset", "Mx", "Free-offset modes (0/1, no Block): outward offset of the roughest shell.", GH_ParamAccess.item, 0.05);
        p[2].Optional = true;
        p.AddNumberParameter("Finish Allowance", "Fa", "Free-offset modes: offset left on the final pass (0 = exact surface).", GH_ParamAccess.item, 0.0);
        p[3].Optional = true;
        p.AddNumberParameter("Feature Boost", "Fb", "Free-offset modes: extra stock at the strongest protrusion (ears/noses), x the offset. 0 = uniform.", GH_ParamAccess.item, 1.0);
        p[4].Optional = true;
        p.AddIntegerParameter("Mode", "Md", "0 = Radial (smoothed normals); 1 = Push-In (along Front Direction); 2 = Flat Top (bbox face along Front Direction - best for reliefs, no Block needed).", GH_ParamAccess.item, 0);
        p.AddVectorParameter("Front Direction", "Fd", "Push-In / Flat-Top direction (e.g. +Z for a flat top); also the axis a Block sits along.", GH_ParamAccess.item, Vector3d.ZAxis);
        p.AddMeshParameter("Block", "B", "Raw stone block (optional). When given, stages are clamped to the block surface (roughest at the block, finish at the target).", GH_ParamAccess.item);
        p[7].Optional = true;
        p.AddBooleanParameter("Run", "R", "Compute (when inputs change). False = keep showing the cached result. Recompute only fires when an input actually changes.", GH_ParamAccess.item, true);
        p[8].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Stages", "S", "Roughing shells, roughest first -> finish last.", GH_ParamAccess.list);
        p.AddNumberParameter("Offsets", "O", "Per-stage offset distance (free modes) or reach fraction 1..0 (Block / Flat Top).", GH_ParamAccess.list);
        p.AddNumberParameter("Feature Weight", "Fw", "Per-vertex protrusion weight 0..1.", GH_ParamAccess.list);
        p.AddIntegerParameter("Count", "N", "Number of stage meshes produced.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Mesh target = null, block = null;
        int stages = 4, mode = 0; double maxOff = 0.05, finish = 0.0, boost = 1.0;
        Vector3d frontDir = Vector3d.ZAxis; bool run = true;
        da.GetData(0, ref target);
        da.GetData(1, ref stages); da.GetData(2, ref maxOff); da.GetData(3, ref finish);
        da.GetData(4, ref boost); da.GetData(5, ref mode); da.GetData(6, ref frontDir);
        da.GetData(7, ref block); da.GetData(8, ref run);

        string hash = BuildHash(target, block, stages, maxOff, finish, boost, mode, frontDir);
        bool inputsChanged = hash != _lastHash;

        // Recompute ONLY when Run and the inputs actually changed; otherwise reuse cache.
        if (run && (inputsChanged || _cachedMeshes == null))
        {
            if (target == null || !target.IsValid)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid or missing Target mesh."); return; }
            if (boost < 0) boost = 0;
            if (stages < 1) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Stages must be >= 1."); return; }
            if ((mode == 1 || mode == 2) && (!frontDir.IsValid || frontDir.IsZero))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Push-In / Flat-Top mode needs a non-zero Front Direction."); return; }

            try { ComputeStages(target, block, stages, maxOff, finish, boost, mode, frontDir); _lastHash = hash; }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }
            Message = "computed";
        }
        else if (!run)
        {
            Message = _cachedMeshes != null ? "cached (Run off)" : "set Run = true";
        }
        else
        {
            Message = "cached"; // inputs unchanged -> no recompute (no freeze on downstream edits)
        }

        if (_cachedMeshes != null)
        {
            da.SetDataList(0, _cachedMeshes);
            da.SetDataList(1, _cachedOffsets);
            da.SetDataList(2, _cachedWeight);
            da.SetData(3, _cachedMeshes.Count);
        }
    }

    // Build all stage meshes and store them in the cache fields.
    private void ComputeStages(Mesh target, Mesh block, int stages, double maxOff, double finish,
        double boost, int mode, Vector3d frontDir)
    {
        var work = target.DuplicateMesh();
        work.Faces.ConvertQuadsToTriangles();
        work.Normals.ComputeNormals();
        int nv = work.Vertices.Count;
        double[] weight = ProtrusionWeights(work);
        // v2 fold-fix: Laplacian-smoothed normals + per-vertex offset cap (half the
        // shortest edge). Together they stop Radial / Push-In offsets folding onto
        // themselves at sharp edges / thin-mesh rims (spikes on reliefs).
        Vector3d[] smoothN = SmoothNormals(work, 2);
        double[] cap = LocalOffsetCaps(work, 0.5);
        bool hasBlock = block != null && block.IsValid && block.Faces.Count > 0;

        var meshes = new List<Mesh>(stages);
        var offsets = new List<double>(stages);

        if (hasBlock || mode == 2)
        {
            Vector3d fd = frontDir; fd.Unitize();
            var dir = new Vector3d[nv];
            var reach = new double[nv];
            if (mode == 2 && !hasBlock)
            {
                double maxProj = double.NegativeInfinity;
                for (int v = 0; v < nv; v++)
                { var p = work.Vertices[v]; double d = p.X * fd.X + p.Y * fd.Y + p.Z * fd.Z; if (d > maxProj) maxProj = d; }
                for (int v = 0; v < nv; v++)
                { var p = work.Vertices[v]; dir[v] = fd; reach[v] = Math.Max(0.0, maxProj - (p.X * fd.X + p.Y * fd.Y + p.Z * fd.Z)); }
            }
            else
            {
                // Block clamp via per-vertex ray. Build an RTree of the block faces
                // ONCE so each ray only tests nearby faces -> fast on dense / scanned
                // blocks (brute-force MeshRay is O(faces) per ray and bogs down).
                var blk = block.DuplicateMesh();
                blk.Faces.ConvertQuadsToTriangles();
                var tree = RTree.CreateMeshFaceTree(blk);
                double maxLen = (blk.GetBoundingBox(true).Diagonal.Length
                                 + work.GetBoundingBox(true).Diagonal.Length) * 1.05;
                if (maxLen <= 0) maxLen = 1.0;
                for (int v = 0; v < nv; v++)
                {
                    // Radial uses the SMOOTHED normal (fold-fix); Push-In uses fd.
                    Vector3d d = mode == 1 ? fd : smoothN[v];
                    if (!d.Unitize()) { dir[v] = Vector3d.Zero; reach[v] = 0; continue; }
                    dir[v] = d;
                    reach[v] = RayBlockReach(tree, blk, (Point3d)work.Vertices[v], d, maxLen);
                }
            }
            for (int s = 0; s < stages; s++)
            {
                double frac = stages == 1 ? 0.0 : 1.0 - (double)s / (stages - 1);
                var shell = work.DuplicateMesh();
                for (int v = 0; v < nv; v++)
                {
                    double k = frac * reach[v];
                    Point3d p = (Point3d)work.Vertices[v] + dir[v] * k;
                    shell.Vertices.SetVertex(v, p.X, p.Y, p.Z);
                }
                shell.Normals.ComputeNormals(); shell.Compact();
                meshes.Add(shell); offsets.Add(frac);
            }
        }
        else
        {
            double[] schedule = CarvingStages.OffsetSchedule(stages, maxOff, finish);
            Vector3d fd = frontDir; fd.Unitize();
            foreach (double baseDist in schedule)
            {
                var shell = work.DuplicateMesh();
                for (int v = 0; v < nv; v++)
                {
                    double w = v < weight.Length ? weight[v] : 0.0;
                    Vector3d n = smoothN[v];          // fold-fix: smoothed normal
                    double localCap = cap[v];
                    if (mode == 1)
                    {
                        double front = n.X * fd.X + n.Y * fd.Y + n.Z * fd.Z; if (front < 0) front = 0;
                        double d = baseDist * front * (1.0 + boost * w);
                        if (d > localCap) d = localCap;   // fold-fix: cap
                        Point3d p = (Point3d)work.Vertices[v] + fd * d;
                        shell.Vertices.SetVertex(v, p.X, p.Y, p.Z);
                    }
                    else if (!n.IsZero)
                    {
                        double d = baseDist * (1.0 + boost * w);
                        if (d > localCap) d = localCap;   // fold-fix: cap
                        Point3d p = (Point3d)work.Vertices[v] + n * d;
                        shell.Vertices.SetVertex(v, p.X, p.Y, p.Z);
                    }
                }
                shell.Normals.ComputeNormals(); shell.Compact();
                meshes.Add(shell); offsets.Add(baseDist);
            }
        }

        _cachedMeshes = meshes;
        _cachedOffsets = offsets;
        _cachedWeight = new List<double>(weight);
    }

    // Cheap proxy hash of the inputs (counts + bbox + params) — changes only when
    // the inputs meaningfully change, so we don't recompute on unrelated edits.
    private static string BuildHash(Mesh target, Mesh block, int stages, double maxOff, double finish,
        double boost, int mode, Vector3d frontDir)
    {
        var inv = CultureInfo.InvariantCulture;
        string MeshSig(Mesh m)
        {
            if (m == null) return "none";
            var bb = m.GetBoundingBox(false);
            return string.Format(inv, "{0}/{1}/{2:F4},{3:F4},{4:F4}/{5:F4},{6:F4},{7:F4}",
                m.Vertices.Count, m.Faces.Count,
                bb.Min.X, bb.Min.Y, bb.Min.Z, bb.Max.X, bb.Max.Y, bb.Max.Z);
        }
        return string.Format(inv, "T[{0}]|B[{1}]|N{2}|Mx{3:R}|Fa{4:R}|Fb{5:R}|Md{6}|Fd{7:F4},{8:F4},{9:F4}",
            MeshSig(target), MeshSig(block), stages, maxOff, finish, boost, mode,
            frontDir.X, frontDir.Y, frontDir.Z);
    }

    // Nearest forward block-surface hit distance for a ray (origin, unit dir),
    // accelerated by the block's RTree face index. Returns 0 if nothing ahead.
    private static double RayBlockReach(RTree tree, Mesh blk, Point3d o, Vector3d d, double maxLen)
    {
        Point3d end = o + d * maxLen;
        var box = new BoundingBox(o, o); box.Union(end); box.Inflate(1e-6);
        var faces = blk.Faces;
        var verts = blk.Vertices;
        double best = double.PositiveInfinity;
        tree.Search(box, (s, e) =>
        {
            var f = faces[e.Id];
            if (RayTri(o, d, (Point3d)verts[f.A], (Point3d)verts[f.B], (Point3d)verts[f.C], out double t) && t < best)
                best = t;
        });
        return best < maxLen ? best : 0.0;
    }

    // Moller-Trumbore ray-triangle intersection; t = forward distance (dir unit).
    private static bool RayTri(Point3d o, Vector3d d, Point3d a, Point3d b, Point3d c, out double t)
    {
        t = 0.0;
        Vector3d e1 = b - a, e2 = c - a;
        Vector3d pv = Vector3d.CrossProduct(d, e2);
        double det = e1 * pv;
        if (Math.Abs(det) < 1e-12) return false;
        double inv = 1.0 / det;
        Vector3d tv = o - a;
        double u = (tv * pv) * inv;
        if (u < -1e-9 || u > 1.0 + 1e-9) return false;
        Vector3d qv = Vector3d.CrossProduct(tv, e1);
        double vv = (d * qv) * inv;
        if (vv < -1e-9 || u + vv > 1.0 + 1e-9) return false;
        t = (e2 * qv) * inv;
        return t > 1e-9;
    }

    private static double[] ProtrusionWeights(Mesh m)
    {
        int nv = m.Vertices.Count;
        var w = new double[nv];
        var tv = m.TopologyVertices;
        double max = 0.0;
        for (int t = 0; t < tv.Count; t++)
        {
            int[] nbrs = tv.ConnectedTopologyVertices(t);
            if (nbrs == null || nbrs.Length == 0) continue;
            Point3d p = tv[t];
            Point3d avg = Point3d.Origin;
            for (int k = 0; k < nbrs.Length; k++) avg += (Point3d)tv[nbrs[k]];
            avg /= nbrs.Length;
            int[] mvi = tv.MeshVertexIndices(t);
            Vector3d nrm = Vector3d.Zero;
            for (int k = 0; k < mvi.Length; k++) nrm += m.Normals[mvi[k]];
            if (!nrm.Unitize()) continue;
            double protr = (p - avg) * nrm;
            double val = protr > 0 ? protr : 0.0;
            if (val > max) max = val;
            for (int k = 0; k < mvi.Length; k++) w[mvi[k]] = val;
        }
        if (max > 1e-12) for (int i = 0; i < nv; i++) w[i] /= max;
        return w;
    }

    // Laplacian smoothing of vertex normals over topology neighbours (v2 fold-fix).
    private static Vector3d[] SmoothNormals(Mesh m, int iters)
    {
        int nv = m.Vertices.Count;
        var src = new Vector3d[nv];
        for (int i = 0; i < nv; i++) src[i] = m.Normals[i];
        if (iters <= 0) return src;
        var tv = m.TopologyVertices;
        var dst = new Vector3d[nv];
        for (int it = 0; it < iters; it++)
        {
            for (int t = 0; t < tv.Count; t++)
            {
                int[] mvi = tv.MeshVertexIndices(t);
                int[] nbrs = tv.ConnectedTopologyVertices(t);
                Vector3d sum = Vector3d.Zero; int cnt = 0;
                for (int j = 0; j < mvi.Length; j++) { sum += src[mvi[j]]; cnt++; }
                if (nbrs != null)
                {
                    for (int q = 0; q < nbrs.Length; q++)
                    {
                        int[] nmvi = tv.MeshVertexIndices(nbrs[q]);
                        for (int j = 0; j < nmvi.Length; j++) { sum += src[nmvi[j]]; cnt++; }
                    }
                }
                if (cnt > 0 && sum.Unitize())
                    for (int j = 0; j < mvi.Length; j++) dst[mvi[j]] = sum;
                else
                    for (int j = 0; j < mvi.Length; j++) dst[mvi[j]] = src[mvi[j]];
            }
            var tmp = src; src = dst; dst = tmp;
        }
        return src;
    }

    // Per-vertex offset cap = frac * shortest distance to a topology neighbour (v2
    // fold-fix). Clamping each offset to this stops a vertex crossing its neighbour.
    private static double[] LocalOffsetCaps(Mesh m, double frac)
    {
        int nv = m.Vertices.Count;
        var cap = new double[nv];
        for (int i = 0; i < nv; i++) cap[i] = double.PositiveInfinity;
        var tv = m.TopologyVertices;
        for (int t = 0; t < tv.Count; t++)
        {
            int[] nbrs = tv.ConnectedTopologyVertices(t);
            if (nbrs == null || nbrs.Length == 0) continue;
            Point3d p = tv[t];
            double minD2 = double.PositiveInfinity;
            for (int q = 0; q < nbrs.Length; q++)
            {
                Point3d pn = tv[nbrs[q]];
                double dx = pn.X - p.X, dy = pn.Y - p.Y, dz = pn.Z - p.Z;
                double d2 = dx * dx + dy * dy + dz * dz;
                if (d2 < minD2) minD2 = d2;
            }
            double localCap = frac * Math.Sqrt(minD2);
            int[] mvi = tv.MeshVertexIndices(t);
            for (int j = 0; j < mvi.Length; j++) if (localCap < cap[mvi[j]]) cap[mvi[j]] = localCap;
        }
        for (int i = 0; i < nv; i++) if (double.IsInfinity(cap[i])) cap[i] = 0.0;
        return cap;
    }
}
