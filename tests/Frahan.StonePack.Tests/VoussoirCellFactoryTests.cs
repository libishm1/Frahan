#nullable disable
using System;
using Frahan.Core.Voussoir;
using Rhino.Geometry;

namespace Frahan.Tests;

// =============================================================================
// VoussoirCellFactoryTests — headless coverage for the arch + pendentive vault
// voussoir cell generators (Frahan.Core.Voussoir.VoussoirCellFactory).
//
// These are MEASURED checks (criterion b): closed solids, sane volumes, correct
// counts, keystone placement, ground anchoring. Visual validation (criterion c)
// is done by opening the generated cells in Rhino. Requires the Rhino native
// loader (mesh + curve ops); the harness configures it unless FRAHAN_SKIP_NATIVE=1.
// =============================================================================

static class VoussoirCellFactoryTests
{
    public static void Arch_Semicircular_Produces11ClosedCells()
    {
        var r = VoussoirCellFactory.BuildArch(
            ArchProfile.Semicircular, 2.0, 0.55, 0.6, 11, 120.0, 0.0, Point3d.Origin);
        Assert(r.Cells.Count == 11, "expected 11 voussoir cells");
        foreach (var m in r.Cells)
        {
            Assert(m != null, "cell mesh is null");
            Assert(m.IsClosed, "voussoir cell is not a closed solid");
            // Signed volume must be POSITIVE: outward orientation is required for
            // CGAL booleans to read the cell as a solid (not its complement).
            Assert(m.Volume() > 1e-6, "voussoir cell is not outward-oriented (negative signed volume)");
        }
        Assert(r.Assembly != null && r.Assembly.Voussoirs.Count == 11, "assembly not populated");
        Assert(r.Volumes.Count == 11 && r.BedPlanes.Count == 11 && r.Centroids.Count == 11,
            "per-voussoir output lists are inconsistent");
        Assert(r.Intrados != null, "intrados curve is null");
    }

    public static void Arch_Semicircular_TotalVolume_InBand()
    {
        // Faceted half-annulus prism: ~ (pi/2)((R+t)^2 - R^2) * w with chord
        // shortfall. R=2, t=0.55, w=0.6 -> smooth ~2.359 m^3; faceted slightly less.
        var r = VoussoirCellFactory.BuildArch(
            ArchProfile.Semicircular, 2.0, 0.55, 0.6, 11, 120.0, 0.0, Point3d.Origin);
        Assert(r.TotalVolume > 2.0 && r.TotalVolume < 2.45,
            $"semicircular arch total volume out of band: {r.TotalVolume}");
    }

    public static void Arch_Semicircular_SpringersOnGround()
    {
        var r = VoussoirCellFactory.BuildArch(
            ArchProfile.Semicircular, 2.0, 0.55, 0.6, 11, 120.0, 0.0, Point3d.Origin);
        var bb = BoundingBox.Empty;
        foreach (var m in r.Cells) bb.Union(m.GetBoundingBox(true));
        AssertNear(bb.Min.Z, 0.0, 1e-6, "arch min Z (springers on ground)");
        // Span across X = 2*(R+t) = 5.1.
        AssertNear(bb.Max.X - bb.Min.X, 2.0 * (2.0 + 0.55), 0.05, "arch X span (2*(R+t))");
    }

    public static void Arch_KeystoneIndex_IsCentral()
    {
        var r = VoussoirCellFactory.BuildArch(
            ArchProfile.Semicircular, 2.0, 0.55, 0.6, 11, 120.0, 0.0, Point3d.Origin);
        Assert(r.KeystoneIndex == 5, $"expected keystone index 5 for N=11, got {r.KeystoneIndex}");
        var key = (VoussoirRecord)r.Assembly.Voussoirs[r.KeystoneIndex];
        Assert(key.JointClass == "key", "keystone voussoir not tagged 'key'");
    }

