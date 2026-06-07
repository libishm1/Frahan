#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Core.Voussoir;

// =============================================================================
// VoussoirCellFactory — generate stereotomic voussoir CELLS (the cut-stone
// geometry), the missing front end of the Voussoir pipeline.
//
// The existing trio INGESTS voussoirs (from an external plugin) then matches
// and packs them. This factory GENERATES them from first principles, so the
// whole top-down flow lives inside Frahan:
//
//   VoussoirCellFactory.BuildArch / BuildPendentiveVault   (THIS)
//     -> VoussoirAssembly (typed)
//       -> VoussoirStoneMatcher (Hungarian) / Rubble Evolved Fit
//         -> CGAL trim (digital ravalement)
//
// Geometry is grounded in wiki/research/stereotomy_voussoir_from_rubble.md:
//   - RADIAL BED-JOINT RULE (Frezier 1737; Monge geometrie descriptive 1798):
//     in an arch the bed joints are normal to the intrados and, for a circular
//     arch, point at the centre of curvature. Each wedge turns the thrust
//     aside, stone to stone (Hooke 1675 inverted catenary; Heyman safe theorem).
//   - Each voussoir is an 8-vertex wedge solid (a hexahedron) bounded by:
//     intrados (douelle), extrados, two radial bed/head joints, two lateral
//     joints. The extrados is the intrados offset by the ring thickness along
//     the outward normal.
//   - For the pendentive (sail) dome the cells follow the sphere's lines of
//     curvature (Monge): a planar square grid lifted onto the sphere, each
//     cell extruded radially by the shell thickness.
//
// Profiles supported: Semicircular, Segmental, Pointed (equilateral), Catenary.
// "Catenary / pointed are a drop-in change of the intrados curve" — the wiki
// note made concrete: every profile builds an intrados Curve, then ONE shared
// path stations it by arc length and lofts the wedges.
//
// Rhino-free? No — this part of Core already depends on RhinoCommon (see
// VoussoirAssembly / VoussoirRecord). The math here is pure trig + Curve
// sampling; no Rhino document is needed (works in the headless harness).
// =============================================================================

/// <summary>Arch intrados profile families.</summary>
public enum ArchProfile
{
    /// <summary>Half circle; springers on the spring line; apex at radius.</summary>
    Semicircular = 0,
    /// <summary>Circular arc of a given included angle (less than 180).</summary>
    Segmental = 1,
    /// <summary>Two-centred equilateral pointed (Gothic) arch.</summary>
    Pointed = 2,
    /// <summary>Inverted catenary (the funicular line of a pure arch).</summary>
    Catenary = 3,
}

/// <summary>The generated voussoir geometry plus the typed assembly that feeds
/// the matcher.</summary>
public sealed class VoussoirCellResult
{
    /// <summary>One closed wedge mesh per voussoir, in install order.</summary>
    public List<Mesh> Cells = new List<Mesh>();
    /// <summary>Per-voussoir lower bed-joint plane (radial). For the springer it
    /// is the springing plane.</summary>
    public List<Plane> BedPlanes = new List<Plane>();
    /// <summary>Per-voussoir centroid.</summary>
    public List<Point3d> Centroids = new List<Point3d>();
    /// <summary>Per-voussoir mesh volume (absolute).</summary>
    public List<double> Volumes = new List<double>();
    /// <summary>Index of the keystone voussoir (-1 if not applicable).</summary>
    public int KeystoneIndex = -1;
    /// <summary>Sum of all voussoir volumes.</summary>
    public double TotalVolume;
    /// <summary>The intrados (soffit) curve, for reference / display.</summary>
    public Curve Intrados;
    /// <summary>The typed assembly, ready for VoussoirStoneMatcher.</summary>
    public VoussoirAssembly Assembly;
    /// <summary>Human-readable build summary.</summary>
    public string Report = "";
}

public static class VoussoirCellFactory
{
    // ------------------------------------------------------------------ ARCH

