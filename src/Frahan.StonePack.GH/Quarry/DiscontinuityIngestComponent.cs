#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Frahan.Core.Discontinuity.Ingest;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Quarry;

// =============================================================================
// DiscontinuityIngestComponent (D5F10049, Frahan > Quarry)
//
// Reads MAPPED structural discontinuities (joints / faults / bedding / measured
// planes / digitised traces) from a vector file and emits Rhino planes + trace
// curves + per-feature dip / dip-direction / set id. The complement to
// "Discontinuity Sets (Async)" (D5F10048): that DISCOVERS sets from a scan; this
// INGESTS orientations a geologist / CloudCompare-Compass survey already recorded.
//
// Formats: .csv (dip/dipdir, normals, or plane coeffs), .geojson, .dxf (LINE /
// LWPOLYLINE / POLYLINE / 3DFACE), .shp (with .prj CRS). Bad rows are skipped
// with a warning, never thrown.
// =============================================================================

[Algorithm("Discontinuity ingest", "ISRM Suggested Methods (Brown 1981) dip/dip-direction; trace->plane TLS PCA",
    Note = "Inverse companion to DSE: reads measured/mapped orientations from CSV/GeoJSON/DXF/SHP.")]
[RelatedComponent("Frahan > Quarry > Discontinuity Sets (Async)", Reason = "Discovers joint sets from a scan; this ingests measured ones.")]
[RelatedComponent("Frahan > Quarry > Joint Set", Reason = "Author a single set by hand instead of reading a file.")]
public sealed class DiscontinuityIngestComponent : FrahanComponentBase
{
    public DiscontinuityIngestComponent()
        : base("Discontinuity Ingest", "DiscIn",
            "Read mapped structural discontinuities (joints / faults / bedding / measured planes / digitised " +
            "traces) from a vector file (.csv / .geojson / .dxf / .shp) into Rhino planes + trace curves + " +
            "per-feature dip / dip-direction / set id. The ingest twin of Discontinuity Sets (Async). " +
            "Bad rows are skipped with a warning.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F10049-ED9E-4ED9-A049-ED9EED9E0049");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("DiscontinuitySets.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("File", "F",
            "Path to a discontinuity vector file: .csv (dip,dipdir[,x,y,z] | nx,ny,nz[,x,y,z] | a,b,c,d), " +
            ".geojson, .dxf (LINE / LWPOLYLINE / POLYLINE / 3DFACE), or .shp.",
            GH_ParamAccess.item);
        p.AddPointParameter("Origin", "O",
            "Optional local offset added to every parsed coordinate (e.g. to bring a UTM survey back to a " +
            "Rhino-friendly origin). Default (0,0,0).", GH_ParamAccess.item, Point3d.Origin);
        p[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPlaneParameter("Planes", "P", "One plane per oriented feature (origin = centroid, normal = lower-hemisphere pole).", GH_ParamAccess.list);
        p.AddCurveParameter("Traces", "T", "Digitised trace polylines (DXF / GeoJSON / SHP lines).", GH_ParamAccess.list);
        p.AddNumberParameter("Dip", "D", "Per-oriented-feature dip (deg, 0..90).", GH_ParamAccess.list);
        p.AddNumberParameter("Dip dir", "Dd", "Per-oriented-feature dip-direction (deg, 0..360).", GH_ParamAccess.list);
        p.AddIntegerParameter("Set id", "S", "Per-oriented-feature set id (-1 if the file did not classify it).", GH_ParamAccess.list);
        p.AddTextParameter("Report", "Re", "Counts, CRS, and any skipped-row warnings.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        string file = null;
        if (!da.GetData(0, ref file) || string.IsNullOrWhiteSpace(file))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide a file path.");
            return;
        }
        var origin = Point3d.Origin;
        da.GetData(1, ref origin);
        var off = new Vector3d(origin);

        DiscontinuityCollection col;
        try { col = DiscontinuityReader.Load(file); }
        catch (FileNotFoundException) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "File not found: " + file); return; }
        catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Read failed: " + ex.Message); return; }

        var planes = new List<Plane>();
        var traces = new List<Curve>();
        var dip = new List<double>();
        var dipdir = new List<double>();
        var setIds = new List<int>();

        foreach (var d in col.Items)
        {
            if (d.HasOrientation)
            {
                var c = d.Centroid + off;
                var pl = new Plane(c, d.Normal);
                if (pl.IsValid) { planes.Add(pl); dip.Add(d.DipDeg); dipdir.Add(d.DipDirDeg); setIds.Add(d.SetId); }
            }
            if (d.Trace != null && d.Trace.Count >= 2)
            {
                var poly = new Polyline(d.Trace.Select(pt => pt + off));
                if (poly.IsValid) traces.Add(poly.ToPolylineCurve());
            }
        }

        da.SetDataList(0, planes);
        da.SetDataList(1, traces);
        da.SetDataList(2, dip);
        da.SetDataList(3, dipdir);
        da.SetDataList(4, setIds);
        da.SetData(5, BuildReport(col, planes.Count, traces.Count));

        if (col.Warnings.Count > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{col.Warnings.Count} row(s) skipped or noted; see Report.");
    }

    private static string BuildReport(DiscontinuityCollection col, int planeCount, int traceCount)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Source: {Path.GetFileName(col.SourceFile)}");
        sb.AppendLine($"Features: {col.Items.Count}  (oriented planes: {planeCount}, traces: {traceCount})");
        if (!string.IsNullOrEmpty(col.CrsWkt)) sb.AppendLine("CRS: " + Trunc(col.CrsWkt, 80));
        if (col.Warnings.Count > 0)
        {
            sb.AppendLine($"Warnings ({col.Warnings.Count}):");
            foreach (var w in col.Warnings.Take(12)) sb.AppendLine("  - " + w);
            if (col.Warnings.Count > 12) sb.AppendLine($"  ... +{col.Warnings.Count - 12} more");
        }
        return sb.ToString().TrimEnd();
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "...";
}