    public static void Arch_AllProfiles_BuildClosedCells()
    {
        foreach (ArchProfile p in new[]
        {
            ArchProfile.Semicircular, ArchProfile.Segmental, ArchProfile.Pointed, ArchProfile.Catenary,
        })
        {
            var r = VoussoirCellFactory.BuildArch(p, 2.0, 0.4, 0.6, 8, 120.0, 1.8, Point3d.Origin);
            Assert(r.Cells.Count == 8, $"{p}: expected 8 cells");
            foreach (var m in r.Cells)
                Assert(m != null && m.IsClosed, $"{p}: a cell is not a closed solid");
            Assert(r.TotalVolume > 0, $"{p}: total volume not positive");
        }
    }

    public static void Arch_BasePoint_Translates()
    {
        var origin = VoussoirCellFactory.BuildArch(
            ArchProfile.Semicircular, 2.0, 0.55, 0.6, 11, 120.0, 0.0, Point3d.Origin);
        var shifted = VoussoirCellFactory.BuildArch(
            ArchProfile.Semicircular, 2.0, 0.55, 0.6, 11, 120.0, 0.0, new Point3d(10, 20, 0));
        AssertNear(shifted.Centroids[0].X - origin.Centroids[0].X, 10.0, 1e-6, "base-point X shift");
        AssertNear(shifted.Centroids[0].Y - origin.Centroids[0].Y, 20.0, 1e-6, "base-point Y shift");
    }

    public static void Pendentive_Produces36ClosedCells()
    {
        var r = VoussoirCellFactory.BuildPendentiveVault(2.5, 1.6, 0.4, 6, 6, true, Point3d.Origin);
        Assert(r.Cells.Count == 36, $"expected 36 cells, got {r.Cells.Count}");
        foreach (var m in r.Cells)
        {
            Assert(m != null, "vault cell is null");
            Assert(m.IsClosed, "vault cell is not a closed solid");
            Assert(Math.Abs(m.Volume()) > 1e-6, "vault cell has ~zero volume");
        }
        Assert(r.Assembly != null && r.Assembly.Voussoirs.Count == 36, "vault assembly not populated");
        Assert(r.TotalVolume > 4.5 && r.TotalVolume < 6.5,
            $"vault total volume out of band: {r.TotalVolume}");
    }

    public static void Pendentive_DropToGround_MinZ_NearZero()
    {
        var r = VoussoirCellFactory.BuildPendentiveVault(2.5, 1.6, 0.4, 6, 6, true, Point3d.Origin);
        var bb = BoundingBox.Empty;
        foreach (var m in r.Cells) bb.Union(m.GetBoundingBox(true));
        AssertNear(bb.Min.Z, 0.0, 1e-6, "vault min Z with dropToGround");
    }

    public static void Pendentive_RejectsCornersOffSphere()
    {
        bool threw = false;
        try { VoussoirCellFactory.BuildPendentiveVault(2.0, 1.6, 0.4, 6, 6, true, Point3d.Origin); }
        catch (ArgumentException) { threw = true; }
        Assert(threw, "expected ArgumentException when 2*h^2 >= R^2");
    }

    public static void Assembly_RecordsReadyForMatcher()
    {
        var r = VoussoirCellFactory.BuildArch(
            ArchProfile.Semicircular, 2.0, 0.55, 0.6, 11, 120.0, 0.0, Point3d.Origin);
        foreach (var goo in r.Assembly.Voussoirs)
        {
            var rec = (VoussoirRecord)goo;
            Assert(rec.Geometry != null && rec.Geometry.IsClosed, "record geometry not closed");
            Assert(rec.Volume > 0, "record volume not positive");
            Assert(rec.OrientedBoundingBox.X.Length > 0, "record OBB degenerate");
        }
        Assert(r.Assembly.GroundAnchorIndices.Count >= 2,
            "expected at least 2 ground anchors (the two springers)");
    }

    private static void Assert(bool cond, string message)
    {
        if (!cond) throw new Exception(message);
    }

    private static void AssertNear(double actual, double expected, double tol, string label)
    {
        if (Math.Abs(actual - expected) > tol)
            throw new Exception($"{label}: expected {expected} ± {tol}, got {actual}");
    }
}