    /// <summary>Build an arch as N radial voussoir cells.</summary>
    /// <param name="profile">Intrados profile family.</param>
    /// <param name="radius">Intrados radius (m). For Catenary it is the span proxy
    /// used when span is left at 0.</param>
    /// <param name="ringThickness">Radial ring thickness intrados->extrados (m).</param>
    /// <param name="width">Out-of-plane voussoir width (m).</param>
    /// <param name="count">Number of voussoirs (>= 1).</param>
    /// <param name="includedAngleDeg">Included angle for Segmental (deg, 0..180).
    /// Ignored for the other profiles.</param>
    /// <param name="rise">Apex rise for Catenary (m). If &lt;= 0 a default of
    /// radius is used.</param>
    /// <param name="basePoint">Translation applied to the whole arch (the arch is
    /// built in the world XZ plane, width along Y, springers on z=0).</param>
    public static VoussoirCellResult BuildArch(
        ArchProfile profile,
        double radius,
        double ringThickness,
        double width,
        int count,
        double includedAngleDeg,
        double rise,
        Point3d basePoint)
    {
        var res = new VoussoirCellResult();
        if (radius <= 0) throw new ArgumentException("radius must be positive.", nameof(radius));
        if (ringThickness <= 0) throw new ArgumentException("ringThickness must be positive.", nameof(ringThickness));
        if (width <= 0) throw new ArgumentException("width must be positive.", nameof(width));
        if (count < 1) throw new ArgumentException("count must be >= 1.", nameof(count));

        Curve intrados = BuildIntrados(profile, radius, includedAngleDeg, rise, out double span, out double apexHeight);
        res.Intrados = intrados;

        // Station the intrados by equal arc length: count+1 stations, count wedges.
        double[] t = intrados.DivideByCount(count, true);
        if (t == null || t.Length != count + 1)
            throw new InvalidOperationException("Failed to station the intrados curve.");

        var pin = new Point3d[count + 1];
        var nout = new Vector3d[count + 1];
        var centroid = Point3d.Origin;
        for (int k = 0; k <= count; k++)
        {
            pin[k] = intrados.PointAt(t[k]);
            centroid += pin[k];
        }
        centroid /= (count + 1);

        // Outward normal per station: rotate the in-plane tangent 90 deg, orient
        // away from the intrados centroid (the arch interior). For a circular arc
        // this equals the exact radial direction.
        for (int k = 0; k <= count; k++)
        {
            Vector3d tan = intrados.TangentAt(t[k]);
            tan.Z = tan.Z; tan.Y = 0; tan.Unitize();
            var n = new Vector3d(tan.Z, 0, -tan.X);
            if (n.Length < 1e-9) n = Vector3d.ZAxis;
            n.Unitize();
            var radial = pin[k] - centroid;
            if (n * radial < 0) n = -n;
            nout[k] = n;
        }

        var widthAxis = Vector3d.YAxis;
        double hw = width * 0.5;
        var shift = basePoint - Point3d.Origin;

        for (int i = 0; i < count; i++)
        {
            // Intrados / extrados corners at stations i and i+1.
            Point3d inA = pin[i] + shift;
            Point3d inB = pin[i + 1] + shift;
            Point3d exA = inA + nout[i] * ringThickness;
            Point3d exB = inB + nout[i + 1] * ringThickness;

            // Front ring (y = -hw): intrados A, extrados A, extrados B, intrados B.
            var front = new[]
            {
                inA - widthAxis * hw,
                exA - widthAxis * hw,
                exB - widthAxis * hw,
                inB - widthAxis * hw,
            };
            var back = new[]
            {
                inA + widthAxis * hw,
                exA + widthAxis * hw,
                exB + widthAxis * hw,
                inB + widthAxis * hw,
            };
            Mesh cell = MakeHexahedron(front, back);
            res.Cells.Add(cell);

            // Lower bed-joint plane at station i (radial). x axis = outward normal,
            // y axis = width axis, so the plane normal is the tangential thrust dir.
            var bedOrigin = (inA + exA) * 0.5;
            var bed = new Plane(bedOrigin, nout[i], widthAxis);
            res.BedPlanes.Add(bed);
        }

        // Keystone = voussoir whose centre is nearest the apex.
        FinalizeResult(res, "arch", profile.ToString(), wedgeLoadAxisFromChord: pin, shift: shift);

        res.KeystoneIndex = NearestToApex(res.Centroids, apexHeight + shift.Z);
        if (res.KeystoneIndex >= 0 && res.KeystoneIndex < res.Assembly.Voussoirs.Count)
        {
            var ks = (VoussoirRecord)res.Assembly.Voussoirs[res.KeystoneIndex];
            ks.JointClass = "key";
        }

        res.Report =
            $"Arch [{profile}]: {count} voussoirs, intrados R={radius:F2} m, ring t={ringThickness:F2} m, " +
            $"width={width:F2} m, span={span:F2} m, rise={apexHeight:F2} m. " +
            $"Total volume {res.TotalVolume:F4} m^3. Keystone index {res.KeystoneIndex}. " +
            $"Radial bed joints (Frezier/Monge). All cells closed: {AllClosed(res.Cells)}.";
        return res;
    }

