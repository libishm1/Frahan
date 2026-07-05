#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // BuildSequenceJsonComponent — emit the masonry build sequence as a
    // JSON string for downstream robot pipelines. Consumer writes to disk
    // via standard Grasshopper file-write components; this stays a pure
    // text emitter to avoid filesystem side effects from the canvas.
    //
    // Schema (1.0):
    //   {
    //     "schema_version": "1.0",
    //     "block_count": N,
    //     "blocks": [
    //       {
    //         "id": "b0",
    //         "order_index": 0,
    //         "layer": 0,
    //         "place": { "origin": [x,y,z], "x_axis": [...],
    //                    "y_axis": [...], "z_axis": [...] }
    //       },
    //       ...
    //     ]
    //   }
    //
    // ComponentGuid: 6789ABCD-EF01-2345-6789-ABCDEF012345
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Build Sequence JSON.
    /// Per-block id + layer + placement plane, encoded as JSON.
    /// </summary>
        [DesignApplication(
        "Encodes the masonry build sequence as a JSON string",
        DesignFlow.TopDown,
        Precedent = "Frahan-original JSON serialiser for build sequence")]
    public sealed class BuildSequenceJsonComponent : FrahanComponentBase
    {
        public BuildSequenceJsonComponent()
            : base(
                "Build Sequence JSON", "BuildJson",
                "Encodes the masonry build sequence as a JSON string. " +
                "Wire Block Ids, Place Planes (or Place Transforms via " +
                "Pick Place Frames), and Layers in matching list order. " +
                "Output is plain text — pipe to a file-writing component " +
                "if you need disk persistence.",
                "Frahan", "Masonry")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        public override Guid ComponentGuid =>
            new Guid("6789ABCD-EF01-2345-6789-ABCDEF012345");

        protected override Bitmap Icon => IconProvider.Load("GcodeExport.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Block Ids", "Id",
                "Per-block identifier. Required.",
                GH_ParamAccess.list);
            p.AddPlaneParameter("Place Planes", "Pl",
                "Per-block placement plane (parallel to Block Ids). " +
                "Required.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Layers", "L",
                "Per-block course number. Optional; if absent, all " +
                "layers are reported as 0.",
                GH_ParamAccess.list);
            p[2].Optional = true;
            p.AddBooleanParameter("Pretty", "P",
                "Indent the JSON for human reading. Default true. Set " +
                "false for compact single-line output.",
                GH_ParamAccess.item, true);
            p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Json", "J",
                "JSON text encoding the build sequence (schema 1.0).",
                GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            var ids = new List<string>();
            var planes = new List<Plane>();
            var layers = new List<int>();
            bool pretty = true;

            if (!da.GetDataList(0, ids) || ids.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No Block Ids provided.");
                return;
            }
            if (!da.GetDataList(1, planes))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No Place Planes provided.");
                return;
            }
            if (planes.Count != ids.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Place Planes count ({planes.Count}) must match " +
                    $"Block Ids count ({ids.Count}).");
                return;
            }
            da.GetDataList(2, layers);
            if (layers.Count > 0 && layers.Count != ids.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Layers count ({layers.Count}) must match Block Ids " +
                    $"count ({ids.Count}) when supplied.");
                return;
            }
            da.GetData(3, ref pretty);

            var sb = new StringBuilder(256 + ids.Count * 256);
            string nl = pretty ? "\n" : "";
            string i1 = pretty ? "  " : "";
            string i2 = pretty ? "    " : "";
            string i3 = pretty ? "      " : "";
            string sp = pretty ? " " : "";

            sb.Append('{').Append(nl);
            sb.Append(i1).Append("\"schema_version\":").Append(sp).Append("\"1.0\",").Append(nl);
            sb.Append(i1).Append("\"block_count\":").Append(sp).Append(ids.Count).Append(',').Append(nl);
            sb.Append(i1).Append("\"blocks\":").Append(sp).Append('[').Append(nl);
            for (int i = 0; i < ids.Count; i++)
            {
                sb.Append(i2).Append('{').Append(nl);
                sb.Append(i3).Append("\"id\":").Append(sp).Append('"').Append(EscapeJsonString(ids[i])).Append("\",").Append(nl);
                sb.Append(i3).Append("\"order_index\":").Append(sp).Append(i).Append(',').Append(nl);
                int layer = layers.Count > 0 ? layers[i] : 0;
                sb.Append(i3).Append("\"layer\":").Append(sp).Append(layer).Append(',').Append(nl);
                AppendPlaneJson(sb, planes[i], "place", i3, pretty);
                sb.Append(nl).Append(i2).Append('}');
                if (i < ids.Count - 1) sb.Append(',');
                sb.Append(nl);
            }
            sb.Append(i1).Append(']').Append(nl);
            sb.Append('}');

            da.SetData(0, sb.ToString());
        }

        private static void AppendPlaneJson(
            StringBuilder sb, Plane pl, string key, string indent, bool pretty)
        {
            string nl = pretty ? "\n" : "";
            string sp = pretty ? " " : "";
            string deeper = pretty ? indent + "  " : "";
            sb.Append(indent).Append('"').Append(key).Append("\":").Append(sp).Append('{').Append(nl);
            AppendVec3Json(sb, pl.Origin, "origin", deeper, pretty); sb.Append(',').Append(nl);
            AppendVec3Json(sb, pl.XAxis, "x_axis", deeper, pretty); sb.Append(',').Append(nl);
            AppendVec3Json(sb, pl.YAxis, "y_axis", deeper, pretty); sb.Append(',').Append(nl);
            AppendVec3Json(sb, pl.ZAxis, "z_axis", deeper, pretty); sb.Append(nl);
            sb.Append(indent).Append('}');
        }

        private static void AppendVec3Json(
            StringBuilder sb, Point3d p, string key, string indent, bool pretty)
        {
            string sp = pretty ? " " : "";
            sb.Append(indent).Append('"').Append(key).Append("\":").Append(sp);
            sb.Append('[')
              .Append(JsonNumber(p.X)).Append(',').Append(sp)
              .Append(JsonNumber(p.Y)).Append(',').Append(sp)
              .Append(JsonNumber(p.Z)).Append(']');
        }

        private static void AppendVec3Json(
            StringBuilder sb, Vector3d v, string key, string indent, bool pretty)
        {
            string sp = pretty ? " " : "";
            sb.Append(indent).Append('"').Append(key).Append("\":").Append(sp);
            sb.Append('[')
              .Append(JsonNumber(v.X)).Append(',').Append(sp)
              .Append(JsonNumber(v.Y)).Append(',').Append(sp)
              .Append(JsonNumber(v.Z)).Append(']');
        }

        private static string JsonNumber(double v)
        {
            // Always invariant culture; never NaN/Infinity in JSON.
            if (double.IsNaN(v) || double.IsInfinity(v)) return "0.0";
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
