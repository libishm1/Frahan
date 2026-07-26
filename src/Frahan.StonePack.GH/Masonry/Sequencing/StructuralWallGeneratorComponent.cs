#nullable disable
using System;
using System.Drawing;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH;
using Frahan.GH.Attributes;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Sequencing;

namespace Frahan.StonePack.GH.Masonry.Sequencing
{
    /// <summary>
    /// Structural Wall (Generator). The RECTANGULAR coursed counterpart of
    /// Polygonal Wall (Generator): a self-supporting single-skin structural
    /// stone wall of big unreinforced blocks in running bond, with rectangular
    /// openings spanned by lintel stones. Layout math lives in the Rhino-free
    /// Core <see cref="StructuralWallGenerator"/>, ported from the verified
    /// demonstrator demos/StructuralStoneFacade (6/6 baseline cases).
    /// </summary>
    [Algorithm("Coursed running-bond structural-stone wall layout",
               "Stone Federation Great Britain, A Guide to Structural Stone (June 2026), self-supporting single-skin category",
               Note = "Frahan-original generator: courses + running bond + opening cut-outs + lintel bearing + " +
                      "trailing-sliver merge + head-joint protection under jambs and over lintel bearings. " +
                      "Ported from demos/StructuralStoneFacade (6/6 baseline cases, 46-block hero facade reproduced exactly).")]
    [Algorithm("Static (safe) theorem of limit analysis",
               "Kao et al. 2022, Computer-Aided Design 146:103216 Coupled Rigid-Block Analysis",
               Doi = "10.1016/j.cad.2022.103216",
               Note = "Not run here. The Assembly output feeds Masonry Stability Check, whose kernel is machine-checked in Lean as thm:cra.")]
    [DesignApplication(
        "Lay a self-supporting single-skin stone wall with doors and windows on lintels",
        DesignFlow.TopDown,
        Precedent = "Stone Federation A Guide to Structural Stone (June 2026), category 3 self-supporting single skin",
        Tolerance = "head joints exactly coincident (shared faces); openings snapped to the bed-joint grid",
        CardSet = "examples/36_structural_stone_facade/")]
    [RelatedComponent("Frahan > Masonry > Polygonal Wall (Generator)",
        Reason = "irregular power-diagram stones instead of rectangular coursed blocks",
        ComponentGuid = "D5F10014-7A11-4C0E-9B22-3F6A1E2C4D80")]
    [RelatedComponent("Frahan > Masonry > Masonry Stability Check",
        Reason = "feed this component's Assembly output straight in for the CRA verdict")]
    [RelatedComponent("Frahan > Masonry > Block Build Order",
        Reason = "feed this component's Assembly output in for a valid laying sequence")]
    public class StructuralWallGeneratorComponent : FrahanComponentBase
    {
        public StructuralWallGeneratorComponent()
          : base("Structural Wall (Generator)", "StructWall",
                 "Build a SELF-SUPPORTING SINGLE-SKIN structural stone wall: rectangular coursed blocks in " +
                 "running bond, openings cut out of the courses, a lintel stone over each opening bearing onto " +
                 "the jambs each side. The Stone Federation's 'A Guide to Structural Stone' (June 2026) third " +
                 "category - 'the stacking of large stones to create a deep, single-skin which supports its own " +
                 "weight, ground to roof'. Head joints are protected: none is left directly under an opening " +
                 "jamb or directly over a lintel bearing (the two stones merge into one longer stone instead). " +
                 "An opening touching z=0 is treated as a DOOR: no sill course beneath it, and the " +
                 "jamb protection below it does not apply. Outputs closed, manifold stone meshes and an " +
                 "exact-contact MasonryAssembly with the lowest course fixed. " +
                 "The downstream Masonry Stability Check and Block Build Order are backed by machine-checked " +
                 "theorems (thm:cra - the static/safe theorem, where infeasibility IS the collapse certificate; " +
                 "thm:kahn - a valid build order always exists on an acyclic support graph). " +
                 "Layout math: Frahan.Masonry.Sequencing.StructuralWallGenerator (Rhino-free Core).",
                 "Frahan", "Masonry")
        { }

