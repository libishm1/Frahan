#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Frahan.Masonry.Ifc;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace Frahan.Tests;

// P6 round-trip tests: write an IFC4 stone assembly with StoneAssemblyIfcWriter,
// REOPEN it with IfcStore.Open, and assert the entity graph + pset values.
// Pure managed (xBIM) — runs headless, no Rhino.

static class StoneAssemblyIfcWriterTests
{
    public static void IfcExport_Wall_RoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), "frahan_ifc_wall_test.ifc");
        var stones = MakePrismStones(6);
        var report = StoneAssemblyIfcWriter.Write(path, "TestWall", stones, StoneContainerKind.Wall);
        if (report.FileBytes < 1000) throw new Exception("suspiciously small ifc file");

        using (var model = IfcStore.Open(path))
        {
            var walls = model.Instances.OfType<IIfcWall>().ToList();
            if (walls.Count != 1) throw new Exception($"expected 1 IfcWall, got {walls.Count}");

            var parts = model.Instances.OfType<IIfcBuildingElementPart>().ToList();
            if (parts.Count != 6) throw new Exception($"expected 6 parts, got {parts.Count}");

            // parts decompose the wall, and are NOT storey-contained
            var agg = model.Instances.OfType<IIfcRelAggregates>()
                .FirstOrDefault(r => r.RelatingObject == walls[0]);
            if (agg == null || agg.RelatedObjects.Count != 6)
                throw new Exception("wall must aggregate exactly the 6 parts");
            var contained = model.Instances.OfType<IIfcRelContainedInSpatialStructure>()
                .SelectMany(r => r.RelatedElements).ToList();
            if (contained.OfType<IIfcBuildingElementPart>().Any())
                throw new Exception("parts must not be storey-contained (IFC4 decomposition rule)");
            if (!contained.Contains(walls[0]))
                throw new Exception("the wall itself must be storey-contained");

            // tessellated bodies: closed, 1-based indices within range
            foreach (var tfs in model.Instances.OfType<IIfcTriangulatedFaceSet>())
            {
                int nv = tfs.Coordinates.CoordList.Count;
                int maxIdx = tfs.CoordIndex.SelectMany(r => r).Max(v => Convert.ToInt32(v.Value));
                int minIdx = tfs.CoordIndex.SelectMany(r => r).Min(v => Convert.ToInt32(v.Value));
                if (minIdx < 1 || maxIdx > nv)
                    throw new Exception($"CoordIndex out of 1-based range: [{minIdx},{maxIdx}] vs {nv} verts");
            }

            // pset round-trip on the first part
            var pset = model.Instances.OfType<IIfcPropertySet>()
                .FirstOrDefault(p => p.Name == "Frahan_Stone");
            if (pset == null) throw new Exception("Frahan_Stone pset missing");
            var props = pset.HasProperties.OfType<IIfcPropertySingleValue>()
                .ToDictionary(p => p.Name.ToString(), p => p.NominalValue);
            foreach (var key in new[] { "BuildOrder", "CarveRatio", "StabilityMargin", "InterlockJ" })
                if (!props.ContainsKey(key)) throw new Exception("pset missing " + key);

            // SI metres
            var lu = model.Instances.OfType<IIfcSIUnit>()
                .FirstOrDefault(u => u.UnitType == IfcUnitEnum.LENGTHUNIT);
            if (lu == null || lu.Name != IfcSIUnitName.METRE || lu.Prefix != null)
                throw new Exception("length unit must be unprefixed SI METRE");
        }
        File.Delete(path);
    }

    public static void IfcExport_Arch_And_Cladding_Containers()
    {
        string pathA = Path.Combine(Path.GetTempPath(), "frahan_ifc_arch_test.ifc");
        StoneAssemblyIfcWriter.Write(pathA, "TestArch", MakePrismStones(5), StoneContainerKind.Arch);
        using (var model = IfcStore.Open(pathA))
        {
            var asm = model.Instances.OfType<IIfcElementAssembly>().FirstOrDefault();
            if (asm == null || asm.PredefinedType != IfcElementAssemblyTypeEnum.ARCH)
                throw new Exception("arch must be IfcElementAssembly(ARCH)");
            var members = model.Instances.OfType<IIfcMember>().ToList();
            if (members.Count != 5 || members.Any(m => m.ObjectType != "Voussoir"))
                throw new Exception("arch parts must be 5 voussoir IfcMembers");
        }
        File.Delete(pathA);

        string pathC = Path.Combine(Path.GetTempPath(), "frahan_ifc_clad_test.ifc");
        StoneAssemblyIfcWriter.Write(pathC, "TestClad", MakePrismStones(4), StoneContainerKind.Cladding);
        using (var model = IfcStore.Open(pathC))
        {
            var cov = model.Instances.OfType<IIfcCovering>().FirstOrDefault();
            if (cov == null || cov.PredefinedType != IfcCoveringTypeEnum.CLADDING)
                throw new Exception("cladding must be IfcCovering(CLADDING)");
            // deprecated IfcRelCoversBldgElements must NOT be emitted; aggregation instead
            if (model.Instances.OfType<IIfcRelCoversBldgElements>().Any())
                throw new Exception("IfcRelCoversBldgElements is deprecated in IFC4 and must not be written");
            var hostAgg = model.Instances.OfType<IIfcRelAggregates>()
                .FirstOrDefault(r => r.RelatedObjects.Contains(cov));
            if (!(hostAgg?.RelatingObject is IIfcWall))
                throw new Exception("covering must be aggregated under the host wall");
        }
        File.Delete(pathC);
    }

    // simple unit-cube stones in a row, metres
    private static List<IfcStoneDto> MakePrismStones(int n)
    {
        var stones = new List<IfcStoneDto>(n);
        for (int i = 0; i < n; i++)
        {
            double x0 = i * 1.1;
            var v = new List<double>();
            foreach (var dz in new[] { 0.0, 0.5 })
                foreach (var (dx, dy) in new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 0.3), (0.0, 0.3) })
                { v.Add(x0 + dx); v.Add(dy); v.Add(dz); }
            var t = new List<int>
            {
                0,2,1, 0,3,2,  4,5,6, 4,6,7,           // bottom (down), top (up)
                0,1,5, 0,5,4,  2,3,7, 2,7,6,           // front, back
                0,4,7, 0,7,3,  1,2,6, 1,6,5,           // left, right
            };
            stones.Add(new IfcStoneDto
            {
                VertexCoordsXyz = v,
                TriangleIndices = t,
                BuildOrder = i,
                CarveRatio = 0.1 * i,
                StabilityMargin = 0.05,
                InterlockJ = 0.68,
            });
        }
        return stones;
    }
}
