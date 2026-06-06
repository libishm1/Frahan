#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.DataModel;

// =============================================================================
// MasonryInterface — one block-block contact in a masonry assembly.
//
// Mirrors compas_cra's per-edge "interfaces" attribute: each interface carries a
// contact polygon (>= 3 vertices), a normal vector pointing FROM block A INTO
// block B (the convention used by aeq_block in cra_helper.py), and two tangent
// vectors that span the contact plane. The force vector at each contact vertex
// expresses the reaction in this (n, t1, t2) frame.
// =============================================================================

/// <summary>
/// One block-block contact. The contact polygon, normal, and tangents form the
/// per-vertex local frame against which contact forces are decomposed in the
/// CRA / RBE matrix layout.
/// </summary>
public sealed class MasonryInterface
{
    /// <param name="blockAId">Identifier of block A. Non-blank.</param>
    /// <param name="blockBId">Identifier of block B. Non-blank, distinct from blockAId.</param>
    /// <param name="contactPolygon">Polygon vertices, ordered counter-clockwise looking from B toward A. At least 3 entries.</param>
    /// <param name="normalX,normalY,normalZ">Unit normal pointing FROM block A INTO block B.</param>
    /// <param name="tangent1X,tangent1Y,tangent1Z">First tangent; should be orthogonal to the normal.</param>
    /// <param name="tangent2X,tangent2Y,tangent2Z">Second tangent; should complete a right-handed (n, t1, t2) basis.</param>
    public MasonryInterface(
        string blockAId,
        string blockBId,
        IReadOnlyList<ContactVertex> contactPolygon,
        double normalX, double normalY, double normalZ,
        double tangent1X, double tangent1Y, double tangent1Z,
        double tangent2X, double tangent2Y, double tangent2Z)
    {
        if (string.IsNullOrWhiteSpace(blockAId))
            throw new ArgumentException("blockAId must be non-blank", nameof(blockAId));
        if (string.IsNullOrWhiteSpace(blockBId))
            throw new ArgumentException("blockBId must be non-blank", nameof(blockBId));
        if (string.Equals(blockAId, blockBId, StringComparison.Ordinal))
            throw new ArgumentException("blockAId and blockBId must be distinct", nameof(blockBId));
        if (contactPolygon == null) throw new ArgumentNullException(nameof(contactPolygon));
        if (contactPolygon.Count < 3)
            throw new ArgumentException(
                $"contactPolygon needs at least 3 vertices, got {contactPolygon.Count}",
                nameof(contactPolygon));

        for (int i = 0; i < contactPolygon.Count; i++)
        {
            if (contactPolygon[i] == null)
                throw new ArgumentException(
                    $"contactPolygon[{i}] is null",
                    nameof(contactPolygon));
        }

        BlockAId = blockAId;
        BlockBId = blockBId;
        ContactPolygon = contactPolygon;
        NormalX = normalX; NormalY = normalY; NormalZ = normalZ;
        Tangent1X = tangent1X; Tangent1Y = tangent1Y; Tangent1Z = tangent1Z;
        Tangent2X = tangent2X; Tangent2Y = tangent2Y; Tangent2Z = tangent2Z;
    }

    public string BlockAId { get; }
    public string BlockBId { get; }
    public IReadOnlyList<ContactVertex> ContactPolygon { get; }

    public double NormalX { get; }
    public double NormalY { get; }
    public double NormalZ { get; }

    public double Tangent1X { get; }
    public double Tangent1Y { get; }
    public double Tangent1Z { get; }

    public double Tangent2X { get; }
    public double Tangent2Y { get; }
    public double Tangent2Z { get; }

    public int VertexCount => ContactPolygon.Count;

    public override string ToString() =>
        $"MasonryInterface({BlockAId} -> {BlockBId}, V={VertexCount}, " +
        $"n=({NormalX:0.##},{NormalY:0.##},{NormalZ:0.##}))";
}
