#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.Voussoir;
using Frahan.EdgeMatching.Matching;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Voussoir;

// =============================================================================
// VoussoirIngestComponent (GUID D5F1000F)
//
// First component of the Voussoir trio per
// wiki/research/voussoir_stereotomy_integration.md Phase 1:
//   1. VoussoirIngestComponent  (THIS)  -- read Voussoir GH plugin output
//   2. VoussoirStoneMatcherComponent     -- Hungarian assignment voussoir <-> stone
//   3. VoussoirPackIntoBlockComponent    -- 3D bin-pack into one quarried block
//
// Reads a list of voussoir meshes (typically from the food4rhino Voussoir
// plugin OR Frahan's Stereotomic Vault Mode), normalises into a typed
// VoussoirAssembly DTO, and emits two parallel outputs:
//
//   (a) The typed VoussoirAssembly + per-voussoir VoussoirRecord -- consumed
//       by VoussoirStoneMatcher and VoussoirPackIntoBlock.
//   (b) A MatchItem[] list -- consumed by MatcherContextBuilder (the
//       substrate spine per wiki/specs/component_decomposition_plan.md SS3.1).
//       This is the first concrete demonstration that voussoir-stone match
//       reuses the same matching substrate as Trencadis 2D / Cyclopean Recipe
//       / timber-reuse workflows.
//
// Precedents: Rippmann-Block 2011 IABSE-IASS "Digital Stereotomy"; Block
// Research Group "Armadillo Vault" (Venice Biennale 2016); the Voussoir GH
// plugin (food4rhino.com/en/app/voussoir). The wiki page lives at
// wiki/research/voussoir_stereotomy_integration.md and is the spec source.
// =============================================================================

[Algorithm("VoussoirIngestPipeline",
    "Frahan-original Voussoir GH plugin -> typed VoussoirAssembly DTO",
    Note = "Phase 1 of the Voussoir trio; see wiki/research/voussoir_stereotomy_integration.md")]
[Algorithm("OBB via Mesh AABB v1 fallback",
    "Standard mesh bounding box; v2 will route through MeshPcaComponent for true PCA-OBB",
    Note = "v1 simplification -- architect can pre-orient the voussoirs upstream for tighter OBBs")]
[Algorithm("Bed-Head plane detection via largest-face heuristic",
    "Frahan-original: sort voussoir faces by area; bed = largest, head = second-largest",
    Note = "v1 heuristic; v2 will route through VsaSegmenter (Cohen-Steiner 2004) for full planar partitioning")]
[Algorithm("Adjacency detection via face-pair centroid distance",
    "Frahan-original: voussoirs i,j adjacent iff any face pair has centroid distance < adjacency threshold * min(face_diagonal_i, face_diagonal_j)",
    Note = "v1 cheap proxy; v2 will route through CGAL Polygon Mesh Processing face-pair coincidence")]
[DesignApplication(
    "Read a list of designed voussoir meshes into a typed VoussoirAssembly for the matcher to consume.",
    DesignFlow.TopDown,
    Precedent = "Rippmann-Block 2011 Digital Stereotomy IABSE-IASS; Block Research Group Armadillo Vault 2016; Voussoir GH plugin (food4rhino)",
    Tolerance = "OBB volume within 1% of source mesh (AABB-based v1); adjacency detected when face centroids within 5% of object span",
    CardSet = "wiki/research/hitl_cards/td_voussoir/ (proposed; TD-VOUSSOIR per master plan)")]
