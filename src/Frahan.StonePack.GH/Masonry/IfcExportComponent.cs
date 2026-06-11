#nullable disable
using System;
using System.Drawing;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.Masonry.Ifc;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // IFC Export (Stone Assembly) — the P6 BIM terminal (2026-06-11).
    // Writes IFC4 via the Core StoneAssemblyIfcWriter (xBIM Essentials v6,
    // managed write path): one container element (Wall / Cladding / Arch /
    // Vault / Column) aggregating one IfcBuildingElementPart (or voussoir
    // IfcMember) per stone, each with a closed IfcTriangulatedFaceSet body and
    // the "Frahan_Stone" pset (BuildOrder / CarveRatio / StabilityMargin /
    // InterlockJ). Terminal export node: synchronous behind a Run gate
    // (default false) per the async-vs-sync decision. Composable primitive —
    // it consumes the SAME stone meshes the generator / matcher / carve-back
    // emit, so any workflow ends in BIM with one node.
    // =========================================================================
    public class IfcExportComponent : GH_Component
    {
        public IfcExportComponent()
            : base("IFC Export (Stone Assembly)", "IfcStones",
                "Write the stone assembly as IFC4: container element (wall / cladding / arch / vault / column) " +
                "with one building-element part per stone (tessellated body + Frahan_Stone property set). " +
                "xBIM Essentials, SI metres.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid => new Guid("D5F10017-3E5A-4B9C-8D26-1F70A4C85E93");
        protected override Bitmap Icon => Frahan.GH.IconProvider.Load("IfcExport.png");
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Stones", "St", "Closed stone meshes in their placed positions", GH_ParamAccess.list);
            p.AddTextParameter("Name", "N", "Container element name", GH_ParamAccess.item, "StoneWall");
            p.AddTextParameter("Path", "P", "Output .ifc path", GH_ParamAccess.item, string.Empty);
            p.AddIntegerParameter("Container", "C",
                "0 Wall | 1 Cladding | 2 Arch | 3 Vault | 4 Column", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Carve", "Cr", "Per-stone carve ratio lambda (optional)", GH_ParamAccess.list);
            p.AddNumberParameter("Stability", "Sm", "Stability margin for the assembly (optional)", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("InterlockJ", "J", "Interlock score J of the pattern (optional)", GH_ParamAccess.item, 0.0);
            p.AddBooleanParameter("Run", "R", "Write the file", GH_ParamAccess.item, false);
            p[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Path", "P", "Written file", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "Export report", GH_ParamAccess.item);
            p.AddBooleanParameter("OK", "OK", "True if the file was written", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var stones = new List<Mesh>();
            string name = "StoneWall", path = string.Empty;
            int kind = 0;
            var carve = new List<double>();
            double stab = 0.0, interlock = 0.0;
            bool run = false;
            if (!da.GetDataList(0, stones)) return;
            da.GetData(1, ref name);
            da.GetData(2, ref path);
            da.GetData(3, ref kind);
            da.GetDataList(4, carve);
            da.GetData(5, ref stab);
            da.GetData(6, ref interlock);
            da.GetData(7, ref run);

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

            // document units -> metres
            double scale = 1.0;
            var doc = Rhino.RhinoDoc.ActiveDoc;
            if (doc != null)
                scale = Rhino.RhinoMath.UnitScale(doc.ModelUnitSystem, Rhino.UnitSystem.Meters);

            var dtos = new List<IfcStoneDto>(stones.Count);
            int skipped = 0;
            for (int i = 0; i < stones.Count; i++)
            {
                var m = stones[i];
                if (m == null) { skipped++; continue; }
                var d = m.DuplicateMesh();
                d.Faces.ConvertQuadsToTriangles();
                d.Vertices.CombineIdentical(true, true);
                d.Weld(Math.PI);
                d.UnifyNormals();
                // outward orientation (the voussoir lesson: flipped solids break consumers)
                if (d.Vertices.Count < 3 || d.Faces.Count < 1) { skipped++; continue; } // degenerate (e.g. empty boolean result)
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
                    BuildOrder = i,
                    CarveRatio = i < carve.Count ? carve[i] : 0.0,
                    StabilityMargin = stab,
                    InterlockJ = interlock,
                });
            }
            if (dtos.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid stone meshes.");
                da.SetData(2, false);
                return;
            }

            try
            {
                var report = StoneAssemblyIfcWriter.Write(
                    path, name, dtos, (StoneContainerKind)Math.Max(0, Math.Min(4, kind)));
                string msg = report + (skipped > 0 ? $" | skipped {skipped} null meshes" : string.Empty);
                da.SetData(0, report.Path);
                da.SetData(1, msg);
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
