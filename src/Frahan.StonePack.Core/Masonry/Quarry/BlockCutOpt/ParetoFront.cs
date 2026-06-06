#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// ParetoFront -- collection of ParetoPoints with dominance-pruning insertion.
//
// O(N^2) worst case for N candidates; in practice the front size stays small
// because most candidates are dominated. Acceptable for BlockCutOpt's search
// budget (10^4 evaluations per sub-zone).
//
// References: Phase 6 of the synthesis roadmap.
// =============================================================================

public sealed class ParetoFront
{
    private readonly List<ParetoPoint> _points = new List<ParetoPoint>();

    public IReadOnlyList<ParetoPoint> Points => _points;
    public int Count => _points.Count;

    /// <summary>
    /// Insert <paramref name="p"/> into the front. If p is dominated by any
    /// existing point, drop p. If p dominates existing points, remove them.
    /// Returns true if p was added.
    /// </summary>
    public bool Insert(in ParetoPoint p)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            if (_points[i].Dominates(p)) return false;
        }

        for (int i = _points.Count - 1; i >= 0; i--)
        {
            if (p.Dominates(_points[i])) _points.RemoveAt(i);
        }

        _points.Add(p);
        return true;
    }

    /// <summary>Recovery-maximum point. Throws if the front is empty.</summary>
    public ParetoPoint BestRecovery()
    {
        if (_points.Count == 0) throw new InvalidOperationException("front is empty");
        var best = _points[0];
        for (int i = 1; i < _points.Count; i++)
        {
            if (_points[i].RecoveryCount > best.RecoveryCount) best = _points[i];
        }
        return best;
    }

    /// <summary>Revenue-maximum point.</summary>
    public ParetoPoint BestRevenue()
    {
        if (_points.Count == 0) throw new InvalidOperationException("front is empty");
        var best = _points[0];
        for (int i = 1; i < _points.Count; i++)
        {
            if (_points[i].Revenue > best.Revenue) best = _points[i];
        }
        return best;
    }

    /// <summary>BCSdbBV-minimum point (lower BCSdbBV is better).</summary>
    public ParetoPoint BestBcsdbBv()
    {
        if (_points.Count == 0) throw new InvalidOperationException("front is empty");
        var best = _points[0];
        for (int i = 1; i < _points.Count; i++)
        {
            if (_points[i].BcsdbBv < best.BcsdbBv) best = _points[i];
        }
        return best;
    }

    /// <summary>KerfTime-minimum point (lower KerfTime is better).</summary>
    public ParetoPoint BestKerfTime()
    {
        if (_points.Count == 0) throw new InvalidOperationException("front is empty");
        var best = _points[0];
        for (int i = 1; i < _points.Count; i++)
        {
            if (_points[i].KerfTime < best.KerfTime) best = _points[i];
        }
        return best;
    }
}
