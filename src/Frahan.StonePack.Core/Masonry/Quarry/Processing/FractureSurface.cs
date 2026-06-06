#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Core.ScanIngest;       // ReconstructionNative (geogram / CGAL shims)

namespace Frahan.Masonry.Quarry.Processing;

// =============================================================================
// FractureSurface -- fracture LINES -> fracture SURFACE meshes.
//
// Two construction paths:
//  (1) LOFT (managed, dependency-free): an ordered fracture polyline extruded
//      along strike, or adjacent parallel section-lines lofted across a survey
//      grid. The surface ORIENTATION FOLLOWS the reflector (sub-horizontal ->
//      sub-horizontal sheet, dipping -> dipping), NOT a forced vertical sheet.
//      One line gives a trace (strike assumed); a GRID gives the true surface.
//  (2) RECONSTRUCT from an unordered fracture point cloud -> a triangulated
//      surface via the GEOGRAM-first reconstruction (Kazhdan screened-Poisson in
//      geogram; CGAL advancing-front fallback, which suits OPEN fracture sheets).
//      Reuses the existing native shims -- geogram preferred per project policy;
//      DAPComputationalGeometry was evaluated and NOT adopted (duplicates these).
//
// Output is a flat (Vertices xyz, Triangles index) mesh -- Rhino-free; a thin GH
// adapter converts it to a Rhino Mesh. Heavy 3D geometry stays in geogram/CGAL.
// =============================================================================

public sealed class FractureSurfaceMesh
{
    public double[] Vertices;     // flat [x0,y0,z0, x1,y1,z1, ...]
    public int[] Triangles;       // flat [a0,b0,c0, ...]
    public int VertexCount => Vertices.Length / 3;
    public int TriangleCount => Triangles.Length / 3;
}

public static class FractureSurface
{
    /// <summary>Extrude one fracture polyline along +Y (strike) into a ribbon surface that
    /// follows the reflector depth profile. z = -depth (down is negative).</summary>
    /// <param name="line">traced fracture line (X[], Depth[] in metres).</param>
    /// <param name="strikeExtent">along-strike (Y) extent in metres.</param>
    /// <param name="steps">strike subdivisions (>=1).</param>
    public static FractureSurfaceMesh Loft(FractureLine line, double strikeExtent, int steps = 1)
    {
        if (line == null) throw new ArgumentNullException(nameof(line));
        int np = line.PointCount;
        if (np < 2) throw new ArgumentException("fracture line needs >= 2 points");
        steps = Math.Max(1, steps);
        int rows = steps + 1;
        var verts = new double[np * rows * 3];
        int vi = 0;
        for (int r = 0; r < rows; r++)
        {
            double y = strikeExtent * r / steps;
            for (int p = 0; p < np; p++)
            {
                verts[vi++] = line.X[p];
                verts[vi++] = y;
                verts[vi++] = -line.Depth[p];
            }
        }
        var tris = new List<int>((np - 1) * steps * 6);
        for (int r = 0; r < steps; r++)
            for (int p = 0; p < np - 1; p++)
            {
                int a = r * np + p, b = r * np + p + 1, c = (r + 1) * np + p + 1, d = (r + 1) * np + p;
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }
        return new FractureSurfaceMesh { Vertices = verts, Triangles = tris.ToArray() };
    }

