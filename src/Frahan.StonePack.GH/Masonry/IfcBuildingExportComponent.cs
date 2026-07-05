#nullable disable
using System;
using System.Drawing;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using Frahan.Masonry.Ifc;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // IFC Export (Building) — the P7 castle-composer terminal (2026-06-11).
    // Multi-container variant of D5F10017: takes a TREE of stone meshes (one
    // branch per building element), parallel Names + Container-kind lists, and
    // writes ONE IFC4 building in which every branch becomes its own container
    // (IfcWall / cladding IfcCovering / IfcElementAssembly ARCH / vault /
    // IfcColumn) aggregating one part per stone. This is the top-down
    // building model (walls + arches + vaults + pendentives + columns) that
    // the bottom-up stone workflows feed — verified per element upstream.
    // =========================================================================
    public class IfcBuildingExportComponent : FrahanComponentBase
    {
        public IfcBuildingExportComponent()
            : base("IFC Export (Building)", "IfcBuilding",
                "Write SEVERAL stone containers (walls, arches, vaults, columns) into one IFC4 building. " +
                "Stones come as a tree: one branch per container; Names and Containers list-match the branches. " +
                "Each stone becomes an IfcBuildingElementPart (or voussoir IfcMember) with a tessellated body.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid => new Guid("D5F10018-5C7B-4E2D-9A41-8B36F1D07C54");
        protected override Bitmap Icon => Frahan.GH.IconProvider.Load("IfcBuilding.png");
        public override GH_Exposure Exposure => GH_Exposure.septenary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Stones", "St", "Stone meshes, ONE BRANCH PER CONTAINER", GH_ParamAccess.tree);
            p.AddTextParameter("Names", "N", "Container names (one per branch)", GH_ParamAccess.list);
            p.AddIntegerParameter("Containers", "C",
                "Per branch: 0 Wall | 1 Cladding | 2 Arch | 3 Vault | 4 Column", GH_ParamAccess.list);
            p.AddTextParameter("Path", "P", "Output .ifc path", GH_ParamAccess.item, string.Empty);
            p.AddTextParameter("Project", "Pr", "IfcProject name", GH_ParamAccess.item, "Frahan Stone Building");
            p.AddBooleanParameter("Run", "R", "Write the file", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Path", "P", "Written file", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "Export report", GH_ParamAccess.item);
            p.AddBooleanParameter("OK", "OK", "True if the file was written", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            GH_Structure<Grasshopper.Kernel.Types.GH_Mesh> tree = null;
            var names = new List<string>();
            var kinds = new List<int>();
            string path = string.Empty, project = "Frahan Stone Building";
            bool run = false;
            if (!da.GetDataTree(0, out tree)) return;
            da.GetDataList(1, names);
            da.GetDataList(2, kinds);
            da.GetData(3, ref path);
            da.GetData(4, ref project);
            da.GetData(5, ref run);

            if (!run)
            {
                da.SetData(1, "Set Run = true to write the IFC.");
                da.SetData(2, false);
                return;
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Path is required.");
                da.SetData(2, false);
                return;
            }

            double scale = 1.0;
            var doc = Rhino.RhinoDoc.ActiveDoc;
            if (doc != null)
                scale = Rhino.RhinoMath.UnitScale(doc.ModelUnitSystem, Rhino.UnitSystem.Meters);

            var specs = new List<StoneAssemblyIfcWriter.IfcContainerSpec>();
            int order = 0;
            for (int b = 0; b < tree.PathCount; b++)
            {
                var branch = tree.Branches[b];
                var dtos = new List<IfcStoneDto>(branch.Count);
                foreach (var gm in branch)
                {
                    var m = gm?.Value;
                    if (m == null) continue;
                    var d = m.DuplicateMesh();
                    d.Faces.ConvertQuadsToTriangles();
                    d.Vertices.CombineIdentical(true, true);
                    d.Weld(Math.PI);
                    d.UnifyNormals();
                    if (d.Vertices.Count < 3 || d.Faces.Count < 1) { continue; } // degenerate (e.g. empty boolean result)
                    var vmp = VolumeMassProperties.Compute(d);
                    if (vmp != null && vmp.Volume < 0) d.Flip(true, true, true);

                    var v = new List<double>(d.Vertices.Count * 3);
                    for (int k = 0; k < d.Vertices.Count; k++)
                    {
                        var pt = d.Vertices[k];
                        v.Add(pt.X * scale); v.Add(pt.Y * scale); v.Add(pt.Z * scale);
                    }
                    var t = new List<int>(d.Faces.Count * 3);
                    for (int f = 0; f < d.Faces.Count; f++)
                    {
                        var face = d.Faces[f];
                        t.Add(face.A); t.Add(face.B); t.Add(face.C);
                    }
                    dtos.Add(new IfcStoneDto
                    {
                        VertexCoordsXyz = v,
                        TriangleIndices = t,
                        BuildOrder = order++,
                        CarveRatio = 0.0,
                        StabilityMargin = 0.0,
                        InterlockJ = 0.0,
                    });
                }
                if (dtos.Count == 0) continue;
                int kind = b < kinds.Count ? kinds[b] : 0;
                specs.Add(new StoneAssemblyIfcWriter.IfcContainerSpec
                {
                    Kind = (StoneContainerKind)Math.Max(0, Math.Min(4, kind)),
                    Name = b < names.Count ? names[b] : $"Container_{b:D2}",
                    Stones = dtos,
                });
            }
            if (specs.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid stone branches.");
                da.SetData(2, false);
                return;
            }

            try
            {
                var report = StoneAssemblyIfcWriter.Write(path, specs, project);
                da.SetData(0, report.Path);
                da.SetData(1, report.ToString());
                da.SetData(2, true);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "IFC write failed: " + ex.Message);
                da.SetData(1, "FAILED: " + ex.Message);
                da.SetData(2, false);
            }
        }
    }
}
