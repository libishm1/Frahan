#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Fractures;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// CuttingGrid -- generates the rotated and translated grid of candidate blocks
// over a rectangular tested area, then filters to candidates whose full
// footprint lies inside the tested area.
//
// Procedure (BlockCutOpt 2020, sections 2.4 and 2.6):
//   1. Compute geometric centroid c of the tested area.
//   2. Construct an axis-aligned grid of cells of size (Lx, Ly, Lz)
//      = (dim_block_x + kerf, dim_block_y + kerf, dim_block_z + kerf)
//      centered on c. The vertical block height is single-stratum
//      (no z displacement search).
//   3. Rotate the grid horizontally by psi around the vertical axis through c.
//   4. Translate the rotated grid by (dx, dy).
//   5. Clip: keep only OBBs whose 8 corners lie inside the tested area
//      polygon (rectangular AABB in v1; arbitrary polygon when needed).
//
// References:
//   - Elkarmoty, Bondua, Bruno 2020. Resources Policy 68:101761. Section 2.
//   - `D:\BlockCutOpt_paper.md` section 2.6 (2D illustration).
//   - `D:\code_ws\wiki\papers\equations_and_diagrams\08_synthesis_and_optimum_algorithm.md` section 6.4.
//
// v1 scope: rectangular tested area only (an AABB in X, Y at fixed Z range).
// Future: polygonal tested area + sub-division (improvement I5 of synthesis).
// =============================================================================

public static class CuttingGrid
{
    /// <summary>
    /// Phase 1-compatible overload: psi-only horizontal rotation, vertical W.
    /// </summary>
    public static IReadOnlyList<OrientedBlock> Generate(
        BoundingBox3 testedArea,
        double blockSizeX,
        double blockSizeY,
        double blockSizeZ,
        double kerf,
        double psiRad,
        double dx,
        double dy)
        => GenerateTilted(testedArea, blockSizeX, blockSizeY, blockSizeZ, kerf, psiRad, 0.0, 0.0, dx, dy);

    /// <summary>
    /// I1: full 3D rotation (psi, theta, phi). Rotation order is R_z(psi) *
    /// R_x(theta) * R_y(phi), applied to the local (X, Y, Z) axes of the OBB.
    /// Pass theta = phi = 0 to recover the Phase 1 psi-only grid.
    /// </summary>
    public static IReadOnlyList<OrientedBlock> GenerateTilted(
        BoundingBox3 testedArea,
        double blockSizeX,
        double blockSizeY,
        double blockSizeZ,
        double kerf,
        double psiRad,
        double thetaRad,
        double phiRad,
        double dx,
        double dy)
    {
        if (testedArea == null) throw new ArgumentNullException(nameof(testedArea));
        if (!(blockSizeX > 0)) throw new ArgumentOutOfRangeException(nameof(blockSizeX));
        if (!(blockSizeY > 0)) throw new ArgumentOutOfRangeException(nameof(blockSizeY));
        if (!(blockSizeZ > 0)) throw new ArgumentOutOfRangeException(nameof(blockSizeZ));
        if (kerf < 0) throw new ArgumentOutOfRangeException(nameof(kerf));

        double cx = testedArea.CenterX;
        double cy = testedArea.CenterY;
        double cz = testedArea.CenterZ;

        // kerf-inflated cell pitch
        double pitchX = blockSizeX + kerf;
        double pitchY = blockSizeY + kerf;

        double halfX = 0.5 * blockSizeX; // inner half (used for the OBB body, NOT pitch)
        double halfY = 0.5 * blockSizeY;
        double halfZ = 0.5 * blockSizeZ;

        // I1: full 3D rotation. Build U, V, W from (psi, theta, phi).
        // R = R_z(psi) * R_x(theta) * R_y(phi)
        // U = R * (1, 0, 0); V = R * (0, 1, 0); W = R * (0, 0, 1)
        double cp = Math.Cos(psiRad), sp = Math.Sin(psiRad);
        double ct = Math.Cos(thetaRad), st = Math.Sin(thetaRad);
        double cf = Math.Cos(phiRad), sf = Math.Sin(phiRad);

        // R_y(phi) acting on (1,0,0): ( cf, 0, -sf )
        // then R_x(theta) on that:    ( cf,  sf*st,  -sf*ct )
        // then R_z(psi) on that:      ( cp*cf - sp*sf*st, sp*cf + cp*sf*st, -sf*ct )
        double uX = cp * cf - sp * sf * st;
        double uY = sp * cf + cp * sf * st;
        double uZ = -sf * ct;

        // R_y(phi) acting on (0,1,0): ( 0, 1, 0 )
        // then R_x(theta) on that:    ( 0, ct, st )
        // then R_z(psi) on that:      ( -sp*ct, cp*ct, st )
        double vX = -sp * ct;
        double vY = cp * ct;
        double vZ = st;

        // R_y(phi) acting on (0,0,1): ( sf, 0,  cf )
        // then R_x(theta) on that:    ( sf, -cf*st, cf*ct )
        // then R_z(psi) on that:      ( cp*sf + sp*cf*st, sp*sf - cp*cf*st, cf*ct )
        double wX = cp * sf + sp * cf * st;
        double wY = sp * sf - cp * cf * st;
        double wZ = cf * ct;

        // bounding circle of the tested area, used to bound the grid index range
        double dxArea = testedArea.SizeX;
        double dyArea = testedArea.SizeY;
        double bound = 0.5 * Math.Sqrt(dxArea * dxArea + dyArea * dyArea) + Math.Max(pitchX, pitchY);
        int idxRadiusX = (int)Math.Ceiling(bound / pitchX) + 1;
        int idxRadiusY = (int)Math.Ceiling(bound / pitchY) + 1;

        var result = new List<OrientedBlock>(checked((2 * idxRadiusX + 1) * (2 * idxRadiusY + 1)));

        for (int i = -idxRadiusX; i <= idxRadiusX; i++)
        for (int j = -idxRadiusY; j <= idxRadiusY; j++)
        {
            // center of cell (i, j) in the rotated, translated grid
            double localX = i * pitchX;
            double localY = j * pitchY;
            double bcX = cx + localX * uX + localY * vX + dx;
            double bcY = cy + localX * uY + localY * vY + dy;
            double bcZ = cz + localX * uZ + localY * vZ;

            // verify the four horizontal corners are all inside the tested AABB.
            // For tilted grids, full 8-corner AABB containment is too strict;
            // we use the horizontal footprint at the OBB centre Z as the
            // clipping test (matches BlockCutOpt 2020 footprint convention).
            bool inside = true;
            for (int corner = 0; corner < 4 && inside; corner++)
            {
                double sX = ((corner & 1) != 0) ? +halfX : -halfX;
                double sY = ((corner & 2) != 0) ? +halfY : -halfY;
                double wx = bcX + sX * uX + sY * vX;
                double wy = bcY + sX * uY + sY * vY;
                if (wx < testedArea.MinX || wx > testedArea.MaxX) inside = false;
                else if (wy < testedArea.MinY || wy > testedArea.MaxY) inside = false;
            }
            if (!inside) continue;

            result.Add(new OrientedBlock(
                bcX, bcY, bcZ,
                uX, uY, uZ,
                vX, vY, vZ,
                wX, wY, wZ,
                halfX, halfY, halfZ));
        }

        return result;
    }
}
