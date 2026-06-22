#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// Whole-side, best-first jigsaw/fragment reassembler. A standalone sibling to
    /// <see cref="AssemblySolver"/> (it uses neither the hash index, the segmenter, nor
    /// ICP) that returns the same <see cref="AssemblyState"/> so downstream wiring is
    /// unchanged. Pipeline: decompose each part into whole sides between min-area-rect
    /// corners (<see cref="WholeSideExtractor"/>), score complementary side pairs
    /// (<see cref="WholeSideMatcher"/>), then grow the assembly from the anchor by a
    /// best-first seam mate with overlap rejection. Validated 9/9 (max 2.9 mm) on a
    /// scattered+rotated 3x3 wavy jigsaw where per-segment matching plateaued at 7/9.
    /// 2D only: non-planar panels are ignored.
    /// </summary>
    public sealed class BestFirstAssembler
    {
        private readonly AssemblyOptions _opt;

        public BestFirstAssembler(AssemblyOptions opt = null)
        {
            _opt = opt ?? new AssemblyOptions();
        }

        private struct Entry
        {
            public double Cost;
            public string ParentId;
            public int ParentSide;
            public string ChildId;
            public int ChildSide;
            public bool Flip;
        }

        // Strict total order: (cost, parentId, parentSide, childId, childSide). Deterministic.
        private static int Compare(Entry x, Entry y)
        {
            int c = x.Cost.CompareTo(y.Cost); if (c != 0) return c;
            c = string.CompareOrdinal(x.ParentId, y.ParentId); if (c != 0) return c;
            c = x.ParentSide.CompareTo(y.ParentSide); if (c != 0) return c;
            c = string.CompareOrdinal(x.ChildId, y.ChildId); if (c != 0) return c;
            return x.ChildSide.CompareTo(y.ChildSide);
        }

        public AssemblyState Solve(IEnumerable<Panel> anchors, IEnumerable<Panel> pool)
        {
            var state = new AssemblyState();

            var anchorList = (anchors ?? Enumerable.Empty<Panel>())
                .Where(p => p != null && p.Mode == PanelMode.Planar2D)
                .OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
            var poolList = (pool ?? Enumerable.Empty<Panel>())
                .Where(p => p != null && p.Mode == PanelMode.Planar2D)
                .OrderBy(p => p.Id, StringComparer.Ordinal).ToList();

            var panelById = new Dictionary<string, Panel>(StringComparer.Ordinal);
            var anchorIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in anchorList) { panelById[a.Id] = a; anchorIds.Add(a.Id); }
            foreach (var p in poolList) if (!panelById.ContainsKey(p.Id)) panelById[p.Id] = p;
            if (panelById.Count == 0) return state;

            var sidesById = new Dictionary<string, List<WholeSide>>(StringComparer.Ordinal);
            foreach (var kv in panelById) sidesById[kv.Key] = WholeSideExtractor.Extract(kv.Value);

            double scale = ComputeScale(panelById.Values);
            double tol = Math.Max(1e-3, scale * 0.02);
            var asmPlane = Plane.WorldXY;   // parts are coplanar in world XY (see WholeSideExtractor)
            double gate = _opt.WholeSideFitGate;

            var placedCurves = new Dictionary<string, Curve>(StringComparer.Ordinal);
            var placedCentre = new Dictionary<string, Point3d>(StringComparer.Ordinal);
            var consumed = new HashSet<string>(StringComparer.Ordinal);
            var frontier = new List<Entry>();

            // Seed anchors at their given transforms (or identity).
            if (anchorList.Count == 0 && poolList.Count > 0)
            {
                // No anchor: promote the lowest-id part as the seed at identity.
                anchorList.Add(poolList[0]);
                poolList.RemoveAt(0);
                anchorIds.Add(anchorList[0].Id);
            }
            foreach (var a in anchorList)
            {
                a.IsAnchored = true;
                Place(state, placedCurves, placedCentre, a, a.AppliedTransform);
            }
            foreach (var a in anchorList) AddCandidates(a.Id, panelById, sidesById, state, frontier, gate);

            while (frontier.Count > 0)
            {
                int bestIdx = -1;
                for (int i = 0; i < frontier.Count; i++)
                {
                    var e = frontier[i];
                    if (state.AppliedTransforms.ContainsKey(e.ChildId)) continue;
                    if (consumed.Contains(Key(e.ParentId, e.ParentSide))) continue;
                    if (bestIdx < 0 || Compare(e, frontier[bestIdx]) < 0) bestIdx = i;
                }
                if (bestIdx < 0) break;
                var mv = frontier[bestIdx];
                frontier.RemoveAt(bestIdx);

                var parentSide = sidesById[mv.ParentId][mv.ParentSide];
                var childPanel = panelById[mv.ChildId];
                var childSide = sidesById[mv.ChildId][mv.ChildSide];
                var tParent = state.AppliedTransforms[mv.ParentId];

                var pStart = parentSide.StartCorner; pStart.Transform(tParent);
                var pEnd = parentSide.EndCorner; pEnd.Transform(tParent);

                // Complementary (CCW) seams mate reversed: childStart->parentEnd,
                // childEnd->parentStart. Honor Flip for the rare same-direction case.
                Transform t = mv.Flip
                    ? TwoPoint(childSide.StartCorner, childSide.EndCorner, pEnd, pStart)
                    : TwoPoint(childSide.StartCorner, childSide.EndCorner, pStart, pEnd);

                var childWorld = (Curve)childPanel.SourceContour.DuplicateCurve();
                childWorld.Transform(t);
                var childCentre = childWorld.GetBoundingBox(true).Center;

                if (Overlaps(childWorld, childCentre, placedCurves, placedCentre, asmPlane, tol)) continue;

                Place(state, placedCurves, placedCentre, childPanel, t);
                state.TotalResidual += mv.Cost;
                consumed.Add(Key(mv.ParentId, mv.ParentSide));
                consumed.Add(Key(mv.ChildId, mv.ChildSide));
                AddCandidates(mv.ChildId, panelById, sidesById, state, frontier, gate);
            }

            return state;
        }

        private static void Place(AssemblyState state, Dictionary<string, Curve> placedCurves,
            Dictionary<string, Point3d> placedCentre, Panel panel, Transform t)
        {
            state.PlacedPanels.Add(panel);
            state.AppliedTransforms[panel.Id] = t;
            var world = (Curve)panel.SourceContour.DuplicateCurve();
            world.Transform(t);
            placedCurves[panel.Id] = world;
            placedCentre[panel.Id] = world.GetBoundingBox(true).Center;
        }

        // For every free non-flat side of the just-placed panel, enqueue mates to every
        // free non-flat side of every still-unplaced panel that passes the gate.
        private void AddCandidates(string placedId, Dictionary<string, Panel> panelById,
            Dictionary<string, List<WholeSide>> sidesById, AssemblyState state, List<Entry> frontier, double gate)
        {
            var placedSides = sidesById[placedId];
            foreach (var unplacedId in panelById.Keys.OrderBy(s => s, StringComparer.Ordinal))
            {
                if (state.AppliedTransforms.ContainsKey(unplacedId)) continue;
                var childSides = sidesById[unplacedId];
                for (int si = 0; si < placedSides.Count; si++)
                {
                    var ps = placedSides[si];
                    if (ps.IsFlat) continue;
                    for (int sj = 0; sj < childSides.Count; sj++)
                    {
                        var cs = childSides[sj];
                        if (cs.IsFlat) continue;
                        var fit = WholeSideMatcher.Score(ps, cs);
                        if (fit.Rejected || fit.Cost > gate) continue;
                        frontier.Add(new Entry
                        {
                            Cost = fit.Cost,
                            ParentId = placedId,
                            ParentSide = si,
                            ChildId = unplacedId,
                            ChildSide = sj,
                            Flip = fit.Flip,
                        });
                    }
                }
            }
        }

        // Reject if a placed part's centroid is inside the candidate or vice versa
        // (Curve.Contains -- a real interpenetration test; centroid distance falsely
        // rejects the short/vertical neighbour). Adjacent parts touch only along a seam
        // and never contain each other's centroid.
        private static bool Overlaps(Curve childWorld, Point3d childCentre,
            Dictionary<string, Curve> placedCurves, Dictionary<string, Point3d> placedCentre,
            Plane plane, double tol)
        {
            foreach (var kv in placedCurves)
            {
                if (childWorld.Contains(placedCentre[kv.Key], plane, tol) == PointContainment.Inside) return true;
                if (kv.Value.Contains(childCentre, plane, tol) == PointContainment.Inside) return true;
            }
            return false;
        }

        // Rigid (rotation+translation) transform mapping s0->t0 and s1->t1, about +Z
        // (the 2D assembly plane). Assumes ~equal segment lengths (true seam corners).
        private static Transform TwoPoint(Point3d s0, Point3d s1, Point3d t0, Point3d t1)
        {
            var sv = s1 - s0; var tv = t1 - t0;
            double ang = Math.Atan2(tv.Y, tv.X) - Math.Atan2(sv.Y, sv.X);
            return Transform.Translation(t0 - s0) * Transform.Rotation(ang, Vector3d.ZAxis, s0);
        }

        // Median per-panel bbox diagonal (scale-relative overlap tolerance).
        private static double ComputeScale(IEnumerable<Panel> panels)
        {
            var diags = new List<double>();
            foreach (var p in panels)
            {
                if (p == null || p.SourceContour == null) continue;
                diags.Add(p.SourceContour.GetBoundingBox(true).Diagonal.Length);
            }
            if (diags.Count == 0) return 1.0;
            diags.Sort();
            double m = diags[diags.Count / 2];
            return m > 1e-9 ? m : 1.0;
        }

        private static string Key(string id, int side) => id + "|" + side.ToString();
    }
}