    /// <summary>Loft a fracture across a SURVEY GRID: parallel section-lines (each at a Y
    /// offset) lofted into one surface. Each line is resampled onto a common X grid (linear
    /// interp) so corresponding points connect. This is the true 3D fracture surface.</summary>
    /// <param name="linesByY">(yOffset, line) for each parallel section, ordered by Y.</param>
    /// <param name="nx">common X-grid resolution.</param>
    public static FractureSurfaceMesh LoftAcrossLines(IReadOnlyList<(double Y, FractureLine Line)> linesByY, int nx = 32)
    {
        if (linesByY == null || linesByY.Count < 2)
            throw new ArgumentException("need >= 2 parallel section-lines to loft a surface");
        // common X range = overlap of all lines
        double x0 = double.MinValue, x1 = double.MaxValue;
        foreach (var (_, L) in linesByY)
        {
            x0 = Math.Max(x0, MinX(L)); x1 = Math.Min(x1, MaxX(L));
        }
        if (x1 <= x0) throw new ArgumentException("section-lines do not overlap in X");
        int rows = linesByY.Count;
        var verts = new double[rows * nx * 3];
        int vi = 0;
        for (int r = 0; r < rows; r++)
        {
            var (y, L) = linesByY[r];
            for (int j = 0; j < nx; j++)
            {
                double x = x0 + (x1 - x0) * j / (nx - 1);
                double depth = InterpDepth(L, x);
                verts[vi++] = x; verts[vi++] = y; verts[vi++] = -depth;
            }
        }
        var tris = new List<int>((rows - 1) * (nx - 1) * 6);
        for (int r = 0; r < rows - 1; r++)
            for (int j = 0; j < nx - 1; j++)
            {
                int a = r * nx + j, b = r * nx + j + 1, c = (r + 1) * nx + j + 1, d = (r + 1) * nx + j;
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }
        return new FractureSurfaceMesh { Vertices = verts, Triangles = tris.ToArray() };
    }

    /// <summary>Reconstruct a fracture surface from an UNORDERED 3D fracture point cloud,
    /// GEOGRAM-first: estimate normals (CGAL) then geogram screened-Poisson; on failure fall
    /// back to CGAL advancing-front (better for OPEN sheets). Returns false + error if the
    /// native shims are unavailable. Use for multi-line/grid clouds where the picks are not
    /// an ordered polyline.</summary>
    public static bool TryReconstructFromCloud(double[] cloudXyz, out FractureSurfaceMesh mesh,
        out string error, bool preferGeogram = true)
    {
        mesh = null; error = null;
        if (cloudXyz == null || cloudXyz.Length % 3 != 0 || cloudXyz.Length < 12)
        { error = "cloud must be non-null, divisible by 3, with >= 4 points"; return false; }

        if (preferGeogram &&
            ReconstructionNative.TryEstimateNormals(cloudXyz, 16, out var normals, out var nerr) &&
            ReconstructionNative.TryPoisson(cloudXyz, normals, 8, 1.5, out var poi, out var perr))
        {
            mesh = new FractureSurfaceMesh { Vertices = poi.Vertices, Triangles = poi.Triangles };
            return true;
        }
        // fallback: CGAL advancing-front (open-surface friendly)
        if (ReconstructionNative.TryAdvancingFront(cloudXyz, 5.0, 0.52, out var af, out var aferr))
        {
            mesh = new FractureSurfaceMesh { Vertices = af.Vertices, Triangles = af.Triangles };
            return true;
        }
        error = "geogram/CGAL reconstruction unavailable (rebuild native shims): " + aferr;
        return false;
    }

    private static double MinX(FractureLine L) { double m = double.MaxValue; foreach (var x in L.X) if (x < m) m = x; return m; }
    private static double MaxX(FractureLine L) { double m = double.MinValue; foreach (var x in L.X) if (x > m) m = x; return m; }

    // linear-interpolate the line's depth at world x (clamped to its endpoints)
    private static double InterpDepth(FractureLine L, double x)
    {
        int n = L.PointCount;
        if (x <= L.X[0]) return L.Depth[0];
        if (x >= L.X[n - 1]) return L.Depth[n - 1];
        for (int k = 0; k < n - 1; k++)
        {
            if (x >= L.X[k] && x <= L.X[k + 1])
            {
                double t = (x - L.X[k]) / (L.X[k + 1] - L.X[k] + 1e-12);
                return L.Depth[k] + t * (L.Depth[k + 1] - L.Depth[k]);
            }
        }
        return L.Depth[n - 1];
    }
}
