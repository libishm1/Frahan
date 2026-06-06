#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Core.ScanIngest;

// =============================================================================
// PhotoSet typed record (MRAC Proposal 2)
//
// Inventory of a folder of photogrammetry photos. v1 SCOPE (trimmed per
// Libish 2026-05-31 decision in handoff §5.5):
//   - File listing (path + name + size + last-modified).
//   - Subfolder discrimination (used / skipped / raw).
//   - Aggregate summary (count, total size, date range).
// v1 does NOT parse EXIF (camera, focal, GPS) -- CloudCompare and ExifTool
// already cover that. v1.x adds EXIF via MetadataExtractor NuGet if a real
// customer asks.
// =============================================================================

public sealed class PhotoEntry
{
    /// <summary>Absolute path to the photo file.</summary>
    public string Path;

    /// <summary>File name without directory.</summary>
    public string Name;

    /// <summary>File size in bytes.</summary>
    public long SizeBytes;

    /// <summary>Last-modified timestamp from the filesystem (NOT EXIF).</summary>
    public DateTime LastModifiedUtc;

    /// <summary>Subfolder bucket: "used" / "skipped" / "raw" / "" (root).
    /// Recognised subfolder names map MRAC's standard conventions.</summary>
    public string Bucket;
}

public sealed class PhotoSet
{
    /// <summary>Root folder the photos were enumerated from.</summary>
    public string RootFolder;

    /// <summary>All photo entries discovered.</summary>
    public IReadOnlyList<PhotoEntry> Entries;

    /// <summary>Counts per bucket: "used", "skipped", "raw", "" (root).</summary>
    public IReadOnlyDictionary<string, int> BucketCounts;

    /// <summary>Total size of all photos in megabytes.</summary>
    public double TotalSizeMb;

    /// <summary>Earliest LastModifiedUtc across all entries; default value if empty.</summary>
    public DateTime EarliestUtc;

    /// <summary>Latest LastModifiedUtc across all entries; default value if empty.</summary>
    public DateTime LatestUtc;

    /// <summary>File extensions seen (lowercase, with leading dot): {.jpg, .jpeg, .png, ...}.</summary>
    public IReadOnlyCollection<string> ExtensionsSeen;
}