    // -------------------------------------------------------- PENDENTIVE VAULT

    /// <summary>Build a pendentive (sail) dome: a sphere over a square plan,
    /// tessellated on a grid into voussoir cells along its lines of curvature,
    /// each extruded radially by the shell thickness.</summary>
    /// <param name="sphereRadius">Sphere radius R (m).</param>
    /// <param name="squareHalfWidth">Half the side of the square plan (m). Must
    /// satisfy 2*halfWidth^2 &lt; R^2 so the corners lie on the sphere.</param>
    /// <param name="shellThickness">Radial shell thickness (m).</param>
    /// <param name="gridU">Cells across the plan in U (>= 1).</param>
    /// <param name="gridV">Cells across the plan in V (>= 1).</param>
    /// <param name="dropToGround">Translate so the springing corners rest on z=0.</param>
    /// <param name="basePoint">Translation applied to the whole vault.</param>
    public static VoussoirCellResult BuildPendentiveVault(
        double sphereRadius,
        double squareHalfWidth,
        double shellThickness,
        int gridU,
        int gridV,
        bool dropToGround,
        Point3d basePoint)
    {
        var res = new VoussoirCellResult();
        if (sphereRadius <= 0) throw new ArgumentException("sphereRadius must be positive.", nameof(sphereRadius));
        if (squareHalfWidth <= 0) throw new ArgumentException("squareHalfWidth must be positive.", nameof(squareHalfWidth));
        if (shellThickness <= 0) throw new ArgumentException("shellThickness must be positive.", nameof(shellThickness));
        if (gridU < 1 || gridV < 1) throw new ArgumentException("gridU and gridV must be >= 1.");
        double corner2 = 2.0 * squareHalfWidth * squareHalfWidth;
        if (corner2 >= sphereRadius * sphereRadius)
            throw new ArgumentException(
                "Square corners fall off the sphere (2*halfWidth^2 >= R^2). " +
                "Increase sphereRadius or decrease squareHalfWidth.");

        double zCorner = Math.Sqrt(sphereRadius * sphereRadius - corner2); // springing z
        double zDrop = dropToGround ? zCorner : 0.0;
        var shift = (basePoint - Point3d.Origin) - Vector3d.ZAxis * zDrop;

        double rOuter = sphereRadius + shellThickness;
        double rRatio = rOuter / sphereRadius;

        // Grid of sphere intrados points P[i,j].
        var pin = new Point3d[gridU + 1, gridV + 1];
        for (int i = 0; i <= gridU; i++)
        {
            double x = -squareHalfWidth + 2.0 * squareHalfWidth * i / gridU;
            for (int j = 0; j <= gridV; j++)
            {
                double y = -squareHalfWidth + 2.0 * squareHalfWidth * j / gridV;
                double zz = sphereRadius * sphereRadius - x * x - y * y;
                if (zz < 0) zz = 0;
                double z = Math.Sqrt(zz);
                pin[i, j] = new Point3d(x, y, z) + shift;
            }
        }

        for (int i = 0; i < gridU; i++)
        {
            for (int j = 0; j < gridV; j++)
            {
                // Intrados ring (CCW in plan), extrados = radial scale about the
                // sphere centre (which sits at z = -shift.Z + 0 i.e. origin+shift).
                var c00 = pin[i, j];
                var c10 = pin[i + 1, j];
                var c11 = pin[i + 1, j + 1];
                var c01 = pin[i, j + 1];

                var front = new[] { c00, c10, c11, c01 };
                var back = new[]
                {
                    Radial(c00, shift, rRatio),
                    Radial(c10, shift, rRatio),
                    Radial(c11, shift, rRatio),
                    Radial(c01, shift, rRatio),
                };
                Mesh cell = MakeHexahedron(front, back);
                res.Cells.Add(cell);

                // Bed plane = the cell's lower (intrados) face plane: origin at the
                // intrados patch centre, normal = outward sphere radial.
                var mid = (c00 + c10 + c11 + c01) * 0.25;
                var sphereCenter = Point3d.Origin + shift;
                var radial = mid - sphereCenter;
                radial.Unitize();
                var any = c10 - c00; any.Unitize();
                var xref = Vector3d.CrossProduct(radial, any);
                if (xref.Length < 1e-9) xref = Vector3d.XAxis;
                res.BedPlanes.Add(new Plane(mid, radial));
            }
        }

        FinalizeResult(res, "pendentive", "Sphere", wedgeLoadAxisFromChord: null, shift: shift);
        res.KeystoneIndex = -1; // a vault has no single keystone

        double apex = (sphereRadius - zDrop) + shift.Z + Vector3d.ZAxis.Z; // top of intrados
        res.Report =
            $"Pendentive vault: sphere R={sphereRadius:F2} m over square 2h={2 * squareHalfWidth:F2} m, " +
            $"thickness t={shellThickness:F2} m, grid {gridU}x{gridV} = {res.Cells.Count} voussoirs. " +
            $"Springing z={zCorner:F2} m, apex rise={(sphereRadius - zCorner):F2} m, dropToGround={dropToGround}. " +
            $"Total volume {res.TotalVolume:F4} m^3. Lines-of-curvature tessellation (Monge). " +
            $"All cells closed: {AllClosed(res.Cells)}.";
        return res;
    }

