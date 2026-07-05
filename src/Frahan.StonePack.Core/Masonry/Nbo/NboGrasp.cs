#nullable disable
using System;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Frahan.Masonry.Nbo;

// =============================================================================
// NboGrasp -- the planner -> robot bridge (Layer-0, route-agnostic). It turns an
// NBO placement (a stone + a mesh-local->world Transform) into the robot TCP
// target frames + a UR-style pose, via a GRASP MODEL that knows how the gripper
// holds the stone.
//
// Why this belongs in Frahan, not a robot library: the grasp/tool offset that
// maps "stone placement pose" -> "gripper TCP frame" depends on the stone's
// geometry (where to grip a balanced, top-accessible point), which only the
// planner knows. The robot backend (Robots/visose, UnderAutomation, compas_fab)
// owns IK + trajectory + execution downstream of these frames.
//
// Grasp model (top-pick): the gripper approaches from ABOVE, centred over the
// centre of mass (balanced lift), on the face OPPOSITE the resting face. The
// TCP frame Z axis points DOWN into the stone (the tool approach direction, UR
// convention), so a placed stone's TCP target has Z ~ world -Z. The grasp is
// stored in the stone's LOCAL (analysed) frame so the SAME grasp transforms to
// both the pick pose (stone at source) and the place pose (stone placed).
//
//   pickFrame_world  = source_transform   * graspLocal
//   placeFrame_world = placement_transform* graspLocal
//   frame_in_base    = ChangeBasis(world -> robotBase) * frame_world
//   approach_frame   = frame offset back along -Z (up) by a clearance
//   UR pose          = ToUrPose(frame) = p[x,y,z, rx,ry,rz] (m, axis-angle)
//
// Rhino-bound (Plane/Transform), deterministic, no extra deps.
// =============================================================================

/// <summary>How the gripper holds one stone, in the stone's local frame.</summary>
public sealed class StoneGrasp
{
    /// <summary>Grasp frame in the stone's analysed/local coords. Origin = the
    /// top contact point over the CoM; Z = tool approach (DOWN into the stone);
    /// X = the stone long axis projected into the grip plane (jaw-open axis).</summary>
    public Plane GraspLocal;
    /// <summary>Stone extent across the jaw axis (grasp Y) -- the opening a
    /// parallel gripper must span.</summary>
    public double GripWidth;
    /// <summary>Stone extent along the jaw-open axis (grasp X).</summary>
    public double GripLength;
}

/// <summary>A UR-style pose: position (m) + axis-angle rotation vector (rad).</summary>
public struct RobotPose
{
    public double X, Y, Z, Rx, Ry, Rz;
    public override string ToString()
        => $"p[{X:F4}, {Y:F4}, {Z:F4}, {Rx:F4}, {Ry:F4}, {Rz:F4}]";
}

public static class NboGrasp
{
    /// <summary>
    /// Top-pick grasp: gripper centred over the CoM on the face opposite the
    /// resting face, approaching straight down. Jaw X follows the stone long
    /// axis (projected into the grip plane).
    /// </summary>
    public static StoneGrasp TopPick(StoneShape shape, DominantFace restFace)
    {
        if (shape == null) throw new ArgumentNullException(nameof(shape));
        if (restFace == null) throw new ArgumentNullException(nameof(restFace));

        Vector3d down = restFace.Normal;          // resting-face outward normal = down when placed
        if (!down.Unitize()) down = -Vector3d.ZAxis;
        Vector3d up = -down;

        // top contact point: ray from CoM straight up to the hull's top surface.
        Point3d origin = shape.Com + up * (0.5 * Math.Max(1e-6, Projo(shape, up)));
        double t = Intersection.MeshRay(shape.Hull, new Ray3d(shape.Com, up));
        if (t >= 0.0) origin = shape.Com + up * t;

        // jaw-open axis = long axis projected perpendicular to the approach.
        Vector3d x = shape.AxisLong - down * (shape.AxisLong * down);
        if (!x.Unitize())
        {
            x = shape.AxisMid - down * (shape.AxisMid * down);
            if (!x.Unitize()) x = Perp(down);
        }
        Vector3d y = Vector3d.CrossProduct(down, x);  // x cross y = down  => frame Z = down
        y.Unitize();

        var grasp = new StoneGrasp
        {
            GraspLocal = new Plane(origin, x, y),
            GripWidth = ExtentAlong(shape.Hull, y),
            GripLength = ExtentAlong(shape.Hull, x),
        };
        return grasp;
    }

    /// <summary>The world TCP target to PLACE the stone (grasp carried by the placement).</summary>
    public static Plane PlaceFrame(StoneGrasp grasp, Transform placement)
    {
        if (grasp == null) throw new ArgumentNullException(nameof(grasp));
        var p = grasp.GraspLocal; p.Transform(placement); return p;
    }