        public override Guid ComponentGuid => new Guid("D5F10058-6C2E-4A73-9D18-5B4F7E1A2C63");
        protected override Bitmap Icon => Frahan.GH.IconProvider.Load("BondPattern.png");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddNumberParameter("Width", "W", "Wall width (m), along the X axis", GH_ParamAccess.item, 7.2);
            p.AddNumberParameter("Height", "H",
                "Wall height (m). Snapped to a whole number of courses; the Report states the height actually built.",
                GH_ParamAccess.item, 5.4);
            p.AddNumberParameter("Depth", "D", "Single-skin depth (m), along the Y axis", GH_ParamAccess.item, 0.6);
            p.AddNumberParameter("Course", "C", "Course height (m) = the bed-joint spacing", GH_ParamAccess.item, 0.6);
            p.AddNumberParameter("Block length", "L",
                "Nominal stone length (m) used to set out the running bond. A joint-protection merge is capped at " +
                "2 x this length, so a stone never exceeds it.",
                GH_ParamAccess.item, 1.2);
            p.AddNumberParameter("Min length", "M",
                "Shortest acceptable stone (m). A trailing stone below this merges into its neighbour instead of " +
                "being laid as a sliver.",
                GH_ParamAccess.item, 0.6);
            p.AddNumberParameter("Lintel bearing", "B",
                "Bearing length (m) the lintel runs onto the masonry each side of its opening. 0 leaves the lintel " +
                "ends flush with the reveals.",
                GH_ParamAccess.item, 0.6);
            p.AddRectangleParameter("Openings", "O",
                "Rectangular openings, read in the wall's XZ plane: each rectangle's world X range gives the " +
                "opening's left and right jambs, its world Z range gives the sill and head. Y is ignored. " +
                "A rectangle touching z=0 is treated as a DOOR (runs to the ground, no sill course beneath it). " +
                "Sill and head are snapped onto the nearest bed joint when within a quarter course; the Report " +
                "states every snap. Empty = solid wall.",
                GH_ParamAccess.list);
            p[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddMeshParameter("Stones", "St",
                "Closed, manifold, unified-outward-normal stone meshes, one per block, in laying order (lowest course first)",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Count", "N", "Number of stones", GH_ParamAccess.item);
            p.AddMeshParameter("Lintels", "Li",
                "The lintel stones alone (a subset of Stones), so they can be previewed or coloured separately",
                GH_ParamAccess.list);
            p.AddTextParameter("Report", "R",
                "Courses, blocks, lintels, bond faults, stone volume and tonnage, largest unit, plus any layout notes",
                GH_ParamAccess.item);
            p.AddGenericParameter("Assembly", "A",
                "The wall as a structural assembly: contact interfaces detected between the exactly-coincident " +
                "block faces, with every block of the lowest course fixed as the foundation. Feed straight into " +
                "Masonry Stability Check's Assembly input or Block Build Order. Models the dry (mortarless) wall.",
                GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            double w = 7.2, h = 5.4, depth = 0.6, course = 0.6;
            double nominal = 1.2, minLen = 0.6, bearing = 0.6;
            var rects = new List<Rectangle3d>();

            da.GetData(0, ref w); da.GetData(1, ref h); da.GetData(2, ref depth);
            da.GetData(3, ref course); da.GetData(4, ref nominal); da.GetData(5, ref minLen);
            da.GetData(6, ref bearing);
            da.GetDataList(7, rects);

            if (!(w > 0) || !(h > 0) || !(depth > 0) || !(course > 0) || !(nominal > 0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Width, Height, Depth, Course and Block length must all be > 0.");
                return;
            }
            if (course > h + 1e-9)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Course height exceeds the wall height; one course is built.");

            var options = new StructuralWallOptions
            {
                Width = w,
                Height = h,
                Depth = depth,
                CourseHeight = course,
                NominalLength = nominal,
                MinLength = Math.Max(0.0, minLen),
                // The joint-protection merge fuses at most two nominal stones, so the
                // cap is 2 x Block length. That is 2.4 m at the 1.2 m default, exactly
                // the demonstrator's MaxLen. Kept implicit to hold the input list short.
                MaxLength = 2.0 * nominal,
                LintelBearing = Math.Max(0.0, bearing),
                Lintels = true,
                Openings = ReadOpenings(rects),
            };

            StructuralWallResult result = StructuralWallGenerator.Generate(options);

            var stones = new List<Mesh>(result.BlockCount);
            var lintels = new List<Mesh>(result.LintelCount);
            for (int i = 0; i < result.Blocks.Count; i++)
            {
                var mesh = BuildStone(result.Blocks[i]);
                if (mesh == null) continue;
                stones.Add(mesh);
                if (result.Blocks[i].IsLintel) lintels.Add(mesh);
            }

            // Exact-contact structural assembly. Rectangular blocks share whole
            // faces, so contacts are detected from the block meshes themselves
            // (MeshContactDetector's coplanar-coincidence resolver recovers the
            // true shared rectangle, KB-9) and the lowest course is fixed as the
            // foundation. Same route as the verified demonstrator.
            MasonryAssembly assembly = null;
            int fixedCount = 0;
            try
            {
                assembly = StructuralWallGenerator.BuildAssembly(result, out fixedCount);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Assembly build skipped: " + ex.Message);
            }

            string report = result.Report;
            if (assembly != null)
                report += "\nassembly: " + assembly.BlockCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + " blocks, " + assembly.InterfaceCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + " interfaces, " + fixedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + " fixed (lowest course)";

            for (int i = 0; i < result.Warnings.Count; i++)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, result.Warnings[i]);
            if (result.BondFaults > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    result.BondFaults.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " running joint(s): a head joint repeats in two vertically adjacent courses. " +
                    "Change Block length, Min length or the opening positions to stagger them.");
            // A jamb bearing on a head joint is a bearing defect, not a bond
            // blemish, so it is raised a level. The generator clears every such
            // joint the bond controls; any that remain are forced by where the
            // openings sit, and result.Warnings names each one.
            if (result.ProtectedJointViolations > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    result.ProtectedJointViolations.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " head joint(s) under an opening jamb or under a lintel bearing. The jamb must " +
                    "bear on solid stone. Shift the opening by half a stone, or change Lintel bearing.");
            if (stones.Count == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No stones generated. Check that the openings do not cover the whole wall.");

            da.SetDataList(0, stones);
            da.SetData(1, stones.Count);
            da.SetDataList(2, lintels);
            da.SetData(3, report);
            if (assembly != null)
                da.SetData(4, new Grasshopper.Kernel.Types.GH_ObjectWrapper(assembly));
        }

        /// <summary>
        /// Reads each rectangle in the wall's XZ plane. The rectangle may sit on
        /// any plane; its four corners are mapped to world and the world X and Z
        /// ranges taken, so a rectangle drawn on the XZ construction plane, on a
        /// wall-aligned plane, or lofted in Y all read the same. Y is ignored -
        /// the wall is a single skin of uniform Depth.
        /// </summary>
        private static List<StructuralWallOpening> ReadOpenings(List<Rectangle3d> rects)
        {
            var openings = new List<StructuralWallOpening>();
            if (rects == null) return openings;
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                if (!r.IsValid) continue;
                var p0 = r.PointAt(0);
                var p1 = r.PointAt(1);
                var p2 = r.PointAt(2);
                var p3 = r.PointAt(3);
                double x0 = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
                double x1 = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
                double z0 = Math.Min(Math.Min(p0.Z, p1.Z), Math.Min(p2.Z, p3.Z));
                double z1 = Math.Max(Math.Max(p0.Z, p1.Z), Math.Max(p2.Z, p3.Z));
                openings.Add(new StructuralWallOpening(x0, x1, z0, z1,
                    "opening " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            return openings;
        }

        /// <summary>
        /// One stone: the block's axis-aligned box as a closed, manifold mesh with
        /// unified outward normals. Vertices are shared between faces (8 vertices,
        /// 12 triangles) and come from the same Core buffer the assembly's contact
        /// detection uses, so neighbouring stones meet on exactly coincident faces.
        /// </summary>
        private static Mesh BuildStone(StructuralWallBlock block)
        {
            List<double> coords;
            List<int> tris;
            StructuralWallGenerator.BoxMesh(block, out coords, out tris);

            var m = new Mesh();
            for (int i = 0; i + 2 < coords.Count; i += 3)
                m.Vertices.Add(coords[i], coords[i + 1], coords[i + 2]);
            for (int i = 0; i + 2 < tris.Count; i += 3)
                m.Faces.AddFace(tris[i], tris[i + 1], tris[i + 2]);

            m.Vertices.CombineIdentical(true, true);
            m.UnifyNormals();
            double vol = 0.0;
            try { vol = m.Volume(); } catch { }
            if (vol < 0) m.Flip(true, true, true);
            m.RebuildNormals();
            m.Compact();
            return m.IsValid ? m : null;
        }
    }
}
