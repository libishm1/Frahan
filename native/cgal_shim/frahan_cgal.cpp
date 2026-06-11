// =============================================================================
// frahan_cgal — CGAL-backed mesh Boolean implementation. Builds against
// CGAL's exact-predicates inexact-constructions kernel via Polygon Mesh
// Processing's corefine_and_compute_* family.
//
// Build instructions: see BUILD.md (vcpkg-based on Windows, apt / brew
// elsewhere). FRAHAN_CGAL_BUILDING is defined by CMakeLists.txt via
// target_compile_definitions; do NOT redefine it here.
//
// Structural invariants (see wiki/specs/20_frahan_cgal_audit.md
// "Build-time issues found and resolved"):
//
//   - All C++-only helpers (typedefs, structs, anonymous-namespace
//     functions) live in ONE anonymous namespace at file scope, BEFORE
//     the extern "C" block. MSVC enforces C linkage for everything
//     declared inside extern "C"; a friend function returning a CGAL
//     C++ type from inside extern "C" fails the C-linkage check.
//   - All exported FRAHAN_CGAL_API entry points live in ONE extern "C"
//     block at the bottom. They reference the anonymous-namespace
//     helpers by name; that's legal because the helpers have C++
//     linkage and the entry-point body is C++ code that just happens
//     to be visible with C linkage to the linker.
//   - OBB requires Eigen. Wrapped in #ifdef FRAHAN_CGAL_HAVE_EIGEN so
//     builds without Eigen still compile (other entry points remain
//     available; OBB is simply not exposed).
// =============================================================================

#include "frahan_cgal.h"

#ifdef _MSC_VER
#  include <float.h>   // _controlfp_s / _MCW_EM (FP-exception mask)
#endif

#include <CGAL/Exact_predicates_inexact_constructions_kernel.h>
#include <CGAL/Surface_mesh.h>
#include <CGAL/Polygon_mesh_processing/corefinement.h>
#include <CGAL/Polygon_mesh_processing/triangulate_faces.h>

// Mesh repair pipeline (GPL via PMP):
#include <CGAL/Polygon_mesh_processing/orientation.h>
#include <CGAL/Polygon_mesh_processing/repair.h>
#include <CGAL/Polygon_mesh_processing/stitch_borders.h>

// Mesh simplification (GPL). Both header paths and class names renamed
// recently in CGAL: Count{,_ratio}_stop_predicate -> Edge_count{,_ratio}_stop_predicate.
#include <CGAL/Surface_mesh_simplification/edge_collapse.h>
#include <CGAL/Surface_mesh_simplification/Policies/Edge_collapse/Edge_count_stop_predicate.h>
#include <CGAL/Surface_mesh_simplification/Policies/Edge_collapse/Edge_count_ratio_stop_predicate.h>
#include <CGAL/Surface_mesh_simplification/Policies/Edge_collapse/Edge_length_stop_predicate.h>

// Extended-ABI dependencies (GPL packages — see BUILD.md):
#include <CGAL/Aff_transformation_3.h>
#include <CGAL/Polygon_2.h>
#include <CGAL/Polygon_with_holes_2.h>
#include <CGAL/create_straight_skeleton_2.h>
#include <CGAL/create_straight_skeleton_from_polygon_with_holes_2.h>

// Surface-mesh segmentation via Shape Diameter Function (GPL package):
#include <CGAL/mesh_segmentation.h>

// Sharp-edge feature detection + connected-components flood fill, used
// for angle-based face clustering:
#include <CGAL/Polygon_mesh_processing/detect_features.h>
#include <CGAL/Polygon_mesh_processing/connected_components.h>

// Heat method (intrinsic geodesics, Crane et al. 2013) - used by
// frahan_cgal_geodesic_voronoi to compute on-surface distance fields:
#include <CGAL/Heat_method_3/Surface_mesh_geodesic_distances_3.h>
#include <CGAL/Partition_traits_2.h>
#include <CGAL/partition_2.h>

// HYBRID kernel mode (COMPAS_CGAL-style EPICK+EPECK):
#include <CGAL/Exact_predicates_exact_constructions_kernel.h>
#include <CGAL/Cartesian_converter.h>
#include <boost/property_map/property_map.hpp>

// OBB needs Eigen3 internally; gate the include and the entry point.
#ifdef FRAHAN_CGAL_HAVE_EIGEN
#  include <CGAL/optimal_bounding_box.h>
#endif

// Phase H/I reconstruction headers. Gated by FRAHAN_CGAL_ENABLE_RECONSTRUCTION
// so the minimal boolean-only build does not pull them. (Previously these
// were only listed in a comment near the entry points and never actually
// included, so enabling reconstruction failed to compile.)
#ifdef FRAHAN_CGAL_ENABLE_RECONSTRUCTION
#  include <CGAL/Alpha_shape_3.h>
#  include <CGAL/Alpha_shape_vertex_base_3.h>
#  include <CGAL/Alpha_shape_cell_base_3.h>
#  include <CGAL/Delaunay_triangulation_3.h>
#  include <CGAL/Advancing_front_surface_reconstruction.h>
#  include <CGAL/pca_estimate_normals.h>
#  include <CGAL/mst_orient_normals.h>
#  include <CGAL/property_map.h>
#  include <CGAL/poisson_surface_reconstruction.h>
#  include <CGAL/compute_average_spacing.h>
#  include <CGAL/boost/graph/iterator.h>
#endif

#include <array>
#include <cstdlib>
#include <cstring>
#include <cmath>
#include <exception>
#include <list>
#include <map>
#include <memory>
#include <string>
#include <vector>

namespace PMP = CGAL::Polygon_mesh_processing;
typedef CGAL::Exact_predicates_inexact_constructions_kernel K;
typedef CGAL::Surface_mesh<K::Point_3>                       Mesh;
typedef Mesh::Vertex_index                                   Vh;
typedef Mesh::Face_index                                     Fh;

// =============================================================================
// All C++-only helpers in ONE anonymous namespace, OUTSIDE extern "C".
// =============================================================================

