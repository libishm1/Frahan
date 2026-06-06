#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Frahan.Masonry.Fractures;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// VtuWriter -- ParaView .vtu UnstructuredGrid output for BlockCutOpt results.
//
// Two cell-sets per VTU file (matching the BlockCutOpt 2020 paper Figure 3
// convention used in the limestone and granite case studies):
//   - non-intersected blocks (cell_status = 1, "orange" in ParaView)
//   - intersected blocks     (cell_status = 0, "dark red" in ParaView)
//
// Each block is a hexahedron with 8 corners (the kerf-inflated OBB).
//
// VTU XML format:
//   <VTKFile type="UnstructuredGrid" version="0.1" byte_order="LittleEndian">
//     <UnstructuredGrid>
//       <Piece NumberOfPoints="N" NumberOfCells="M">
//         <Points>
//           <DataArray type="Float64" NumberOfComponents="3" format="ascii">
//             ... 3*N coords ...
//           </DataArray>
//         </Points>
//         <Cells>
//           <DataArray type="Int32" Name="connectivity" format="ascii">
//             ... 8*M indices ...
//           </DataArray>
//           <DataArray type="Int32" Name="offsets" format="ascii">
//             8, 16, 24, ...
//           </DataArray>
//           <DataArray type="UInt8" Name="types" format="ascii">
//             12, 12, 12, ...   (12 = VTK_HEXAHEDRON)
//           </DataArray>
//         </Cells>
//         <CellData Scalars="cell_status">
//           <DataArray type="Int32" Name="cell_status" format="ascii">
//             1, 0, 1, ...
//           </DataArray>
//         </CellData>
//       </Piece>
//     </UnstructuredGrid>
//   </VTKFile>
//
// Reference: BlockCutOpt 2020, Figure 3 (limestone) and Figure 6 (granite).
// =============================================================================

public static class VtuWriter
{
    private const int VtkHexahedron = 12;
    private const int CellStatusNonIntersected = 1;
    private const int CellStatusIntersected = 0;

    /// <summary>
    /// Write a VTU file with the supplied non-intersected and intersected
    /// OrientedBlocks. The two lists are concatenated and tagged via the
    /// `cell_status` cell-data array (1 = good, 0 = intersected).
    /// </summary>
    public static void Write(
        string path,
        IReadOnlyList<OrientedBlock> nonIntersected,
        IReadOnlyList<OrientedBlock> intersected)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path", nameof(path));
        if (nonIntersected == null) throw new ArgumentNullException(nameof(nonIntersected));
        if (intersected == null) throw new ArgumentNullException(nameof(intersected));

        int totalCells = nonIntersected.Count + intersected.Count;
        int totalPoints = totalCells * 8;

        var sb = new StringBuilder(totalPoints * 60);
        var inv = CultureInfo.InvariantCulture;

        sb.AppendLine("<?xml version=\"1.0\"?>");
        sb.AppendLine("<VTKFile type=\"UnstructuredGrid\" version=\"0.1\" byte_order=\"LittleEndian\">");
        sb.AppendLine("  <UnstructuredGrid>");
        sb.AppendLine($"    <Piece NumberOfPoints=\"{totalPoints}\" NumberOfCells=\"{totalCells}\">");

        // Points
        sb.AppendLine("      <Points>");
        sb.AppendLine("        <DataArray type=\"Float64\" NumberOfComponents=\"3\" format=\"ascii\">");
        WriteCorners(sb, nonIntersected, inv);
        WriteCorners(sb, intersected, inv);
        sb.AppendLine("        </DataArray>");
        sb.AppendLine("      </Points>");

        // Cells
        sb.AppendLine("      <Cells>");
        sb.AppendLine("        <DataArray type=\"Int32\" Name=\"connectivity\" format=\"ascii\">");
        for (int i = 0; i < totalCells; i++)
        {
            int b = i * 8;
            // VTK hex ordering: 0..3 = bottom (CCW from below), 4..7 = top (CCW from below)
            // We've written the 8 corners in our own bit order (000..111). The bit order is:
            //   k=0 (000) -> -u-v-w  // 0
            //   k=1 (001) -> +u-v-w  // 1
            //   k=2 (010) -> -u+v-w  // 3
            //   k=3 (011) -> +u+v-w  // 2
            //   k=4 (100) -> -u-v+w  // 4
            //   k=5 (101) -> +u-v+w  // 5
            //   k=6 (110) -> -u+v+w  // 7
            //   k=7 (111) -> +u+v+w  // 6
            // So VTK_HEX order is (b+0, b+1, b+3, b+2, b+4, b+5, b+7, b+6)
            sb.Append($"{b + 0} {b + 1} {b + 3} {b + 2} {b + 4} {b + 5} {b + 7} {b + 6} ");
        }
        sb.AppendLine();
        sb.AppendLine("        </DataArray>");

