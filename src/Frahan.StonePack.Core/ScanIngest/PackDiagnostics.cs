#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Core.ScanIngest;

// =============================================================================
// PackDiagnostics — Phase F5 of the UX architecture report §7.7.C scan
// ingest rollout. Three diagnostics for the 3D-packing chain:
//
//   PerStoneOverlap   — fraction of each stone's vertices that lie inside
//                       another stone. Cheap stand-in for volumetric
//                       overlap (avoids mesh-mesh Boolean cost).
//   CentreOfMassInContainer — projects each stone's centroid onto the
//                       container's XY footprint and checks containment.
//   PileStability     — simple CoM-over-support-polygon check: a stone is
//                       stable if its centroid's XY projection lies within
//                       the union of the XY footprints of the stones it
//                       rests on (or the ground, when at the floor).
//
// All math is pure managed and uses RhinoCommon's managed Mesh /
// Point3d / Curve / Polyline API. No native shim, no third-party deps.
//
// These are pragmatic-not-perfect diagnostics — they catch the obvious
// failures (penetration, tipped stones, unsupported overhangs) without
// the cost of a full Boolean / RBE solve. For full physics use the
// existing Frahan.Masonry MasonryStabilityRbeComponent.
// =============================================================================

public static class PackDiagnostics
{
    /// <summary>
    /// For each placed mesh, count how many of its vertices lie strictly
    /// inside any *other* placed mesh. Returns a fraction in [0, 1].
    /// A small non-zero value (≤ 1 %) is usually just edge-touching;
    /// a large value (≥ 10 %) means real penetration.
    /// </summary>
    public static double[] PerStoneOverlap(IReadOnlyList<Mesh> placed,
        double tolerance = 1e-6)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        int n = placed.Count;
        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            var mi = placed[i];
            if (mi == null || mi.Vertices.Count == 0) { result[i] = 0.0; continue; }
            int inside = 0;
            int vc = mi.Vertices.Count;
            for (int v = 0; v < vc; v++)
            {
                var p = (Point3d)mi.Vertices[v];
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    var mj = placed[j];
                    if (mj == null) continue;
                    if (!mj.IsClosed) continue; // open meshes have no inside
                    if (mj.IsPointInside(p, tolerance, false))
                    {
                        inside++;
                        break; // count once per vertex
                    }
                }
            }
            result[i] = (double)inside / vc;
        }
        return result;
    }

    /// <summary>
    /// Per-stone centre-of-mass check. The CoM is taken as the centroid
    /// of the mesh's vertex set (a cheap approximation; for true volume
    /// centroid use RhinoCommon's <see cref="VolumeMassProperties"/>).
    /// </summary>
    /// <param name="placed">Placed stone meshes.</param>
    /// <param name="container">A closed Mesh defining the container.</param>
    /// <returns>Per-stone pair: (inside, com).</returns>
    public static (bool[] InsideContainer, Point3d[] CentresOfMass) CentreOfMassInContainer(
        IReadOnlyList<Mesh> placed, Mesh container, double tolerance = 1e-6)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        if (container == null) throw new ArgumentNullException(nameof(container));
        if (!container.IsClosed)
            throw new ArgumentException("Container mesh must be closed.", nameof(container));

        int n = placed.Count;
        var inside = new bool[n];
        var coms = new Point3d[n];
        for (int i = 0; i < n; i++)
        {
            var m = placed[i];
            if (m == null) { inside[i] = false; coms[i] = Point3d.Unset; continue; }
            coms[i] = VertexCentroid(m);
            inside[i] = container.IsPointInside(coms[i], tolerance, false);
        }
        return (inside, coms);
    }

    /// <summary>
    /// Per-stone pile-stability check. A stone is "stable" if either:
    ///   (a) its CoM's XY projection lies inside its own footprint AND its
    ///       footprint touches the ground plane (Z ≤ Z_floor + ε), OR
    ///   (b) its CoM's XY projection lies inside the *union* of the XY
    ///       footprints of the stones below it (within ε in Z).
    ///
    /// This is a quick geometric stability proxy, not a full RBE solve.
    /// Falling stones (CoM outside all supports) get the "unstable" verdict.
    /// </summary>
    /// <param name="placed">Placed stone meshes.</param>
    /// <param name="up">World up vector (default world Z+).</param>
    /// <param name="floorZ">Z of the floor plane.</param>
    /// <param name="zTolerance">How close to a stone's top a candidate
    /// supporter must be (model units).</param>
    public static (bool[] Stable, int[] FallingIds) PileStability(
        IReadOnlyList<Mesh> placed,
        Vector3d up = default,
        double floorZ = 0.0,
        double zTolerance = 1e-3)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        // World Z+ default if up is the zero vector.
        if (up.IsZero) up = Vector3d.ZAxis;

        int n = placed.Count;
        var stable = new bool[n];
        var falling = new List<int>();

        // Pre-compute AABBs and centroids.
        var bbox = new BoundingBox[n];
        var com = new Point3d[n];
        for (int i = 0; i < n; i++)
        {
            if (placed[i] == null) continue;
            bbox[i] = placed[i].GetBoundingBox(true);
            com[i] = VertexCentroid(placed[i]);
        }

        for (int i = 0; i < n; i++)
        {
            var mi = placed[i];
            if (mi == null) { stable[i] = false; continue; }

            // Test (a): grounded.
            double zmin = bbox[i].Min.Z;
            if (zmin <= floorZ + zTolerance)
            {
                // CoM must lie inside the stone's own XY footprint.
                if (PointInBoxXy(com[i], bbox[i]))
                {
                    stable[i] = true;
                    continue;
                }
            }

            // Test (b): supported by lower stones.
            //
            // A stone j supports i when:
            //   - j's top face Z (bbox[j].Max.Z) lies within zTolerance of
            //     i's bottom face Z (bbox[i].Min.Z), and
            //   - j's XY AABB overlaps i's CoM XY projection.
            // We accumulate supporter XY AABBs into a polygonal hull
            // approximation (just the union of their XY rectangles) and
            // accept i as stable when the CoM XY is inside any one of them.
            bool supported = false;
            for (int j = 0; j < n; j++)
            {
                if (j == i || placed[j] == null) continue;
                double topJ = bbox[j].Max.Z;
                if (Math.Abs(topJ - zmin) > zTolerance) continue;
                if (PointInBoxXy(com[i], bbox[j])) { supported = true; break; }
            }
            stable[i] = supported;
            if (!supported) falling.Add(i);
        }
        return (stable, falling.ToArray());
    }

    /// <summary>Mean of all mesh vertices (cheap centroid).</summary>
    public static Point3d VertexCentroid(Mesh m)
    {
        if (m == null) throw new ArgumentNullException(nameof(m));
        int n = m.Vertices.Count;
        if (n == 0) return Point3d.Origin;
        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < n; i++)
        {
            var v = m.Vertices[i];
            cx += v.X; cy += v.Y; cz += v.Z;
        }
        return new Point3d(cx / n, cy / n, cz / n);
    }

    private static bool PointInBoxXy(Point3d p, BoundingBox b) =>
        p.X >= b.Min.X && p.X <= b.Max.X &&
        p.Y >= b.Min.Y && p.Y <= b.Max.Y;
}
