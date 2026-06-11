#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.IO;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.SharedBldgElements;
using Xbim.Ifc4.SharedComponentElements;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.PropertyResource;
using Xbim.Ifc4.RepresentationResource;
using Xbim.Ifc4.GeometricConstraintResource;

namespace Frahan.Masonry.Ifc;

// =============================================================================
// StoneAssemblyIfcWriter — P6 of EVOLUTION_PLAN_MASONRY.md (2026-06-11): the
// BIM/IFC terminal. Writes an IFC4 model in which each placed stone is a
// first-class part of a top-down container element — the schema-level form of
// the imposition<->negotiation balance ("a wall needs a container anyway"):
//
//   IfcProject -> IfcSite -> IfcBuilding -> IfcBuildingStorey      (spatial)
//     └ container (IfcWall / IfcCovering CLADDING / IfcElementAssembly ARCH or
//        USERDEFINED vault / IfcColumn), contained in the storey
//         └ IfcRelAggregates -> one IfcBuildingElementPart (or IfcMember for
//            voussoirs) PER STONE, each carrying a closed
//            IfcTriangulatedFaceSet body (1-BASED CoordIndex) and the
//            "Frahan_Stone" property set (BuildOrder / CarveRatio /
//            StabilityMargin / InterlockJ).
//
// Per IFC4 ADD2 TC1 (researched + source-verified 2026-06-11, see
// outputs/2026-06-10/masonry_evolution/P6_IFC_EXPORT_SPEC.md):
//   * units are SI METRES (Initialize gives mm; we override LENGTHUNIT);
//   * parts decompose the container and are NOT storey-contained themselves;
//   * custom psets must NOT use the reserved "Pset_" prefix;
//   * IfcRelCoversBldgElements is deprecated -> cladding is aggregated;
//   * IfcTriangulatedFaceSet is Reference-View-legal Body tessellation.
// xBIM Essentials v6 (managed write path; no native geometry engine).
// =============================================================================

/// <summary>What top-down container adopts the stones.</summary>
public enum StoneContainerKind
{
    Wall = 0,
    Cladding = 1,
    Arch = 2,
    Vault = 3,
    Column = 4,
}

/// <summary>One stone for export: closed triangulated mesh + the Frahan metrics.</summary>
public sealed class IfcStoneDto
{
    /// <summary>Vertex coordinates in METRES, flat xyz triples.</summary>
    public IReadOnlyList<double> VertexCoordsXyz;
    /// <summary>0-based triangle indices (converted to IFC's 1-based on write).</summary>
    public IReadOnlyList<int> TriangleIndices;
    public int BuildOrder;
    public double CarveRatio;
    public double StabilityMargin;
    public double InterlockJ;
}

/// <summary>Result summary (also used by the round-trip tests).</summary>
public sealed class IfcWriteReport
{
    public string Path;
    public int StoneCount;
    public string Container;
    public long FileBytes;
    public override string ToString() =>
        $"IFC4 written: {Container} with {StoneCount} stone parts -> {Path} ({FileBytes / 1024} KB)";
}

/// <summary>
/// Writes stone assemblies as IFC4. Pure managed (xBIM Essentials); no Rhino
/// dependency — callers convert meshes to <see cref="IfcStoneDto"/> buffers.
/// </summary>
public static class StoneAssemblyIfcWriter
{
    /// <summary>One container of a multi-container building export (P7).</summary>
    public sealed class IfcContainerSpec
    {
        public StoneContainerKind Kind;
        public string Name;
        public IReadOnlyList<IfcStoneDto> Stones;
    }

