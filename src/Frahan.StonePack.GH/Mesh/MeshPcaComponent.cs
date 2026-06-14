#nullable disable
using System;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // MeshPcaComponent — principal-component analysis of a mesh's vertex
    // cloud. Emits the three principal axes (sorted by variance, longest
    // first) plus a Plane oriented along PC1 / PC2 at the centroid, plus
    // the three principal extents (min-to-max along each PC axis).
    //
    // Use when a quarry block isn't axis-aligned: the PCA Plane gives the
    // "natural" frame so you can transform the block before feeding it
    // to Ashlar Pack (which assumes blocks have axis-aligned principal
    // dimensions).
    //
    // Algorithm: covariance matrix of vertex coords, 3x3 symmetric Jacobi
    // eigendecomposition. Pure managed; no Rhino math required.
    //
    // ComponentGuid: BCDEF012-3456-789A-BCDE-F0123456789A
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Mesh &gt; Mesh PCA.
    /// Principal-component analysis of a mesh's vertex cloud.
    /// </summary>
        [Algorithm("Principal component analysis (covariance eigendecomposition)", "Frahan-original", Note = "Textbook 3x3 covariance + Jacobi eigensolver; no specific algorithmic paper")]
        [DesignApplication(
        "Principal-component analysis of a mesh's vertex cloud",
        DesignFlow.Bridges,
        Precedent = "Standard 3x3 covariance + Jacobi eigendecomposition")]
    public sealed class MeshPcaComponent : FrahanComponentBase
    {
        public MeshPcaComponent()
            : base(
                "Mesh PCA", "PCA",
                "Principal-component analysis of a mesh's vertex cloud. Returns " +
                "a Plane aligned to the natural axes (PC1 = longest, PC2 = " +
                "second, PC3 = shortest = plane normal), plus the three extent " +
                "lengths along each axis. Use to align rough quarry blocks. " +
                "Frahan-original method.",
                "Frahan", "Mesh")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("BCDEF012-3456-789A-BCDE-F0123456789A");

        protected override Bitmap Icon => IconProvider.Load("FrameBuilder.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M",
                "Input mesh.",
                GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddPlaneParameter("Frame", "F",
                "Plane at the centroid, X-axis = PC1 (longest), Y-axis = PC2, " +
                "Z-axis = PC3 (shortest, = plane normal).",
                GH_ParamAccess.item);
            p.AddNumberParameter("Length 1", "L1",
                "Extent along PC1 (longest principal axis).",
                GH_ParamAccess.item);
            p.AddNumberParameter("Length 2", "L2",
                "Extent along PC2.",
                GH_ParamAccess.item);
            p.AddNumberParameter("Length 3", "L3",
                "Extent along PC3 (shortest, = thickness through the plane normal).",
                GH_ParamAccess.item);
            p.AddPointParameter("Centroid", "C",
                "Centroid of the vertex cloud (unweighted average).",
                GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh mesh = null;
            if (!da.GetData(0, ref mesh) || mesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No mesh provided.");
                return;
            }
            int n = mesh.Vertices.Count;
            if (n < 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Mesh needs at least 3 vertices, got {n}.");
                return;
            }

            // Centroid.
            double cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < n; i++)
            {
                var v = mesh.Vertices[i];
                cx += v.X; cy += v.Y; cz += v.Z;
            }
            cx /= n; cy /= n; cz /= n;

            // Covariance matrix.
            double sxx = 0, syy = 0, szz = 0, sxy = 0, sxz = 0, syz = 0;
            for (int i = 0; i < n; i++)
            {
                var v = mesh.Vertices[i];
                double dx = v.X - cx, dy = v.Y - cy, dz = v.Z - cz;
                sxx += dx * dx; syy += dy * dy; szz += dz * dz;
                sxy += dx * dy; sxz += dx * dz; syz += dy * dz;
            }
            sxx /= n; syy /= n; szz /= n;
            sxy /= n; sxz /= n; syz /= n;

            var cov = new double[,]
            {
                { sxx, sxy, sxz },
                { sxy, syy, syz },
                { sxz, syz, szz },
            };

            JacobiEigen3(cov, out double[] eigVals, out double[][] eigVecs);

            // Sort descending by |eigVal|.
            int[] order = { 0, 1, 2 };
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                    if (Math.Abs(eigVals[order[i]]) < Math.Abs(eigVals[order[j]]))
                    {
                        int t = order[i]; order[i] = order[j]; order[j] = t;
                    }

            Vector3d pc1 = new Vector3d(eigVecs[order[0]][0], eigVecs[order[0]][1], eigVecs[order[0]][2]);
            Vector3d pc2 = new Vector3d(eigVecs[order[1]][0], eigVecs[order[1]][1], eigVecs[order[1]][2]);
            // PC3 = PC1 × PC2 (ensures right-handed frame).
            Vector3d pc3 = Vector3d.CrossProduct(pc1, pc2);
            pc3.Unitize();
            // Re-orthogonalise PC2 so the frame is exactly orthogonal.
            pc2 = Vector3d.CrossProduct(pc3, pc1);
            pc2.Unitize();
            pc1.Unitize();

            var centre = new Point3d(cx, cy, cz);
            var frame = new Plane(centre, pc1, pc2);

            // Compute extents along each PC axis (max - min of dot products).
            ComputeExtent(mesh, centre, pc1, out double l1);
            ComputeExtent(mesh, centre, pc2, out double l2);
            ComputeExtent(mesh, centre, pc3, out double l3);

            da.SetData(0, frame);
            da.SetData(1, l1);
            da.SetData(2, l2);
            da.SetData(3, l3);
            da.SetData(4, centre);
        }

        private static void ComputeExtent(Mesh mesh, Point3d centre, Vector3d axis, out double extent)
        {
            double tMin = double.PositiveInfinity, tMax = double.NegativeInfinity;
            int n = mesh.Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                var v = mesh.Vertices[i];
                double dx = v.X - centre.X, dy = v.Y - centre.Y, dz = v.Z - centre.Z;
                double t = dx * axis.X + dy * axis.Y + dz * axis.Z;
                if (t < tMin) tMin = t;
                if (t > tMax) tMax = t;
            }
            extent = tMax - tMin;
        }

        // ─── 3×3 symmetric Jacobi eigendecomposition ──────────────────────────
        private static void JacobiEigen3(double[,] a, out double[] eigVals, out double[][] eigVecs)
        {
            // Working copy.
            double[,] A = (double[,])a.Clone();
            double[,] V = new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

            const int maxSweeps = 50;
            for (int sweep = 0; sweep < maxSweeps; sweep++)
            {
                double off = Math.Abs(A[0, 1]) + Math.Abs(A[0, 2]) + Math.Abs(A[1, 2]);
                if (off < 1e-15) break;

                JacobiRotate(A, V, 0, 1);
                JacobiRotate(A, V, 0, 2);
                JacobiRotate(A, V, 1, 2);
            }

            eigVals = new double[] { A[0, 0], A[1, 1], A[2, 2] };
            eigVecs = new double[3][];
            for (int k = 0; k < 3; k++)
            {
                eigVecs[k] = new double[] { V[0, k], V[1, k], V[2, k] };
            }
        }

        private static void JacobiRotate(double[,] A, double[,] V, int p, int q)
        {
            double apq = A[p, q];
            if (Math.Abs(apq) < 1e-20) return;
            double app = A[p, p];
            double aqq = A[q, q];
            double theta = (aqq - app) / (2.0 * apq);
            double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(1.0 + theta * theta));
            if (theta == 0.0) t = 1.0;
            double c = 1.0 / Math.Sqrt(1.0 + t * t);
            double s = t * c;

            A[p, p] = app - t * apq;
            A[q, q] = aqq + t * apq;
            A[p, q] = 0.0;
            A[q, p] = 0.0;

            for (int r = 0; r < 3; r++)
            {
                if (r != p && r != q)
                {
                    double arp = A[r, p];
                    double arq = A[r, q];
                    A[r, p] = c * arp - s * arq;
                    A[p, r] = A[r, p];
                    A[r, q] = c * arq + s * arp;
                    A[q, r] = A[r, q];
                }
                double vrp = V[r, p];
                double vrq = V[r, q];
                V[r, p] = c * vrp - s * vrq;
                V[r, q] = c * vrq + s * vrp;
            }
        }
    }
}