namespace {

// ─── FP-exception mask guard ─────────────────────────────────────────────
// Some hosts (Rhino + Cycles / 3Dconnexion plugins) run with floating-point
// exceptions UNMASKED. CGAL's reconstruction / predicate math legitimately
// produces denormals / overflow, which then trap as hardware FP exceptions and
// crash the whole host process. Mask all FP exceptions for the duration of a
// native entry point, and restore the host's setting on exit. Construct one
// `FpuGuard _fp;` at the top of each compute-heavy entry point.
#ifdef _MSC_VER
struct FpuGuard {
    unsigned int saved = 0;
    FpuGuard() {
        _controlfp_s(&saved, 0, 0);              // read current control word
        unsigned int cur;
        _controlfp_s(&cur, _MCW_EM, _MCW_EM);    // set all mask bits -> no traps
    }
    ~FpuGuard() {
        unsigned int cur;
        _controlfp_s(&cur, saved, _MCW_EM);      // restore host's FP mask bits
    }
};
#else
struct FpuGuard { FpuGuard() {} ~FpuGuard() {} };
#endif

// ─── Error string + basic mesh helpers ──────────────────────────────────

thread_local std::string g_lastError;

void set_error(const char* msg) { g_lastError = msg ? msg : ""; }
void set_error(const std::string& msg) { g_lastError = msg; }

// Build a CGAL Surface_mesh from flat double / int arrays.
bool build_mesh(Mesh& m,
                const double* verts, int vc,
                const int* tris, int tc) {
    if (verts == nullptr || tris == nullptr || vc < 0 || tc < 0) {
        set_error("null buffer or negative count");
        return false;
    }
    std::vector<Vh> idx;
    idx.reserve(vc);
    for (int i = 0; i < vc; ++i) {
        idx.push_back(m.add_vertex(K::Point_3(
            verts[3 * i + 0], verts[3 * i + 1], verts[3 * i + 2])));
    }
    for (int i = 0; i < tc; ++i) {
        int a = tris[3 * i + 0], b = tris[3 * i + 1], c = tris[3 * i + 2];
        if (a < 0 || a >= vc || b < 0 || b >= vc || c < 0 || c >= vc) {
            set_error("triangle index out of range");
            return false;
        }
        Fh f = m.add_face(idx[a], idx[b], idx[c]);
        if (f == Mesh::null_face()) {
            set_error("CGAL refused triangle (likely non-manifold input). "
                      "Sanitise the mesh before passing it.");
            return false;
        }
    }
    return true;
}

// Extract the result mesh into newly malloc'd flat arrays.
int extract_mesh(const Mesh& m,
                 double** out_verts, int* out_vcount,
                 int** out_tris, int* out_tcount) {
    const int vc = static_cast<int>(m.number_of_vertices());
    const int tc = static_cast<int>(m.number_of_faces());
    *out_vcount = vc;
    *out_tcount = tc;

    if (vc == 0 || tc == 0) {
        *out_verts = nullptr;
        *out_tris = nullptr;
        return 0;
    }

    *out_verts = static_cast<double*>(std::malloc(sizeof(double) * 3 * vc));
    *out_tris  = static_cast<int*>(std::malloc(sizeof(int) * 3 * tc));
    if (*out_verts == nullptr || *out_tris == nullptr) {
        std::free(*out_verts); *out_verts = nullptr;
        std::free(*out_tris);  *out_tris  = nullptr;
        *out_vcount = 0; *out_tcount = 0;
        set_error("malloc failure");
        return -1;
    }

    std::map<Vh, int> idx_of;
    int v = 0;
    for (auto vh : m.vertices()) {
        const auto& p = m.point(vh);
        (*out_verts)[3 * v + 0] = CGAL::to_double(p.x());
        (*out_verts)[3 * v + 1] = CGAL::to_double(p.y());
        (*out_verts)[3 * v + 2] = CGAL::to_double(p.z());
        idx_of[vh] = v++;
    }
    int t = 0;
    for (auto fh : m.faces()) {
        auto h0 = m.halfedge(fh);
        auto h1 = m.next(h0);
        auto h2 = m.next(h1);
        if (m.next(h2) != h0) {
            set_error("non-triangle face in CGAL output");
            std::free(*out_verts); *out_verts = nullptr;
            std::free(*out_tris);  *out_tris  = nullptr;
            *out_vcount = 0; *out_tcount = 0;
            return -2;
        }
        (*out_tris)[3 * t + 0] = idx_of[m.target(h0)];
        (*out_tris)[3 * t + 1] = idx_of[m.target(h1)];
        (*out_tris)[3 * t + 2] = idx_of[m.target(h2)];
        ++t;
    }
    return 0;
}

enum class BoolOp { Union, Intersection, Difference };

int run_op(BoolOp op,
           const double* av, int avc, const int* at, int atc,
           const double* bv, int bvc, const int* bt, int btc,
           double** ov, int* ovc, int** ot, int* otc) {
    try {
        FpuGuard _fp;   // mask FP exceptions (host may unmask them)
        Mesh a, b, out;
        if (!build_mesh(a, av, avc, at, atc)) return -10;
        if (!build_mesh(b, bv, bvc, bt, btc)) return -11;
        PMP::triangulate_faces(a);
        PMP::triangulate_faces(b);

        bool ok = false;
        switch (op) {
            case BoolOp::Union:
                ok = PMP::corefine_and_compute_union(a, b, out);
                break;
            case BoolOp::Intersection:
                ok = PMP::corefine_and_compute_intersection(a, b, out);
                break;
            case BoolOp::Difference:
                ok = PMP::corefine_and_compute_difference(a, b, out);
                break;
        }
        if (!ok) {
            set_error("CGAL corefinement failed (inputs not closed / "
                      "manifold / consistently oriented?)");
            return -12;
        }
        PMP::triangulate_faces(out);
        return extract_mesh(out, ov, ovc, ot, otc);
    } catch (const std::exception& e) {
        set_error(e.what());
        return -20;
    } catch (...) {
        set_error("unknown C++ exception");
        return -21;
    }
}

// ─── 2D polygon helpers (skeleton + partition) ──────────────────────────

typedef CGAL::Polygon_2<K>             Polygon2;
typedef CGAL::Polygon_with_holes_2<K>  PolygonWithHoles2;
typedef CGAL::Straight_skeleton_2<K>   Ss2;

bool build_polygon2(Polygon2& out,
                    const double* v, int vc,
                    bool reverse_if_cw) {
    out.clear();
    if (v == nullptr || vc < 3) {
        set_error("polygon needs at least 3 vertices");
        return false;
    }
    for (int i = 0; i < vc; ++i) {
        out.push_back(K::Point_2(v[2 * i + 0], v[2 * i + 1]));
    }
    if (reverse_if_cw && out.is_clockwise_oriented()) {
        out.reverse_orientation();
    }
    return true;
}

bool build_polygon2_cw(Polygon2& out,
                       const double* v, int vc) {
    out.clear();
    if (v == nullptr || vc < 3) return false;
    for (int i = 0; i < vc; ++i) {
        out.push_back(K::Point_2(v[2 * i + 0], v[2 * i + 1]));
    }
    if (out.is_counterclockwise_oriented()) {
        out.reverse_orientation();
    }
    return true;
}

// ─── HYBRID kernel — EPICK storage + EPECK construction ─────────────────

typedef CGAL::Exact_predicates_exact_constructions_kernel EKernel;
typedef EKernel::Point_3                                  EPoint;
typedef CGAL::Cartesian_converter<K, EKernel>             ToExact;
typedef CGAL::Cartesian_converter<EKernel, K>             ToInexact;
typedef Mesh::Property_map<Mesh::Vertex_index, EPoint>    EPointMap;

// Read/write property map that intercepts CGAL's vertex-point accesses
// during corefinement. GET returns the EPECK exact point; PUT writes
// both the EPECK store (so downstream corefinement sees exact
// arithmetic) AND the inexact mesh point (so the mesh remains usable
// as a regular Surface_mesh<EPICK::Point_3>). MUST be at C++ linkage
// scope — friend get/put return CGAL::Point_3 which has no C linkage.
struct ExactVertexPointMap {
    Mesh*     mesh = nullptr;
    EPointMap epm;

    typedef Mesh::Vertex_index                    key_type;
    typedef EPoint                                value_type;
    typedef value_type                            reference;
    typedef boost::read_write_property_map_tag    category;

    friend value_type get(const ExactVertexPointMap& m, key_type k) {
        return m.epm[k];
    }
    friend void put(const ExactVertexPointMap& m, key_type k, const value_type& v) {
        m.epm[k] = v;
        ToInexact to_inexact;
        m.mesh->point(k) = to_inexact(v);
    }
};

bool build_mesh_hybrid(Mesh& m, EPointMap& epm,
                       const double* verts, int vc,
                       const int* tris, int tc) {
    if (verts == nullptr || tris == nullptr || vc < 0 || tc < 0) {
        set_error("null buffer or negative count");
        return false;
    }
    ToExact to_exact;
    std::vector<Vh> idx;
    idx.reserve(vc);
    for (int i = 0; i < vc; ++i) {
        K::Point_3 p(verts[3 * i + 0], verts[3 * i + 1], verts[3 * i + 2]);
        Vh v = m.add_vertex(p);
        idx.push_back(v);
        epm[v] = to_exact(p);
    }
    for (int i = 0; i < tc; ++i) {
        int a = tris[3 * i + 0], b = tris[3 * i + 1], c = tris[3 * i + 2];
        if (a < 0 || a >= vc || b < 0 || b >= vc || c < 0 || c >= vc) {
            set_error("triangle index out of range");
            return false;
        }
        Fh f = m.add_face(idx[a], idx[b], idx[c]);
        if (f == Mesh::null_face()) {
            set_error("CGAL refused triangle (likely non-manifold input). "
                      "Sanitise the mesh before passing it.");
            return false;
        }
    }
    return true;
}

int run_op_hybrid(BoolOp op,
                  const double* av, int avc, const int* at, int atc,
                  const double* bv, int bvc, const int* bt, int btc,
                  double** ov, int* ovc, int** ot, int* otc) {
    try {
        FpuGuard _fp;   // mask FP exceptions (host may unmask them)
        Mesh a, b, out;
        auto aEpmRes = a.add_property_map<Mesh::Vertex_index, EPoint>("v:epoint");
        auto bEpmRes = b.add_property_map<Mesh::Vertex_index, EPoint>("v:epoint");
        auto oEpmRes = out.add_property_map<Mesh::Vertex_index, EPoint>("v:epoint");
        if (!aEpmRes.second || !bEpmRes.second || !oEpmRes.second) {
            set_error("could not add EPECK property map");
            return -60;
        }
        EPointMap aEpm = aEpmRes.first;
        EPointMap bEpm = bEpmRes.first;
        EPointMap oEpm = oEpmRes.first;

        if (!build_mesh_hybrid(a, aEpm, av, avc, at, atc)) return -61;
        if (!build_mesh_hybrid(b, bEpm, bv, bvc, bt, btc)) return -62;
        PMP::triangulate_faces(a);
        PMP::triangulate_faces(b);

        ExactVertexPointMap vpmA{&a, aEpm};
        ExactVertexPointMap vpmB{&b, bEpm};
        ExactVertexPointMap vpmO{&out, oEpm};

        bool ok = false;
        switch (op) {
            case BoolOp::Union:
                ok = PMP::corefine_and_compute_union(a, b, out,
                    PMP::parameters::vertex_point_map(vpmA),
                    PMP::parameters::vertex_point_map(vpmB),
                    PMP::parameters::vertex_point_map(vpmO));
                break;
            case BoolOp::Intersection:
                ok = PMP::corefine_and_compute_intersection(a, b, out,
                    PMP::parameters::vertex_point_map(vpmA),
                    PMP::parameters::vertex_point_map(vpmB),
                    PMP::parameters::vertex_point_map(vpmO));
                break;
            case BoolOp::Difference:
                ok = PMP::corefine_and_compute_difference(a, b, out,
                    PMP::parameters::vertex_point_map(vpmA),
                    PMP::parameters::vertex_point_map(vpmB),
                    PMP::parameters::vertex_point_map(vpmO));
                break;
        }
        if (!ok) {
            set_error("CGAL hybrid corefinement failed (inputs not closed / "
                      "manifold / consistently oriented?)");
            return -63;
        }
        PMP::triangulate_faces(out);
        return extract_mesh(out, ov, ovc, ot, otc);
    } catch (const std::exception& e) {
        set_error(e.what());
        return -64;
    } catch (...) {
        set_error("unknown C++ exception in run_op_hybrid");
        return -65;
    }
}

} // anonymous namespace