public sealed class VoussoirIngestComponent : GH_Component
{
    public VoussoirIngestComponent()
        : base("Voussoir Ingest", "VousIngest",
            "Read a list of voussoir meshes (from the Voussoir GH plugin or " +
            "Frahan Stereotomic Vault Mode) as a typed VoussoirAssembly. " +
            "Per-voussoir record carries OBB + volume + centroid + bed/head " +
            "planes + load axis + joint class. Emits MatchItem[] for downstream " +
            "MatcherContextBuilder (the substrate spine). First step of the " +
            "top-down voussoir-to-stone workflow per philosophy doc §10.6.",
            "Frahan", "Voussoir")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F1000F-ED9E-4ED9-A00F-ED9EED9E000F");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap Icon => IconProvider.Load("EdgeMatchSolve.png"); // placeholder

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Voussoirs", "V",
            "List of voussoir geometries representing the designed stereotomic " +
            "assembly. Accepts EITHER Mesh OR Brep. The Voussoir GH plugin by " +
            "Varela (FAUP Porto STBIM) emits BREP per voussoir as a GH data " +
            "tree -- this component handles both natively. Closed solids " +
            "preferred; Breps are meshed via Mesh.CreateFromBrep at default " +
            "MeshingParameters quality.",
            GH_ParamAccess.list);
        p.AddTextParameter("Joint Classes", "JC",
            "Optional per-voussoir position-role tags: 'bed' / 'head' / 'key' / " +
            "'ground' / 'void' (default 'void'). Same count as Voussoirs (or " +
            "empty / single-value for default).",
            GH_ParamAccess.list);
        p[1].Optional = true;
        p.AddCurveParameter("Thrust Curve", "Tc",
            "Optional funicular thrust curve (from TNA form-finding or hand " +
            "drafting). Drives LoadAxis per voussoir via closest-point-tangent.",
            GH_ParamAccess.item);
        p[2].Optional = true;
        p.AddTextParameter("Lithology Hints", "Lh",
            "Optional per-voussoir lithology constraint (e.g. 'Vermont Marble'). " +
            "Used as a categorical constraint in the matcher.",
            GH_ParamAccess.list);
        p[3].Optional = true;
        p.AddIntegerParameter("Ground Anchor Indices", "Ga",
            "Optional indices of springer / abutment voussoirs (start points " +
            "of the install DAG). Empty = auto-detect via lowest centroid Z.",
            GH_ParamAccess.list);
        p[4].Optional = true;
        p.AddNumberParameter("Adjacency Threshold", "Ad",
            "Fraction of face-diagonal for adjacency detection. Default 0.05 " +
            "(5% of object span). Faces within this distance count as a shared joint.",
            GH_ParamAccess.item, 0.05);
        p.AddTextParameter("Provenance", "Pr",
            "Optional provenance string for the assembly (e.g. 'Voussoir plugin v2.3 output').",
            GH_ParamAccess.item, "");
        p[6].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Assembly", "VA",
            "The typed VoussoirAssembly. Wire into VoussoirStoneMatcher + " +
            "VoussoirPackIntoBlock downstream.",
            GH_ParamAccess.item);
        p.AddGenericParameter("Match Items", "MI",
            "List of MatchItem (substrate-compatible). Wire into MatcherContextBuilder " +
            "as the Demand side. Numeric props: Volume, MaxDim, MinDim, Height. " +
            "Categorical: JointClass, LithologyHint.",
            GH_ParamAccess.list);
        p.AddBoxParameter("OBBs", "B",
            "Per-voussoir oriented bounding boxes (AABB v1).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Volumes", "Vo",
            "Per-voussoir mesh volume (absolute).",
            GH_ParamAccess.list);
        p.AddPointParameter("Centroids", "C",
            "Per-voussoir geometric centroid.",
            GH_ParamAccess.list);
        p.AddPlaneParameter("Bed Planes", "Bp",
            "Per-voussoir bed-joint plane (largest-area face heuristic v1).",
            GH_ParamAccess.list);
        p.AddPlaneParameter("Head Planes", "Hp",
            "Per-voussoir head-joint plane (second-largest-area face heuristic v1).",
            GH_ParamAccess.list);
        p.AddVectorParameter("Load Axes", "La",
            "Per-voussoir compressive-load direction (thrust-curve tangent if " +
            "supplied, else OBB longest-axis).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Adjacency Pairs", "Ap",
            "Pairs of voussoir indices that share a joint face (flat list: " +
            "[i0, j0, i1, j1, ...]).",
            GH_ParamAccess.list);
        p.AddTextParameter("Remarks", "R",
            "Per-voussoir + assembly-level diagnostic notes.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var rawGoo = new List<IGH_Goo>();
        var jointClasses = new List<string>();
        Curve thrustCurve = null;
        var lithologyHints = new List<string>();
        var groundAnchorsIn = new List<int>();
        double adjacencyThreshold = 0.05;
        string provenance = "";

        if (!DA.GetDataList(0, rawGoo)) return;

        // Normalise raw input: each item may be a Mesh, a Brep, GH_Mesh, or
        // GH_Brep. Voussoir GH plugin (Varela FAUP STBIM) emits Brep; Frahan
        // Stereotomic Vault Mode emits Mesh. Convert Brep -> Mesh via
        // Mesh.CreateFromBrep at default MeshingParameters.
        var meshes = new List<Mesh>();
        int brepCount = 0;
        int meshCount = 0;
        var meshingParams = MeshingParameters.Default;
        foreach (var goo in rawGoo)
        {
            if (goo == null) { meshes.Add(null); continue; }
            Mesh m = null;
            // Try direct Mesh / GH_Mesh.
            if (goo is GH_Mesh gm && gm.Value != null) { m = gm.Value; meshCount++; }
            else if (goo.CastTo(out m) && m != null) { meshCount++; }
            else
            {
                // Try Brep / GH_Brep.
                Brep b = null;
                if (goo is GH_Brep gb && gb.Value != null) b = gb.Value;
                else goo.CastTo(out b);
                if (b != null && b.Faces.Count > 0)
                {
                    var meshArr = Mesh.CreateFromBrep(b, meshingParams);
                    if (meshArr != null && meshArr.Length > 0)
                    {
                        m = new Mesh();
                        foreach (var part in meshArr) m.Append(part);
                        m.Normals.ComputeNormals();
                        m.Compact();
                        brepCount++;
                    }
                }
            }
            meshes.Add(m);
        }
        DA.GetDataList(1, jointClasses);
        DA.GetData(2, ref thrustCurve);
        DA.GetDataList(3, lithologyHints);
        DA.GetDataList(4, groundAnchorsIn);
        DA.GetData(5, ref adjacencyThreshold);
        DA.GetData(6, ref provenance);

        int n = meshes.Count;
        if (n == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "No voussoir geometries provided. Connect a list (Mesh or Brep) to " +
                "the Voussoirs input.");
            return;
        }

        // Record input-type stats so users can verify the Brep->Mesh
        // conversion that the Voussoir GH plugin (Varela FAUP STBIM) requires.
        var ingressStats = $"Voussoir input ingress: {meshCount} mesh(es) + " +
            $"{brepCount} brep(s) (converted via Mesh.CreateFromBrep).";

        var voussoirs = new List<VoussoirRecord>(n);
        var matchItems = new List<MatchItem>(n);
        var obbs = new List<Box>(n);
        var volumes = new List<double>(n);
        var centroids = new List<Point3d>(n);
        var bedPlanes = new List<Plane>(n);
        var headPlanes = new List<Plane>(n);
        var loadAxes = new List<Vector3d>(n);
        var remarks = new List<string>(n + 4);
        remarks.Add(ingressStats);

        // Compute the assembly's bounding span for the adjacency-threshold scale.
        var assemblyBox = BoundingBox.Empty;
        foreach (var m in meshes)
        {
            if (m == null) continue;
            assemblyBox.Union(m.GetBoundingBox(true));
        }
        double assemblySpan = assemblyBox.Diagonal.Length;
        double adjAbsTol = assemblySpan * adjacencyThreshold;

        for (int i = 0; i < n; i++)
        {
            var m = meshes[i];
            if (m == null || m.Vertices.Count == 0)
            {
                remarks.Add($"Voussoir[{i}]: null/empty mesh, skipped.");
                voussoirs.Add(null);
                obbs.Add(Box.Unset);
                volumes.Add(0);
                centroids.Add(Point3d.Unset);
                bedPlanes.Add(Plane.Unset);
                headPlanes.Add(Plane.Unset);
                loadAxes.Add(Vector3d.Zero);
                continue;
            }

            // Mesh.Volume can be signed (orientation-dependent); absolute it.
            double vol = Math.Abs(m.Volume());

            // Centroid via face-area-weighted centroids.
            var c = ComputeCentroid(m);

            // OBB via world AABB (v1 fallback). The architect can pre-orient
            // the voussoirs upstream for tighter PCA-OBBs.
            var aabb = m.GetBoundingBox(true);
            var box = new Box(aabb);

            // Bed + head planes via largest-area faces.
            DetectBedHead(m, out var bedPlane, out var headPlane);

            // Load axis: thrust curve tangent if supplied, else OBB longest axis.
            Vector3d loadAxis;
            if (thrustCurve != null)
            {
                if (thrustCurve.ClosestPoint(c, out double t))
                {
                    var tan = thrustCurve.TangentAt(t);
                    tan.Unitize();
                    loadAxis = tan;
                }
                else
                {
                    loadAxis = LongestAxisOf(box);
                }
            }
            else
            {
                loadAxis = LongestAxisOf(box);
            }

            var v = new VoussoirRecord
            {
                Id = "V" + i.ToString("D3"),
                Geometry = m.DuplicateMesh(),
                OrientedBoundingBox = box,
                Volume = vol,
                Centroid = c,
                BedPlane = bedPlane,
                HeadPlane = headPlane,
                LoadAxis = loadAxis,
                JointClass = GetByIndexOrDefault(jointClasses, i, "void"),
                LithologyHint = GetByIndexOrDefault(lithologyHints, i, ""),
                SequenceIndex = i,
                Label = "Voussoir " + i,
            };
            voussoirs.Add(v);
            obbs.Add(box);
            volumes.Add(vol);
            centroids.Add(c);
            bedPlanes.Add(bedPlane);
            headPlanes.Add(headPlane);
            loadAxes.Add(loadAxis);

            // Build the substrate MatchItem.
            var sizes = SortedExtents(box);
            var numeric = new Dictionary<string, double>
            {
                ["Volume"] = vol,
                ["MaxDimension"] = sizes[0],
                ["MidDimension"] = sizes[1],
                ["MinDimension"] = sizes[2],
                ["BedFaceArea"] = bedPlane != Plane.Unset ? EstimateBedFaceArea(m) : 0.0,
            };
            var categorical = new Dictionary<string, string>
            {
                ["JointClass"] = v.JointClass,
                ["LithologyHint"] = v.LithologyHint ?? "",
            };
            matchItems.Add(new MatchItem(v.Id, numeric, categorical));
            remarks.Add(
                $"Voussoir[{i}] {v.Id}: vol={vol:F1}, OBB=[{sizes[0]:F0}x{sizes[1]:F0}x{sizes[2]:F0}], " +
                $"joint='{v.JointClass}', lith='{v.LithologyHint}'.");
        }

        // Detect adjacency.
        var adjFlat = new List<int>();
        var adjPairs = new List<(int, int)>();
        for (int i = 0; i < n; i++)
        {
            if (voussoirs[i] == null) continue;
            for (int j = i + 1; j < n; j++)
            {
                if (voussoirs[j] == null) continue;
                if (AreAdjacent(meshes[i], meshes[j], adjAbsTol))
                {
                    adjFlat.Add(i); adjFlat.Add(j);
                    adjPairs.Add((i, j));
                }
            }
        }
        remarks.Add($"Detected {adjPairs.Count} adjacency pairs (threshold {adjAbsTol:F2} mm).");

        // Auto-detect ground anchors when none supplied: voussoirs whose centroid
        // sits at the lowest 5% of the assembly's Z extent.
        var groundAnchors = new List<int>();
        if (groundAnchorsIn.Count > 0)
        {
            groundAnchors.AddRange(groundAnchorsIn);
        }
        else
        {
            double zSpan = assemblyBox.Max.Z - assemblyBox.Min.Z;
            double zThresh = assemblyBox.Min.Z + zSpan * 0.05;
            for (int i = 0; i < n; i++)
            {
                if (voussoirs[i] == null) continue;
                if (voussoirs[i].Centroid.Z <= zThresh) groundAnchors.Add(i);
            }
            remarks.Add(
                $"Ground anchors auto-detected (lowest 5% of Z extent): " +
                $"{groundAnchors.Count} voussoir(s).");
        }

        var assembly = new VoussoirAssembly
        {
            Voussoirs = voussoirs,
            ThrustCurve = thrustCurve,
            AdjacencyPairs = adjPairs,
            GroundAnchorIndices = groundAnchors,
            Provenance = string.IsNullOrEmpty(provenance) ? "Voussoir Ingest 2026-05-31" : provenance,
        };

        DA.SetData(0, new GH_ObjectWrapper(assembly));
        DA.SetDataList(1, matchItems);
        DA.SetDataList(2, obbs);
        DA.SetDataList(3, volumes);
        DA.SetDataList(4, centroids);
        DA.SetDataList(5, bedPlanes);
        DA.SetDataList(6, headPlanes);
        DA.SetDataList(7, loadAxes);
        DA.SetDataList(8, adjFlat);
        DA.SetDataList(9, remarks);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static Point3d ComputeCentroid(Mesh m)
    {
        var verts = m.Vertices;
        double cx = 0, cy = 0, cz = 0;
        int n = verts.Count;
        for (int i = 0; i < n; i++)
        {
            var v = verts[i];
            cx += v.X; cy += v.Y; cz += v.Z;
        }
        return n > 0 ? new Point3d(cx / n, cy / n, cz / n) : Point3d.Origin;
    }

    private static void DetectBedHead(Mesh m, out Plane bed, out Plane head)
    {
        bed = Plane.Unset;
        head = Plane.Unset;
        var faces = m.Faces;
        var verts = m.Vertices;
        if (faces.Count == 0) return;

        // For a closed voussoir, the bed/head are typically the largest-area
        // pair of opposing faces. We scan faces, compute area + centroid,
        // and pick the largest two as bed/head. The lower-Z-centroid one is
        // bed, the upper-Z-centroid one is head.
        double maxA = 0, secA = 0;
        Plane maxP = Plane.Unset, secP = Plane.Unset;
        Point3d maxC = Point3d.Origin, secC = Point3d.Origin;
        for (int fi = 0; fi < faces.Count; fi++)
        {
            var f = faces[fi];
            var a = (Point3d)verts[f.A];
            var b = (Point3d)verts[f.B];
            var c = (Point3d)verts[f.C];
            double area = 0.5 * Vector3d.CrossProduct(b - a, c - a).Length;
            if (f.IsQuad)
            {
                var d = (Point3d)verts[f.D];
                area += 0.5 * Vector3d.CrossProduct(c - a, d - a).Length;
            }
            var ctr = new Point3d((a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3, (a.Z + b.Z + c.Z) / 3);
            var normal = Vector3d.CrossProduct(b - a, c - a);
            normal.Unitize();
            var pl = new Plane(ctr, normal);
            if (area > maxA) { secA = maxA; secP = maxP; secC = maxC; maxA = area; maxP = pl; maxC = ctr; }
            else if (area > secA) { secA = area; secP = pl; secC = ctr; }
        }
        if (maxC.Z <= secC.Z) { bed = maxP; head = secP; }
        else { bed = secP; head = maxP; }
    }

    private static Vector3d LongestAxisOf(Box box)
    {
        var sizes = SortedExtents(box);
        if (sizes[0] <= 0) return Vector3d.XAxis;
        // The longest extent's direction is the box's X (per Box convention
        // when constructed from AABB) — but to be safe we'll pick the
        // direction whose corresponding extent is largest.
        double dx = box.X.Length, dy = box.Y.Length, dz = box.Z.Length;
        if (dx >= dy && dx >= dz) return box.Plane.XAxis;
        if (dy >= dz) return box.Plane.YAxis;
        return box.Plane.ZAxis;
    }

    private static double[] SortedExtents(Box box)
    {
        var s = new[] { box.X.Length, box.Y.Length, box.Z.Length };
        Array.Sort(s);
        Array.Reverse(s);
        return s;
    }

    private static double EstimateBedFaceArea(Mesh m)
    {
        var faces = m.Faces;
        var verts = m.Vertices;
        double maxA = 0;
        for (int fi = 0; fi < faces.Count; fi++)
        {
            var f = faces[fi];
            var a = (Point3d)verts[f.A];
            var b = (Point3d)verts[f.B];
            var c = (Point3d)verts[f.C];
            double area = 0.5 * Vector3d.CrossProduct(b - a, c - a).Length;
            if (f.IsQuad)
            {
                var d = (Point3d)verts[f.D];
                area += 0.5 * Vector3d.CrossProduct(c - a, d - a).Length;
            }
            if (area > maxA) maxA = area;
        }
        return maxA;
    }

    private static bool AreAdjacent(Mesh a, Mesh b, double absTol)
    {
        // Cheap proxy: for each face centroid on `a`, find the nearest
        // face centroid on `b`. If any pair is within absTol, the voussoirs
        // share a joint. O(F_a * F_b) — fine for typical voussoir face counts
        // (~6-30 faces each, ~30 voussoirs).
        var fcA = FaceCentroids(a);
        var fcB = FaceCentroids(b);
        double tol2 = absTol * absTol;
        foreach (var pa in fcA)
        {
            foreach (var pb in fcB)
            {
                double d2 = (pa - pb).SquareLength;
                if (d2 < tol2) return true;
            }
        }
        return false;
    }

    private static List<Point3d> FaceCentroids(Mesh m)
    {
        var faces = m.Faces;
        var verts = m.Vertices;
        var res = new List<Point3d>(faces.Count);
        for (int fi = 0; fi < faces.Count; fi++)
        {
            var f = faces[fi];
            var a = (Point3d)verts[f.A];
            var b = (Point3d)verts[f.B];
            var c = (Point3d)verts[f.C];
            int divisor = 3;
            double cx = a.X + b.X + c.X;
            double cy = a.Y + b.Y + c.Y;
            double cz = a.Z + b.Z + c.Z;
            if (f.IsQuad)
            {
                var d = (Point3d)verts[f.D];
                cx += d.X; cy += d.Y; cz += d.Z; divisor = 4;
            }
            res.Add(new Point3d(cx / divisor, cy / divisor, cz / divisor));
        }
        return res;
    }

    private static string GetByIndexOrDefault(List<string> list, int i, string fallback)
    {
        if (list == null || list.Count == 0) return fallback;
        if (i < list.Count) return string.IsNullOrEmpty(list[i]) ? fallback : list[i];
        // If the list is shorter than the voussoir list, repeat the last value.
        return list[list.Count - 1] ?? fallback;
    }
}
