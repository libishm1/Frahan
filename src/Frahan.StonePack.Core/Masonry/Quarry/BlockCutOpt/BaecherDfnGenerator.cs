#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// BaecherDfnGenerator -- a STOCHASTIC, finite-persistence discrete fracture
// network (Baecher et al. 1977 disc model), the rigorous alternative to the
// infinite-plane JointSetDfnGenerator. Each fracture is a finite circular disc:
//   - centre   ~ uniform Poisson point process in the domain,
//   - pole     ~ Fisher (1953) distribution about the set mean (dispersion kappa),
//   - radius   ~ lognormal (mean persistence + CV), the trace-length distribution.
// Intensity is set by P10 = 1/spacing (frequency along the set normal); the disc
// count is N = P10 * V / (pi * E[r^2]). The discs are fan-triangulated into a
// single PlyMesh consumable by BlockCutOptSolver -- finite persistence then falls
// out naturally (a block in a sparsely-fractured pocket survives because no disc
// reaches it), unlike the infinite-plane model which always fully partitions the
// bench.
//
// References: Baecher, Lanney & Einstein (1977); Fisher (1953); Priest (1993)
// DFN; Dershowitz & Herda (1992) P10/P32 intensity. Deterministic given seed.
// =============================================================================

public sealed class BaecherSet
{
    public BaecherSet(double nx, double ny, double nz, double kappa,
                      double spacing, double meanDiameter, double diameterCv = 1.0)
    {
        double L = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (L < 1e-12) throw new ArgumentException("zero normal");
        Nx = nx / L; Ny = ny / L; Nz = nz / L;
        Kappa = Math.Max(0.01, kappa);
        Spacing = spacing > 0 ? spacing : throw new ArgumentOutOfRangeException(nameof(spacing));
        MeanDiameter = meanDiameter > 0 ? meanDiameter : throw new ArgumentOutOfRangeException(nameof(meanDiameter));
        DiameterCv = Math.Max(0.0, diameterCv);
    }
    public double Nx, Ny, Nz, Kappa, Spacing, MeanDiameter, DiameterCv;
}

public sealed class BaecherDfnResult
{
    public PlyMesh Mesh;
    public int FractureCount;
    public double P32;            // fracture area per unit volume (1/m)
    public IReadOnlyList<int> PerSetCount;
}

public static class BaecherDfnGenerator
{
    private const int HardCapPerSet = 200000;   // runaway backstop

    public static BaecherDfnResult Generate(
        IReadOnlyList<BaecherSet> sets, BoundingBox3 domain, int seed, int discSegments = 14)
    {
        if (sets == null) throw new ArgumentNullException(nameof(sets));
        if (domain == null) throw new ArgumentNullException(nameof(domain));
        if (discSegments < 6) discSegments = 6;

        var rng = new Random(seed);
        double x0 = domain.MinX, y0 = domain.MinY, z0 = domain.MinZ;
        double dx = domain.MaxX - domain.MinX, dy = domain.MaxY - domain.MinY, dz = domain.MaxZ - domain.MinZ;
        double V = Math.Max(1e-9, dx * dy * dz);

        var verts = new List<double>(4096);
        var tris = new List<int>(8192);
        var perSet = new List<int>(sets.Count);
        double totalArea = 0;
        int count = 0;

        foreach (var s in sets)
        {
            double rMean = s.MeanDiameter * 0.5;
            double er2 = rMean * rMean * (1.0 + s.DiameterCv * s.DiameterCv); // E[r^2], lognormal
            double p10 = 1.0 / s.Spacing;
            int n = (int)Math.Round(p10 * V / (Math.PI * Math.Max(1e-9, er2)));
            n = Math.Max(0, Math.Min(HardCapPerSet, n));
            perSet.Add(n);

            // a stable in-plane basis perpendicular to the set mean pole
            BasisFromNormal(s.Nx, s.Ny, s.Nz, out double[] u0, out double[] v0);

            for (int i = 0; i < n; i++)
            {
                double cx = x0 + dx * rng.NextDouble();
                double cy = y0 + dy * rng.NextDouble();
                double cz = z0 + dz * rng.NextDouble();
                // Fisher-sampled pole about the set mean
                FisherSample(rng, s.Nx, s.Ny, s.Nz, s.Kappa, out double pnx, out double pny, out double pnz);
                // in-plane basis of THIS disc (perp to its own pole)
                BasisFromNormal(pnx, pny, pnz, out double[] u, out double[] v);
                double r = Math.Max(1e-3, 0.5 * LogNormal(rng, s.MeanDiameter, s.DiameterCv));
                totalArea += Math.PI * r * r;

                int baseIdx = verts.Count / 3;
                verts.Add(cx); verts.Add(cy); verts.Add(cz);               // centre
                for (int k = 0; k < discSegments; k++)
                {
                    double a = 2.0 * Math.PI * k / discSegments;
                    double ca = Math.Cos(a) * r, sa = Math.Sin(a) * r;
                    verts.Add(cx + u[0] * ca + v[0] * sa);
                    verts.Add(cy + u[1] * ca + v[1] * sa);
                    verts.Add(cz + u[2] * ca + v[2] * sa);
                }
                for (int k = 0; k < discSegments; k++)
                {
                    tris.Add(baseIdx);
                    tris.Add(baseIdx + 1 + k);
                    tris.Add(baseIdx + 1 + (k + 1) % discSegments);
                }
                count++;
            }
        }

        if (tris.Count == 0)
        {
            verts.Add(1e9); verts.Add(1e9); verts.Add(1e9);
            verts.Add(1e9 + 1e-3); verts.Add(1e9); verts.Add(1e9);
            verts.Add(1e9); verts.Add(1e9 + 1e-3); verts.Add(1e9);
            tris.Add(0); tris.Add(1); tris.Add(2);
        }

        return new BaecherDfnResult
        {
            Mesh = new PlyMesh(verts, tris, null),
            FractureCount = count,
            P32 = totalArea / V,
            PerSetCount = perSet
        };
    }

