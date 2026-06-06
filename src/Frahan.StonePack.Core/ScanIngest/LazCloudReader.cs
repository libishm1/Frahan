#nullable disable
using System;
using System.IO;
using laszip.net;

namespace Frahan.Core.ScanIngest;

// =============================================================================
// LazCloudReader — native .las / .laz point-cloud loader.
//
// Wraps Unofficial.laszip.net (a pure-managed C# port of LASzip; net40 ->
// net48 compatible, reads both uncompressed .las and compressed .laz). Points
// are streamed one at a time and folded straight into the SAME voxel hash-grid
// (VoxelGridSink) used by StreamingCloudReader, so a 357M-point LAZ reduces to
// a manageable centroid cloud with memory bounded by occupied voxels, not the
// input point count.
//
// laszip_get_coordinates applies the LAS header scale + offset internally, so
// the doubles handed to the sink are already real-world UTM / project
// coordinates. (Equivalent to real = raw_int * scale + offset.)
//
// API reference (Unofficial.laszip.net 2.2.0):
//   var r = new laszip_dll();
//   bool compressed = true; r.laszip_open_reader(path, ref compressed);
//   ulong n = r.header.extended_number_of_point_records != 0
//                 ? r.header.extended_number_of_point_records
//                 : r.header.number_of_point_records;
//   var c = new double[3];
//   for (...) { r.laszip_read_point(); r.laszip_get_coordinates(c); ... }
//   r.laszip_close_reader();
// =============================================================================

public static class LazCloudReader
{
    /// <summary>
    /// Open a .las / .laz file and voxel-downsample on the fly. When
    /// <paramref name="voxelSize"/> is &lt;= 0 the points are kept verbatim
    /// (no spatial reduction); for the very large LAZ files this reader
    /// targets, that path can exhaust memory, so the caller should warn.
    /// </summary>
    public static StreamingCloudResult ReadAndDownsample(string path, double voxelSize)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("LAS/LAZ file not found", path);

        var reader = new laszip_dll();
        var sink = new VoxelGridSink(voxelSize);

        // laszip toggles 'compressed' to reflect what it actually found, so .las
        // (uncompressed) and .laz (compressed) both open through the same call.
        bool compressed = true;
        int err = reader.laszip_open_reader(path, ref compressed);
        if (err != 0)
            throw new IOException(
                $"laszip_open_reader failed (code {err}) for '{path}': {SafeError(reader)}");

        try
        {
            // LAS 1.4 stores the count in extended_number_of_point_records; older
            // versions in number_of_point_records. Prefer the extended field.
            ulong count = reader.header.extended_number_of_point_records != 0UL
                ? reader.header.extended_number_of_point_records
                : reader.header.number_of_point_records;

            var coord = new double[3];
            for (ulong i = 0; i < count; i++)
            {
                int rerr = reader.laszip_read_point();
                if (rerr != 0)
                    throw new IOException(
                        $"laszip_read_point failed (code {rerr}) at point {i}/{count}: {SafeError(reader)}");

                // Real-world coordinates: scale + offset already applied inside.
                reader.laszip_get_coordinates(coord);
                sink.Add(coord[0], coord[1], coord[2]);
            }
        }
        finally
        {
            reader.laszip_close_reader();
        }

        return sink.ToResult();
    }

    private static string SafeError(laszip_dll reader)
    {
        try { return reader.laszip_get_error() ?? "(no message)"; }
        catch { return "(no message)"; }
    }
}