    // ----------------------------------------------------------------- HELPERS

    private static Point3d Radial(Point3d p, Vector3d shift, double ratio)
    {
        // Scale p about the (shifted) sphere centre by ratio.
        var center = Point3d.Origin + shift;
        return center + (p - center) * ratio;
    }

    private static Curve BuildIntrados(
        ArchProfile profile, double radius, double includedAngleDeg, double rise,
        out double span, out double apexHeight)
    {
        switch (profile)
        {
            case ArchProfile.Semicircular:
                return BuildArcIntrados(radius, 180.0, out span, out apexHeight);
            case ArchProfile.Segmental:
            {
                double a = includedAngleDeg;
                if (a <= 0 || a >= 180) a = 120.0;
                return BuildArcIntrados(radius, a, out span, out apexHeight);
            }
            case ArchProfile.Pointed:
                return BuildPointedIntrados(radius, out span, out apexHeight);
            case ArchProfile.Catenary:
                return BuildCatenaryIntrados(radius, rise > 0 ? rise : radius, out span, out apexHeight);
            default:
                return BuildArcIntrados(radius, 180.0, out span, out apexHeight);
        }
    }

    private static Curve BuildArcIntrados(double radius, double includedAngleDeg, out double span, out double apexHeight)
    {
        double a = includedAngleDeg * Math.PI / 180.0;
        double half = a * 0.5;
        double zc = -radius * Math.Cos(half); // centre z so springers sit on z=0
        var center = new Point3d(0, 0, zc);
        var leftSpringer = new Point3d(-radius * Math.Sin(half), 0, 0);
        var rightSpringer = new Point3d(radius * Math.Sin(half), 0, 0);
        var apex = new Point3d(0, 0, zc + radius);
        span = 2.0 * radius * Math.Sin(half);
        apexHeight = apex.Z;
        // Arc through three points: left springer -> apex -> right springer.
        var arc = new Arc(leftSpringer, apex, rightSpringer);
        return arc.ToNurbsCurve();
    }

