#nullable disable
using System;
using Frahan.EdgeMatching;
using Rhino;
using Rhino.Geometry;

namespace Frahan.Tests;

// Needs Rhino runtime + MathNet.Numerics. SKIPs when rhcommon_c
// cannot initialise on the agent host.
static class EdgeMatchingIcp3DTests
{
    public static void Refine_IdentityOnSamePolyline_ReturnsZeroResidual()
    {
        var poly = MakeTwistedCurve();
        var seg = MakeSegment3D("S", poly);
        var panel = new Panel("S", poly.ToPolylineCurve(), PanelKind.Frame);
        panel.IsAnchored = true;

        var icp = new ConstrainedIcp3D(new IcpOptions { MaxIterations = 20 });
        var refined = icp.Refine(seg, seg, panel, substrate: null, Transform.Identity);

        Assert(refined.Residual < 1e-6,
            $"expected near-zero residual on identity match, got {refined.Residual}");
    }

    public static void Refine_KnownRigidTransform_RecoversTruth()
    {
        // Truth: rotation around an oblique axis + non-axial translation.
        // The Kabsch SVD must recover all six DoF; if the reflection-guard
        // is broken (spec D8), this test fails because the recovered
        // transform inverts chirality.
        Vector3d axis = new Vector3d(1.0, 2.0, 3.0);
        axis.Unitize();
        double theta = RhinoMath.ToRadians(7.5);
        var truth = Transform.Multiply(
            Transform.Translation(15.0, -8.0, 4.0),
            Transform.Rotation(theta, axis, Point3d.Origin));

        var polyA = MakeTwistedCurve();
        var polyB = TransformPolyline(polyA, truth);
        var aSeg = MakeSegment3D("A", polyA);
        var bSeg = MakeSegment3D("B", polyB);
        var panelB = new Panel("B", polyB.ToPolylineCurve(), PanelKind.Frame);
        panelB.IsAnchored = true;

        // Initial guess: truth perturbed by ~0.5 mm and ~0.3°.
        double perturbTheta = RhinoMath.ToRadians(0.3);
        var perturb = Transform.Multiply(
            Transform.Translation(0.5, -0.5, 0.5),
            Transform.Rotation(perturbTheta, Vector3d.ZAxis, Point3d.Origin));
        var initial = Transform.Multiply(perturb, truth);

        var icp = new ConstrainedIcp3D(new IcpOptions
        {
            MaxIterations = 100,
            TranslationTol = 1e-6,
            RotationTolDeg = 1e-4,
        });
        var refined = icp.Refine(aSeg, bSeg, panelB, substrate: null, initial);

        Assert(refined.Residual < 0.1,
            $"expected sub-0.1 mm residual after refinement, got {refined.Residual}");

        double dx = refined.AontoB.M03 - truth.M03;
        double dy = refined.AontoB.M13 - truth.M13;
        double dz = refined.AontoB.M23 - truth.M23;
        double dTrans = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        Assert(dTrans < 0.3,
            $"expected recovered translation within 0.3 mm of truth, got {dTrans}");

        // Reflection guard: rotation matrix determinant must be +1, not -1.
        // det(R) ≈ M00*(M11*M22 - M12*M21) - M01*(M10*M22 - M12*M20) + M02*(M10*M21 - M11*M20)
        var r = refined.AontoB;
        double det = r.M00 * (r.M11 * r.M22 - r.M12 * r.M21)
                   - r.M01 * (r.M10 * r.M22 - r.M12 * r.M20)
                   + r.M02 * (r.M10 * r.M21 - r.M11 * r.M20);
        Assert(Math.Abs(det - 1.0) < 1e-6,
            $"refined rotation has determinant {det}; reflection guard failed");
    }

    private static Polyline MakeTwistedCurve()
    {
        // A non-planar polyline: helix-like, but bounded so Z stays small.
        // Bounded enough that the panel's LocalFrame is well-defined yet
        // the contour is genuinely 3D (RMS > 0.5 mm).
        var poly = new Polyline();
        for (int i = 0; i <= 24; i++)
        {
            double t = i * 0.25;
            poly.Add(new Point3d(Math.Cos(t) * 10, Math.Sin(t) * 10, t * 0.5));
        }
        return poly;
    }

    private static Polyline TransformPolyline(Polyline src, Transform t)
    {
        var dst = new Polyline();
        for (int i = 0; i < src.Count; i++)
        {
            var p = src[i];
            p.Transform(t);
            dst.Add(p);
        }
        return dst;
    }

    private static Segment MakeSegment3D(string id, Polyline poly)
    {
        // ICP doesn't read the signatures; minimal valid arrays suffice.
        var sig = new double[64];
        var curvature = new double[64];
        var torsion = new double[64];
        return new Segment(
            id, 0, poly,
            poly.First.DistanceTo(poly.Last),
            0.0,
            +1,
            sig, curvature, torsion,
            panelPlanarityRms: 2.0);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