    // orthonormal (u,v) spanning the plane with the given unit normal n
    private static void BasisFromNormal(double nx, double ny, double nz, out double[] u, out double[] v)
    {
        double ax = Math.Abs(nx), ay = Math.Abs(ny), az = Math.Abs(nz);
        // pick the axis least aligned with n to cross with
        double tx = 0, ty = 0, tz = 0;
        if (ax <= ay && ax <= az) tx = 1; else if (ay <= az) ty = 1; else tz = 1;
        // u = normalize(n x t)
        double ux = ny * tz - nz * ty, uy = nz * tx - nx * tz, uz = nx * ty - ny * tx;
        double ul = Math.Sqrt(ux * ux + uy * uy + uz * uz); if (ul < 1e-12) ul = 1;
        ux /= ul; uy /= ul; uz /= ul;
        // v = n x u
        double vx = ny * uz - nz * uy, vy = nz * ux - nx * uz, vz = nx * uy - ny * ux;
        u = new[] { ux, uy, uz }; v = new[] { vx, vy, vz };
    }

    // sample a unit vector from Fisher(mean=(mx,my,mz), kappa)
    private static void FisherSample(Random rng, double mx, double my, double mz, double kappa,
        out double ox, out double oy, out double oz)
    {
        double u1 = rng.NextDouble(), u2 = rng.NextDouble();
        double w = 1.0 + Math.Log(u1 + (1.0 - u1) * Math.Exp(-2.0 * kappa)) / kappa; // colatitude cosine
        if (w > 1) w = 1; if (w < -1) w = -1;
        double s = Math.Sqrt(Math.Max(0.0, 1.0 - w * w));
        double phi = 2.0 * Math.PI * u2;
        double lx = s * Math.Cos(phi), ly = s * Math.Sin(phi), lz = w; // in frame where mean=z
        BasisFromNormal(mx, my, mz, out double[] e1, out double[] e2);
        ox = e1[0] * lx + e2[0] * ly + mx * lz;
        oy = e1[1] * lx + e2[1] * ly + my * lz;
        oz = e1[2] * lx + e2[2] * ly + mz * lz;
        double L = Math.Sqrt(ox * ox + oy * oy + oz * oz); if (L < 1e-12) L = 1;
        ox /= L; oy /= L; oz /= L;
    }

    // lognormal with the given arithmetic mean and coefficient of variation
    private static double LogNormal(Random rng, double mean, double cv)
    {
        if (cv <= 1e-9) return mean;
        double sigma2 = Math.Log(1.0 + cv * cv);
        double mu = Math.Log(mean) - 0.5 * sigma2;
        // Box-Muller
        double u1 = Math.Max(1e-12, rng.NextDouble()), u2 = rng.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return Math.Exp(mu + Math.Sqrt(sigma2) * z);
    }
}