    private static Curve BuildPointedIntrados(double radius, out double span, out double apexHeight)
    {
        // Equilateral two-centred arch: span S = radius, each arc radius = S,
        // centres at the two springers, apex at S*sqrt(3)/2.
        double s = radius;
        double h = s * Math.Sqrt(3.0) * 0.5;
        span = s;
        apexHeight = h;
        var leftSpringer = new Point3d(-s * 0.5, 0, 0);
        var rightSpringer = new Point3d(s * 0.5, 0, 0);
        var apex = new Point3d(0, 0, h);
        var cr = new Point3d(s * 0.5, 0, 0);  // centre of the LEFT half
        var cl = new Point3d(-s * 0.5, 0, 0); // centre of the RIGHT half

        // Left half: from left springer (psi=180) to apex (psi=120) about cr.
        var leftMidDir = AngleDir(150.0);
        var leftMid = cr + leftMidDir * s;
        var leftArc = new Arc(leftSpringer, leftMid, apex).ToNurbsCurve();

        // Right half: from apex (psi=60) to right springer (psi=0) about cl.
        var rightMidDir = AngleDir(30.0);
        var rightMid = cl + rightMidDir * s;
        var rightArc = new Arc(apex, rightMid, rightSpringer).ToNurbsCurve();

        var poly = new PolyCurve();
        poly.Append(leftArc);
        poly.Append(rightArc);
        return poly;
    }

    private static Vector3d AngleDir(double deg)
    {
        double r = deg * Math.PI / 180.0;
        return new Vector3d(Math.Cos(r), 0, Math.Sin(r));
    }

    private static Curve BuildCatenaryIntrados(double span, double rise, out double spanOut, out double apexHeight)
    {
        // Inverted catenary of span S and rise H. Hanging chain y=a*cosh(x/a);
        // sag over [-S/2,S/2] = a*cosh(S/2a)-a. Solve a so sag = H, then invert.
        double s = span;
        double h = rise;
        double a = SolveCatenaryA(s, h);
        spanOut = s;
        apexHeight = h;
        int samples = Math.Max(24, 4);
        var pts = new List<Point3d>(samples + 1);
        for (int k = 0; k <= samples; k++)
        {
            double x = -s * 0.5 + s * k / samples;
            double z = h - (a * Math.Cosh(x / a) - a);
            if (z < 0) z = 0;
            pts.Add(new Point3d(x, 0, z));
        }
        var c = Curve.CreateInterpolatedCurve(pts, 3);
        return c ?? new PolylineCurve(pts);
    }

