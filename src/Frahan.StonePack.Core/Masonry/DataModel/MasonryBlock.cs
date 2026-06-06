#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.DataModel;

// =============================================================================
// MasonryBlock — pure-managed rigid block DTO for the C# port of compas_cra.
//
// Source: BlockResearchGroup/compas_cra (MIT, ETH Zurich BRG, Kao et al. 2022).
// Reference paper: Kao, G. T.-C. et al. "Coupled Rigid-Block Analysis: Stability-
// Aware Design of Complex Discrete-Element Assemblies." Computer-Aided Design,
// vol. 146, art. 103216 (2022). DOI 10.1016/j.cad.2022.103216.
//
// Geometry-runtime-agnostic: vertex coords as flat double[] (length = 3 * V),
// triangle indices as int[] (length = 3 * T). Same convention as
// Frahan.NativeBridge.IGeometryBackend so the same primitives flow end-to-end.
// =============================================================================

/// <summary>
/// One rigid block in a masonry assembly. Geometry is stored in a runtime-
/// agnostic flat representation; conversion to / from RhinoCommon
/// <c>Mesh</c> happens at the GH-component edge, not in Core.
/// </summary>
public sealed class MasonryBlock
{
    /// <param name="id">Stable identifier; not blank, used as the assembly graph key.</param>
    /// <param name="vertexCoordsXyz">Flat [x0,y0,z0,x1,y1,z1,...]; length must be a multiple of 3.</param>
    /// <param name="triangleIndices">Flat [i0,j0,k0,i1,j1,k1,...]; length must be a multiple of 3; each index in [0, vertexCount).</param>
    /// <param name="density">Material density (kg/m^3 or any consistent unit). Must be &gt; 0.</param>
    public MasonryBlock(
        string id,
        IReadOnlyList<double> vertexCoordsXyz,
        IReadOnlyList<int> triangleIndices,
        double density)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id must be non-blank", nameof(id));
        if (vertexCoordsXyz == null) throw new ArgumentNullException(nameof(vertexCoordsXyz));
        if (triangleIndices == null) throw new ArgumentNullException(nameof(triangleIndices));
        if (vertexCoordsXyz.Count % 3 != 0)
            throw new ArgumentException(
                $"vertexCoordsXyz length must be a multiple of 3, got {vertexCoordsXyz.Count}",
                nameof(vertexCoordsXyz));
        if (triangleIndices.Count % 3 != 0)
            throw new ArgumentException(
                $"triangleIndices length must be a multiple of 3, got {triangleIndices.Count}",
                nameof(triangleIndices));
        if (density <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(density), "must be > 0");

        int vertexCount = vertexCoordsXyz.Count / 3;
        for (int i = 0; i < triangleIndices.Count; i++)
        {
            int idx = triangleIndices[i];
            if (idx < 0 || idx >= vertexCount)
                throw new ArgumentException(
                    $"triangleIndices[{i}] = {idx} out of range [0, {vertexCount})",
                    nameof(triangleIndices));
        }

        Id = id;
        VertexCoordsXyz = vertexCoordsXyz;
        TriangleIndices = triangleIndices;
        Density = density;
    }

    public string Id { get; }
    public IReadOnlyList<double> VertexCoordsXyz { get; }
    public IReadOnlyList<int> TriangleIndices { get; }
    public double Density { get; }

    public int VertexCount => VertexCoordsXyz.Count / 3;
    public int TriangleCount => TriangleIndices.Count / 3;

    public override string ToString() =>
        $"MasonryBlock(id={Id}, V={VertexCount}, T={TriangleCount}, density={Density:0.###})";
}
