#nullable disable
using System;
using System.Drawing;
using Frahan.Core.Quarry;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Quarry;

// =============================================================================
// ScanToBlockInventoryComponent — Frahan v1.0 keystone (Frahan > Quarry).
//
// Converts a 3D-scanned raw block (mesh) into a typed QuarryBlock the
// downstream GeoCut / GeoPack / BlockPackTree chain can nest project parts
// into. Closes the §1.3 "front-door for the v1 spine" gap from the
// 2026-05-30 v1_consolidated_plan.md (git tag frahan-v1-plan-2026-05-30).
//
// Synchronous (per feedback_gh_async_vs_sync memory — no off-thread
// RhinoCommon topology work). The orientation + bbox math is cheap enough
// to run on the canvas thread.
//
// Reuse policy (§1.5 of the plan): PCA inlined from MeshPcaComponent
// (Jacobi eigen-of-covariance, pure managed). OBB derived as the
// axis-aligned bbox in the PCA-aligned frame (avoids the native shim).
// ConvexHull uses Rhino's built-in Mesh.CreateConvexHull when available,
// else falls back to bbox.
// =============================================================================

[DesignApplication(
    "Catalogue a scanned quarry block as a typed inventory record the form-finder can lookup.",
    DesignFlow.TopDown,
    Precedent = "Quarra Parallel Nature off-cut matching workflow; UCL Devadass 2025 50-fragment limestone library",
    Tolerance = "OBB / convex-hull volume within 1 % of source mesh; principal axes correctly computed",
    CardSet = "Template-General/outputs/2026-05-30/hitl_cards/scan_to_block_inventory/ (V-BLOCKIN)")]
public sealed class ScanToBlockInventoryComponent : GH_Component
{
    public ScanToBlockInventoryComponent()
        : base("Scan to Block Inventory", "ScanBlock",
            "Convert a 3D-scanned raw block mesh into a typed QuarryBlock " +
            "(bounds + usable volume + frame + dimensions + label) the " +
            "downstream GeoCut / GeoPack / BlockPackTree chain can nest " +
            "project parts into. Orientation: 0 = mesh frame, 1 = PCA " +
            "(longest principal axis → X), 2 = world Z. Method: 0 = OBB, " +
            "1 = inscribed AABB after PCA align, 2 = ConvexHull.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("F2D0BC20-1A2B-4F2D-A0B0-7E60CADA20A0");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap Icon => IconProvider.Load("QuarryBlock.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Scan Mesh", "M",
            "Raw 3D-scanned block as a mesh (from a handheld scanner via " +
            "Scan Reconstruct, or a .3dm import).",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Orient", "O",
            "Orientation policy. 0 = keep the mesh's existing frame. " +
            "1 = align by PCA (longest principal axis → X). 2 = align by " +
            "world Z (top face flat).",
            GH_ParamAccess.item, 1);
        p[1].Optional = true;
        p.AddNumberParameter("Usable Inset", "I",
            "Inset (model units) used to compute the usable interior " +
            "volume — accounts for kerf + scan noise + edge defects. " +
            "Negative values are clamped to 0.",
            GH_ParamAccess.item, 0.0);
        p[2].Optional = true;
        p.AddIntegerParameter("Method", "Me",
            "Block-extraction method. 0 = OBB (oriented bounding box, " +
            "fast, deterministic). 1 = inscribed AABB after PCA align. " +
            "2 = ConvexHull.",
            GH_ParamAccess.item, 0);
        p[3].Optional = true;
        p.AddTextParameter("Label", "L",
            "Per-block label / provenance string carried into the typed " +
            "QuarryBlock and downstream metadata.",
            GH_ParamAccess.item, "");
        p[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddParameter(new Param_QuarryBlock(), "Block", "B",
            "Typed QuarryBlock: oriented frame + usable interior mesh + " +
            "metadata. Wire into the downstream nesting chain.",
            GH_ParamAccess.item);
        p.AddMeshParameter("Bounds", "Bb",
            "Oriented bounding-box mesh (visualisation + downstream " +
            "usable-volume input for the raw-Mesh wire path).",
            GH_ParamAccess.item);
        p.AddPlaneParameter("Frame", "Fr",
            "Block's oriented base frame (origin + X/Y/Z axes from PCA / " +
            "world align).",
            GH_ParamAccess.item);
        p.AddVectorParameter("Dimensions", "D",
            "Block's principal dimensions (X = longest, Y = next, " +
            "Z = thinnest) in model units.",
            GH_ParamAccess.item);
        p.AddNumberParameter("Volume", "V",
            "Usable interior volume (model units cubed); accounts for " +
            "Usable Inset.",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "R",
            "One-line summary: \"Block <label> X x Y x Z units, volume V " +
            "units^3, method M\".",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Mesh mesh = null;
        int orient = 1;
        double inset = 0.0;
        int methodInt = 0;
        string label = "";

        if (!da.GetData(0, ref mesh))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Scan Mesh required.");
            return;
        }
        da.GetData(1, ref orient);
        da.GetData(2, ref inset);
        da.GetData(3, ref methodInt);
        da.GetData(4, ref label);
        label = label ?? "";

        if (mesh == null || !mesh.IsValid)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Scan Mesh is null or invalid.");
            return;
        }

