#nullable disable
using System;
using Frahan.EdgeMatching;
using Rhino;
using Rhino.Geometry;

namespace Frahan.Tests;

// Needs Rhino runtime (Curve.ClosestPoint, Polyline.ToPolylineCurve).
static class EdgeMatchingIcp2DTests
{
    public static void Refine_IdentityOnSamePolyline_ReturnsZeroResidual()
    {
        var poly = MakeSineSegment();
        var seg = MakeSegment("S", poly);
        var panel = new Panel("S", poly.ToPolylineCurve(), PanelKind.Frame);
        panel.IsAnchored = true;

        var icp = new ConstrainedIcp2D(new IcpOptions { MaxIterations = 20 });
        var refined = icp.Refine(seg, seg, panel, Transform.Identity);

        Assert(refined.Residual < 1e-6,
            $"expected near-zero residual on identity match, got {refined.Residual}");
    }

    public static void Refine_PerturbedTransform_RecoversTruth()
    {
        double theta = RhinoMath.ToRadians(3.0);
        var truth = Transform.Multiply(
            Transform.Translation(50, 30, 0),
            Transform.Rotation(theta, Vector3d.ZAxis, Point3d.Origin));

        var polyA = MakeSineSegment();
        var polyB = TransformPolyline(polyA, truth);

        var aSeg = MakeSegment("A", polyA);
        var bSeg = MakeSegment("B", polyB);
        var panelB = new Panel("B", polyB.ToPolylineCurve(), PanelKind.Frame);
        panelB.IsAnchored = true;

        // Initial = truth perturbed by ~1 mm + ~0.5°.
        double perturbTheta = RhinoMath.ToRadians(0.5);
        var perturb = Transform.Multiply(
            Transform.Translation(1.0, -1.0, 0),
            Transform.Rotation(perturbTheta, Vector3d.ZAxis, Point3d.Origin));
        var initial = Transform.Multiply(perturb, truth);

        var icp = new ConstrainedIcp2D(new IcpOptions { MaxIterations = 100, TranslationTol = 1e-6 });
        var refined = icp.Refine(aSeg, bSeg, panelB, initial);

        Assert(refined.Residual < 0.1,
            $"expected sub-0.1 mm residual after refinement, got {refined.Residual}");

        // Refined transform should land within 0.2 mm of truth on translation.
        double dx = refined.AontoB.M03 - truth.M03;
        double dy = refined.AontoB.M13 - truth.M13;
        double dTrans = Math.Sqrt(dx * dx + dy * dy);
        Assert(dTrans < 0.2,
            $"expected ICP translation within 0.2 mm of truth, got {dTrans}");
    }

    private static Polyline MakeSineSegment()
    {
        var poly = new Polyline();
        for (int i = 0; i <= 20; i++)
            poly.Add(new Point3d(i, Math.Sin(i * 0.5), 0));
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

    private static Segment MakeSegment(string id, Polyline poly)
    {
        var sig = new double[64];
        var curvature = new double[64];
        return new Segment(
            id, 0, poly,
            poly.First.DistanceTo(poly.Last),
            0.0,
            +1,
            sig, curvature, null);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
