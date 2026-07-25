// Compile-only shims for the RhinoCommon-native geometry methods the Soft-ICP
// kernel calls ONLY inside its penetration (non-penetration) term:
//
//   Mesh.IsPointInside(Point3d, double, bool)   (ApplyPenetrationTargets / MeasureMaxPenetration)
//   Mesh.ClosestPoint(Point3d) -> Point3d       (surface redirect + depth)
//   Curve.Contains(Point3d, Plane, double)       (2D contour inside-test)
//   Curve.ClosestPoint(Point3d, out double)      (2D contour surface point)
//
// These live in RhinoCommon (they require the native Rhino compute kernel) and
// are ABSENT from the Rhino3dm NuGet the suite uses for headless value types
// (verified by reflection: Mesh has no IsPointInside/ClosestPoint, Curve has no
// Contains/ClosestPoint(out)). SoftIcpRefiner.cs / SoftIcpLbfgs.cs reference
// them, so the file will not compile against Rhino3dm without these symbols.
//
// The Soft-ICP verification (SoftIcpTests) exercises ONLY the CPD contact +
// weighted-Kabsch EM path, with every Fragment.Solid = null and Contour2D =
// null, so the penetration branches are skipped at runtime and these methods
// are NEVER invoked. They exist purely to satisfy the compiler for the
// unexercised branch (exactly the Stubs/ philosophy: a symbol the linked kernel
// references but the tests never run). Each body throws so any accidental
// invocation is loud rather than silently wrong.
//
// As C# extension methods in the Rhino.Geometry namespace, they are only
// selected when NO instance method of that name applies — which is the case
// for these four on the Rhino3dm types — so they never shadow real behaviour.

using System;
using Rhino.Geometry;

namespace Rhino.Geometry
{
    internal static class RhinoCommonPenetrationShims
    {
        public static bool IsPointInside(this Mesh mesh, Point3d point, double tolerance, bool strictlyIn)
            => throw new NotSupportedException(
                "Mesh.IsPointInside is a RhinoCommon-native method; the headless Soft-ICP " +
                "verification runs with Solid=null so it is never reached.");

        public static Point3d ClosestPoint(this Mesh mesh, Point3d point)
            => throw new NotSupportedException(
                "Mesh.ClosestPoint is a RhinoCommon-native method; the headless Soft-ICP " +
                "verification runs with Solid=null so it is never reached.");

        public static PointContainment Contains(this Curve curve, Point3d point, Plane plane, double tolerance)
            => throw new NotSupportedException(
                "Curve.Contains is a RhinoCommon-native method; the headless Soft-ICP " +
                "verification runs with Contour2D=null so it is never reached.");

        public static bool ClosestPoint(this Curve curve, Point3d point, out double t)
            => throw new NotSupportedException(
                "Curve.ClosestPoint(out double) is a RhinoCommon-native method; the headless " +
                "Soft-ICP verification runs with Contour2D=null so it is never reached.");
    }
}
