using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Frahan.Packing.TwoD;

namespace Frahan.Nest2D
{
    // JSON-in / JSON-out facade over the Rhino-free 2D nester. This is the
    // boundary the browser calls: the Blazor WebAssembly host exposes Nest(json)
    // to JavaScript via [JSExport], so a DXF/SVG parsed in the page becomes a
    // request here and the placements come back as JSON to render. No server:
    // the whole thing runs in the user's browser on GitHub Pages.

    public sealed class NestRequest
    {
        // each polygon is a flat [x0,y0,x1,y1,...] ring (outer, CCW)
        public double[] Sheet { get; set; }
        public double[][] SheetHoles { get; set; }
        public double[][] Parts { get; set; }
        public double[][][] PartHoles { get; set; } // per part, list of hole rings
        public double Spacing { get; set; } = 0.0;
        public int BaseRotations { get; set; } = 4;
        public int ContactRotations { get; set; } = 6;
        public int MultiStart { get; set; } = 4;
        public int BoundaryMode { get; set; } = 0;
        public double MinBoundaryContact { get; set; } = 0.25;
    }

    public sealed class PlacedPart
    {
        public int SourceIndex { get; set; }
        public double Tx { get; set; }
        public double Ty { get; set; }
        public double AngleRad { get; set; }
        public double[] PlacedOuter { get; set; } // flat [x,y,...] at final pose
        public int Sheet { get; set; }
        public bool Nested { get; set; }
    }

    public sealed class NestResponse
    {
        public int PlacedCount { get; set; }
        public int PartCount { get; set; }
        public double Density { get; set; }
        public bool Valid { get; set; }
        public double ElapsedMs { get; set; }
        public string Note { get; set; }
        public List<PlacedPart> Placed { get; set; } = new List<PlacedPart>();
    }

    // Source-generated (JsonSerializerContext) so serialization works under
    // WebAssembly trimming, where reflection-based System.Text.Json is disabled
    // (the JsonSerializerIsReflectionDisabled error). Case-insensitive so the
    // JS-side property casing is tolerated.
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(NestRequest))]
    [JsonSerializable(typeof(NestResponse))]
    internal partial class NestJsonContext : JsonSerializerContext
    {
    }

    public static class NestApi
    {
        /// <summary>Parse a request, nest, and return the response, all as JSON.</summary>
        public static string Nest(string requestJson)
        {
            NestRequest req;
            try
            {
                req = JsonSerializer.Deserialize(requestJson, NestJsonContext.Default.NestRequest);
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new NestResponse
                {
                    Valid = false,
                    Note = "bad request json: " + ex.Message
                }, NestJsonContext.Default.NestResponse);
            }
            if (req == null || req.Sheet == null || req.Parts == null || req.Parts.Length == 0)
                return JsonSerializer.Serialize(
                    new NestResponse { Valid = false, Note = "empty request" },
                    NestJsonContext.Default.NestResponse);

            var sheet = ToLoop(req.Sheet);
            var sheetHoles = (req.SheetHoles ?? Array.Empty<double[]>())
                .Select(ToLoop).Cast<IReadOnlyList<(double X, double Y)>>().ToList();

            var parts = new List<HoleNestPart>(req.Parts.Length);
            for (int i = 0; i < req.Parts.Length; i++)
            {
                var holes = (req.PartHoles != null && i < req.PartHoles.Length && req.PartHoles[i] != null)
                    ? req.PartHoles[i].Select(ToLoop).Cast<IReadOnlyList<(double X, double Y)>>().ToList()
                    : null;
                parts.Add(new HoleNestPart { Outer = ToLoop(req.Parts[i]), Holes = holes });
            }

            HoleNestResult r;
            try
            {
                r = ContactNfpHoleNester.Pack(
                    sheet, sheetHoles, parts,
                    spacing: req.Spacing,
                    baseRotationCount: Math.Max(1, req.BaseRotations),
                    contactRotations: Math.Max(0, req.ContactRotations),
                    multiStartOrders: Math.Max(1, req.MultiStart),
                    boundaryMode: req.BoundaryMode,
                    minBoundaryContact: req.MinBoundaryContact);
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new NestResponse
                {
                    Valid = false,
                    PartCount = parts.Count,
                    Note = "pack failed: " + ex.GetType().Name + ": " + ex.Message
                }, NestJsonContext.Default.NestResponse);
            }

            var resp = new NestResponse
            {
                PlacedCount = r.PlacedCount,
                PartCount = parts.Count,
                Density = r.Density,
                Valid = r.Valid,
                ElapsedMs = r.ElapsedMs,
                Note = string.IsNullOrEmpty(r.Note) ? "ok" : r.Note
            };
            foreach (var p in r.Placements)
            {
                resp.Placed.Add(new PlacedPart
                {
                    SourceIndex = p.PartIndex,
                    Tx = p.Tx,
                    Ty = p.Ty,
                    AngleRad = p.AngleRad,
                    Sheet = 0,
                    Nested = false,
                    PlacedOuter = Flatten(p.PlacedOuter)
                });
            }
            return JsonSerializer.Serialize(resp, NestJsonContext.Default.NestResponse);
        }

        private static List<(double X, double Y)> ToLoop(double[] flat)
        {
            var loop = new List<(double X, double Y)>(flat.Length / 2);
            for (int i = 0; i + 1 < flat.Length; i += 2) loop.Add((flat[i], flat[i + 1]));
            return loop;
        }

        private static double[] Flatten(IReadOnlyList<(double X, double Y)> loop)
        {
            var a = new double[loop.Count * 2];
            for (int i = 0; i < loop.Count; i++) { a[2 * i] = loop[i].X; a[2 * i + 1] = loop[i].Y; }
            return a;
        }
    }
}
