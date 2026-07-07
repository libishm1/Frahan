#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Core.ScanIngest;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // ReadPlyMeshComponent (Phase F1+F2 rescue-extend, UX architecture
    // report §9.14: keep the original GUID, broaden to multi-format).
    //
    // Now reads PLY, OBJ, and STL via the pure-managed Frahan.Core.ScanIngest
    // parsers. Auto-detect by extension (and magic-byte fallback for files
    // with unknown extensions). Multi-group OBJ files emit one mesh per
    // group; PLY and STL produce a single mesh.
    //
    // Subcategory moved from Masonry → Mesh (semantically a scan-prep tool,
    // not a masonry algorithm). Nickname stays "ReadPLY" so existing .gh
    // files that canvas-search "ReadPLY" still resolve.
    //
    // ComponentGuid: 789ABCDE-F012-3456-789A-BCDEF0123456 (UNCHANGED — per
    // AGENTS.md §8 GUID stability).
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Mesh &gt; Scan Read (rescue-extend of the original
    /// Read PLY Mesh component). Accepts PLY, OBJ, and STL via pure-managed
    /// parsers in Frahan.Core.ScanIngest. Vertex colours preserved when
    /// the source format carries them (PLY only today).
    /// </summary>
    [Algorithm("PLY parse", "Turk 1994 (PLY Polygon File Format)",
        Note = "pure-managed; ascii + binary_little_endian + binary_big_endian")]
    [Algorithm("OBJ / STL parse", "Wavefront OBJ; 3D Systems STL",
        Note = "pure-managed; OBJ multi-group, STL ascii + binary")]
    [Algorithm("VRML IndexedFaceSet parse", "VRML97 / ISO-IEC 14772-1:1997",
        Note = "pure-managed; n-gon fan triangulation; reads Artec Studio .wrl")]
        [DesignApplication(
        "Loads a mesh from a .ply, .obj, or .stl file via pure-managed  parsers (no third-party native code)",
        DesignFlow.Bridges,
        Precedent = "Stanford PLY format (Greenberg Turk 1994)")]
    public sealed class ReadPlyMeshComponent : FrahanComponentBase
    {
        public ReadPlyMeshComponent()
            : base(
                "Scan Read", "ReadPLY",
                "Loads a mesh from a .ply, .obj, or .stl file via pure-managed " +
                "parsers (no third-party native code). PLY: ASCII + binary_LE; " +
                "OBJ: v + f with vertex/tex/normal triplet syntax, multi-group " +
                "files emit one mesh per group; STL: ASCII + binary, vertex " +
                "welding at 1e-7 model units. Vertex colours preserved on PLY. [Turk 1994]",
                "Frahan", "Ingest")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("789ABCDE-F012-3456-789A-BCDEF0123456");

        protected override Bitmap Icon => IconProvider.Load("PlyReader.png");
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("File Path", "F",
                "Absolute path to a .ply / .obj / .stl file.",
                GH_ParamAccess.item);
            p.AddIntegerParameter("Format", "Fmt",
                "0 = Auto (detect from extension and magic bytes), " +
                "1 = PLY, 2 = OBJ, 3 = STL.",
                GH_ParamAccess.item, 0);
            p[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Meshes", "M",
                "One mesh per group/object in the source file. PLY and STL " +
                "always produce a single mesh; OBJ may produce many.",
                GH_ParamAccess.list);
            p.AddTextParameter("Names", "N",
                "Per-mesh name (OBJ group/object name, PLY/STL file stem).",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Vertex Counts", "V",
                "Per-mesh vertex count.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Triangle Counts", "T",
                "Per-mesh triangle count.",
                GH_ParamAccess.list);
            p.AddTextParameter("Detected Format", "D",
                "Format the dispatcher actually used (PLY / OBJ / STL).",
                GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            string path = null;
            int formatInt = 0;
            if (!da.GetData(0, ref path) || string.IsNullOrWhiteSpace(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No file path provided.");
                return;
            }
            if (!System.IO.File.Exists(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"File not found: '{path}'.");
                return;
            }
            da.GetData(1, ref formatInt);
            if (formatInt < 0 || formatInt > 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Format must be 0 (Auto), 1 (PLY), 2 (OBJ), or 3 (STL); got {formatInt}.");
                return;
            }
            var format = (ScanFormat)formatInt;
            var resolved = format == ScanFormat.Auto ? MultiFormatMeshReader.Detect(path) : format;

            IReadOnlyList<ScanMesh> scans;
            try
            {
                scans = MultiFormatMeshReader.ReadFile(path, format);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Failed to parse {resolved}: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            var meshes = new List<Mesh>(scans.Count);
            var names = new List<string>(scans.Count);
            var vertexCounts = new List<int>(scans.Count);
            var triangleCounts = new List<int>(scans.Count);

            for (int s = 0; s < scans.Count; s++)
            {
                var sm = scans[s];
                var mesh = new Mesh();
                int vc = sm.VertexCount;
                for (int i = 0; i < vc; i++)
                {
                    mesh.Vertices.Add(
                        sm.VertexCoordsXyz[3 * i + 0],
                        sm.VertexCoordsXyz[3 * i + 1],
                        sm.VertexCoordsXyz[3 * i + 2]);
                }
                int tc = sm.TriangleCount;
                for (int i = 0; i < tc; i++)
                {
                    mesh.Faces.AddFace(
                        sm.TriangleIndices[3 * i + 0],
                        sm.TriangleIndices[3 * i + 1],
                        sm.TriangleIndices[3 * i + 2]);
                }
                if (sm.HasColors)
                {
                    for (int i = 0; i < vc; i++)
                    {
                        mesh.VertexColors.Add(
                            sm.VertexColorsRgb[3 * i + 0],
                            sm.VertexColorsRgb[3 * i + 1],
                            sm.VertexColorsRgb[3 * i + 2]);
                    }
                }
                mesh.Normals.ComputeNormals();
                mesh.Compact();

                meshes.Add(mesh);
                names.Add(sm.Name);
                vertexCounts.Add(vc);
                triangleCounts.Add(tc);
            }

            da.SetDataList(0, meshes);
            da.SetDataList(1, names);
            da.SetDataList(2, vertexCounts);
            da.SetDataList(3, triangleCounts);
            da.SetData(4, resolved.ToString());
        }
    }
}
