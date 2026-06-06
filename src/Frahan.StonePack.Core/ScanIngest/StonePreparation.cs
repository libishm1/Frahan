#nullable disable
using System;
using System.Collections.Generic;
using System.Text;
using Frahan.Surface;
using Rhino.Geometry;

namespace Frahan.Core.ScanIngest;

// =============================================================================
// StonePreparation — Phase F4 one-button scan-cleanup pipeline.
//
// Wraps the existing Frahan.Surface.MeshRepair + Rhino's managed
// Mesh.Reduce + Frahan.Surface.StoneDescriptorBuilder with per-stage
// toggles. Closes UX architecture report §6.6 friction: "Repair →
// Decimate → OBB → StoneDesc is a four-component chain that every scan
// workflow repeats verbatim. A single `Stone Prep (Scan)` component
// that wraps the four (with per-stage toggles) cuts canvas clutter."
//
// Pure managed — uses RhinoCommon's managed Mesh.Reduce (quadric edge
// collapse) for the decimate stage, so no native shim is needed.
// =============================================================================

public sealed class StonePrepOptions
{
    public StonePrepOptions(
        bool repair = true,
        bool decimate = false,
        int targetTriangleCount = 0,
        double weldAngleRadians = Math.PI / 8.0,
        double healEdgeDistance = 0.001)
    {
        if (targetTriangleCount < 0)
            throw new ArgumentOutOfRangeException(nameof(targetTriangleCount),
                $"target triangle count must be >= 0, got {targetTriangleCount}");
        Repair = repair;
        Decimate = decimate;
        TargetTriangleCount = targetTriangleCount;
        WeldAngleRadians = weldAngleRadians;
        HealEdgeDistance = healEdgeDistance;
    }

    /// <summary>If true, run MeshRepair.RepairAll before downstream steps.</summary>
    public bool Repair { get; }
    /// <summary>If true, reduce to <see cref="TargetTriangleCount"/> via
    /// quadric edge collapse. No-op when target ≥ current count.</summary>
    public bool Decimate { get; }
    /// <summary>Target triangle count for the decimate stage. 0 disables
    /// decimation regardless of <see cref="Decimate"/>.</summary>
    public int TargetTriangleCount { get; }
    public double WeldAngleRadians { get; }
    public double HealEdgeDistance { get; }
}

/// <summary>
/// Output of the prep pipeline. Carries the cleaned mesh, the descriptor
/// built from it, the per-stage trace (one bullet per stage with a
/// before/after diff), and the input id passed through.
/// </summary>
public sealed class StonePrepResult
{
    public StonePrepResult(string id, Mesh cleanedMesh, StoneDescriptor descriptor,
        IReadOnlyList<string> trace)
    {
        Id = id ?? string.Empty;
        CleanedMesh = cleanedMesh ?? throw new ArgumentNullException(nameof(cleanedMesh));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Trace = trace ?? Array.Empty<string>();
    }

    public string Id { get; }
    public Mesh CleanedMesh { get; }
    public StoneDescriptor Descriptor { get; }
    public IReadOnlyList<string> Trace { get; }
}

public static class StonePreparation
{
    /// <summary>
    /// Run the prep pipeline against a single mesh. Each stage is
    /// optional; the descriptor stage always runs (the goal of the
    /// pipeline is to emit a descriptor, even when no cleanup is wanted).
    /// </summary>
    public static StonePrepResult Run(string id, Mesh source, StonePrepOptions options = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        options ??= new StonePrepOptions();
        var trace = new List<string>();

        Mesh cur = source.DuplicateMesh();
        int trisBefore = cur.Faces.Count;
        int vertsBefore = cur.Vertices.Count;

        // Stage 1 — Repair.
        if (options.Repair)
        {
            try
            {
                var (repaired, repairTrace) = MeshRepair.RepairAll(
                    cur, options.WeldAngleRadians, options.HealEdgeDistance);
                cur = repaired;
                int changed = 0;
                foreach (var t in repairTrace) if (t.Changed) changed++;
                trace.Add($"Repair: {changed} of {repairTrace.Count} sub-steps changed mesh");
            }
            catch (Exception ex)
            {
                trace.Add($"Repair: SKIPPED ({ex.GetType().Name}: {ex.Message})");
            }
        }
        else
        {
            trace.Add("Repair: disabled");
        }

        // Stage 2 — Decimate (quadric edge collapse via RhinoCommon's
        // managed Mesh.Reduce). Only runs when the target is < current
        // triangle count.
        if (options.Decimate && options.TargetTriangleCount > 0
            && options.TargetTriangleCount < cur.Faces.Count)
        {
            int before = cur.Faces.Count;
            try
            {
                bool ok = cur.Reduce(options.TargetTriangleCount, true, 10, false);
                int after = cur.Faces.Count;
                trace.Add(ok
                    ? $"Decimate: {before} → {after} tris (target {options.TargetTriangleCount})"
                    : $"Decimate: Mesh.Reduce returned false; left {after} tris");
            }
            catch (Exception ex)
            {
                trace.Add($"Decimate: SKIPPED ({ex.GetType().Name}: {ex.Message})");
            }
        }
        else if (options.Decimate)
        {
            trace.Add(
                $"Decimate: skipped (target {options.TargetTriangleCount} >= current {cur.Faces.Count} or 0)");
        }
        else
        {
            trace.Add("Decimate: disabled");
        }

        // Stage 3 — Descriptor (mandatory; this is the pipeline's
        // primary output).
        StoneDescriptor descriptor;
        try
        {
            descriptor = StoneDescriptorBuilder.BuildFromMesh(id ?? string.Empty, cur);
            trace.Add(
                $"Descriptor: V={cur.Vertices.Count} T={cur.Faces.Count} " +
                $"closed={descriptor.IsClosed} manifold={descriptor.IsManifold} " +
                $"compactness={descriptor.Compactness:0.000}");
        }
        catch (Exception ex)
        {
            trace.Add($"Descriptor: FAILED ({ex.GetType().Name}: {ex.Message})");
            throw;
        }

        trace.Insert(0, $"Input: V={vertsBefore} T={trisBefore}");
        return new StonePrepResult(id, cur, descriptor, trace);
    }

    /// <summary>
    /// Run the prep pipeline against a list of meshes. Per-mesh ids are
    /// optional; missing entries default to <c>$"stone-{index}"</c>.
    /// Null meshes produce a null entry in the result list so positions
    /// stay aligned with the input.
    /// </summary>
    public static IReadOnlyList<StonePrepResult> RunBatch(
        IReadOnlyList<Mesh> meshes,
        IReadOnlyList<string> ids = null,
        StonePrepOptions options = null)
    {
        if (meshes == null) throw new ArgumentNullException(nameof(meshes));
        var result = new List<StonePrepResult>(meshes.Count);
        for (int i = 0; i < meshes.Count; i++)
        {
            if (meshes[i] == null) { result.Add(null); continue; }
            string id = (ids != null && ids.Count > i && !string.IsNullOrEmpty(ids[i]))
                ? ids[i]
                : $"stone-{i}";
            result.Add(Run(id, meshes[i], options));
        }
        return result;
    }

    /// <summary>Render a per-stone trace as a multi-line text block
    /// suitable for a Grasshopper panel.</summary>
    public static string FormatTrace(StonePrepResult r)
    {
        if (r == null) return "(null result)";
        var sb = new StringBuilder();
        sb.Append("[").Append(r.Id).AppendLine("]");
        foreach (var line in r.Trace) sb.Append("  ").AppendLine(line);
        return sb.ToString();
    }
}
