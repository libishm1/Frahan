#nullable disable
using System;
using Frahan.Core.ScanIngest;
using Rhino.Geometry;

namespace Frahan.Tests;

// =============================================================================
// ScanPrepTests — Phase F3 (ScaleCalibration) + Phase F4 (StonePreparation).
// Pure managed; Rhino types (Curve, Mesh, Transform) come from the managed
// part of RhinoCommon and resolve without the native rhcommon_c.dll on
// most paths.
// =============================================================================

static class ScanPrepTests
{
    // ─── ScaleCalibration ─────────────────────────────────────────────────

    public static void ScaleCalibration_Identity_FactorOne()
    {
        var result = ScaleCalibration.Solve(1.0, 1.0, "m");
        AssertNear(result.ScaleFactor, 1.0, 1e-12, "factor");
        AssertNear(result.MeasuredLength, 1.0, 1e-12, "measured length");
        AssertNear(result.ReferenceLength, 1.0, 1e-12, "reference length");
        Assert(result.ReportedUnits == "m", $"units should be 'm', got '{result.ReportedUnits}'");
    }

    public static void ScaleCalibration_MillimetresToMetres_FactorThousand()
    {
        // The scan thinks the curve is 1 unit long; user says that's 1 metre,
        // so the curve must currently be in mm → factor = 1000 (1m / 1mm).
        // Here we model that as measured=1, reference=1000 (mm). Tests just
        // check the arithmetic: factor = reference / measured.
        var result = ScaleCalibration.Solve(measuredCurveLength: 1.0,
                                             referenceLength: 1000.0,
                                             reportedUnits: "mm");
        AssertNear(result.ScaleFactor, 1000.0, 1e-9, "factor");
    }

    public static void ScaleCalibration_KilometresToMetres_FactorTenth()
    {
        var result = ScaleCalibration.Solve(10.0, 1.0, "m");
        AssertNear(result.ScaleFactor, 0.1, 1e-12, "factor");
    }

    public static void ScaleCalibration_ScaleTransform_IsUniformAndCentredAtOrigin()
    {
        var result = ScaleCalibration.Solve(2.0, 6.0, "m");
        // Factor = 3. Apply to (1,1,1) → (3,3,3).
        var p = new Point3d(1, 1, 1);
        p.Transform(result.ScaleTransform);
        AssertNear(p.X, 3.0, 1e-9, "px");
        AssertNear(p.Y, 3.0, 1e-9, "py");
        AssertNear(p.Z, 3.0, 1e-9, "pz");
        // Origin stays fixed.
        var o = new Point3d(0, 0, 0);
        o.Transform(result.ScaleTransform);
        AssertNear(o.X, 0.0, 1e-9, "origin x");
    }

    public static void ScaleCalibration_NegativeMeasured_Throws()
    {
        try
        {
            ScaleCalibration.Solve(-1.0, 1.0);
            throw new Exception("Expected ArgumentOutOfRangeException for negative measured length.");
        }
        catch (ArgumentOutOfRangeException) { /* expected */ }
    }

    public static void ScaleCalibration_ZeroReference_Throws()
    {
        try
        {
            ScaleCalibration.Solve(1.0, 0.0);
            throw new Exception("Expected ArgumentOutOfRangeException for zero reference length.");
        }
        catch (ArgumentOutOfRangeException) { /* expected */ }
    }

    public static void ScaleCalibration_NullCurve_Throws()
    {
        try
        {
            ScaleCalibration.SolveFromCurve(null, 1.0);
            throw new Exception("Expected ArgumentNullException for null curve.");
        }
        catch (ArgumentNullException) { /* expected */ }
    }

    public static void ScaleCalibration_LineCurve_LengthMatches()
    {
        // A LineCurve from (0,0,0) to (4,3,0) has length 5. Reference 10 → factor 2.
        var line = new LineCurve(new Point3d(0, 0, 0), new Point3d(4, 3, 0));
        var result = ScaleCalibration.SolveFromCurve(line, referenceLength: 10.0);
        AssertNear(result.MeasuredLength, 5.0, 1e-9, "measured length");
        AssertNear(result.ScaleFactor, 2.0, 1e-9, "factor");
    }

