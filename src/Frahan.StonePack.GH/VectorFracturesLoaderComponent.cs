#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Quarry.Ingestion;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Quarry;

// =============================================================================
// Frahan > Ingest > Vector Fractures Loader.
//
// Thin canvas adapter over the Rhino-independent vector ingest layer in
// Frahan.Masonry.Quarry.Ingestion. Dispatches by extension:
//
//   .shp / .geojson / .json -> VectorFractureReader.Load(path)
//
// Outputs:
//   - one open PolylineCurve per fracture trace
//   - the source CRS WKT (when present in a companion .prj for Shapefile)
//   - trace count
//   - per-trace attribute keys + values as parallel data trees
//
// The Core reader returns FractureTrace POCOs in the source file CRS units.
// This component does NOT reproject; if your file is in EUREF_FIN_TM35FIN
// metres (Loviisa) or any other projected system, the output curves are
// in those source units.
// =============================================================================

[DesignApplication(
    "Load fracture traces from a Shapefile (.shp) or GeoJSON (.geojson) into Rhino  as open PolylineCurves, plus...",
    DesignFlow.Bridges,
    Precedent = "NetTopologySuite.IO.Esri Shapefile + GeoJSON readers; OGC Simple Features spec")]
[Algorithm("Vector fracture import", "ESRI Shapefile / OGC Simple Features (standard)",
    Note = "industry format via NetTopologySuite.IO.Esri; not a paper")]
public sealed class VectorFracturesLoaderComponent : GH_Component
{
    public VectorFracturesLoaderComponent()
        : base("Vector Fractures Loader", "VecFrac",
            "Load fracture traces from a Shapefile (.shp) or GeoJSON (.geojson) into Rhino " +
            "as open PolylineCurves, plus their attributes and source CRS WKT. Uses " +
            "NetTopologySuite under the hood; format dispatched by file extension. " +
            "Reads ESRI Shapefile / OGC Simple Features (industry standard, not a published algorithm).",
            "Frahan", "Ingest")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("F2D00BEC-2026-4522-B0B0-1ABE15A0DEAD");

    protected override Bitmap Icon => IconProvider.Load("ShapefileImport.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("File Path", "F",
            "Absolute path to a .shp or .geojson file. For Shapefiles, " +
            "the companion .dbf + .shx + .prj must sit alongside.",
            GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddCurveParameter("Traces", "T",
            "One open PolylineCurve per fracture trace, in the source CRS units.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Count", "N",
            "Number of traces returned.", GH_ParamAccess.item);
        p.AddTextParameter("CRS WKT", "Crs",
            "Coordinate reference system as WKT (Shapefile .prj). Empty for GeoJSON.",
            GH_ParamAccess.item);
        p.AddTextParameter("Attribute Keys", "Ak",
            "Per-trace attribute keys as a {trace_index;0} data tree.",
            GH_ParamAccess.tree);
        p.AddTextParameter("Attribute Values", "Av",
            "Per-trace attribute values parallel to Attribute Keys.",
            GH_ParamAccess.tree);
        p.AddTextParameter("Source File", "Src",
            "The source file path echoed verbatim.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        string path = string.Empty;
        if (!da.GetData(0, ref path)) return;

        FractureTraceCollection coll;
        try
        {
            coll = VectorFractureReader.Load(path);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Load failed: {ex.Message}");
            return;
        }

        var curves = new List<Curve>(coll.Count);
        var attrKeysTree = new GH_Structure<GH_String>();
        var attrValsTree = new GH_Structure<GH_String>();
        for (int i = 0; i < coll.Count; i++)
        {
            var trace = coll.Traces[i];
            var pts = new List<Point3d>(trace.VertexCount);
            for (int j = 0; j < trace.VertexCount; j++)
            {
                var v = trace.Vertices[j];
                pts.Add(new Point3d(v.X, v.Y, 0.0));
            }
            var poly = new Polyline(pts);
            curves.Add(new PolylineCurve(poly));

            var path_i = new GH_Path(i);
            foreach (var kv in trace.Attributes)
            {
                attrKeysTree.Append(new GH_String(kv.Key), path_i);
                attrValsTree.Append(new GH_String(kv.Value), path_i);
            }
        }

        da.SetDataList(0, curves);
        da.SetData(1, coll.Count);
        da.SetData(2, coll.CrsWkt);
        da.SetDataTree(3, attrKeysTree);
        da.SetDataTree(4, attrValsTree);
        da.SetData(5, coll.SourceFile);
    }
}