    private static double SolveCatenaryA(double s, double h)
    {
        // f(a) = a*cosh(s/(2a)) - a - h. Decreasing from +inf to -h; bisection.
        double lo = s * 1e-4;
        double hi = s * 1e4;
        Func<double, double> f = a => a * Math.Cosh(s / (2.0 * a)) - a - h;
        double flo = f(lo);
        double fhi = f(hi);
        if (flo == 0) return lo;
        if (fhi == 0) return hi;
        // flo should be > 0, fhi < 0.
        for (int it = 0; it < 200; it++)
        {
            double mid = 0.5 * (lo + hi);
            double fm = f(mid);
            if (Math.Abs(fm) < 1e-9 || (hi - lo) < 1e-9) return mid;
            if ((fm > 0) == (flo > 0)) { lo = mid; flo = fm; }
            else { hi = mid; }
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>Build a closed welded hexahedron from two matching quad rings
    /// (front[4] and back[4], in corresponding order). Prism connectivity:
    /// 2 ring faces + 4 side faces. Normals unified outward.</summary>
    public static Mesh MakeHexahedron(Point3d[] front, Point3d[] back)
    {
        if (front == null || back == null || front.Length != 4 || back.Length != 4)
            throw new ArgumentException("front and back must each have 4 points.");
        var m = new Mesh();
        for (int i = 0; i < 4; i++) m.Vertices.Add(front[i]);
        for (int i = 0; i < 4; i++) m.Vertices.Add(back[i]);
        // Ring faces.
        m.Faces.AddFace(0, 1, 2, 3);   // front
        m.Faces.AddFace(4, 7, 6, 5);   // back (reversed)
        // Side faces around the ring.
        m.Faces.AddFace(0, 4, 5, 1);
        m.Faces.AddFace(1, 5, 6, 2);
        m.Faces.AddFace(2, 6, 7, 3);
        m.Faces.AddFace(3, 7, 4, 0);
        m.Vertices.CombineIdentical(true, true);
        m.Faces.CullDegenerateFaces();
        m.RebuildNormals();
        m.UnifyNormals();
        // Force OUTWARD orientation (positive signed volume). UnifyNormals only
        // makes the winding consistent, not necessarily outward; an inward solid
        // has negative signed volume and silently breaks CGAL booleans (the cell
        // is read as "everything except the cell"), so the trim returns the stock
        // minus the cell instead of the carved voussoir. Flip if inverted.
        if (m.Volume() < 0) m.Flip(true, true, true);
        m.RebuildNormals();
        m.Compact();
        return m;
    }

    private static void FinalizeResult(
        VoussoirCellResult res, string kind, string profile,
        Point3d[] wedgeLoadAxisFromChord, Vector3d shift)
    {
        var voussoirs = new List<VoussoirRecord>(res.Cells.Count);
        double total = 0;
        for (int i = 0; i < res.Cells.Count; i++)
        {
            var cell = res.Cells[i];
            double vol = Math.Abs(cell.Volume());
            total += vol;
            var aabb = cell.GetBoundingBox(true);
            var box = new Box(aabb);
            var c = aabb.Center;
            res.Centroids.Add(c);
            res.Volumes.Add(vol);

            Vector3d loadAxis = Vector3d.ZAxis;
            if (wedgeLoadAxisFromChord != null && i + 1 < wedgeLoadAxisFromChord.Length)
            {
                loadAxis = (wedgeLoadAxisFromChord[i + 1] - wedgeLoadAxisFromChord[i]);
                if (loadAxis.Length > 1e-9) loadAxis.Unitize(); else loadAxis = Vector3d.ZAxis;
            }

            var rec = new VoussoirRecord
            {
                Id = "V" + i.ToString("D3"),
                Geometry = cell,
                OrientedBoundingBox = box,
                Volume = vol,
                Centroid = c,
                BedPlane = i < res.BedPlanes.Count ? res.BedPlanes[i] : Plane.Unset,
                HeadPlane = Plane.Unset,
                LoadAxis = loadAxis,
                JointClass = "void",
                SequenceIndex = i,
                Label = kind + " voussoir " + i,
            };
            voussoirs.Add(rec);
        }
        res.TotalVolume = total;

        // Springers (lowest course) marked as ground anchors.
        var ground = new List<int>();
        double zmin = double.MaxValue, zmax = double.MinValue;
        foreach (var c in res.Centroids) { zmin = Math.Min(zmin, c.Z); zmax = Math.Max(zmax, c.Z); }
        double band = zmin + 0.05 * Math.Max(zmax - zmin, 1e-9);
        for (int i = 0; i < voussoirs.Count; i++)
            if (voussoirs[i].Centroid.Z <= band) { voussoirs[i].JointClass = "ground"; ground.Add(i); }

        res.Assembly = new VoussoirAssembly
        {
            Voussoirs = voussoirs,
            AdjacencyPairs = new List<(int, int)>(),
            GroundAnchorIndices = ground,
            Provenance = "VoussoirCellFactory " + kind + " (" + profile + ")",
        };
    }

    private static int NearestToApex(List<Point3d> centroids, double apexZ)
    {
        int best = -1;
        double bestDx = double.MaxValue;
        for (int i = 0; i < centroids.Count; i++)
        {
            // Apex voussoir: highest, nearest the plane of symmetry (x=0).
            double dz = apexZ - centroids[i].Z;
            double dx = Math.Abs(centroids[i].X) + Math.Abs(dz);
            if (centroids[i].Z > 0 && dx < bestDx) { bestDx = dx; best = i; }
        }
        return best;
    }

    private static bool AllClosed(List<Mesh> meshes)
    {
        foreach (var m in meshes) if (m == null || !m.IsClosed) return false;
        return true;
    }
}
