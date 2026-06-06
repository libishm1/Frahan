"""
frahan_e57_worker.py -- out-of-process E57 -> voxel-downsampled PLY worker for
the Frahan "Load E57 Cloud" Grasshopper component.

Why a subprocess: .NET has no managed E57 reader, and parsing a multi-GB E57 +
voxel grid in-process inside Rhino risks both an OOM and a native fault taking
down the host. Following the OutOfProcessReconstructor / PythonSubprocessFractureDetector
pattern, the heavy read runs here; a crash kills only this worker.

Contract (called by Frahan.Core.ScanIngest.E57CloudWorker):
    argv[1] = absolute path to input .e57
    argv[2] = absolute path to output .ply (binary_little_endian, float x/y/z)
    argv[3] = voxel edge length in metres (<= 0 means no downsample)
  stdout  = exactly one line beginning "RESULT " followed by a compact JSON
            object: in_points, valid_points, out_points, bbox_min[3], bbox_max[3],
            shift[3], voxel, ply
  stderr  = "PROGRESS ..." lines (optional, surfaced as component status) and
            any error/traceback
  exit 0  = success; non-zero = failure (stderr carries the reason)

The cloud is shifted by an integer-metre global offset (floor of bbox-min) so
PLY float32 coordinates stay sub-mm accurate even for projected/UTM scans. The
shift is reported so the component can place + georeference the cloud back.

Deps: pye57, numpy. (No open3d -- voxel downsample is a pure-numpy sort-reduce.)
"""
import sys
import json
import numpy as np


def log(msg):
    sys.stderr.write("PROGRESS " + msg + "\n")
    sys.stderr.flush()


def voxel_centroid(xyz, v):
    """Centroid-per-voxel downsample of an (N,3) float64 array, pure numpy.

    Encodes the per-axis voxel index (offset to non-negative) into one int64
    linear key, sorts, then segment-reduces. Only the cloud EXTENT (not the
    absolute coordinate magnitude) drives the key range, so large UTM-style
    coordinates are fine.
    """
    if xyz.shape[0] == 0 or v <= 0.0:
        return xyz
    keys = np.floor(xyz / v)
    kmin = keys.min(axis=0)
    k = (keys - kmin).astype(np.int64)
    nx = int(k[:, 0].max()) + 1
    ny = int(k[:, 1].max()) + 1
    lin = k[:, 0] + nx * (k[:, 1] + ny * k[:, 2])
    order = np.argsort(lin, kind="stable")
    lin_s = lin[order]
    xyz_s = xyz[order]
    _, start = np.unique(lin_s, return_index=True)
    sums = np.add.reduceat(xyz_s, start, axis=0)
    counts = np.diff(np.append(start, lin_s.shape[0])).astype(np.float64)
    return sums / counts[:, None]


def write_ply_binary_le_float(path, xyz_f32, voxel):
    n = int(xyz_f32.shape[0])
    header = (
        "ply\n"
        "format binary_little_endian 1.0\n"
        "comment Frahan E57 ingest, voxel %.5f m, coords shifted (see RESULT json)\n"
        "element vertex %d\n"
        "property float x\n"
        "property float y\n"
        "property float z\n"
        "end_header\n" % (voxel, n)
    ).encode("ascii")
    with open(path, "wb") as fh:
        fh.write(header)
        fh.write(np.ascontiguousarray(xyz_f32, dtype="<f4").tobytes())


def main():
    if len(sys.argv) < 4:
        sys.stderr.write("usage: frahan_e57_worker.py <in.e57> <out.ply> <voxel>\n")
        return 2
    in_e57, out_ply = sys.argv[1], sys.argv[2]
    voxel = float(sys.argv[3])

    try:
        import pye57
    except Exception as e:  # noqa: BLE001
        sys.stderr.write(
            "pye57 not available (%s). Install with: pip install pye57\n" % e)
        return 3

    f = pye57.E57(in_e57)
    n_scans = f.scan_count
    log("opened %s : %d scans" % (in_e57, n_scans))

    kept = []
    total_in = 0
    total_valid = 0
    for i in range(n_scans):
        d = f.read_scan(i, intensity=False, colors=False, row_column=False,
                        transform=True, ignore_missing_fields=True)
        x = d["cartesianX"]; y = d["cartesianY"]; z = d["cartesianZ"]
        total_in += int(x.shape[0])
        inv = d.get("cartesianInvalidState")
        if inv is not None:
            m = inv == 0
            x = x[m]; y = y[m]; z = z[m]
        total_valid += int(x.shape[0])
        if x.shape[0] == 0:
            continue
        xyz = np.column_stack((x, y, z)).astype(np.float64)
        kept.append(voxel_centroid(xyz, voxel))
        if (i + 1) % 50 == 0 or i == n_scans - 1:
            sofar = sum(a.shape[0] for a in kept)
            log("scan %d/%d  valid=%d  voxel_pts=%d" % (i + 1, n_scans, total_valid, sofar))

    if not kept:
        sys.stderr.write("no valid points in any scan\n")
        return 4

    merged = np.concatenate(kept, axis=0)
    del kept
    log("merged %d voxel points; final downsample" % merged.shape[0])
    merged = voxel_centroid(merged, voxel)

    mn = merged.min(axis=0)
    mx = merged.max(axis=0)
    shift = np.floor(mn) if voxel > 0 else np.zeros(3)
    shifted = (merged - shift).astype(np.float32)
    write_ply_binary_le_float(out_ply, shifted, voxel)

    result = {
        "in_points": total_in,
        "valid_points": total_valid,
        "out_points": int(merged.shape[0]),
        "bbox_min": mn.tolist(),
        "bbox_max": mx.tolist(),
        "shift": shift.tolist(),
        "voxel": voxel,
        "ply": out_ply,
    }
    # Human/debug line (JSON) + a flat all-numeric SUMMARY line that the C#
    # runner parses without a JSON dependency (counts, then bbox min/max, shift).
    sys.stdout.write("RESULT " + json.dumps(result) + "\n")
    sys.stdout.write("SUMMARY %d %d %d %.6f %.6f %.6f %.6f %.6f %.6f %.6f %.6f %.6f\n" % (
        total_in, total_valid, int(merged.shape[0]),
        mn[0], mn[1], mn[2], mx[0], mx[1], mx[2],
        shift[0], shift[1], shift[2]))
    sys.stdout.flush()
    return 0


if __name__ == "__main__":
    sys.exit(main())
