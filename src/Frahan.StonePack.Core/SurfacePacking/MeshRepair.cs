using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Surface;

/// <summary>
/// Per-step trace from a <see cref="MeshRepair"/> run. Pure data.
/// </summary>
public sealed class MeshRepairTrace
{
    public MeshRepairTrace(string step, bool changed, string detail)
    {
        Step = step ?? throw new ArgumentNullException(nameof(step));
        Changed = changed;
        Detail = detail ?? "";
    }
    public string Step { get; }
    public bool Changed { get; }
    public string Detail { get; }
    public override string ToString() => $"[{Step}] {(Changed ? "changed" : "no change")} - {Detail}";
}

/// <summary>
/// Rhino-bound mesh repair pipeline. Wraps a sensible chain of RhinoCommon
/// mesh-fix operations (weld vertices, heal naked edges, unify normals,
/// remove degenerate faces) and reports a per-step trace.
///
/// Spec 11 + runbook section 16.6 component family "Frahan Mesh Repair".
/// Lives in Frahan.Surface alongside the surface-pack mesh utilities.
/// </summary>
public static class MeshRepair
{
    /// <summary>
    /// Run the full repair pipeline against a copy of the input mesh. Does
    /// NOT mutate the caller's mesh. Returns the repaired copy plus a trace
    /// of every step.
    /// </summary>
    public static (Mesh Repaired, IReadOnlyList<MeshRepairTrace> Trace) RepairAll(
        Mesh source,
        double weldAngleRadians = Math.PI / 8.0,
        double healEdgeDistance = 0.001)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        var copy = source.DuplicateMesh();
        var trace = new List<MeshRepairTrace>();

        // 1. Combine duplicate faces (RhinoCommon helper if available; manual no-op otherwise).
        try
        {
            int removedDup = copy.Faces.CullDegenerateFaces();
            trace.Add(new MeshRepairTrace("CullDegenerateFaces",
                changed: removedDup > 0,
                detail: $"removed {removedDup} degenerate face(s)"));
        }
        catch (Exception ex)
        {
            trace.Add(new MeshRepairTrace("CullDegenerateFaces", false,
                "exception: " + ex.Message));
        }

        // 2. Weld vertices within the angle threshold.
        try
        {
            copy.Weld(weldAngleRadians);
            trace.Add(new MeshRepairTrace("Weld", true,
                $"weld angle = {weldAngleRadians * 180.0 / Math.PI:0.##} deg"));
        }
        catch (Exception ex)
        {
            trace.Add(new MeshRepairTrace("Weld", false, "exception: " + ex.Message));
        }

        // 3. Compact + remove unused vertices.
        try
        {
            int removedUnused = copy.Vertices.CullUnused();
            trace.Add(new MeshRepairTrace("CullUnusedVertices",
                changed: removedUnused > 0,
                detail: $"removed {removedUnused} unused vertex(s)"));
        }
        catch (Exception ex)
        {
            trace.Add(new MeshRepairTrace("CullUnusedVertices", false,
                "exception: " + ex.Message));
        }

        // 4. Heal naked edges (close small gaps).
        try
        {
            bool healed = copy.HealNakedEdges(healEdgeDistance);
            trace.Add(new MeshRepairTrace("HealNakedEdges", healed,
                $"distance threshold = {healEdgeDistance}"));
        }
        catch (Exception ex)
        {
            trace.Add(new MeshRepairTrace("HealNakedEdges", false,
                "exception: " + ex.Message));
        }

        // 5. Unify normals so face windings agree.
        try
        {
            int flipped = copy.UnifyNormals();
            trace.Add(new MeshRepairTrace("UnifyNormals",
                changed: flipped > 0,
                detail: $"flipped {flipped} face(s)"));
        }
        catch (Exception ex)
        {
            trace.Add(new MeshRepairTrace("UnifyNormals", false,
                "exception: " + ex.Message));
        }

        // 6. Recompute face + vertex normals.
        try
        {
            copy.FaceNormals.ComputeFaceNormals();
            copy.Normals.ComputeNormals();
            trace.Add(new MeshRepairTrace("ComputeNormals", true, "face + vertex normals recomputed"));
        }
        catch (Exception ex)
        {
            trace.Add(new MeshRepairTrace("ComputeNormals", false, "exception: " + ex.Message));
        }

        return (copy, trace);
    }
}