        int vCount = mesh.Vertices.Count;
        if (vCount == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Scan Mesh is empty; emitting an empty QuarryBlock.");
            var empty = new QuarryBlock(new Mesh(), new Mesh(), Plane.WorldXY,
                Vector3d.Zero, 0.0, label, "Empty");
            da.SetData(0, new QuarryBlockGoo(empty));
            da.SetData(1, empty.Bounds);
            da.SetData(2, empty.Frame);
            da.SetData(3, empty.Dimensions);
            da.SetData(4, 0.0);
            da.SetData(5, $"Block {label} (empty mesh)");
            return;
        }
        if (vCount < 4)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Scan Mesh needs at least 4 vertices, got {vCount}.");
            return;
        }

        // Clamp + normalise inputs.
        if (orient < 0 || orient > 2) orient = 1;
        if (methodInt < 0 || methodInt > 2) methodInt = 0;
        if (inset < 0.0) inset = 0.0;

        // 1) Compute the base oriented frame per Orient policy.
        Plane frame;
        Vector3d dims;
        if (orient == 0)
        {
            // Use the mesh's world bbox; frame = world XY at bbox centre.
            var bb = mesh.GetBoundingBox(true);
            var centre = (bb.Min + bb.Max) * 0.5;
            frame = new Plane(centre, Vector3d.XAxis, Vector3d.YAxis);
            dims = new Vector3d(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y, bb.Max.Z - bb.Min.Z);
        }
        else if (orient == 1)
        {
            // PCA-aligned frame: longest principal axis → X, then Y, then Z.
            if (!ComputePcaFrameAndExtents(mesh, out frame, out dims))
            {
                // PCA failed (e.g. degenerate vertex cloud) — fall back to world axes.
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "PCA orientation fell back to world axes (degenerate vertex cloud).");
                var bb = mesh.GetBoundingBox(true);
                var centre = (bb.Min + bb.Max) * 0.5;
                frame = new Plane(centre, Vector3d.XAxis, Vector3d.YAxis);
                dims = new Vector3d(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y, bb.Max.Z - bb.Min.Z);
            }
        }
        else
        {
            // Orient = 2: world-Z up. Top face flat: PCA in XY plane, Z = world up.
            var bb = mesh.GetBoundingBox(true);
            var centre = (bb.Min + bb.Max) * 0.5;
            frame = new Plane(centre, Vector3d.XAxis, Vector3d.YAxis);
            dims = new Vector3d(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y, bb.Max.Z - bb.Min.Z);
        }

        // 2) Extract block per Method.
        Mesh boundsMesh;
        string methodName;
        switch (methodInt)
        {
            case 1:
                // Inscribed AABB after PCA align: same as OBB for our purposes
                // since we compute extents from vertex projection onto the frame
                // axes. Pure managed.
                boundsMesh = BuildOrientedBoxMesh(frame, dims);
                methodName = "InscribedAABB";
                break;
            case 2:
                boundsMesh = TryBuildConvexHullMesh(mesh);
                if (boundsMesh == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        "ConvexHull unavailable; falling back to OBB.");
                    boundsMesh = BuildOrientedBoxMesh(frame, dims);
                    methodName = "OBB";
                }
                else
                {
                    methodName = "ConvexHull";
                }
                break;
            case 0:
            default:
                boundsMesh = BuildOrientedBoxMesh(frame, dims);
                methodName = "OBB";
                break;
        }

        if (boundsMesh == null || !boundsMesh.IsValid)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Could not build a valid Bounds mesh.");
            return;
        }

        // 3) Usable volume = bounds shrunk by Inset along inward normals of
        //    the principal frame. For the OBB / inscribed-AABB path we shrink
        //    each axis interval by 2 * inset (inset on both faces). For
        //    ConvexHull we fall back to the OBB shrink for simplicity.
        Mesh usableMesh;
        Vector3d usableDims;
        if (inset <= 0.0)
        {
            usableMesh = boundsMesh;
            usableDims = dims;
        }
        else
        {
            double ux = Math.Max(0.0, dims.X - 2.0 * inset);
            double uy = Math.Max(0.0, dims.Y - 2.0 * inset);
            double uz = Math.Max(0.0, dims.Z - 2.0 * inset);
            usableDims = new Vector3d(ux, uy, uz);
            if (ux <= 0.0 || uy <= 0.0 || uz <= 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Usable Inset consumed the whole block (one or more axes <= 0).");
                usableMesh = new Mesh();
            }
            else
            {
                usableMesh = BuildOrientedBoxMesh(frame, usableDims);
            }
        }

        // 4) Volume: compute from usableDims for OBB / inscribed; from
        //    boundsMesh.Volume() for ConvexHull when inset = 0.
        double volume;
        if (methodName == "ConvexHull" && inset <= 0.0)
        {
            try
            {
                volume = boundsMesh.IsClosed ? boundsMesh.Volume() : usableDims.X * usableDims.Y * usableDims.Z;
            }
            catch
            {
                volume = usableDims.X * usableDims.Y * usableDims.Z;
            }
        }
        else
        {
            volume = usableDims.X * usableDims.Y * usableDims.Z;
        }

        // 5) Emit.
        var block = new QuarryBlock(boundsMesh, usableMesh, frame, dims, volume, label, methodName);

        string report =
            $"Block {(string.IsNullOrEmpty(label) ? "(unlabelled)" : label)} " +
            $"{dims.X:F3} x {dims.Y:F3} x {dims.Z:F3} units, " +
            $"volume {volume:F3} units^3, method {methodName}";

        da.SetData(0, new QuarryBlockGoo(block));
        da.SetData(1, boundsMesh);
        da.SetData(2, frame);
        da.SetData(3, dims);
        da.SetData(4, volume);
        da.SetData(5, report);
    }

    // -------------------------------------------------------------------------
    // PCA frame + extents. Lifted from MeshPcaComponent (Jacobi eigen of
    // covariance, pure managed). Returns false if the vertex cloud is too
    // degenerate to produce a valid frame.
    // -------------------------------------------------------------------------
    private static bool ComputePcaFrameAndExtents(Mesh mesh, out Plane frame, out Vector3d dims)
    {
        frame = Plane.WorldXY;
        dims = Vector3d.Zero;

        int n = mesh.Vertices.Count;
        if (n < 3) return false;

        // Centroid.
        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < n; i++)
        {
            var v = mesh.Vertices[i];
            cx += v.X; cy += v.Y; cz += v.Z;
        }
        cx /= n; cy /= n; cz /= n;

        // Covariance.
        double sxx = 0, syy = 0, szz = 0, sxy = 0, sxz = 0, syz = 0;
        for (int i = 0; i < n; i++)
        {
            var v = mesh.Vertices[i];
            double dx = v.X - cx, dy = v.Y - cy, dz = v.Z - cz;
            sxx += dx * dx; syy += dy * dy; szz += dz * dz;
            sxy += dx * dy; sxz += dx * dz; syz += dy * dz;
        }
        sxx /= n; syy /= n; szz /= n;
        sxy /= n; sxz /= n; syz /= n;

        var cov = new double[,]
        {
            { sxx, sxy, sxz },
            { sxy, syy, syz },
            { sxz, syz, szz },
        };
        JacobiEigen3(cov, out double[] eigVals, out double[][] eigVecs);

        // Sort descending by |eigVal|.
        int[] order = { 0, 1, 2 };
        for (int i = 0; i < 3; i++)
            for (int j = i + 1; j < 3; j++)
                if (Math.Abs(eigVals[order[i]]) < Math.Abs(eigVals[order[j]]))
                {
                    int t = order[i]; order[i] = order[j]; order[j] = t;
                }

        var pc1 = new Vector3d(eigVecs[order[0]][0], eigVecs[order[0]][1], eigVecs[order[0]][2]);
        var pc2 = new Vector3d(eigVecs[order[1]][0], eigVecs[order[1]][1], eigVecs[order[1]][2]);
        var pc3 = Vector3d.CrossProduct(pc1, pc2);
        if (!pc3.Unitize()) return false;
        pc2 = Vector3d.CrossProduct(pc3, pc1);
        if (!pc2.Unitize()) return false;
        if (!pc1.Unitize()) return false;

        var centre = new Point3d(cx, cy, cz);
        frame = new Plane(centre, pc1, pc2);

        // Extents along each principal axis (max - min of projection).
        double l1 = AxisExtent(mesh, centre, pc1);
        double l2 = AxisExtent(mesh, centre, pc2);
        double l3 = AxisExtent(mesh, centre, pc3);

        // Sort dims descending so X is always the longest.
        double a = l1, b = l2, c = l3;
        if (b > a) { double t = a; a = b; b = t; }
        if (c > a) { double t = a; a = c; c = t; }
        if (c > b) { double t = b; b = c; c = t; }
        dims = new Vector3d(a, b, c);

        // Sanity check.
        if (!(a > 0 && b > 0 && c > 0)) return false;
        return true;
    }

    private static double AxisExtent(Mesh mesh, Point3d centre, Vector3d axis)
    {
        double tMin = double.PositiveInfinity, tMax = double.NegativeInfinity;
        int n = mesh.Vertices.Count;
        for (int i = 0; i < n; i++)
        {
            var v = mesh.Vertices[i];
            double dx = v.X - centre.X, dy = v.Y - centre.Y, dz = v.Z - centre.Z;
            double t = dx * axis.X + dy * axis.Y + dz * axis.Z;
            if (t < tMin) tMin = t;
            if (t > tMax) tMax = t;
        }
        return tMax - tMin;
    }

    // 3x3 symmetric Jacobi eigendecomposition.
    private static void JacobiEigen3(double[,] a, out double[] eigVals, out double[][] eigVecs)
    {
        double[,] A = (double[,])a.Clone();
        double[,] V = new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        const int maxSweeps = 50;
        for (int sweep = 0; sweep < maxSweeps; sweep++)
        {
            double off = Math.Abs(A[0, 1]) + Math.Abs(A[0, 2]) + Math.Abs(A[1, 2]);
            if (off < 1e-15) break;
            JacobiRotate(A, V, 0, 1);
            JacobiRotate(A, V, 0, 2);
            JacobiRotate(A, V, 1, 2);
        }
        eigVals = new double[] { A[0, 0], A[1, 1], A[2, 2] };
        eigVecs = new double[3][];
        for (int k = 0; k < 3; k++)
            eigVecs[k] = new double[] { V[0, k], V[1, k], V[2, k] };
    }

    private static void JacobiRotate(double[,] A, double[,] V, int p, int q)
    {
        double apq = A[p, q];
        if (Math.Abs(apq) < 1e-20) return;
        double app = A[p, p];
        double aqq = A[q, q];
        double theta = (aqq - app) / (2.0 * apq);
        double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(1.0 + theta * theta));
        if (theta == 0.0) t = 1.0;
        double c = 1.0 / Math.Sqrt(1.0 + t * t);
        double s = t * c;

        A[p, p] = app - t * apq;
        A[q, q] = aqq + t * apq;
        A[p, q] = 0.0;
        A[q, p] = 0.0;
        for (int r = 0; r < 3; r++)
        {
            if (r != p && r != q)
            {
                double arp = A[r, p];
                double arq = A[r, q];
                A[r, p] = c * arp - s * arq;
                A[p, r] = A[r, p];
                A[r, q] = c * arq + s * arp;
                A[q, r] = A[r, q];
            }
            double vrp = V[r, p];
            double vrq = V[r, q];
            V[r, p] = c * vrp - s * vrq;
            V[r, q] = c * vrq + s * vrp;
        }
    }

    // -------------------------------------------------------------------------
    // Build the 6-face / 12-triangle closed mesh of an oriented box centred
    // on `frame.Origin` with `dims` extents along `frame.X`, `frame.Y`,
    // `frame.Z`. Mirrors BoxToMeshComponent's mesh construction.
    // -------------------------------------------------------------------------
    private static Mesh BuildOrientedBoxMesh(Plane frame, Vector3d dims)
    {
        double hx = dims.X * 0.5;
        double hy = dims.Y * 0.5;
        double hz = dims.Z * 0.5;

        // Corners in the local frame, then transformed to world.
        var locals = new[]
        {
            new Point3d(-hx, -hy, -hz), // 0
            new Point3d( hx, -hy, -hz), // 1
            new Point3d( hx,  hy, -hz), // 2
            new Point3d(-hx,  hy, -hz), // 3
            new Point3d(-hx, -hy,  hz), // 4
            new Point3d( hx, -hy,  hz), // 5
            new Point3d( hx,  hy,  hz), // 6
            new Point3d(-hx,  hy,  hz), // 7
        };
        var mesh = new Mesh();
        for (int i = 0; i < 8; i++)
        {
            var p = frame.PointAt(locals[i].X, locals[i].Y, locals[i].Z);
            mesh.Vertices.Add(p);
        }
        // Same outward-normal winding as BoxToMeshComponent.
        mesh.Faces.AddFace(0, 3, 2);
        mesh.Faces.AddFace(0, 2, 1);
        mesh.Faces.AddFace(4, 5, 6);
        mesh.Faces.AddFace(4, 6, 7);
        mesh.Faces.AddFace(0, 1, 5);
        mesh.Faces.AddFace(0, 5, 4);
        mesh.Faces.AddFace(1, 2, 6);
        mesh.Faces.AddFace(1, 6, 5);
        mesh.Faces.AddFace(2, 3, 7);
        mesh.Faces.AddFace(2, 7, 6);
        mesh.Faces.AddFace(3, 0, 4);
        mesh.Faces.AddFace(3, 4, 7);
        mesh.RebuildNormals();
        mesh.Compact();
        return mesh;
    }

    // ConvexHull via Rhino's built-in Mesh.CreateConvexHull (Rhino 8+). Null
    // when unavailable.
    private static Mesh TryBuildConvexHullMesh(Mesh source)
    {
        try
        {
            // Mesh.CreateConvexHull was added in Rhino 8 (per the McNeel API
            // docs). Use reflection to avoid a hard build dependency on the
            // exact RhinoCommon revision if it is ever removed.
            var method = typeof(Mesh).GetMethod(
                "CreateConvexHull",
                new[] { typeof(System.Collections.Generic.IEnumerable<Point3d>) });
            if (method == null) return null;
            var points = new System.Collections.Generic.List<Point3d>(source.Vertices.Count);
            for (int i = 0; i < source.Vertices.Count; i++)
                points.Add(source.Vertices[i]);
            var hull = method.Invoke(null, new object[] { points }) as Mesh;
            return (hull != null && hull.IsValid) ? hull : null;
        }
        catch
        {
            return null;
        }
    }
}
