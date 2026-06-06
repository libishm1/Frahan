#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH.Masonry;
using Frahan.Masonry.Cutting;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.Tests;

// =============================================================================
// InteropPhasesTests — validates the 5-phase data-flow interop pass:
//   Phase 1: SlabCutByFractures.Plane input accepts FracturePlane DTO + Rhino Plane.
//   Phase 2: every slab-producing component emits a Mesh output.
//   Phase 3: AutoInterfaces / AshlarPack accept a Rhino Mesh on Slabs input.
//   Phase 4: FracturePolygonFromCurve has a ForceProject input (count = 2).
//   Phase 5: every component reports Category = "Frahan" (single tab).
// =============================================================================

static class InteropPhasesTests
{
    // ─── Phase 1: SlabCutByFractures cross-type Plane input ────────────────

    public static void Phase1_SlabCutByFracturesComponent_PlaneInput_IsGeneric()
    {
        var c = new SlabCutByFracturesComponent();
        var planeParam = c.Params.Input[1];
        Assert(planeParam.Name == "Plane",
            $"Plane param expected at index 1, got '{planeParam.Name}'");
        // Param_GenericObject is the Generic-parameter base class. We accept
        // anything except Param_Plane (which would re-introduce the bug).
        Assert(planeParam.GetType().Name != "Param_Plane",
            $"Plane param must NOT be Param_Plane (was the canvas-Goo-conversion bug). " +
            $"Got {planeParam.GetType().FullName}");
    }

    public static void Phase1_GhInterop_UnwrapPlane_AcceptsFracturePlaneDto()
    {
        var fp = new FracturePlane(0.5, 0.0, 0.0, 1.0, 0.0, 0.0);
        var unwrapped = GhInteropAccessor.UnwrapPlane(fp);
        Assert(unwrapped != null, "UnwrapPlane returned null on FracturePlane DTO");
        Assert(Math.Abs(unwrapped.PointX - 0.5) < 1e-12, "PointX round-trip");
        Assert(Math.Abs(unwrapped.NormalX - 1.0) < 1e-12, "NormalX round-trip");
    }

    public static void Phase1_GhInterop_UnwrapPlane_AcceptsRhinoPlane()
    {
        var p = new Plane(new Point3d(0.5, 0, 0), new Vector3d(1, 0, 0));
        var unwrapped = GhInteropAccessor.UnwrapPlane(p);
        Assert(unwrapped != null, "UnwrapPlane returned null on Rhino Plane");
        Assert(Math.Abs(unwrapped.PointX - 0.5) < 1e-9, "PointX round-trip from Rhino");
        Assert(Math.Abs(unwrapped.NormalX - 1.0) < 1e-9, "NormalX round-trip from Rhino");
    }

    public static void Phase1_GhInterop_UnwrapPlane_RejectsNonsense()
    {
        Assert(GhInteropAccessor.UnwrapPlane(null) == null, "null should yield null");
        Assert(GhInteropAccessor.UnwrapPlane("a string") == null, "string should yield null");
        Assert(GhInteropAccessor.UnwrapPlane(42) == null, "int should yield null");
    }

    // ─── Phase 2: Mesh outputs on slab-producing components ────────────────

    public static void Phase2_SlabFromMesh_HasMeshOutput()
    {
        var c = new SlabFromMeshComponent();
        Assert(c.Params.Output.Count == 2, $"expected 2 outputs, got {c.Params.Output.Count}");
        Assert(c.Params.Output[1].Name == "Mesh", $"output[1] expected 'Mesh', got '{c.Params.Output[1].Name}'");
    }

    public static void Phase2_SlabCutByFractures_HasMeshOutput()
    {
        var c = new SlabCutByFracturesComponent();
        Assert(c.Params.Output.Count == 5, $"expected 5 outputs, got {c.Params.Output.Count}");
        Assert(c.Params.Output[4].Name == "Mesh", $"output[4] expected 'Mesh', got '{c.Params.Output[4].Name}'");
    }

    public static void Phase2_SlabCutByFracturePolygons_HasMeshOutput()
    {
        var c = new SlabCutByFracturePolygonsComponent();
        Assert(c.Params.Output.Count == 4, $"expected 4 outputs, got {c.Params.Output.Count}");
        Assert(c.Params.Output[3].Name == "Mesh", $"output[3] expected 'Mesh', got '{c.Params.Output[3].Name}'");
    }

    public static void Phase2_QuarryDecompose_HasMeshOutput()
    {
        var c = new QuarryDecomposeComponent();
        Assert(c.Params.Output.Count == 3, $"expected 3 outputs, got {c.Params.Output.Count}");
        Assert(c.Params.Output[2].Name == "Mesh", $"output[2] expected 'Mesh', got '{c.Params.Output[2].Name}'");
    }

    public static void Phase2_MeshShellSplit_HasMeshOutput()
    {
        var c = new MeshShellSplitComponent();
        Assert(c.Params.Output.Count == 2, $"expected 2 outputs, got {c.Params.Output.Count}");
        Assert(c.Params.Output[1].Name == "Mesh", $"output[1] expected 'Mesh', got '{c.Params.Output[1].Name}'");
    }

