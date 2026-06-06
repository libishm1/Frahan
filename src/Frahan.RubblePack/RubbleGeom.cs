#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.GH.RubblePack
{
    /// <summary>
    /// Shared geometry helpers for the rubble-carving components. All
    /// enclosure tests are TRUE containment: every block vertex must be
    /// inside the closed stone mesh (Mesh.IsPointInside, parity ray-cast).
    /// net48-safe (no Span, no HashCode.Combine).
    /// </summary>
    internal static class RubbleGeom
    {
        /// <summary>Absolute mesh volume (orientation-robust).</summary>
        public static double Volume(Mesh m)
        {
            if (m == null) return 0.0;
            var v = VolumeMassProperties.Compute(m);
            return v != null ? Math.Abs(v.Volume) : 0.0;
        }

        public static double[] SortedDims(BoundingBox bb)
        {
            var d = new[] { bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y, bb.Max.Z - bb.Min.Z };
            Array.Sort(d);
            return d;
        }

        /// <summary>
        /// The 24 proper rotations of the cube group as axis-permutation
        /// transforms (signed permutations with determinant +1).
        /// </summary>
        public static List<Transform> ProperRotations()
        {
            int[][] perms =
            {
                new[] {0,1,2}, new[] {1,2,0}, new[] {2,0,1},
                new[] {0,2,1}, new[] {1,0,2}, new[] {2,1,0}
            };
            int[] parity = { 1, 1, 1, -1, -1, -1 };
            var rots = new List<Transform>();
            for (int pi = 0; pi < 6; pi++)
                for (int s = 0; s < 8; s++)
                {
                    int s0 = (s & 1) == 0 ? 1 : -1;
                    int s1 = (s & 2) == 0 ? 1 : -1;
                    int s2 = (s & 4) == 0 ? 1 : -1;
                    if (s0 * s1 * s2 * parity[pi] != 1) continue;
                    var t = Transform.Identity;
                    t.M00 = t.M01 = t.M02 = 0;
                    t.M10 = t.M11 = t.M12 = 0;
                    t.M20 = t.M21 = t.M22 = 0;
                    var pp = perms[pi];
                    t[0, pp[0]] = s0;
                    t[1, pp[1]] = s1;
                    t[2, pp[2]] = s2;
                    rots.Add(t);
                }
            return rots;
        }

        /// <summary>
        /// Count how many of the given vertices fall OUTSIDE the stone after
        /// the transform. Early-exits once the count reaches <paramref name="limit"/>.
        /// </summary>
        public static int OutsideCount(Point3d[] verts, Transform xf, Mesh stone, int limit)
        {
            int c = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                var p = verts[i];
                p.Transform(xf);
                if (!stone.IsPointInside(p, 1e-6, false))
                {
                    c++;
                    if (c >= limit) return c;
                }
            }
            return c;
        }

        public static Point3d[] VertexArray(Mesh m)
        {
            var vs = new Point3d[m.Vertices.Count];
            for (int i = 0; i < vs.Length; i++)
            {
                var p = m.Vertices[i];
                vs[i] = new Point3d(p.X, p.Y, p.Z);
            }
            return vs;
        }

        /// <summary>Rotation about cube centre that maps a transform's rotation but keeps centroid.</summary>
        public static Transform RotateAbout(Transform rot, Point3d centre)
        {
            return Transform.Translation((Vector3d)centre) * rot *
                   Transform.Translation(new Vector3d(-centre.X, -centre.Y, -centre.Z));
        }
    }
}
