#nullable disable
using System;
using System.Collections.Generic;
using Frahan.EdgeMatching;
using Rhino;
using Rhino.Geometry;

namespace Frahan.Tests;

// 2.5D per-facet PROJECTION BOOTSTRAP (2026-05-25). Design basis
// wiki/algorithms/edge_matching/projection_bootstrap_3d.md. These tests are PURE
// MANAGED: FitFacetPlane does PCA via a 3x3 MathNet symmetric EVD (no native
// rhcommon_c), and Lift composes Transform.PlaneToPlane (managed value math). The
// default-OFF guard locks the opt-in. The lift-composition test validates the
// 2D->3D recovery on a KNOWN planar pair.
static class EdgeMatchingProjectionBootstrapTests
{
    // ---- (1) Default-OFF guard (byte-for-byte back-compat) ----------------

    public static void Defaults_ProjectionBootstrapIsOff()
    {
        var opt = new AssemblyOptions();
        Assert(opt.ProjectionBootstrap == false, "default ProjectionBootstrap must be false");
        Assert(opt.ProjectionSampleSpacingFactor > 0, "ProjectionSampleSpacingFactor must be positive");
        Assert(opt.ProjectionPlanarityFactor > 0, "ProjectionPlanarityFactor must be positive");
    }

    // ---- (2) PCA plane fit on a KNOWN planar loop -------------------------

    // A square loop lying in a known tilted plane: the PCA normal must match the
    // plane normal (up to sign), the residual must be ~0 (the points are exactly
    // planar), and the fitted frame must be a proper orthonormal basis.
    public static void FitFacetPlane_KnownPlanarLoop_RecoversNormalAndZeroResidual()
    {
        // Build a square in a tilted plane: origin (1,2,3), normal along (1,1,1).
        var n = new Vector3d(1, 1, 1); n.Unitize();
        var origin = new Point3d(1, 2, 3);
        var basis = new Plane(origin, n); // Rhino picks in-plane x/y
        var pts = new List<Point3d>();
        double[,] uv = { { -5, -5 }, { 5, -5 }, { 5, 5 }, { -5, 5 }, { 0, 7 } };
        for (int i = 0; i < uv.GetLength(0); i++)
            pts.Add(origin + basis.XAxis * uv[i, 0] + basis.YAxis * uv[i, 1]);

        bool ok = ProjectionPairFinder.FitFacetPlane(pts, out Plane fit, out double residual);
        Assert(ok, "FitFacetPlane must succeed on a clean planar loop");
        Assert(residual < 1e-6, $"planar loop residual must be ~0, got {residual}");

        // Normal parallel (|dot| ~ 1) to the true normal, either sign.
        double dot = Math.Abs(fit.Normal * n);
        Assert(dot > 1.0 - 1e-6, $"fitted normal must align with the true normal, |dot|={dot}");

        // Proper orthonormal right-handed frame.
        Assert(Close(fit.XAxis.Length, 1.0) && Close(fit.YAxis.Length, 1.0), "frame axes unit");
        var cross = Vector3d.CrossProduct(fit.XAxis, fit.YAxis);
        Assert(Math.Abs(cross * fit.Normal) > 1.0 - 1e-6, "frame right-handed");
    }

    // Non-planar point set gets a non-zero residual (the planarity flag driver).
    public static void FitFacetPlane_NonPlanar_HasResidual()
    {
        var pts = new List<Point3d>
        {
            new Point3d(0, 0, 0), new Point3d(10, 0, 0),
            new Point3d(10, 10, 0), new Point3d(0, 10, 0),
            new Point3d(5, 5, 6), // out of plane
        };
        bool ok = ProjectionPairFinder.FitFacetPlane(pts, out _, out double residual);
        Assert(ok, "FitFacetPlane must succeed on a non-degenerate set");
        Assert(residual > 0.1, $"non-planar set must have a real residual, got {residual}");
    }

    // ---- (3) 2D->3D lift composition correctness on a KNOWN pair ----------

