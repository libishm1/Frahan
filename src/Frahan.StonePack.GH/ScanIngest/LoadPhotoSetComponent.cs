#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Frahan.Core.ScanIngest;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace Frahan.GH.ScanIngest;

// =============================================================================
// LoadPhotoSetComponent (MRAC Proposal 2, trimmed scope per handoff §5.5)
//
// Inventory a folder of photogrammetry photos into a Frahan PhotoSet typed
// record. v1 ships filesystem-level info only (path, name, size, mtime,
// bucket); no EXIF parsing. CloudCompare + ExifTool already do EXIF; the
// PhotoSet record is what downstream Frahan components consume.
//
// Recognised subfolder buckets (case-insensitive):
//   - "used"  : final calibrated set (the dominant case).
//   - "skipped": photos rejected from alignment.
//   - "raw"   : pre-processing raws.
//   - root    : no bucket subfolder.
//
// Recognised extensions: .jpg .jpeg .png .tif .tiff .dng .nef .cr2 .arw.
// =============================================================================

[Algorithm("Folder enumeration + bucket classification + filesystem metadata aggregate",
    "Frahan-original; trimmed-scope v1 deliberately skips EXIF (ExifTool / CloudCompare covers that)",
    Note = "v1.x extension: add EXIF via MetadataExtractor NuGet when a real customer asks")]
[DesignApplication(
    "Inventory a folder of photogrammetry photos into a typed PhotoSet record for downstream Frahan ingestion (deferred SfM external worker, marker positioning, etc).",
    DesignFlow.Bridges,
    Precedent = "MRAC IAAC Barcelona 2023 photogrammetry workflow (1_Photogrammetry/ folder convention per wiki/research/mrac_workshop_2023/exercise_dossier.md); Agisoft Metashape 'Add Photos' convention",
    Tolerance = "exact file-count match between filesystem and PhotoEntry list; bucket classification 100% deterministic on standard subfolder names",
    CardSet = "Template-General/outputs/2026-05-31/hitl_cards/scan_to_cut_pipeline/ (proposed extension)")]
public sealed class LoadPhotoSetComponent : GH_Component
{
    public LoadPhotoSetComponent()
        : base("Load Photo Set", "LoadPhotoSet",
            "Inventory a folder of photogrammetry photos into a typed PhotoSet. " +
            "v1 SCOPE: filesystem listing + bucket classification (used / " +
            "skipped / raw) + aggregate summary. No EXIF parsing (use " +
            "ExifTool externally if needed). The typed PhotoSet is what " +
            "downstream Frahan components consume.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F10041-ED9E-4ED9-A041-ED9EED9E0041");

    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override Bitmap Icon => IconProvider.Load("Downsample.png"); // placeholder

    private static readonly string[] DefaultExtensions =
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".dng", ".nef", ".cr2", ".arw"
    };

    private static readonly string[] DefaultBuckets = { "used", "skipped", "raw" };

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Photo Folder", "F",
            "Root folder containing photogrammetry photos. Subfolders named " +
            "'used', 'skipped', 'raw' (case-insensitive) bucket-classify the " +
            "entries.",
            GH_ParamAccess.item);
        p.AddBooleanParameter("Recurse Subfolders", "R",
            "If true, recurse into subfolders. Default true (the MRAC convention).",
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Photo Set", "PS",
            "Typed PhotoSet record (Frahan.Core.ScanIngest.PhotoSet). Wire " +
            "into downstream Frahan ingest components.",
            GH_ParamAccess.item);
        p.AddIntegerParameter("Photo Count", "N",
            "Total photo count.", GH_ParamAccess.item);
        p.AddNumberParameter("Total Size MB", "Sz",
            "Total size in megabytes.", GH_ParamAccess.item);
        p.AddTextParameter("Remarks", "Rm",
            "Per-bucket counts + date range + extensions seen.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string root = null;
        bool recurse = true;
        if (!DA.GetData(0, ref root)) return;
        DA.GetData(1, ref recurse);

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Photo Folder does not exist: " + (root ?? "<null>"));
            return;
        }

        var entries = new List<PhotoEntry>();
        var extSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalSize = 0;
        DateTime earliest = DateTime.MaxValue;
        DateTime latest = DateTime.MinValue;

        var searchOpt = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var path in EnumerateFiles(root, searchOpt))
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (!DefaultExtensions.Contains(ext)) continue;

            var info = new FileInfo(path);
            extSet.Add(ext);
            totalSize += info.Length;
            if (info.LastWriteTimeUtc < earliest) earliest = info.LastWriteTimeUtc;
            if (info.LastWriteTimeUtc > latest) latest = info.LastWriteTimeUtc;

            entries.Add(new PhotoEntry
            {
                Path = info.FullName,
                Name = info.Name,
                SizeBytes = info.Length,
                LastModifiedUtc = info.LastWriteTimeUtc,
                Bucket = ClassifyBucket(info.FullName, root),
            });
        }

        var bucketCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            string k = string.IsNullOrEmpty(e.Bucket) ? "" : e.Bucket;
            bucketCounts.TryGetValue(k, out int n);
            bucketCounts[k] = n + 1;
        }

        var photoSet = new PhotoSet
        {
            RootFolder = Path.GetFullPath(root),
            Entries = entries,
            BucketCounts = bucketCounts,
            TotalSizeMb = totalSize / (1024.0 * 1024.0),
            EarliestUtc = entries.Count > 0 ? earliest : default,
            LatestUtc = entries.Count > 0 ? latest : default,
            ExtensionsSeen = extSet.ToArray(),
        };

        var remarks = new List<string>
        {
            "Photos: " + entries.Count + " across " + bucketCounts.Count + " bucket(s).",
            "Buckets: " + string.Join(", ",
                bucketCounts.Select(kv => (string.IsNullOrEmpty(kv.Key) ? "<root>" : kv.Key) + "=" + kv.Value)),
            "Total size: " + photoSet.TotalSizeMb.ToString("F1") + " MB.",
            "Date range: " + (entries.Count > 0
                ? (photoSet.EarliestUtc.ToString("u") + " to " + photoSet.LatestUtc.ToString("u"))
                : "n/a"),
            "Extensions: " + string.Join(", ", photoSet.ExtensionsSeen),
            "v1 trimmed scope: no EXIF parsing; use ExifTool / CloudCompare for camera + GPS metadata.",
        };

        DA.SetData(0, new GH_ObjectWrapper(photoSet));
        DA.SetData(1, entries.Count);
        DA.SetData(2, photoSet.TotalSizeMb);
        DA.SetDataList(3, remarks);
    }

    private static IEnumerable<string> EnumerateFiles(string root, SearchOption opt)
    {
        // Wrap to silently skip access-denied subfolders rather than throwing.
        IEnumerable<string> safe;
        try { safe = Directory.EnumerateFiles(root, "*", opt); }
        catch { yield break; }
        foreach (var p in safe) yield return p;
    }

    private static string ClassifyBucket(string fullPath, string root)
    {
        try
        {
            string rel = Path.GetFullPath(fullPath).Substring(Path.GetFullPath(root).Length).TrimStart('\\', '/');
            var parts = rel.Split('\\', '/');
            if (parts.Length < 2) return ""; // file sits directly in root
            foreach (var seg in parts)
            {
                foreach (var bucket in DefaultBuckets)
                {
                    if (string.Equals(seg, bucket, StringComparison.OrdinalIgnoreCase))
                        return bucket.ToLowerInvariant();
                }
            }
            return "";
        }
        catch { return ""; }
    }
}