    /// <summary>The world TCP target to PICK the stone at its source pose
    /// (identity for an inventory stone sitting at its modelled location).</summary>
    public static Plane PickFrame(StoneGrasp grasp, Transform source)
    {
        if (grasp == null) throw new ArgumentNullException(nameof(grasp));
        var p = grasp.GraspLocal; p.Transform(source); return p;
    }

    /// <summary>Express a world frame in the robot base frame (<paramref name="robotBase"/>
    /// = the robot base plane in world coords). The result is what you send to the robot.</summary>
    public static Plane InBase(Plane world, Plane robotBase)
    {
        var t = Transform.ChangeBasis(Plane.WorldXY, robotBase);
        var p = world; p.Transform(t); return p;
    }

    /// <summary>Offset a TCP frame back along its approach (up, -Z of the frame
    /// since frame Z points down) by <paramref name="clearance"/> -- the pre-grasp
    /// / retract waypoint above the stone.</summary>
    public static Plane WithApproach(Plane tcp, double clearance)
    {
        var p = tcp; p.Origin = p.Origin - p.ZAxis * clearance; return p;
    }

    /// <summary>Rhino plane -> UR pose: position (m) + axis-angle rotation vector
    /// (the UR <c>p[x,y,z,rx,ry,rz]</c> convention). The plane's axes are the
    /// columns of the rotation matrix.</summary>
    public static RobotPose ToUrPose(Plane p)
    {
        Vector3d X = p.XAxis, Y = p.YAxis, Z = p.ZAxis;
        // rotation matrix columns = frame axes.
        double m00 = X.X, m01 = Y.X, m02 = Z.X;
        double m10 = X.Y, m11 = Y.Y, m12 = Z.Y;
        double m20 = X.Z, m21 = Y.Z, m22 = Z.Z;

        double trace = m00 + m11 + m22;
        double angle = Math.Acos(Clamp((trace - 1.0) * 0.5, -1.0, 1.0));

        double rx, ry, rz;
        if (angle < 1e-9)
        {
            rx = ry = rz = 0.0;                        // no rotation
        }
        else if (Math.PI - angle < 1e-6)
        {
            // 180 deg: axis from the largest diagonal term of (R + I)/2.
            double xx = (m00 + 1.0) * 0.5, yy = (m11 + 1.0) * 0.5, zz = (m22 + 1.0) * 0.5;
            double ax, ay, az;
            if (xx >= yy && xx >= zz)
            {
                ax = Math.Sqrt(Math.Max(0.0, xx)); ay = (m01 + m10) * 0.25 / ax; az = (m02 + m20) * 0.25 / ax;
            }
            else if (yy >= zz)
            {
                ay = Math.Sqrt(Math.Max(0.0, yy)); ax = (m01 + m10) * 0.25 / ay; az = (m12 + m21) * 0.25 / ay;
            }
            else
            {
                az = Math.Sqrt(Math.Max(0.0, zz)); ax = (m02 + m20) * 0.25 / az; ay = (m12 + m21) * 0.25 / az;
            }
            double n = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (n < 1e-12) { ax = 1; ay = az = 0; n = 1; }
            rx = ax / n * angle; ry = ay / n * angle; rz = az / n * angle;
        }
        else
        {
            double s = 2.0 * Math.Sin(angle);
            double ax = (m21 - m12) / s, ay = (m02 - m20) / s, az = (m10 - m01) / s;
            rx = ax * angle; ry = ay * angle; rz = az * angle;
        }

        return new RobotPose { X = p.OriginX, Y = p.OriginY, Z = p.OriginZ, Rx = rx, Ry = ry, Rz = rz };
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

    private static double ExtentAlong(Mesh hull, Vector3d axis)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < hull.Vertices.Count; i++)
        {
            Point3d p = hull.Vertices[i];
            double s = p.X * axis.X + p.Y * axis.Y + p.Z * axis.Z;
            if (s < lo) lo = s; if (s > hi) hi = s;
        }
        return hi - lo;
    }

    // half-extent of the hull about the CoM along `dir` (fallback grasp height).
    private static double Projo(StoneShape shape, Vector3d dir)
    {
        double hi = 0.0;
        for (int i = 0; i < shape.Hull.Vertices.Count; i++)
        {
            Point3d p = shape.Hull.Vertices[i];
            double s = (p.X - shape.Com.X) * dir.X + (p.Y - shape.Com.Y) * dir.Y + (p.Z - shape.Com.Z) * dir.Z;
            if (s > hi) hi = s;
        }
        return hi;
    }

    private static Vector3d Perp(Vector3d v)
    {
        Vector3d a = Math.Abs(v.X) < 0.9 ? Vector3d.XAxis : Vector3d.YAxis;
        var p = Vector3d.CrossProduct(v, a); p.Unitize(); return p;
    }
}