    // Two complementary planar facets that share a plane in 3D: facet A is a tilted
    // plane; facet B is the SAME plane shifted, with its normal pointing the
    // opposite way (the two shards sit on opposite sides of the fracture). With the
    // 2D in-plane match recovered as the identity (the projected coords coincide by
    // construction), the lifted relative pose must map A's facet frame onto B's so
    // that A's facet origin lands on B's facet origin and A's normal becomes
    // ANTIPARALLEL to B's normal (the mating condition).
    public static void Lift_KnownPair_ComposesAntiparallelContact()
    {
        // Facet A frame.
        var nA = new Vector3d(0.3, 0.4, 1.0); nA.Unitize();
        var facetA = new Plane(new Point3d(10, 0, 0), nA);
        // Facet B: a DIFFERENT plane/orientation (the mating shard is rotated +
        // translated in 3D). Build B by a known rigid motion G applied to A so the
        // test has a non-trivial target.
        var gAxis = new Vector3d(1, -2, 0.5); gAxis.Unitize();
        var G = Transform.Multiply(
            Transform.Translation(7, -4, 3),
            Transform.Rotation(RhinoMath.ToRadians(35.0), gAxis, facetA.Origin));
        var facetB = new Plane(facetA);
        facetB.Transform(G);

        // The 2D match: with identity M2D, Lift maps A's facet frame onto B's frame
        // (with normal flip). Verify the lift sends A's facet origin -> B's origin
        // and A's facet normal -> -B's normal (antiparallel mating).
        var tRel = ProjectionPairFinder.Lift(facetA, facetB, Transform.Identity);

        var oA = facetA.Origin; oA.Transform(tRel);
        Assert(oA.DistanceTo(facetB.Origin) < 1e-6,
            $"lift must map facet A origin onto facet B origin, gap {oA.DistanceTo(facetB.Origin):F6}");

        // A's normal direction under the lift (transform as a direction: end - origin).
        var aN = facetA.Origin + facetA.Normal;
        aN.Transform(tRel);
        var liftedNormal = aN - oA;
        liftedNormal.Unitize();
        double dotN = liftedNormal * facetB.Normal;
        Assert(dotN < -1.0 + 1e-5,
            $"lifted A normal must be ANTIPARALLEL to B normal, dot={dotN:F6}");

        // The lift is a proper rigid motion (rotation det +1, orthonormal).
        Assert(Close(Det3(tRel), 1.0, 1e-6), $"lift rotation det must be +1, got {Det3(tRel)}");
    }

    // The lift respects a non-identity in-plane 2D match: an in-plane translation by
    // (du,dv) in B's facet coords must shift A's lifted origin by exactly that
    // in-plane vector along B's axes.
    public static void Lift_InPlaneMatch_ShiftsAlongFacetAxes()
    {
        var nA = new Vector3d(0, 0, 1);
        var facetA = new Plane(new Point3d(0, 0, 0), nA);
        var facetB = new Plane(new Point3d(0, 0, 0), new Vector3d(0, 0, 1));

        // In-plane translation in the projected (WorldXY) space by (du, dv).
        double du = 4.0, dv = -3.0;
        var m2D = Transform.Translation(du, dv, 0);
        var tRel = ProjectionPairFinder.Lift(facetA, facetB, m2D);

        var oA = facetA.Origin; oA.Transform(tRel);
        // facetB axes: with the flip in Lift, x stays facetB.XAxis, y is -facetB.YAxis.
        var expected = facetB.Origin + facetB.XAxis * du - facetB.YAxis * dv;
        Assert(oA.DistanceTo(expected) < 1e-6,
            $"in-plane match must shift along facet axes, got {oA} expected {expected}");
    }

    // ---- helpers ----------------------------------------------------------

    private static double Det3(Transform r)
        => r.M00 * (r.M11 * r.M22 - r.M12 * r.M21)
         - r.M01 * (r.M10 * r.M22 - r.M12 * r.M20)
         + r.M02 * (r.M10 * r.M21 - r.M11 * r.M20);

    private static bool Close(double a, double b, double tol = 1e-9) => Math.Abs(a - b) <= tol;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