    /// <summary>
    /// P7 castle composer terminal: write SEVERAL containers (walls, arches,
    /// vaults, columns) into ONE IFC4 building — the top-down model that the
    /// bottom-up stone workflows feed. Containers share the storey; each keeps
    /// its own aggregated stone parts.
    /// </summary>
    public static IfcWriteReport Write(
        string path,
        IReadOnlyList<IfcContainerSpec> containers,
        string projectName = "Frahan Stone Building")
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));
        if (containers == null || containers.Count == 0)
            throw new ArgumentException("at least one container", nameof(containers));

        int total = 0;
        using (var model = CreateModel(projectName, out IfcBuildingStorey storey))
        {
            using (var txn = model.BeginTransaction("Stone building"))
            {
                foreach (var c in containers)
                {
                    if (c?.Stones == null || c.Stones.Count == 0) continue;
                    AddContainerWithStones(model, storey, c.Name ?? c.Kind.ToString(), c.Stones, c.Kind);
                    total += c.Stones.Count;
                }
                txn.Commit();
            }
            model.SaveAs(path, StorageType.Ifc);
        }
        return new IfcWriteReport
        {
            Path = path,
            StoneCount = total,
            Container = $"{containers.Count} containers",
            FileBytes = new System.IO.FileInfo(path).Length,
        };
    }

    /// <summary>Write one container with its stones to <paramref name="path"/> (.ifc).</summary>
    public static IfcWriteReport Write(
        string path,
        string name,
        IReadOnlyList<IfcStoneDto> stones,
        StoneContainerKind kind = StoneContainerKind.Wall,
        string projectName = "Frahan Stone Assembly")
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));
        if (stones == null || stones.Count == 0) throw new ArgumentException("at least one stone", nameof(stones));

        using (var model = CreateModel(projectName, out IfcBuildingStorey storey))
        {
            using (var txn = model.BeginTransaction("Stone assembly"))
            {
                AddContainerWithStones(model, storey, name, stones, kind);
                txn.Commit();
            }
            model.SaveAs(path, StorageType.Ifc);
        }
        return new IfcWriteReport
        {
            Path = path,
            StoneCount = stones.Count,
            Container = kind.ToString(),
            FileBytes = new System.IO.FileInfo(path).Length,
        };
    }

    /// <summary>Project + SI-metre units + spatial tree. Caller owns the store.</summary>
    public static IfcStore CreateModel(string projectName, out IfcBuildingStorey storey)
    {
        var creds = new XbimEditorCredentials
        {
            ApplicationFullName = "Frahan StonePack",
            ApplicationIdentifier = "Frahan.StonePack",
            ApplicationVersion = "0.7",
            ApplicationDevelopersName = "Frahan",
            EditorsGivenName = "Libish",
            EditorsFamilyName = "Murugean",
            EditorsOrganisationName = "Independent Research",
        };
        var model = IfcStore.Create(creds, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);
        using (var txn = model.BeginTransaction("Init"))
        {
            var project = model.Instances.New<IfcProject>(p => p.Name = projectName);
            project.Initialize(ProjectUnits.SIUnitsUK);            // SI units + Model context (mm)
            ((IfcUnitAssignment)project.UnitsInContext)
                .SetOrChangeSiUnit(IfcUnitEnum.LENGTHUNIT, IfcSIUnitName.METRE, null); // -> metres

            var site = model.Instances.New<IfcSite>(s => s.Name = "Site");
            var building = model.Instances.New<IfcBuilding>(b =>
            {
                b.Name = "Building";
                b.CompositionType = IfcElementCompositionEnum.ELEMENT;
            });
            var st = model.Instances.New<IfcBuildingStorey>(s => { s.Name = "Level 0"; s.Elevation = 0.0; });

            site.ObjectPlacement = NewPlacement(model, null);
            building.ObjectPlacement = NewPlacement(model, site.ObjectPlacement);
            st.ObjectPlacement = NewPlacement(model, building.ObjectPlacement);

            project.AddSite(site);
            site.AddToSpatialDecomposition(building);
            building.AddToSpatialDecomposition(st);
            txn.Commit();
            storey = st;
        }
        return model;
    }

    /// <summary>Add a container + its stone parts inside an OPEN transaction.</summary>
    public static IfcProduct AddContainerWithStones(
        IfcStore model, IfcBuildingStorey storey, string name,
        IReadOnlyList<IfcStoneDto> stones, StoneContainerKind kind)
    {
        IfcProduct container;
        bool partsAreMembers = false;
        switch (kind)
        {
            case StoneContainerKind.Cladding:
            {
                // host wall + aggregated covering (IfcRelCoversBldgElements is
                // deprecated in IFC4 — aggregation is the sanctioned wiring)
                var host = model.Instances.New<IfcWall>(w =>
                {
                    w.Name = name + "/host";
                    w.PredefinedType = IfcWallTypeEnum.SOLIDWALL;
                });
                host.ObjectPlacement = NewPlacement(model, storey.ObjectPlacement);
                storey.AddElement(host);
                var cov = model.Instances.New<IfcCovering>(c =>
                {
                    c.Name = name;
                    c.PredefinedType = IfcCoveringTypeEnum.CLADDING;
                });
                cov.ObjectPlacement = NewPlacement(model, host.ObjectPlacement);
                model.Instances.New<IfcRelAggregates>(r =>
                {
                    r.RelatingObject = host;
                    r.RelatedObjects.Add(cov);
                });
                container = cov;
                break;
            }
            case StoneContainerKind.Arch:
            case StoneContainerKind.Vault:
            {
                var asm = model.Instances.New<IfcElementAssembly>(a =>
                {
                    a.Name = name;
                    if (kind == StoneContainerKind.Arch)
                    {
                        a.PredefinedType = IfcElementAssemblyTypeEnum.ARCH;   // native IFC4 value
                    }
                    else
                    {
                        a.PredefinedType = IfcElementAssemblyTypeEnum.USERDEFINED;
                        a.ObjectType = "PendentiveVault";
                    }
                });
                asm.ObjectPlacement = NewPlacement(model, storey.ObjectPlacement);
                storey.AddElement(asm);
                container = asm;
                partsAreMembers = true;                                       // voussoirs
                break;
            }
            case StoneContainerKind.Column:
            {
                var col = model.Instances.New<IfcColumn>(c =>
                {
                    c.Name = name;
                    c.PredefinedType = IfcColumnTypeEnum.COLUMN;
                });
                col.ObjectPlacement = NewPlacement(model, storey.ObjectPlacement);
                storey.AddElement(col);
                container = col;
                break;
            }
            default:
            {
                var wall = model.Instances.New<IfcWall>(w =>
                {
                    w.Name = name;
                    w.PredefinedType = IfcWallTypeEnum.SOLIDWALL;
                });
                wall.ObjectPlacement = NewPlacement(model, storey.ObjectPlacement);
                storey.AddElement(wall);
                container = wall;
                break;
            }
        }

        var agg = model.Instances.New<IfcRelAggregates>(r => r.RelatingObject = container);
        for (int i = 0; i < stones.Count; i++)
        {
            var s = stones[i];
            IfcElement part;
            if (partsAreMembers)
            {
                part = model.Instances.New<IfcMember>(m =>
                {
                    m.Name = $"{name}/voussoir_{s.BuildOrder:D4}";
                    m.PredefinedType = IfcMemberTypeEnum.USERDEFINED;
                    m.ObjectType = "Voussoir";
                });
            }
            else
            {
                part = model.Instances.New<IfcBuildingElementPart>(p =>
                {
                    p.Name = $"{name}/stone_{s.BuildOrder:D4}";
                    p.PredefinedType = IfcBuildingElementPartTypeEnum.USERDEFINED;
                    p.ObjectType = "NaturalStone";
                });
            }
            part.ObjectPlacement = NewPlacement(model, container.ObjectPlacement);
            part.Representation = NewTessellatedBody(model, s.VertexCoordsXyz, s.TriangleIndices);
            AttachPset(model, part, "Frahan_Stone", new Dictionary<string, IfcValue>
            {
                ["BuildOrder"] = new IfcInteger(s.BuildOrder),
                ["CarveRatio"] = new IfcReal(s.CarveRatio),
                ["StabilityMargin"] = new IfcReal(s.StabilityMargin),
                ["InterlockJ"] = new IfcReal(s.InterlockJ),
            });
            agg.RelatedObjects.Add(part);  // parts decompose the container; NOT storey-contained
        }
        return container;
    }

    // ─── helpers ──────────────────────────────────────────────────────────

    private static IfcProductDefinitionShape NewTessellatedBody(
        IfcStore m, IReadOnlyList<double> coords, IReadOnlyList<int> tris)
    {
        if (coords == null || coords.Count < 9 || coords.Count % 3 != 0)
            throw new ArgumentException("vertex buffer must be xyz triples (>= 3 vertices)");
        if (tris == null || tris.Count < 3 || tris.Count % 3 != 0)
            throw new ArgumentException("triangle buffer must be index triples");

        var ctx = m.Instances.OfType<IfcGeometricRepresentationContext>()
                   .First(c => c.ContextType == "Model");
        var pts = m.Instances.New<IfcCartesianPointList3D>();
        int nv = coords.Count / 3;
        for (int i = 0; i < nv; i++)
        {
            var row = pts.CoordList.GetAt(i);
            row.Add(new IfcLengthMeasure(coords[i * 3]));
            row.Add(new IfcLengthMeasure(coords[i * 3 + 1]));
            row.Add(new IfcLengthMeasure(coords[i * 3 + 2]));
        }
        var tfs = m.Instances.New<IfcTriangulatedFaceSet>(t => { t.Coordinates = pts; t.Closed = true; });
        int nf = tris.Count / 3;
        for (int i = 0; i < nf; i++)
        {
            var row = tfs.CoordIndex.GetAt(i);
            // IFC CoordIndex is 1-BASED — the +1 is load-bearing.
            row.Add(new IfcPositiveInteger(tris[i * 3] + 1));
            row.Add(new IfcPositiveInteger(tris[i * 3 + 1] + 1));
            row.Add(new IfcPositiveInteger(tris[i * 3 + 2] + 1));
        }
        var shape = m.Instances.New<IfcShapeRepresentation>(sr =>
        {
            sr.ContextOfItems = ctx;
            sr.RepresentationIdentifier = "Body";
            sr.RepresentationType = "Tessellation";
            sr.Items.Add(tfs);
        });
        return m.Instances.New<IfcProductDefinitionShape>(r => r.Representations.Add(shape));
    }

    private static IfcLocalPlacement NewPlacement(IfcStore m, IfcObjectPlacement relTo)
        => m.Instances.New<IfcLocalPlacement>(lp =>
        {
            lp.PlacementRelTo = relTo;
            lp.RelativePlacement = m.Instances.New<IfcAxis2Placement3D>(a =>
                a.Location = m.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(0, 0, 0)));
        });

    private static void AttachPset(
        IfcStore m, IfcObjectDefinition target, string name, IDictionary<string, IfcValue> vals)
    {
        var pset = m.Instances.New<IfcPropertySet>(p =>
        {
            p.Name = name;   // custom psets must NOT use the reserved "Pset_" prefix
            foreach (var kv in vals)
                p.HasProperties.Add(m.Instances.New<IfcPropertySingleValue>(sv =>
                {
                    sv.Name = kv.Key;
                    sv.NominalValue = kv.Value;
                }));
        });
        m.Instances.New<IfcRelDefinesByProperties>(r =>
        {
            r.RelatedObjects.Add((IfcObject)target);
            r.RelatingPropertyDefinition = pset;
        });
    }
}
