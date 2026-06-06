#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.DataModel;

/// <summary>
/// Which blocks in an assembly are grounded / fixed (their 6 DOFs are removed
/// from the equilibrium matrix). Mirrors compas_cra's
/// <c>set_boundary_conditions(keys)</c> API.
/// </summary>
public sealed class BoundaryConditions
{
    private readonly HashSet<string> _fixed;

    public BoundaryConditions(IEnumerable<string> fixedBlockIds)
    {
        if (fixedBlockIds == null) throw new ArgumentNullException(nameof(fixedBlockIds));
        _fixed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in fixedBlockIds)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("fixedBlockIds contains a blank id", nameof(fixedBlockIds));
            _fixed.Add(id);
        }
    }

    public IReadOnlyCollection<string> FixedBlockIds => _fixed;

    public int FixedCount => _fixed.Count;

    /// <summary>
    /// Returns true if the block is grounded (its DOFs are removed from the
    /// free-DOF list). Any unknown id is treated as free.
    /// </summary>
    public bool IsFixed(string blockId)
    {
        if (string.IsNullOrWhiteSpace(blockId))
            throw new ArgumentException("blockId must be non-blank", nameof(blockId));
        return _fixed.Contains(blockId);
    }

    public override string ToString() => $"BoundaryConditions(fixed={FixedCount})";
}