// =============================================================================
// All exported FRAHAN_CGAL_API functions in ONE extern "C" block.
// =============================================================================

extern "C" {

FRAHAN_CGAL_API const char* frahan_cgal_version(void) {
    return "Frahan-CGAL 0.2 (CGAL " CGAL_VERSION_STR ")";
}

FRAHAN_CGAL_API const char* frahan_cgal_last_error(void) {
    return g_lastError.c_str();
}

FRAHAN_CGAL_API int frahan_cgal_mesh_union(
    const double* av, int avc, const int* at, int atc,
    const double* bv, int bvc, const int* bt, int btc,
    double** ov, int* ovc, int** ot, int* otc) {
    return run_op(BoolOp::Union, av, avc, at, atc, bv, bvc, bt, btc,
                  ov, ovc, ot, otc);
}

FRAHAN_CGAL_API int frahan_cgal_mesh_intersection(
    const double* av, int avc, const int* at, int atc,
    const double* bv, int bvc, const int* bt, int btc,
    double** ov, int* ovc, int** ot, int* otc) {
    return run_op(BoolOp::Intersection, av, avc, at, atc, bv, bvc, bt, btc,
                  ov, ovc, ot, otc);
}

FRAHAN_CGAL_API int frahan_cgal_mesh_difference(
    const double* av, int avc, const int* at, int atc,
    const double* bv, int bvc, const int* bt, int btc,
    double** ov, int* ovc, int** ot, int* otc) {
    return run_op(BoolOp::Difference, av, avc, at, atc, bv, bvc, bt, btc,
                  ov, ovc, ot, otc);
}

FRAHAN_CGAL_API void frahan_cgal_free_buffers(double* verts, int* tris) {
    if (verts != nullptr) std::free(verts);
    if (tris  != nullptr) std::free(tris);
}

// ─── Mesh repair pipeline ───────────────────────────────────────────────

FRAHAN_CGAL_API int frahan_cgal_repair_mesh(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    double** out_verts,  int* out_vcount,
    int**    out_tris,   int* out_tcount) {
    try {
        FpuGuard _fp;   // mask FP exceptions (host may unmask them)
        Mesh m;
        if (!build_mesh(m, verts, vcount, tris, tcount)) return -70;

        // 1. Triangulate any non-triangle faces (no-op if already triangulated).
        PMP::triangulate_faces(m);

        // 2. Stitch coincident half-edges. Closes fissures from
        //    independent face additions sharing geometry.
        PMP::stitch_borders(m);

        // 3. Drop zero-area triangles (e.g. cap triangles produced by
        //    upstream simplification). Threshold defaults to the
        //    machine epsilon for K's FT.
        PMP::remove_degenerate_faces(m);

        // 4. If the mesh is now closed, ensure faces consistently bound
        //    a volume (outward-pointing normals). Open meshes are left
        //    as-is — orientation is undefined for non-watertight inputs.
        if (CGAL::is_closed(m)) {
            PMP::orient_to_bound_a_volume(m);
        }

        // 5. Reclaim indices freed by the repairs.
        m.collect_garbage();

        return extract_mesh(m, out_verts, out_vcount, out_tris, out_tcount);
    } catch (const std::exception& e) {
        set_error(e.what());
        return -71;
    } catch (...) {
        set_error("unknown C++ exception in repair_mesh");
        return -72;
    }
}

FRAHAN_CGAL_API void frahan_cgal_free_pdouble(double* p) {
    if (p != nullptr) std::free(p);
}

FRAHAN_CGAL_API void frahan_cgal_free_pint(int* p) {
    if (p != nullptr) std::free(p);
}

// ─── Mesh decimation ────────────────────────────────────────────────────

FRAHAN_CGAL_API int frahan_cgal_decimate_mesh(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    int     stop_kind,
    double  stop_value,
    double** out_verts,  int* out_vcount,
    int**    out_tris,   int* out_tcount) {
    namespace SMS = CGAL::Surface_mesh_simplification;
    try {
        FpuGuard _fp;   // mask FP exceptions (host may unmask them)
        Mesh m;
        if (!build_mesh(m, verts, vcount, tris, tcount)) return -80;
        PMP::triangulate_faces(m);

        switch (stop_kind) {
            case 0: { // count ratio in (0, 1)
                if (stop_value <= 0.0 || stop_value >= 1.0) {
                    set_error("count_ratio must be in (0, 1)");
                    return -81;
                }
                SMS::Edge_count_ratio_stop_predicate<Mesh> stop(stop_value);
                SMS::edge_collapse(m, stop);
                break;
            }
            case 1: { // target edge count, >= 1
                if (stop_value < 1.0) {
                    set_error("edge_count must be >= 1");
                    return -82;
                }
                SMS::Edge_count_stop_predicate<Mesh> stop(static_cast<std::size_t>(stop_value));
                SMS::edge_collapse(m, stop);
                break;
            }
            case 2: { // minimum edge length, > 0
                if (stop_value <= 0.0) {
                    set_error("edge_length must be > 0");
                    return -83;
                }
                SMS::Edge_length_stop_predicate<K::FT> stop(stop_value);
                SMS::edge_collapse(m, stop);
                break;
            }
            default:
                set_error("invalid stop_kind (0=ratio, 1=count, 2=length)");
                return -84;
        }

        m.collect_garbage();
        return extract_mesh(m, out_verts, out_vcount, out_tris, out_tcount);
    } catch (const std::exception& e) {
        set_error(e.what());
        return -85;
    } catch (...) {
        set_error("unknown C++ exception in decimate_mesh");
        return -86;
    }
}

// ─── Oriented bounding box (3D) — Eigen-gated ───────────────────────────

#ifdef FRAHAN_CGAL_HAVE_EIGEN
FRAHAN_CGAL_API int frahan_cgal_obb_3d(
    const double* verts, int vcount,
    const int* tris, int tcount,
    double out_obb[15]) {
    if (verts == nullptr || vcount <= 0 || out_obb == nullptr) {
        set_error("null buffer or non-positive vertex count");
        return -30;
    }
    try {
        FpuGuard _fp;   // mask FP exceptions (host may unmask them)
        std::vector<K::Point_3> pts;
        pts.reserve(vcount);
        for (int i = 0; i < vcount; ++i) {
            pts.emplace_back(verts[3 * i + 0], verts[3 * i + 1], verts[3 * i + 2]);
        }
        // tris is informational; CGAL OBB takes a point cloud regardless.
        (void)tris; (void)tcount;

        std::array<K::Point_3, 8> obb;
        CGAL::oriented_bounding_box(pts, obb);

        // CGAL's OBB returns 8 corner points (origin, +X, +X+Y, +Y, +Z,
        // +X+Z, +X+Y+Z, +Y+Z). Extract origin + axis vectors + extents.
        const auto& o  = obb[0];
        const auto& px = obb[1];
        const auto& py = obb[3];
        const auto& pz = obb[4];

        const double ox = CGAL::to_double(o.x());
        const double oy = CGAL::to_double(o.y());
        const double oz = CGAL::to_double(o.z());

        double xx = CGAL::to_double(px.x()) - ox;
        double xy = CGAL::to_double(px.y()) - oy;
        double xz = CGAL::to_double(px.z()) - oz;
        double yx = CGAL::to_double(py.x()) - ox;
        double yy = CGAL::to_double(py.y()) - oy;
        double yz = CGAL::to_double(py.z()) - oz;
        double zx = CGAL::to_double(pz.x()) - ox;
        double zy = CGAL::to_double(pz.y()) - oy;
        double zz = CGAL::to_double(pz.z()) - oz;

        const double xLen = std::sqrt(xx * xx + xy * xy + xz * xz);
        const double yLen = std::sqrt(yx * yx + yy * yy + yz * yz);
        const double zLen = std::sqrt(zx * zx + zy * zy + zz * zz);

        if (xLen < 1e-15 || yLen < 1e-15 || zLen < 1e-15) {
            set_error("degenerate OBB (zero-length axis)");
            return -31;
        }

        out_obb[0]  = ox;
        out_obb[1]  = oy;
        out_obb[2]  = oz;
        out_obb[3]  = xx / xLen;
        out_obb[4]  = xy / xLen;
        out_obb[5]  = xz / xLen;
        out_obb[6]  = yx / yLen;
        out_obb[7]  = yy / yLen;
        out_obb[8]  = yz / yLen;
        out_obb[9]  = zx / zLen;
        out_obb[10] = zy / zLen;
        out_obb[11] = zz / zLen;
        out_obb[12] = xLen;
        out_obb[13] = yLen;
        out_obb[14] = zLen;
        return 0;
    } catch (const std::exception& e) {
        set_error(e.what());
        return -32;
    } catch (...) {
        set_error("unknown C++ exception in obb_3d");
        return -33;
    }
}
#endif // FRAHAN_CGAL_HAVE_EIGEN

// ─── Straight skeleton (2D, interior) ───────────────────────────────────

FRAHAN_CGAL_API int frahan_cgal_straight_skeleton_2d(
    const double* outer_verts, int outer_vc,
    const double* hole_verts, const int* hole_vcounts, int hole_count,
    double** out_verts, int* out_vcount,
    int** out_edges,    int* out_ecount,
    double** out_times, int* out_tcount) {
    if (out_verts == nullptr || out_vcount == nullptr ||
        out_edges == nullptr || out_ecount == nullptr ||
        out_times == nullptr || out_tcount == nullptr) {
        set_error("null output pointer");
        return -40;
    }
    *out_verts = nullptr; *out_vcount = 0;
    *out_edges = nullptr; *out_ecount = 0;
    *out_times = nullptr; *out_tcount = 0;

    try {
        FpuGuard _fp;   // mask FP exceptions (host may unmask them)
        Polygon2 outer;
        if (!build_polygon2(outer, outer_verts, outer_vc, /*reverse_if_cw*/ true)) {
            return -41;
        }

        std::vector<Polygon2> holes;
        if (hole_count > 0 && hole_verts != nullptr && hole_vcounts != nullptr) {
            int offset = 0;
            holes.reserve(hole_count);
            for (int i = 0; i < hole_count; ++i) {
                int hc = hole_vcounts[i];
                if (hc < 3) {
                    set_error("hole has fewer than 3 vertices");
                    return -42;
                }
                Polygon2 h;
                if (!build_polygon2_cw(h, hole_verts + 2 * offset, hc)) return -43;
                holes.push_back(std::move(h));
                offset += hc;
            }
        }

        std::shared_ptr<Ss2> ss;
        if (holes.empty()) {
            ss = CGAL::create_interior_straight_skeleton_2(outer);
        } else {
            PolygonWithHoles2 pwh(outer);
            for (auto& h : holes) pwh.add_hole(h);
            ss = CGAL::create_interior_straight_skeleton_2(pwh);
        }
        if (!ss) {
            set_error("create_interior_straight_skeleton_2 returned null");
            return -44;
        }

        const int nv = static_cast<int>(ss->size_of_vertices());
        const int ne = static_cast<int>(ss->size_of_halfedges() / 2);

        if (nv == 0) { return 0; }

        *out_verts = static_cast<double*>(std::malloc(sizeof(double) * 2 * nv));
        *out_times = static_cast<double*>(std::malloc(sizeof(double) * nv));
        if (!*out_verts || !*out_times) {
            std::free(*out_verts); *out_verts = nullptr;
            std::free(*out_times); *out_times = nullptr;
            set_error("malloc failure (skeleton verts)");
            return -45;
        }

        std::map<typename Ss2::Vertex_const_handle, int> idx;
        int v = 0;
        for (auto vh = ss->vertices_begin(); vh != ss->vertices_end(); ++vh) {
            (*out_verts)[2 * v + 0] = CGAL::to_double(vh->point().x());
            (*out_verts)[2 * v + 1] = CGAL::to_double(vh->point().y());
            (*out_times)[v]         = CGAL::to_double(vh->time());
            idx[vh] = v;
            ++v;
        }
        *out_vcount = v;
        *out_tcount = v;

        if (ne == 0) {
            *out_edges = nullptr;
            *out_ecount = 0;
            return 0;
        }

        *out_edges = static_cast<int*>(std::malloc(sizeof(int) * 2 * ne));
        if (!*out_edges) {
            std::free(*out_verts); *out_verts = nullptr;
            std::free(*out_times); *out_times = nullptr;
            *out_vcount = 0; *out_tcount = 0;
            set_error("malloc failure (skeleton edges)");
            return -46;
        }

        // Each undirected edge appears as two opposite halfedges; emit only
        // the canonical direction (smaller source-target index pair) to
        // avoid duplicates.
        int e = 0;
        for (auto hh = ss->halfedges_begin(); hh != ss->halfedges_end(); ++hh) {
            auto src = hh->opposite()->vertex();
            auto tgt = hh->vertex();
            int si = idx[src];
            int ti = idx[tgt];
            if (si < ti) {
                (*out_edges)[2 * e + 0] = si;
                (*out_edges)[2 * e + 1] = ti;
                ++e;
            }
        }
        *out_ecount = e;
        return 0;
    } catch (const std::exception& ex) {
        set_error(ex.what());
        return -47;
    } catch (...) {
        set_error("unknown C++ exception in straight_skeleton_2d");
        return -48;
    }
}

// ─── Mesh boolean — HYBRID kernel ───────────────────────────────────────

FRAHAN_CGAL_API int frahan_cgal_mesh_boolean_hybrid(
    int op_kind,
    const double* av, int avc, const int* at, int atc,
    const double* bv, int bvc, const int* bt, int btc,
    double** ov, int* ovc, int** ot, int* otc) {
    BoolOp op;
    switch (op_kind) {
        case 0: op = BoolOp::Union; break;
        case 1: op = BoolOp::Intersection; break;
        case 2: op = BoolOp::Difference; break;
        default:
            set_error("invalid op_kind (0=union, 1=intersection, 2=difference)");
            return -66;
    }
    return run_op_hybrid(op, av, avc, at, atc, bv, bvc, bt, btc,
                         ov, ovc, ot, otc);
}

// ─── Polygon partition (2D) ─────────────────────────────────────────────

FRAHAN_CGAL_API int frahan_cgal_polygon_partition_2d(
    const double* verts, int vcount,
    int kind,
    double** out_verts,   int* out_vcount,
    int**    out_indices, int* out_icount,
    int**    out_starts,  int* out_pcount) {
    if (out_verts == nullptr || out_vcount == nullptr ||
        out_indices == nullptr || out_icount == nullptr ||
        out_starts == nullptr || out_pcount == nullptr) {
        set_error("null output pointer");
        return -50;
    }
    *out_verts = nullptr;   *out_vcount = 0;
    *out_indices = nullptr; *out_icount = 0;
    *out_starts = nullptr;  *out_pcount = 0;

    try {
        FpuGuard _fp;   // mask FP exceptions (host may unmask them)
        Polygon2 poly;
        if (!build_polygon2(poly, verts, vcount, /*reverse_if_cw*/ true)) {
            return -51;
        }
        if (!poly.is_simple()) {
            set_error("polygon must be simple (no self-intersections)");
            return -52;
        }

        typedef CGAL::Partition_traits_2<K> Traits;
        std::list<Traits::Polygon_2> result;

        switch (kind) {
            case 0: // Hertel-Mehlhorn — fast, approximate convex
                CGAL::approx_convex_partition_2(
                    poly.vertices_begin(), poly.vertices_end(),
                    std::back_inserter(result));
                break;
            case 1: // Greene optimal convex
                CGAL::optimal_convex_partition_2(
                    poly.vertices_begin(), poly.vertices_end(),
                    std::back_inserter(result));
                break;
            case 2: // y-monotone partition
                CGAL::y_monotone_partition_2(
                    poly.vertices_begin(), poly.vertices_end(),
                    std::back_inserter(result));
                break;
            default:
                set_error("invalid partition kind (0=approx_convex, 1=optimal_convex, 2=y_monotone)");
                return -53;
        }

        // Partition_2 emits polygons whose vertices are SUBSETS of the
        // input vertices (no Steiner points). Identify each output vertex
        // by (x, y) coordinate equality and re-index.
        std::map<std::pair<double, double>, int> vmap;
        std::vector<double> outV;
        std::vector<int>    outIdx;
        std::vector<int>    outStart;
        outStart.push_back(0);

        for (const auto& sub : result) {
            for (auto vit = sub.vertices_begin(); vit != sub.vertices_end(); ++vit) {
                double x = CGAL::to_double(vit->x());
                double y = CGAL::to_double(vit->y());
                auto key = std::make_pair(x, y);
                auto it = vmap.find(key);
                int vi;
                if (it == vmap.end()) {
                    vi = static_cast<int>(outV.size() / 2);
                    outV.push_back(x);
                    outV.push_back(y);
                    vmap[key] = vi;
                } else {
                    vi = it->second;
                }
                outIdx.push_back(vi);
            }
            outStart.push_back(static_cast<int>(outIdx.size()));
        }

        const int vc = static_cast<int>(outV.size() / 2);
        const int ic = static_cast<int>(outIdx.size());
        const int pc = static_cast<int>(outStart.size() - 1);

        *out_verts   = static_cast<double*>(std::malloc(sizeof(double) * outV.size()));
        *out_indices = static_cast<int*>(std::malloc(sizeof(int) * outIdx.size()));
        *out_starts  = static_cast<int*>(std::malloc(sizeof(int) * outStart.size()));
        if (!*out_verts || !*out_indices || !*out_starts) {
            std::free(*out_verts);   *out_verts = nullptr;
            std::free(*out_indices); *out_indices = nullptr;
            std::free(*out_starts);  *out_starts = nullptr;
            set_error("malloc failure (partition)");
            return -54;
        }
        std::memcpy(*out_verts,   outV.data(),     sizeof(double) * outV.size());
        std::memcpy(*out_indices, outIdx.data(),   sizeof(int)    * outIdx.size());
        std::memcpy(*out_starts,  outStart.data(), sizeof(int)    * outStart.size());

        *out_vcount = vc;
        *out_icount = ic;
        *out_pcount = pc;
        return 0;
    } catch (const std::exception& ex) {
        set_error(ex.what());
        return -55;
    } catch (...) {
        set_error("unknown C++ exception in polygon_partition_2d");
        return -56;
    }
}

// =============================================================================
// Surface mesh segmentation via Shape Diameter Function (SDF).
//
// Two-stage CGAL pipeline (mesh_segmentation.h):
//   1. CGAL::sdf_values - compute one SDF value per face by casting nb_rays
//      rays into the inward cone of half-angle cone_angle/2 from each
//      facet centroid; SDF = robust mean of ray hit-distances.
//   2. CGAL::segmentation_from_sdf_values - GMM cluster the SDF values
//      into nb_clusters classes, then graph-cut the per-face labels with
//      a smoothness penalty (smoothing_lambda) for spatial coherence.
//
// Output is one segment_id (in [0, actual_clusters)) per input triangle.
// Caller groups triangles by segment_id to extract per-segment sub-meshes
// (same SplitBySeedId pattern used by the Geogram RVD components).
//
// SEMANTIC NOTE: SDF segmentation cuts at concave features (narrow necks,
// deep folds). Convex / mostly-convex inputs produce few or one segment
// regardless of nb_clusters requested. For Voronoi-style spatial chopping
// of a convex block, use the Geogram block partition instead.
// =============================================================================

FRAHAN_CGAL_API int frahan_cgal_segment_sdf(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    int     nb_clusters,
    double  smoothing_lambda,
    double  cone_angle_radians,
    int     nb_rays,
    int     postprocess,
    int**   out_segment_ids, int* out_idcount,
    int*    out_actual_clusters) {
    if (out_segment_ids == nullptr || out_idcount == nullptr ||
        out_actual_clusters == nullptr) {
        set_error("null output pointer");
        return -310;
    }
    *out_segment_ids = nullptr;
    *out_idcount = 0;
    *out_actual_clusters = 0;

    if (nb_clusters < 2) {
        set_error("nb_clusters must be >= 2");
        return -311;
    }
    if (smoothing_lambda < 0.0 || smoothing_lambda > 1.0) {
        set_error("smoothing_lambda must be in [0, 1]");
        return -312;
    }

    try {
        FpuGuard _fp;   // mask FP exceptions (host may unmask them)
        Mesh m;
        if (!build_mesh(m, verts, vcount, tris, tcount)) return -313;

        const int faceCount = static_cast<int>(m.number_of_faces());
        if (faceCount == 0) {
            set_error("mesh has no faces");
            return -314;
        }

        // Per-face SDF values + segment IDs as Surface_mesh property maps.
        typedef Mesh::Property_map<Fh, double>      SdfMap;
        typedef Mesh::Property_map<Fh, std::size_t> SegMap;
        SdfMap sdf_pm = m.add_property_map<Fh, double>(
            "f:sdf", 0.0).first;
        SegMap seg_pm = m.add_property_map<Fh, std::size_t>(
            "f:segment", 0).first;

        // Step 1: SDF values. CGAL defaults: cone_angle = 2/3 * pi
        // (~120 degrees), nb_rays = 25, postprocess = true.
        const double cone =
            cone_angle_radians > 0.0
                ? cone_angle_radians
                : (2.0 / 3.0) * CGAL_PI;
        const std::size_t rays =
            (nb_rays > 0)
                ? static_cast<std::size_t>(nb_rays)
                : static_cast<std::size_t>(25);
        const bool post = (postprocess != 0);

        CGAL::sdf_values(m, sdf_pm, cone, rays, post);

        // Step 2: cluster + graph-cut. Returns the actual number of
        // segments, which can be smaller than nb_clusters if some
        // clusters end up empty.
        const std::size_t actual = CGAL::segmentation_from_sdf_values(
            m, sdf_pm, seg_pm,
            static_cast<std::size_t>(nb_clusters),
            smoothing_lambda);

        *out_actual_clusters = static_cast<int>(actual);
        *out_idcount = faceCount;
        *out_segment_ids = static_cast<int*>(
            std::malloc(sizeof(int) * faceCount));
        if (*out_segment_ids == nullptr) {
            set_error("malloc failure (segment_sdf)");
            *out_idcount = 0;
            *out_actual_clusters = 0;
            return -315;
        }

        // Surface_mesh face indices are 0..faceCount-1 in the order they
        // were added by build_mesh, which matches the input tris[] order.
        for (Fh f : m.faces()) {
            const int idx = static_cast<int>(f);
            if (idx >= 0 && idx < faceCount) {
                (*out_segment_ids)[idx] = static_cast<int>(seg_pm[f]);
            }
        }
        return 0;
    } catch (const std::exception& ex) {
        set_error(ex.what());
        return -316;
    } catch (...) {
        set_error("unknown C++ exception in segment_sdf");
        return -317;
    }
}

// =============================================================================
// Angle-based face segmentation - "split where the surface bends sharply".
//
// Two PMP calls:
//   1. detect_sharp_edges(mesh, angle_deg, edge_is_feature_pmap)
//      Marks every edge whose dihedral angle exceeds the threshold as
//      a feature edge.
//   2. connected_components(mesh, face_pmap, np.edge_is_constrained_map(...))
//      Flood-fills faces, treating feature edges as walls. Each
//      connected region of soft-edge-connected faces gets one
//      segment_id.
//
// Output is one segment_id per input triangle, identical shape to
// frahan_cgal_segment_sdf. Caller groups by segment_id to get one
// sub-mesh per smoothly-connected region.
//
// Tuning:
//   * Small angle (5-15 deg) - only very flat regions group.
//   * 30-60 deg - typical sweet spot for "smooth band detection" on
//     curved organic forms.
//   * 90+ deg - only orthogonal-ish creases separate regions.
// =============================================================================

FRAHAN_CGAL_API int frahan_cgal_segment_by_angle(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    double  angle_threshold_degrees,
    int**   out_segment_ids, int* out_idcount,
    int*    out_actual_clusters) {
    if (out_segment_ids == nullptr || out_idcount == nullptr ||
        out_actual_clusters == nullptr) {
        set_error("null output pointer");
        return -320;
    }
    *out_segment_ids = nullptr;
    *out_idcount = 0;
    *out_actual_clusters = 0;

    if (!(angle_threshold_degrees > 0.0 && angle_threshold_degrees < 180.0)) {
        set_error("angle_threshold_degrees must be in (0, 180)");
        return -321;
    }

    try {
        FpuGuard _fp;   // mask FP exceptions (host may unmask them)
        Mesh m;
        if (!build_mesh(m, verts, vcount, tris, tcount)) return -322;

        const int faceCount = static_cast<int>(m.number_of_faces());
        if (faceCount == 0) {
            set_error("mesh has no faces");
            return -323;
        }

        typedef Mesh::Edge_index Eh;
        typedef Mesh::Property_map<Eh, bool>        FeaturePMap;
        typedef Mesh::Property_map<Fh, std::size_t> SegPMap;

        FeaturePMap feat_pm =
            m.add_property_map<Eh, bool>("e:feature", false).first;
        SegPMap seg_pm =
            m.add_property_map<Fh, std::size_t>("f:cc", 0).first;

        PMP::detect_sharp_edges(m, angle_threshold_degrees, feat_pm);

        const std::size_t actual = PMP::connected_components(
            m, seg_pm,
            CGAL::parameters::edge_is_constrained_map(feat_pm));

        *out_actual_clusters = static_cast<int>(actual);
        *out_idcount = faceCount;
        *out_segment_ids = static_cast<int*>(
            std::malloc(sizeof(int) * faceCount));
        if (*out_segment_ids == nullptr) {
            set_error("malloc failure (segment_by_angle)");
            *out_idcount = 0;
            *out_actual_clusters = 0;
            return -324;
        }
        for (Fh f : m.faces()) {
            const int idx = static_cast<int>(f);
            if (idx >= 0 && idx < faceCount) {
                (*out_segment_ids)[idx] = static_cast<int>(seg_pm[f]);
            }
        }
        return 0;
    } catch (const std::exception& ex) {
        set_error(ex.what());
        return -325;
    } catch (...) {
        set_error("unknown C++ exception in segment_by_angle");
        return -326;
    }
}

// =============================================================================
// Geodesic Voronoi via the Heat Method (Crane, Weischedel, Wardetzky 2013).
//
// For each seed point, snap to the nearest mesh vertex, then call CGAL's
// Surface_mesh_geodesic_distances_3 to compute the distance field FROM
// that vertex over the mesh. The factorisation is reused across seeds,
// so cost is one factorisation + N back-solves rather than N from
// scratch.
//
// Each mesh vertex is then assigned to the seed with the smallest
// geodesic distance (running argmin), and each triangle inherits the
// majority seed-id of its three vertices.
//
// The result is a geodesic Voronoi partition on the surface - the cut
// curves between cells follow on-surface equidistance, giving "neat"
// boundaries that respect curvature instead of slicing through the
// mesh the way Euclidean Voronoi (Geogram RVD with seeds) does.
//
// Reference: https://www.cgal.org/2019/01/23/Heat_Method/
// =============================================================================

FRAHAN_CGAL_API int frahan_cgal_geodesic_voronoi(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    const double* seed_points, int seed_count,
    int**   out_segment_ids, int* out_idcount,
    int*    out_actual_clusters) {
    if (out_segment_ids == nullptr || out_idcount == nullptr ||
        out_actual_clusters == nullptr) {
        set_error("null output pointer");
        return -330;
    }
    *out_segment_ids = nullptr;
    *out_idcount = 0;
    *out_actual_clusters = 0;
    if (seed_points == nullptr || seed_count < 1) {
        set_error("need at least 1 seed point");
        return -331;
    }

    try {
        FpuGuard _fp;   // mask FP exceptions (host may unmask them)
        Mesh m;
        if (!build_mesh(m, verts, vcount, tris, tcount)) return -332;
        const int faceCount = static_cast<int>(m.number_of_faces());
        const int vertCount = static_cast<int>(m.number_of_vertices());
        if (faceCount == 0 || vertCount == 0) {
            set_error("mesh has no faces or vertices");
            return -333;
        }

        // Snap each input seed point to its nearest mesh vertex.
        // Linear scan is fine: typical seed_count is small (< 100) and
        // we already pay O(V) for the heat solve, so an O(V * S) scan
        // never dominates. Build a parallel array of vertex_descriptors.
        std::vector<Vh> seed_vh(seed_count);
        for (int s = 0; s < seed_count; ++s) {
            const double sx = seed_points[3 * s + 0];
            const double sy = seed_points[3 * s + 1];
            const double sz = seed_points[3 * s + 2];
            double best_d2 = std::numeric_limits<double>::infinity();
            Vh best_vh = Mesh::null_vertex();
            for (Vh v : m.vertices()) {
                const auto& p = m.point(v);
                const double dx = CGAL::to_double(p.x()) - sx;
                const double dy = CGAL::to_double(p.y()) - sy;
                const double dz = CGAL::to_double(p.z()) - sz;
                const double d2 = dx*dx + dy*dy + dz*dz;
                if (d2 < best_d2) { best_d2 = d2; best_vh = v; }
            }
            if (best_vh == Mesh::null_vertex()) {
                set_error("could not snap seed to a mesh vertex");
                return -334;
            }
            seed_vh[s] = best_vh;
        }

        // Heat method - default mode is Intrinsic_Delaunay, which is
        // robust on poorly-shaped meshes. The class caches the
        // cotangent Laplacian + factorisation; clear_sources() and
        // re-add per seed reuses both.
        namespace HM = CGAL::Heat_method_3;
        typedef HM::Surface_mesh_geodesic_distances_3<Mesh> HeatSolver;

        Mesh::Property_map<Vh, double> dist_pm =
            m.add_property_map<Vh, double>("v:hm_dist", 0.0).first;

        // Per-vertex running argmin: nearest seed and its distance.
        std::vector<double> best_dist(vertCount,
            std::numeric_limits<double>::infinity());
        std::vector<int> best_seed(vertCount, -1);

        HeatSolver hm(m);
        for (int s = 0; s < seed_count; ++s) {
            hm.clear_sources();
            hm.add_source(seed_vh[s]);
            hm.estimate_geodesic_distances(dist_pm);

            for (Vh v : m.vertices()) {
                const int vi = static_cast<int>(v);
                const double d = dist_pm[v];
                if (d < best_dist[vi]) {
                    best_dist[vi] = d;
                    best_seed[vi] = s;
                }
            }
        }

        // Per-face seed_id by majority vote of its 3 vertices.
        // Three-way ties (all three vertices on different cells) fall
        // back to the first vertex's seed - those faces sit exactly on
        // a cell-boundary triangle and any choice is locally valid.
        *out_segment_ids = static_cast<int*>(
            std::malloc(sizeof(int) * faceCount));
        if (*out_segment_ids == nullptr) {
            set_error("malloc failure (geodesic_voronoi)");
            return -335;
        }

        for (Fh f : m.faces()) {
            auto h = m.halfedge(f);
            int v0 = static_cast<int>(m.target(h));
            int v1 = static_cast<int>(m.target(m.next(h)));
            int v2 = static_cast<int>(m.target(m.next(m.next(h))));
            int s0 = best_seed[v0];
            int s1 = best_seed[v1];
            int s2 = best_seed[v2];
            int win;
            if (s0 == s1 || s0 == s2)      win = s0;
            else if (s1 == s2)             win = s1;
            else                           win = s0;
            const int fi = static_cast<int>(f);
            if (fi >= 0 && fi < faceCount) {
                (*out_segment_ids)[fi] = win;
            }
        }

        *out_idcount = faceCount;
        *out_actual_clusters = seed_count;
        return 0;
    } catch (const std::exception& ex) {
        set_error(ex.what());
        return -336;
    } catch (...) {
        set_error("unknown C++ exception in geodesic_voronoi");
        return -337;
    }
}

/* =============================================================================
 * Phase H + I — Reconstruction primitives and cloud ICP normal estimation.
 * Wraps CGAL Alpha_shape_3, Advancing_front_surface_reconstruction, and
 * pca_estimate_normals + mst_orient_normals.
 * Required CGAL headers in the file scope above:
 *   <CGAL/Alpha_shape_3.h>
 *   <CGAL/Alpha_shape_vertex_base_3.h>
 *   <CGAL/Alpha_shape_cell_base_3.h>
 *   <CGAL/Delaunay_triangulation_3.h>
 *   <CGAL/Advancing_front_surface_reconstruction.h>
 *   <CGAL/pca_estimate_normals.h>
 *   <CGAL/mst_orient_normals.h>
 *   <CGAL/property_map.h>
 * Add these to the top of frahan_cgal.cpp when enabling Phase H/I.
 * ============================================================================= */

FRAHAN_CGAL_API int frahan_cgal_alpha_shape_3(
    const double* points, int pcount,
    double  alpha,
    double**out_verts,   int* out_vcount,
    int**   out_tris,    int* out_tcount)
{
    *out_verts = nullptr; *out_tris = nullptr;
    *out_vcount = 0; *out_tcount = 0;
    if (!points || pcount < 4) { set_error("alpha_shape_3: need >=4 points"); return -401; }
#if defined(FRAHAN_CGAL_ENABLE_RECONSTRUCTION)
    try {
        FpuGuard _fp;   // mask FP exceptions for the duration (host may unmask them)
        typedef CGAL::Exact_predicates_inexact_constructions_kernel    K;
        typedef CGAL::Alpha_shape_vertex_base_3<K>                     Vb;
        typedef CGAL::Alpha_shape_cell_base_3<K>                       Cb;
        typedef CGAL::Triangulation_data_structure_3<Vb, Cb>           Tds;
        typedef CGAL::Delaunay_triangulation_3<K, Tds, CGAL::Fast_location> Delaunay;
        typedef CGAL::Alpha_shape_3<Delaunay>                          AlphaShape;
        typedef K::Point_3                                             Point;

        std::vector<Point> ps;
        ps.reserve(pcount);
        for (int i = 0; i < pcount; ++i)
            ps.emplace_back(points[3*i + 0], points[3*i + 1], points[3*i + 2]);

        AlphaShape as(ps.begin(), ps.end());
        // REGULARIZED mode (not GENERAL): removes singular / lower-dimensional features
        // so the alpha complex is a clean bounding surface. GENERAL mode + collecting
        // SINGULAR facets was the source of the "weird mesh" (dangling spikes / loose
        // flaps): a SINGULAR facet is bounded by exterior on BOTH sides and does not
        // bound the solid. CGAL Alpha_shape_3 docs; Edelsbrunner & Mucke 1994.
        as.set_mode(AlphaShape::REGULARIZED);
        if (alpha <= 0.0) {
            auto opt = as.find_optimal_alpha(1);
            as.set_alpha(opt != as.alpha_end() ? *opt : *as.alpha_begin());
        } else {
            as.set_alpha(static_cast<K::FT>(alpha));
        }

        // Collect boundary facets: REGULAR only (on the interior/exterior boundary).
        // SINGULAR is deliberately dropped -- those are the spikes the managed
        // ReconstructionCleanup also removes as a safety net for old DLLs.
        std::vector<std::tuple<Point, Point, Point>> tri_points;
        for (auto fit = as.alpha_shape_facets_begin();
             fit != as.alpha_shape_facets_end(); ++fit)
        {
            auto cls = as.classify(*fit);
            if (cls == AlphaShape::REGULAR)
            {
                auto cell = fit->first;
                int   opp  = fit->second;
                int i0 = (opp+1) & 3, i1 = (opp+2) & 3, i2 = (opp+3) & 3;
                tri_points.emplace_back(
                    cell->vertex(i0)->point(),
                    cell->vertex(i1)->point(),
                    cell->vertex(i2)->point());
            }
        }
        if (tri_points.empty()) { set_error("alpha_shape_3: empty boundary at chosen alpha"); return -402; }

        // Weld duplicate vertices using a coordinate-hash.
        std::vector<double> verts; verts.reserve(tri_points.size() * 9);
        std::vector<int>    tris;  tris.reserve(tri_points.size() * 3);
        std::map<std::tuple<double,double,double>, int> index;
        auto add = [&](const Point& p) -> int {
            auto k = std::make_tuple((double)CGAL::to_double(p.x()),
                                     (double)CGAL::to_double(p.y()),
                                     (double)CGAL::to_double(p.z()));
            auto it = index.find(k);
            if (it != index.end()) return it->second;
            int id = static_cast<int>(verts.size() / 3);
            verts.push_back(std::get<0>(k));
            verts.push_back(std::get<1>(k));
            verts.push_back(std::get<2>(k));
            index[k] = id;
            return id;
        };
        for (auto& t : tri_points) {
            tris.push_back(add(std::get<0>(t)));
            tris.push_back(add(std::get<1>(t)));
            tris.push_back(add(std::get<2>(t)));
        }

        *out_vcount = static_cast<int>(verts.size() / 3);
        *out_tcount = static_cast<int>(tris.size() / 3);
        *out_verts = static_cast<double*>(std::malloc(verts.size() * sizeof(double)));
        *out_tris  = static_cast<int*>   (std::malloc(tris.size()  * sizeof(int)));
        if (!*out_verts || !*out_tris) { set_error("alpha_shape_3: malloc failed"); return -403; }
        std::memcpy(*out_verts, verts.data(), verts.size() * sizeof(double));
        std::memcpy(*out_tris,  tris.data(),  tris.size()  * sizeof(int));
        return 0;
    } catch (const std::exception& ex) { set_error(ex.what()); return -410; }
    catch (...) { set_error("unknown C++ exception in alpha_shape_3"); return -411; }
#else
    set_error("alpha_shape_3: not enabled in this build (define FRAHAN_CGAL_ENABLE_RECONSTRUCTION)");
    return -400;
#endif
}

FRAHAN_CGAL_API int frahan_cgal_advancing_front_reconstruct(
    const double* points, int pcount,
    double  radius_ratio,
    double  beta,
    double**out_verts,   int* out_vcount,
    int**   out_tris,    int* out_tcount)
{
    *out_verts = nullptr; *out_tris = nullptr;
    *out_vcount = 0; *out_tcount = 0;
    if (!points || pcount < 4) { set_error("advancing_front: need >=4 points"); return -421; }
#if defined(FRAHAN_CGAL_ENABLE_RECONSTRUCTION)
    try {
        FpuGuard _fp;   // mask FP exceptions for the duration (host may unmask them)
        typedef CGAL::Exact_predicates_inexact_constructions_kernel K;
        typedef K::Point_3                                          Point;
        typedef std::array<std::size_t, 3>                          Facet;

        std::vector<Point> ps;
        ps.reserve(pcount);
        for (int i = 0; i < pcount; ++i)
            ps.emplace_back(points[3*i + 0], points[3*i + 1], points[3*i + 2]);

        std::vector<Facet> facets;
        double rr   = (radius_ratio > 0.0) ? radius_ratio : 5.0;
        double bt   = (beta         > 0.0) ? beta         : 0.52;
        CGAL::advancing_front_surface_reconstruction(
            ps.begin(), ps.end(),
            std::back_inserter(facets),
            rr, bt);

        if (facets.empty()) { set_error("advancing_front: produced no facets"); return -422; }

        *out_vcount = pcount;
        *out_tcount = static_cast<int>(facets.size());
        *out_verts = static_cast<double*>(std::malloc(3 * pcount * sizeof(double)));
        *out_tris  = static_cast<int*>   (std::malloc(3 * facets.size() * sizeof(int)));
        if (!*out_verts || !*out_tris) { set_error("advancing_front: malloc failed"); return -423; }
        std::memcpy(*out_verts, points, 3 * pcount * sizeof(double));
        for (size_t i = 0; i < facets.size(); ++i) {
            (*out_tris)[3*i + 0] = static_cast<int>(facets[i][0]);
            (*out_tris)[3*i + 1] = static_cast<int>(facets[i][1]);
            (*out_tris)[3*i + 2] = static_cast<int>(facets[i][2]);
        }
        return 0;
    } catch (const std::exception& ex) { set_error(ex.what()); return -430; }
    catch (...) { set_error("unknown C++ exception in advancing_front"); return -431; }
#else
    set_error("advancing_front: not enabled in this build (define FRAHAN_CGAL_ENABLE_RECONSTRUCTION)");
    return -420;
#endif
}

FRAHAN_CGAL_API int frahan_cgal_poisson_reconstruct(
    const double* points, int pcount,
    const double* normals,
    double  sm_angle, double sm_radius, double sm_distance,
    double**out_verts,   int* out_vcount,
    int**   out_tris,    int* out_tcount)
{
    *out_verts = nullptr; *out_tris = nullptr;
    *out_vcount = 0; *out_tcount = 0;
    if (!points || !normals || pcount < 8) { set_error("poisson: need >=8 oriented points"); return -451; }
#if defined(FRAHAN_CGAL_ENABLE_RECONSTRUCTION)
    try {
        FpuGuard _fp;   // mask FP exceptions for the duration (host may unmask them)
        typedef CGAL::Exact_predicates_inexact_constructions_kernel K;
        typedef K::Point_3   Point;
        typedef K::Vector_3  Vector;
        typedef std::pair<Point, Vector> Pwn;
        typedef CGAL::Surface_mesh<Point> SMesh;

        std::vector<Pwn> pwn;
        pwn.reserve(pcount);
        for (int i = 0; i < pcount; ++i)
            pwn.emplace_back(Point(points[3*i+0], points[3*i+1], points[3*i+2]),
                             Vector(normals[3*i+0], normals[3*i+1], normals[3*i+2]));

        double spacing = CGAL::compute_average_spacing<CGAL::Sequential_tag>(
            pwn, 6, CGAL::parameters::point_map(CGAL::First_of_pair_property_map<Pwn>()));

        double a = (sm_angle    > 0.0) ? sm_angle    : 20.0;
        double r = (sm_radius   > 0.0) ? sm_radius   : 30.0;
        double d = (sm_distance > 0.0) ? sm_distance : 0.375;

        SMesh mesh;
        bool ok = CGAL::poisson_surface_reconstruction_delaunay(
            pwn.begin(), pwn.end(),
            CGAL::First_of_pair_property_map<Pwn>(),
            CGAL::Second_of_pair_property_map<Pwn>(),
            mesh, spacing, a, r, d);
        if (!ok || mesh.is_empty()) { set_error("poisson: reconstruction failed or empty"); return -452; }

        // Compact vertex remap (robust regardless of index packing).
        std::map<typename SMesh::Vertex_index, int> vmap;
        std::vector<double> vbuf;
        vbuf.reserve(3 * mesh.number_of_vertices());
        int vi = 0;
        for (typename SMesh::Vertex_index vd : mesh.vertices()) {
            const Point& p = mesh.point(vd);
            vmap[vd] = vi++;
            vbuf.push_back(CGAL::to_double(p.x()));
            vbuf.push_back(CGAL::to_double(p.y()));
            vbuf.push_back(CGAL::to_double(p.z()));
        }
        std::vector<int> tbuf;
        tbuf.reserve(3 * mesh.number_of_faces());
        for (typename SMesh::Face_index fd : mesh.faces()) {
            int tri[3]; int j = 0;
            for (typename SMesh::Vertex_index vd :
                 CGAL::vertices_around_face(mesh.halfedge(fd), mesh)) {
                if (j < 3) tri[j] = vmap[vd];
                ++j;
            }
            if (j == 3) { tbuf.push_back(tri[0]); tbuf.push_back(tri[1]); tbuf.push_back(tri[2]); }
        }
        if (vbuf.empty() || tbuf.empty()) { set_error("poisson: empty output mesh"); return -454; }

        *out_vcount = static_cast<int>(vbuf.size() / 3);
        *out_tcount = static_cast<int>(tbuf.size() / 3);
        *out_verts = static_cast<double*>(std::malloc(vbuf.size() * sizeof(double)));
        *out_tris  = static_cast<int*>   (std::malloc(tbuf.size() * sizeof(int)));
        if (!*out_verts || !*out_tris) { set_error("poisson: malloc failed"); return -453; }
        std::memcpy(*out_verts, vbuf.data(), vbuf.size() * sizeof(double));
        std::memcpy(*out_tris,  tbuf.data(), tbuf.size() * sizeof(int));
        return 0;
    } catch (const std::exception& ex) { set_error(ex.what()); return -450; }
    catch (...) { set_error("unknown C++ exception in poisson"); return -459; }
#else
    set_error("poisson: not enabled in this build (define FRAHAN_CGAL_ENABLE_RECONSTRUCTION)");
    return -440;
#endif
}

FRAHAN_CGAL_API int frahan_cgal_estimate_normals(
    const double* points, int pcount,
    int     k_neighbours,
    double**out_normals)
{
    *out_normals = nullptr;
    if (!points || pcount < 3) { set_error("estimate_normals: need >=3 points"); return -441; }
#if defined(FRAHAN_CGAL_ENABLE_RECONSTRUCTION)
    try {
        FpuGuard _fp;   // mask FP exceptions for the duration (host may unmask them)
        typedef CGAL::Exact_predicates_inexact_constructions_kernel K;
        typedef K::Point_3                                          Point;
        typedef K::Vector_3                                         Vector;
        typedef std::pair<Point, Vector>                            PNPair;

        std::vector<PNPair> pn;
        pn.reserve(pcount);
        for (int i = 0; i < pcount; ++i)
            pn.emplace_back(Point(points[3*i + 0], points[3*i + 1], points[3*i + 2]),
                            Vector(0,0,0));

        int k = (k_neighbours > 0) ? k_neighbours : 18;
        CGAL::pca_estimate_normals<CGAL::Sequential_tag>(
            pn, k,
            CGAL::parameters::point_map(CGAL::First_of_pair_property_map<PNPair>())
                             .normal_map(CGAL::Second_of_pair_property_map<PNPair>()));
        CGAL::mst_orient_normals(pn, k,
            CGAL::parameters::point_map(CGAL::First_of_pair_property_map<PNPair>())
                             .normal_map(CGAL::Second_of_pair_property_map<PNPair>()));

        *out_normals = static_cast<double*>(std::malloc(3 * pcount * sizeof(double)));
        if (!*out_normals) { set_error("estimate_normals: malloc failed"); return -442; }
        for (int i = 0; i < pcount; ++i) {
            const auto& n = pn[i].second;
            (*out_normals)[3*i + 0] = CGAL::to_double(n.x());
            (*out_normals)[3*i + 1] = CGAL::to_double(n.y());
            (*out_normals)[3*i + 2] = CGAL::to_double(n.z());
        }
        return 0;
    } catch (const std::exception& ex) { set_error(ex.what()); return -450; }
    catch (...) { set_error("unknown C++ exception in estimate_normals"); return -451; }
#else
    set_error("estimate_normals: not enabled in this build (define FRAHAN_CGAL_ENABLE_RECONSTRUCTION)");
    return -440;
#endif
}

} // extern "C"