        sb.AppendLine("        <DataArray type=\"Int32\" Name=\"offsets\" format=\"ascii\">");
        for (int i = 1; i <= totalCells; i++) sb.Append($"{i * 8} ");
        sb.AppendLine();
        sb.AppendLine("        </DataArray>");

        sb.AppendLine("        <DataArray type=\"UInt8\" Name=\"types\" format=\"ascii\">");
        for (int i = 0; i < totalCells; i++) sb.Append($"{VtkHexahedron} ");
        sb.AppendLine();
        sb.AppendLine("        </DataArray>");
        sb.AppendLine("      </Cells>");

        // CellData
        sb.AppendLine("      <CellData Scalars=\"cell_status\">");
        sb.AppendLine("        <DataArray type=\"Int32\" Name=\"cell_status\" format=\"ascii\">");
        for (int i = 0; i < nonIntersected.Count; i++) sb.Append($"{CellStatusNonIntersected} ");
        for (int i = 0; i < intersected.Count; i++) sb.Append($"{CellStatusIntersected} ");
        sb.AppendLine();
        sb.AppendLine("        </DataArray>");
        sb.AppendLine("      </CellData>");

        sb.AppendLine("    </Piece>");
        sb.AppendLine("  </UnstructuredGrid>");
        sb.AppendLine("</VTKFile>");

        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
    }

    private static void WriteCorners(
        StringBuilder sb,
        IReadOnlyList<OrientedBlock> blocks,
        CultureInfo inv)
    {
        for (int b = 0; b < blocks.Count; b++)
        {
            var obb = blocks[b];
            for (int k = 0; k < 8; k++)
            {
                double sx = ((k & 1) != 0 ? +1 : -1) * obb.HalfX;
                double sy = ((k & 2) != 0 ? +1 : -1) * obb.HalfY;
                double sz = ((k & 4) != 0 ? +1 : -1) * obb.HalfZ;
                // I1: full 3D corner using all three OBB axes
                double wx = obb.CenterX + sx * obb.UX + sy * obb.VX + sz * obb.WX;
                double wy = obb.CenterY + sx * obb.UY + sy * obb.VY + sz * obb.WY;
                double wz = obb.CenterZ + sx * obb.UZ + sy * obb.VZ + sz * obb.WZ;
                sb.AppendLine(string.Format(inv, "          {0} {1} {2}", wx, wy, wz));
            }
        }
    }

    /// <summary>
    /// Convenience: split a flat grid into non-intersected and intersected
    /// via the given BVH, then write the VTU.
    /// </summary>
    public static (int NonIntersectedCount, int IntersectedCount) WriteFromGridAndBvh(
        string path,
        IReadOnlyList<OrientedBlock> grid,
        TriangleAabbBvh bvh)
    {
        if (grid == null) throw new ArgumentNullException(nameof(grid));
        if (bvh == null) throw new ArgumentNullException(nameof(bvh));
        var good = new List<OrientedBlock>(grid.Count);
        var bad = new List<OrientedBlock>(grid.Count);
        for (int i = 0; i < grid.Count; i++)
        {
            var obb = grid[i];
            if (bvh.AnyTriangleIntersects(in obb)) bad.Add(obb);
            else good.Add(obb);
        }
        Write(path, good, bad);
        return (good.Count, bad.Count);
    }

    /// <summary>
    /// Write the Shao 2022 AMRR plane sequence as a ParaView VTU. Each cut
    /// plane is rendered as a unit-axis-aligned quad polygon centred on the
    /// step's tangent point (scaled by the bounding-sphere radius). Cell
    /// data: <c>step_index</c>, <c>removed_volume_mm3</c>, <c>cutting_time_min</c>.
    ///
    /// Renderable in ParaView: open the VTU, colour by step_index, optionally
    /// extrude by removed_volume for a 3D bar visualisation of the cut history.
    /// </summary>
    public static void WriteAmrrSequence(
        string path,
        AmrrPlanResult plan,
        double quadSizeMetres = 0.5)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path", nameof(path));
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (!(quadSizeMetres > 0)) throw new ArgumentOutOfRangeException(nameof(quadSizeMetres));

        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        int n = plan.Steps.Count;

        sb.AppendLine("<?xml version=\"1.0\"?>");
        sb.AppendLine("<VTKFile type=\"UnstructuredGrid\" version=\"0.1\" byte_order=\"LittleEndian\">");
        sb.AppendLine("  <UnstructuredGrid>");
        sb.AppendLine($"    <Piece NumberOfPoints=\"{n * 4}\" NumberOfCells=\"{n}\">");

        // Points: 4 corners of a quad in the cutting plane around each step's point
        sb.AppendLine("      <Points>");
        sb.AppendLine("        <DataArray type=\"Float64\" NumberOfComponents=\"3\" format=\"ascii\">");
        for (int s = 0; s < n; s++)
        {
            var step = plan.Steps[s];
            // build a 2D basis (e, f) in the plane
            double nx = step.PlaneNx, ny = step.PlaneNy, nz = step.PlaneNz;
            double eX, eY, eZ;
            if (Math.Abs(nz) < 0.9) { eX = -ny; eY = nx; eZ = 0.0; }
            else                    { eX = 1.0; eY = 0.0; eZ = 0.0; }
            double el = Math.Sqrt(eX * eX + eY * eY + eZ * eZ);
            if (el < 1e-12) { eX = 1; eY = 0; eZ = 0; el = 1; }
            eX /= el; eY /= el; eZ /= el;
            double fX = ny * eZ - nz * eY;
            double fY = nz * eX - nx * eZ;
            double fZ = nx * eY - ny * eX;
            double h = 0.5 * quadSizeMetres;
            var px = step.PlanePx; var py = step.PlanePy; var pz = step.PlanePz;
            for (int k = 0; k < 4; k++)
            {
                double su = ((k == 0 || k == 3) ? -h : +h);
                double sv = ((k == 0 || k == 1) ? -h : +h);
                double x = px + su * eX + sv * fX;
                double y = py + su * eY + sv * fY;
                double z = pz + su * eZ + sv * fZ;
                sb.AppendLine(string.Format(inv, "          {0} {1} {2}", x, y, z));
            }
        }
        sb.AppendLine("        </DataArray>");
        sb.AppendLine("      </Points>");

        // Cells: one VTK_QUAD per step
        sb.AppendLine("      <Cells>");
        sb.AppendLine("        <DataArray type=\"Int32\" Name=\"connectivity\" format=\"ascii\">");
        for (int s = 0; s < n; s++)
        {
            int b = s * 4;
            sb.Append($"{b} {b + 1} {b + 2} {b + 3} ");
        }
        sb.AppendLine();
        sb.AppendLine("        </DataArray>");
        sb.AppendLine("        <DataArray type=\"Int32\" Name=\"offsets\" format=\"ascii\">");
        for (int s = 1; s <= n; s++) sb.Append($"{s * 4} ");
        sb.AppendLine();
        sb.AppendLine("        </DataArray>");
        sb.AppendLine("        <DataArray type=\"UInt8\" Name=\"types\" format=\"ascii\">");
        for (int s = 0; s < n; s++) sb.Append("9 "); // VTK_QUAD = 9
        sb.AppendLine();
        sb.AppendLine("        </DataArray>");
        sb.AppendLine("      </Cells>");

        // CellData: step_index, removed_volume (m^3 -> displayed as mm^3 in caller), time
        sb.AppendLine("      <CellData Scalars=\"step_index\">");
        sb.AppendLine("        <DataArray type=\"Int32\" Name=\"step_index\" format=\"ascii\">");
        for (int s = 0; s < n; s++) sb.Append($"{s} ");
        sb.AppendLine();
        sb.AppendLine("        </DataArray>");
        sb.AppendLine("        <DataArray type=\"Float64\" Name=\"removed_volume_m3\" format=\"ascii\">");
        for (int s = 0; s < n; s++)
            sb.Append(string.Format(inv, "{0} ", plan.Steps[s].RemovalVolumeMetres3));
        sb.AppendLine();
        sb.AppendLine("        </DataArray>");
        sb.AppendLine("        <DataArray type=\"Float64\" Name=\"cutting_time_min\" format=\"ascii\">");
        for (int s = 0; s < n; s++)
            sb.Append(string.Format(inv, "{0} ", plan.Steps[s].CuttingTimeMin));
        sb.AppendLine();
        sb.AppendLine("        </DataArray>");
        sb.AppendLine("      </CellData>");

        sb.AppendLine("    </Piece>");
        sb.AppendLine("  </UnstructuredGrid>");
        sb.AppendLine("</VTKFile>");

        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
    }
}