    // ─── StonePreparation ─────────────────────────────────────────────────

    public static void StonePreparation_NullMesh_Throws()
    {
        try
        {
            StonePreparation.Run("x", null);
            throw new Exception("Expected ArgumentNullException for null mesh.");
        }
        catch (ArgumentNullException) { /* expected */ }
    }

    public static void StonePreparation_BoxMesh_ProducesDescriptor()
    {
        var box = new Box(Plane.WorldXY, new Interval(0, 2), new Interval(0, 3), new Interval(0, 5));
        var mesh = Mesh.CreateFromBox(box, 1, 1, 1);
        var result = StonePreparation.Run("stone-A", mesh,
            new StonePrepOptions(repair: true, decimate: false));
        Assert(result != null, "result must be non-null");
        Assert(result.Id == "stone-A", $"id should round-trip, got '{result.Id}'");
        Assert(result.CleanedMesh != null && result.CleanedMesh.Faces.Count > 0,
            "cleaned mesh must have faces");
        Assert(result.Descriptor != null, "descriptor must be non-null");
        Assert(result.Descriptor.MeshVolume > 0,
            $"box mesh volume must be positive, got {result.Descriptor.MeshVolume}");
        // Box 2x3x5 = 30 model units cubed (within mesh-approx).
        AssertNear(result.Descriptor.MeshVolume, 30.0, 1e-6, "box mesh volume");
    }

    public static void StonePreparation_RepairDisabled_TraceSaysSo()
    {
        var box = new Box(Plane.WorldXY, new Interval(0, 1), new Interval(0, 1), new Interval(0, 1));
        var mesh = Mesh.CreateFromBox(box, 1, 1, 1);
        var result = StonePreparation.Run("b", mesh, new StonePrepOptions(repair: false));
        // Trace[0] = Input summary, Trace[1] = Repair line.
        Assert(result.Trace.Count >= 2, "trace must have at least Input + Repair lines");
        Assert(result.Trace[1].Contains("Repair: disabled"),
            $"expected 'Repair: disabled' line, got '{result.Trace[1]}'");
    }

    public static void StonePreparation_DecimateEnabledWithTarget_TriangleCountDrops()
    {
        // A 5x5x5 subdivided box has many more triangles than a coarse one.
        var box = new Box(Plane.WorldXY, new Interval(0, 1), new Interval(0, 1), new Interval(0, 1));
        var mesh = Mesh.CreateFromBox(box, 8, 8, 8);
        int before = mesh.Faces.Count;
        Assert(before > 20, $"need a decimation-worthy mesh; got only {before} faces");

        var result = StonePreparation.Run("d", mesh,
            new StonePrepOptions(repair: false, decimate: true, targetTriangleCount: 20));
        int after = result.CleanedMesh.Faces.Count;
        // Mesh.Reduce is approximate; verify monotonic drop rather than exact target.
        Assert(after < before,
            $"decimate should reduce triangle count; before={before} after={after}");
    }

    public static void StonePreparation_BatchWithNulls_PositionsPreserved()
    {
        var box = new Box(Plane.WorldXY, new Interval(0, 1), new Interval(0, 1), new Interval(0, 1));
        var meshes = new Mesh[]
        {
            Mesh.CreateFromBox(box, 1, 1, 1),
            null,
            Mesh.CreateFromBox(box, 1, 1, 1),
        };
        var ids = new[] { "a", "b", "c" };
        var results = StonePreparation.RunBatch(meshes, ids);
        Assert(results.Count == 3, $"expected 3 results, got {results.Count}");
        Assert(results[0] != null && results[0].Id == "a", "first slot must be valid 'a'");
        Assert(results[1] == null, "null mesh slot must stay null in results");
        Assert(results[2] != null && results[2].Id == "c", "third slot must be valid 'c'");
    }

    // ─── helpers ──────────────────────────────────────────────────────────

    private static void Assert(bool cond, string message)
    {
        if (!cond) throw new Exception(message);
    }

    private static void AssertNear(double actual, double expected, double tol, string label)
    {
        if (Math.Abs(actual - expected) > tol)
            throw new Exception($"{label}: expected {expected} ± {tol}, got {actual}");
    }
}