    public static void Phase2_ConvexHullSlab_HasMeshOutput()
    {
        var c = new ConvexHullSlabComponent();
        Assert(c.Params.Output.Count == 2, $"expected 2 outputs, got {c.Params.Output.Count}");
        Assert(c.Params.Output[1].Name == "Mesh", $"output[1] expected 'Mesh', got '{c.Params.Output[1].Name}'");
    }

    public static void Phase2_GhInterop_SlabToMesh_RoundTrips()
    {
        var slab = Slab.Box(0, 0, 0, 1, 1, 1);
        var mesh = GhInteropAccessor.SlabToMesh(slab);
        Assert(mesh != null, "SlabToMesh returned null");
        Assert(mesh.Vertices.Count == 8, $"expected 8 vertices, got {mesh.Vertices.Count}");
        Assert(mesh.Faces.Count == 12, $"expected 12 triangles (6 quads × 2), got {mesh.Faces.Count}");
    }

    // ─── Phase 3: Mesh-first masonry inputs ────────────────────────────────

    public static void Phase3_GhInterop_UnwrapSlab_AcceptsRhinoMesh()
    {
        var mesh = new Mesh();
        mesh.Vertices.Add(0, 0, 0);
        mesh.Vertices.Add(1, 0, 0);
        mesh.Vertices.Add(1, 1, 0);
        mesh.Vertices.Add(0, 1, 0);
        mesh.Vertices.Add(0, 0, 1);
        mesh.Vertices.Add(1, 0, 1);
        mesh.Vertices.Add(1, 1, 1);
        mesh.Vertices.Add(0, 1, 1);
        mesh.Faces.AddFace(0, 3, 2, 1);
        mesh.Faces.AddFace(4, 5, 6, 7);
        mesh.Faces.AddFace(0, 1, 5, 4);
        mesh.Faces.AddFace(1, 2, 6, 5);
        mesh.Faces.AddFace(2, 3, 7, 6);
        mesh.Faces.AddFace(3, 0, 4, 7);

        var slab = GhInteropAccessor.UnwrapSlab(mesh);
        Assert(slab != null, "UnwrapSlab returned null on Rhino Mesh");
        Assert(slab.VertexCount == 8, $"expected 8 vertices, got {slab.VertexCount}");
    }

    public static void Phase3_GhInterop_UnwrapSlab_AcceptsSlabDto()
    {
        var slab = Slab.Box(0, 0, 0, 1, 1, 1);
        var unwrapped = GhInteropAccessor.UnwrapSlab(slab);
        Assert(ReferenceEquals(unwrapped, slab), "UnwrapSlab should return the same instance for a Slab DTO");
    }

    // ─── Phase 4: ForceProject on FracturePolygonFromCurve ─────────────────

    public static void Phase4_FracturePolygonFromCurve_HasForceProjectInput()
    {
        var c = new FracturePolygonFromCurveComponent();
        Assert(c.Params.Input.Count == 2,
            $"expected 2 inputs (Curve + ForceProject), got {c.Params.Input.Count}");
        Assert(c.Params.Input[1].Name == "ForceProject",
            $"input[1] expected 'ForceProject', got '{c.Params.Input[1].Name}'");
        Assert(c.Params.Input[1].Optional, "ForceProject must be Optional");
    }

    // ─── Phase 5: Single ribbon tab ────────────────────────────────────────

    public static void Phase5_AllMasonryComponents_ShareFrahanCategory()
    {
        var components = new GH_Component[]
        {
            new SlabFromMeshComponent(),
            new SlabCutByFracturesComponent(),
            new SlabCutByFracturePolygonsComponent(),
            new FracturePolygonFromCurveComponent(),
            new GridFracturePlanesComponent(),
            new RandomFracturePlanesComponent(),
            new VoronoiFracturePlanesComponent(),
            new LayeredFracturePlanesComponent(),
            new RadialFracturePlanesComponent(),
            new BrickPatternFracturePlanesComponent(),
            new JitteredGridFracturePlanesComponent(),
            new FracturePlaneFilterComponent(),
            new QuarryDecomposeComponent(),
            new MeshShellSplitComponent(),
            new ConvexHullSlabComponent(),
            new MasonryBlockComponent(),
            new MasonryAssemblyComponent(),
            new MasonryStabilityRbeComponent(),
            new AutoInterfacesComponent(),
            new AshlarPackComponent(),
            new WallFrameComponent(),
            new AshlarPackOptionsComponent(),
            new PackDiagnosticsComponent(),
            new PackPreviewComponent(),
        };
        Assert(components.Length == 24, $"expected 24 masonry components, got {components.Length}");
        for (int i = 0; i < components.Length; i++)
        {
            var c = components[i];
            Assert(c.Category == "Frahan",
                $"{c.GetType().Name} Category expected 'Frahan', got '{c.Category}'");
        }
    }

    // ─── helpers ───────────────────────────────────────────────────────────

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal static class GhInteropAccessor
{
    public static FracturePlane UnwrapPlane(object raw) => GhInterop.UnwrapPlane(raw);
    public static Slab UnwrapSlab(object raw) => GhInterop.UnwrapSlab(raw);
    public static Mesh SlabToMesh(Slab slab) => GhInterop.SlabToMesh(slab);
}
