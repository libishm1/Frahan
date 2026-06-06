#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Fractures;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// CompositeBlock -- I14: a logical block made of one or more convex pieces.
//
// Required by:
//   - Phase 9 (AMRR plane sequence) to tag intermediate CPHs as members of
//     the same logical "block".
//   - Phase 12 (Goodman-Shi keyblock stability) which generally deals with
//     L-shaped or wedge-with-notch removable blocks.
//   - Future non-convex bench geometry (decompose a curved working face
//     into convex pieces and treat them as one logical bench).
//
// Mirrors the `tag` array in Zhang 2024 cut-code's ConvexSystem class.
// =============================================================================

public sealed class CompositeBlock
{
    private readonly List<ConvexPolyhedron> _pieces;
    private BoundingBox3 _aabbCache;
    private bool _aabbCached;

    public CompositeBlock(string id, IEnumerable<ConvexPolyhedron> pieces)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("id is required", nameof(id));
        if (pieces == null) throw new ArgumentNullException(nameof(pieces));
        Id = id;
        _pieces = new List<ConvexPolyhedron>(pieces);
        if (_pieces.Count == 0)
            throw new ArgumentException("CompositeBlock must have at least one piece", nameof(pieces));
    }

    public string Id { get; }
    public IReadOnlyList<ConvexPolyhedron> Pieces => _pieces;
    public int PieceCount => _pieces.Count;

    /// <summary>Sum of per-piece volumes (assumes pieces do not overlap).</summary>
    public double TotalVolume
    {
        get
        {
            double sum = 0.0;
            for (int i = 0; i < _pieces.Count; i++) sum += _pieces[i].Volume();
            return sum;
        }
    }

    /// <summary>Union AABB of all pieces.</summary>
    public BoundingBox3 Aabb
    {
        get
        {
            if (_aabbCached) return _aabbCache;
            double xMin = double.PositiveInfinity, yMin = double.PositiveInfinity, zMin = double.PositiveInfinity;
            double xMax = double.NegativeInfinity, yMax = double.NegativeInfinity, zMax = double.NegativeInfinity;
            for (int p = 0; p < _pieces.Count; p++)
            {
                var verts = _pieces[p].Vertices;
                for (int v = 0; v < verts.Count; v++)
                {
                    var pt = verts[v];
                    if (pt.X < xMin) xMin = pt.X; if (pt.X > xMax) xMax = pt.X;
                    if (pt.Y < yMin) yMin = pt.Y; if (pt.Y > yMax) yMax = pt.Y;
                    if (pt.Z < zMin) zMin = pt.Z; if (pt.Z > zMax) zMax = pt.Z;
                }
            }
            _aabbCache = new BoundingBox3(xMin, yMin, zMin, xMax, yMax, zMax);
            _aabbCached = true;
            return _aabbCache;
        }
    }

    /// <summary>
    /// Return the first piece that contains the point, or null if none.
    /// Linear scan; for k pieces and a single query, O(k * faces_per_piece).
    /// </summary>
    public ConvexPolyhedron PieceContaining(double x, double y, double z, double tol = 0.0)
    {
        for (int i = 0; i < _pieces.Count; i++)
        {
            if (_pieces[i].ContainsPoint(x, y, z, tol)) return _pieces[i];
        }
        return null;
    }

    public override string ToString() =>
        $"CompositeBlock({Id}, {PieceCount} pieces, V={TotalVolume:0.000} m^3)";
}
