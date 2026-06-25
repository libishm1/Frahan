#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Frahan.Masonry.Solvers;   // MasonryStabilityChecker, StabilityResult

namespace Frahan.Masonry.Nbo;

// =============================================================================
// NboCra -- the CRA wall-gate: the THIRD and strongest stability tier of the NBO
// loop, after the cheap analytic gate (NboPlanner.Gate) and the Bullet settle
// confirmation (NboSettle.ConfirmSettle).
//
// It wraps the shipped MasonryStabilityChecker.CheckMeshes, which:
//   * auto-detects contact interfaces between the placed stones
//     (MeshContactDetector: proximity sweeps + PCA plane-fit, robust to bumpy
//      non-planar contacts),
//   * fixes the lowest course as supports,
//   * builds a MasonryAssembly and runs the compas-CRA convex-QP rigid-block
//     limit analysis (EquilibriumMatrixBuilder -> FrictionConeBuilder ->
//     RbeQpFormulation -> ManagedQpSolver).
// It returns the rich StabilityResult: IsStable (feasible compression-only +
// frictional equilibrium), the QP status, max friction-cone utilization, and the
// weakest interface -- the wall-level "does it actually stand?" verdict the
// analytic gate cannot give.
//
// This is a per-wall confirmation (run on the produced sequence), not yet folded
// into the per-step accept/reject -- the loop commits on the analytic gate and
// CRA confirms the whole wall (the standard cheap-gate / expensive-confirm split).
// =============================================================================

public static class NboCra
{
    /// <summary>
    /// Run the compas-CRA rigid-block-equilibrium check on a placed wall (the NBO
    /// sequence's stones). Returns the shipped <see cref="StabilityResult"/>
    /// (IsStable + status + max friction utilization + weakest interface).
    /// </summary>
    /// <param name="placed">Placed stone meshes at their world wall poses.</param>
    /// <param name="density">Stone density (consistent units; default granite ~2400).</param>
    /// <param name="mu">Interface friction coefficient (dry stone ~0.7).</param>
    /// <param name="faceCount">Friction-cone facets (more = tighter cone).</param>
    public static StabilityResult ConfirmCra(
        IReadOnlyList<Mesh> placed,
        double density = 2400.0,
        double mu = 0.7,
        int faceCount = 8,
        double contactDistanceTol = 0.01,    // 10 mm: as-DROPPED irregular stone touches at
                                             // points with wedge gaps; a CAD-tight 1 mm finds no
                                             // patches. Widen for pre-settle contact detection.
        double contactAngleTolDeg = 8.0)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        if (placed.Count == 0) throw new ArgumentException("at least one stone is required", nameof(placed));

        var verts = new List<IReadOnlyList<double>>(placed.Count);
        var tris = new List<IReadOnlyList<int>>(placed.Count);
        foreach (var m in placed)
        {
            Flatten(m, out double[] v, out int[] t);
            verts.Add(v);
            tris.Add(t);
        }
        return MasonryStabilityChecker.CheckMeshes(
            verts, tris, density, contactDistanceTol, contactAngleTolDeg,
            fixBelowZ: 1e-3, mu: mu, faceCount: faceCount, inscribed: true);
    }

    /// <summary>
    /// Settle the wall in Bullet FIRST, then run CRA on the settled geometry. This
    /// is the correct order: drop-to-contact leaves point contacts (the QP is
    /// degenerate -> SolverError), but Bullet rocks each stone into seat, creating
    /// the contact PATCHES the CRA equilibrium needs. Falls back to CRA on the raw
    /// meshes when the Bullet backend is unavailable.
    ///
    /// 2026-06-24: the settle is now ACCURATE. The old whole-wall "scatter" was a Bullet
    /// collision-margin bug (the 0.04 m default rounded the hulls so stones rolled off every
    /// face); fixed at hull.Margin=0.0015 + rotation about the volume CoM. This runs the
    /// INCREMENTAL settle (each stone seated onto the FIXED already-built wall), so the verdict
    /// reflects real seated contacts. For the strongest verdict, build the wall with Seat /
    /// NboFillOptions.SettleValidate so every stone is physically bedded first; an un-validated
    /// wall can have slipping upper courses and the verdict will (truthfully) reflect that. The
    /// analytic gate remains the cheap per-pose pre-check.
    /// </summary>
    public static StabilityResult ConfirmSettledCra(
        IReadOnlyList<Mesh> placed,
        double density = 2400.0,
        double mu = 0.7,
        int faceCount = 8)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        // INCREMENTAL settle (each stone seated onto the FIXED already-built wall): avoids the
        // whole-wall push-apart cascade, so the CRA verdict reflects real seated contacts. Build the
        // wall with Seat (SettleValidate) so the placement is physically bedded first; otherwise a
        // stone the planner placed without a seat still falls and the verdict reflects that.
        var settle = NboSettle.SettleIncremental(placed);
        if (settle.Available && settle.SettledMeshes != null)
        {
            var settled = new List<Mesh>(settle.SettledMeshes.Length);
            foreach (var m in settle.SettledMeshes) if (m != null) settled.Add(m);
            if (settled.Count > 0) return ConfirmCra(settled, density, mu, faceCount);
        }
        return ConfirmCra(placed, density, mu, faceCount);
    }

    // RhinoCommon Mesh -> flat [x,y,z,...] vertices + flat triangle indices
    // (quads fan-triangulated A,B,C + A,C,D), the CheckMeshes contract.
    private static void Flatten(Mesh m, out double[] verts, out int[] tris)
    {
        verts = new double[m.Vertices.Count * 3];
        for (int i = 0; i < m.Vertices.Count; i++)
        {
            Point3d p = m.Vertices[i];
            verts[3 * i + 0] = p.X;
            verts[3 * i + 1] = p.Y;
            verts[3 * i + 2] = p.Z;
        }
        var tl = new List<int>(m.Faces.Count * 3);
        for (int f = 0; f < m.Faces.Count; f++)
        {
            var face = m.Faces[f];
            tl.Add(face.A); tl.Add(face.B); tl.Add(face.C);
            if (face.IsQuad) { tl.Add(face.A); tl.Add(face.C); tl.Add(face.D); }
        }
        tris = tl.ToArray();
    }
}
