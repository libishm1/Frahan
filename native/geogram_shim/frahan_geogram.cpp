// =============================================================================
// frahan_geogram - Geogram-backed mesh operations.
//
// Builds against Bruno Levy's Geogram (linked as `geogram` static target
// by CMakeLists.txt). FRAHAN_GEOGRAM_BUILDING is defined by CMake via
// target_compile_definitions; do NOT redefine it here.
//
// Structural invariants (mirror frahan_cgal / frahan_coacd):
//   - All C++-only helpers in ONE anonymous namespace BEFORE extern "C".
//   - All exported FRAHAN_GEOGRAM_API entry points in ONE extern "C"
//     block at the bottom.
//   - SEH translator (/EHa) installed inside each entry point so native
//     crashes inside Geogram surface as last_error strings instead of
//     bypassing the .NET try/catch as SEHException.
// =============================================================================

#include "frahan_geogram.h"

// Geogram public API.
#include <geogram/basic/common.h>
#include <geogram/basic/numeric.h>
#include <geogram/basic/geometry.h>
#include <geogram/basic/attributes.h>
#include <geogram/basic/command_line.h>
#include <geogram/basic/command_line_args.h>
#include <geogram/mesh/mesh.h>
#include <geogram/mesh/mesh_decimate.h>
#include <geogram/mesh/mesh_fill_holes.h>
#include <geogram/mesh/mesh_repair.h>
#include <geogram/mesh/mesh_remesh.h>
#include <geogram/mesh/mesh_tetrahedralize.h>
#include <geogram/points/principal_axes.h>
#include <geogram/points/nn_search.h>
#include <geogram/voronoi/CVT.h>
#include <geogram/voronoi/RVD.h>
#include <geogram/delaunay/delaunay.h>
#if defined(FRAHAN_GEOGRAM_ENABLE_POISSON)
// Kazhdan's screened-Poisson reconstruction, bundled inside Geogram. Reachable
// via the geogram target's PUBLIC include dir (see CMakeLists note).
#  include <geogram/third_party/PoissonRecon/poisson_geogram.h>
#endif

#include <algorithm>
#include <cmath>

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <exception>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>

#ifdef _MSC_VER
#  include <eh.h>
#  include <windows.h>
#  include <float.h>   // _controlfp_s / _MCW_EM (FP-exception mask)
#endif

// =============================================================================
// Anonymous namespace - C++-only helpers.
// =============================================================================

namespace {

thread_local std::string g_lastError;
std::once_flag g_geogram_init_flag;

void set_error(const char* msg) { g_lastError = msg ? msg : ""; }
void set_error(const std::string& msg) { g_lastError = msg; }

// One-time Geogram initialization. Geogram requires GEO::initialize()
// to be called before any other API. GEOGRAM_INSTALL_NONE avoids
// installing Geogram's signal/error handlers - those are for
// stand-alone apps, not libraries embedded in a host process.
//
// IMPORTANT: many algorithms (CentroidalVoronoiTesselation, RVD,
// Delaunay) read Geogram CmdLine variables internally (e.g.
// "algo:nn_search", "algo:delaunay", "algo:predicates"). Without
// the matching CmdLine arg groups imported, those lookups fail in
// Environment::get_value() with an assertion at environment.cpp:217
// ("variable_exists"). Importing "standard" + "algo" is the minimal
// set that covers every entry point in this shim.
void ensure_geogram_initialized() {
    std::call_once(g_geogram_init_flag, []() {
        GEO::initialize(GEO::GEOGRAM_INSTALL_NONE);
        GEO::CmdLine::import_arg_group("standard");
        GEO::CmdLine::import_arg_group("algo");
    });
}

// SEH-to-C++-exception translator. Same pattern as coacd_shim.
#ifdef _MSC_VER
const char* seh_name(unsigned int code) {
    switch (code) {
        case EXCEPTION_ACCESS_VIOLATION:         return "ACCESS_VIOLATION";
        case EXCEPTION_STACK_OVERFLOW:           return "STACK_OVERFLOW";
        case EXCEPTION_INT_DIVIDE_BY_ZERO:       return "INT_DIVIDE_BY_ZERO";
        case EXCEPTION_FLT_DIVIDE_BY_ZERO:       return "FLT_DIVIDE_BY_ZERO";
        case EXCEPTION_FLT_INVALID_OPERATION:    return "FLT_INVALID_OPERATION";
        case EXCEPTION_ILLEGAL_INSTRUCTION:      return "ILLEGAL_INSTRUCTION";
        case EXCEPTION_PRIV_INSTRUCTION:         return "PRIV_INSTRUCTION";
        case EXCEPTION_IN_PAGE_ERROR:            return "IN_PAGE_ERROR";
        case EXCEPTION_NONCONTINUABLE_EXCEPTION: return "NONCONTINUABLE";
        default:                                 return "UNKNOWN";
    }
}

struct SehScope {
    _se_translator_function prev = nullptr;
    unsigned int fpu_saved = 0;
    SehScope() {
        // CRITICAL: mask ALL floating-point exceptions for the duration of the
        // native call. Some hosts run with FP exceptions UNMASKED (Rhino +
        // Cycles / 3Dconnexion plugins toggle the x87/SSE control word).
        // Geogram's reconstruction math legitimately produces denormals /
        // overflow / invalid results; with FP exceptions unmasked those become
        // hardware traps, the SEH translator below turns them into a C++ throw,
        // and the throw unwinds to std::terminate -> abort() -> the whole Rhino
        // process dies with 0xC0000409. Save the host's control word and
        // restore it in the dtor so we never disturb the host globally.
        _controlfp_s(&fpu_saved, 0, 0);            // read current control word
        unsigned int cur;
        _controlfp_s(&cur, _MCW_EM, _MCW_EM);      // set all mask bits -> no traps
        prev = _set_se_translator([](unsigned int code, EXCEPTION_POINTERS* /*ep*/) {
            char buf[96];
            std::snprintf(buf, sizeof(buf),
                "SEH 0x%08X (%s) inside Geogram",
                code, seh_name(code));
            throw std::runtime_error(buf);
        });
    }
    ~SehScope() {
        _set_se_translator(prev);
        unsigned int cur;
        _controlfp_s(&cur, fpu_saved, _MCW_EM);    // restore host's FP mask bits
    }
};
#else
struct SehScope { SehScope() {} ~SehScope() {} };
#endif

// Build a GEO::Mesh from flat double / int arrays.
bool build_mesh(GEO::Mesh& m,
                const double* verts, int vc,
                const int* tris, int tc) {
    if (verts == nullptr || tris == nullptr || vc < 0 || tc < 0) {
        set_error("null buffer or negative count");
        return false;
    }
    GEO::index_t v_first = m.vertices.create_vertices(static_cast<GEO::index_t>(vc));
    for (int i = 0; i < vc; ++i) {
        double* p = m.vertices.point_ptr(v_first + i);
        p[0] = verts[3 * i + 0];
        p[1] = verts[3 * i + 1];
        p[2] = verts[3 * i + 2];
    }
    GEO::index_t t_first = m.facets.create_triangles(static_cast<GEO::index_t>(tc));
    for (int i = 0; i < tc; ++i) {
        int a = tris[3 * i + 0], b = tris[3 * i + 1], c = tris[3 * i + 2];
        if (a < 0 || a >= vc || b < 0 || b >= vc || c < 0 || c >= vc) {
            set_error("triangle index out of range");
            return false;
        }
        m.facets.set_vertex(t_first + i, 0, static_cast<GEO::index_t>(a));
        m.facets.set_vertex(t_first + i, 1, static_cast<GEO::index_t>(b));
        m.facets.set_vertex(t_first + i, 2, static_cast<GEO::index_t>(c));
    }
    m.facets.connect();
    return true;
}

// Extract an RVD result mesh (post compute_RVD) into newly malloc'd flat
// arrays. Same shape as extract_mesh, plus a per-facet seed_id pulled
// from the "region" or "chart" attribute (whichever Geogram populated).
//
// All three out pointers are written. On malloc failure or empty result,
// they are set to nullptr / 0 and the call returns 0 for empty or -1 for
// allocation failure. The seed_id buffer always has length out_tcount.
int extract_rvd_result(const GEO::Mesh& result,
                       double** out_verts,    int* out_vcount,
                       int**    out_tris,     int* out_tcount,
                       int**    out_seed_ids, int* out_idcount) {
    const int vc = static_cast<int>(result.vertices.nb());
    const int tc = static_cast<int>(result.facets.nb());
    *out_vcount  = vc;
    *out_tcount  = tc;
    *out_idcount = tc;
    if (vc == 0 || tc == 0) return 0;

    *out_verts    = static_cast<double*>(std::malloc(sizeof(double) * 3 * vc));
    *out_tris     = static_cast<int*>(std::malloc(sizeof(int) * 3 * tc));
    *out_seed_ids = static_cast<int*>(std::malloc(sizeof(int) * tc));
    if (*out_verts == nullptr || *out_tris == nullptr || *out_seed_ids == nullptr) {
        std::free(*out_verts);    *out_verts    = nullptr;
        std::free(*out_tris);     *out_tris     = nullptr;
        std::free(*out_seed_ids); *out_seed_ids = nullptr;
        *out_vcount = 0; *out_tcount = 0; *out_idcount = 0;
        set_error("malloc failure (extract_rvd_result)");
        return -1;
    }

    for (int v = 0; v < vc; ++v) {
        const double* p = result.vertices.point_ptr(static_cast<GEO::index_t>(v));
        (*out_verts)[3 * v + 0] = p[0];
        (*out_verts)[3 * v + 1] = p[1];
        (*out_verts)[3 * v + 2] = p[2];
    }

    GEO::Attribute<GEO::index_t> region_attr;
    bool have_region = false;
    if (result.facets.attributes().is_defined("region")) {
        region_attr.bind(result.facets.attributes(), "region");
        have_region = true;
    } else if (result.facets.attributes().is_defined("chart")) {
        region_attr.bind(result.facets.attributes(), "chart");
        have_region = true;
    }

    for (int t = 0; t < tc; ++t) {
        const GEO::index_t f = static_cast<GEO::index_t>(t);
        // Defensive: every RVD facet should be a triangle, but
        // volumetric-mode boundaries can occasionally emit larger
        // polygons. Fan-triangulate would change tcount; instead drop to
        // first three corners if longer (they are coplanar by
        // construction). For 3-corner facets this is a no-op.
        (*out_tris)[3 * t + 0] = static_cast<int>(result.facets.vertex(f, 0));
        (*out_tris)[3 * t + 1] = static_cast<int>(result.facets.vertex(f, 1));
        (*out_tris)[3 * t + 2] = static_cast<int>(result.facets.vertex(f, 2));
        (*out_seed_ids)[t]     = have_region ? static_cast<int>(region_attr[f]) : 0;
    }
    if (have_region) region_attr.unbind();
    return 0;
}

// Extract a GEO::Mesh into newly malloc'd flat arrays.
int extract_mesh(const GEO::Mesh& m,
                 double** out_verts, int* out_vcount,
                 int** out_tris, int* out_tcount) {
    const int vc = static_cast<int>(m.vertices.nb());
    const int tc = static_cast<int>(m.facets.nb());
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
        set_error("malloc failure (extract_mesh)");
        return -1;
    }

    for (int v = 0; v < vc; ++v) {
        const double* p = m.vertices.point_ptr(static_cast<GEO::index_t>(v));
        (*out_verts)[3 * v + 0] = p[0];
        (*out_verts)[3 * v + 1] = p[1];
        (*out_verts)[3 * v + 2] = p[2];
    }
    for (int t = 0; t < tc; ++t) {
        // mesh_decimate_vertex_clustering keeps facets as triangles.
        // Defensive: drop anything that isn't a 3-corner facet.
        const GEO::index_t f = static_cast<GEO::index_t>(t);
        if (m.facets.nb_vertices(f) != 3) {
            set_error("non-triangle facet in Geogram output");
            std::free(*out_verts); *out_verts = nullptr;
            std::free(*out_tris);  *out_tris  = nullptr;
            *out_vcount = 0; *out_tcount = 0;
            return -2;
        }
        (*out_tris)[3 * t + 0] = static_cast<int>(m.facets.vertex(f, 0));
        (*out_tris)[3 * t + 1] = static_cast<int>(m.facets.vertex(f, 1));
        (*out_tris)[3 * t + 2] = static_cast<int>(m.facets.vertex(f, 2));
    }
    return 0;
}

// Extract a GEO::Mesh whose facets may be n-gons (PoissonReconstruction emits
// polygons via create_polygon) into flat triangle arrays. Each k-gon is
// fan-triangulated into (k-2) triangles. For all-triangle meshes this is a
// no-op equivalent to extract_mesh.
int extract_mesh_triangulated(const GEO::Mesh& m,
                              double** out_verts, int* out_vcount,
                              int** out_tris, int* out_tcount) {
    const GEO::index_t vc = m.vertices.nb();
    if (vc == 0 || m.facets.nb() == 0) {
        *out_verts = nullptr; *out_tris = nullptr;
        *out_vcount = 0; *out_tcount = 0;
        return 0;
    }
    // Count output triangles.
    long long tri_total = 0;
    for (GEO::index_t f = 0; f < m.facets.nb(); ++f) {
        const GEO::index_t k = m.facets.nb_vertices(f);
        if (k >= 3) tri_total += static_cast<long long>(k) - 2;
    }
    if (tri_total <= 0) {
        *out_verts = nullptr; *out_tris = nullptr;
        *out_vcount = 0; *out_tcount = 0;
        set_error("no triangulable facets in Poisson output");
        return -1;
    }

    *out_verts = static_cast<double*>(std::malloc(sizeof(double) * 3 * vc));
    *out_tris  = static_cast<int*>(std::malloc(sizeof(int) * 3 * tri_total));
    if (*out_verts == nullptr || *out_tris == nullptr) {
        std::free(*out_verts); *out_verts = nullptr;
        std::free(*out_tris);  *out_tris  = nullptr;
        *out_vcount = 0; *out_tcount = 0;
        set_error("malloc failure (extract_mesh_triangulated)");
        return -1;
    }

    for (GEO::index_t v = 0; v < vc; ++v) {
        const double* p = m.vertices.point_ptr(v);
        (*out_verts)[3 * v + 0] = p[0];
        (*out_verts)[3 * v + 1] = p[1];
        (*out_verts)[3 * v + 2] = p[2];
    }
    int t = 0;
    for (GEO::index_t f = 0; f < m.facets.nb(); ++f) {
        const GEO::index_t k = m.facets.nb_vertices(f);
        if (k < 3) continue;
        const int v0 = static_cast<int>(m.facets.vertex(f, 0));
        for (GEO::index_t c = 1; c + 1 < k; ++c) {
            (*out_tris)[3 * t + 0] = v0;
            (*out_tris)[3 * t + 1] = static_cast<int>(m.facets.vertex(f, c));
            (*out_tris)[3 * t + 2] = static_cast<int>(m.facets.vertex(f, c + 1));
            ++t;
        }
    }
    *out_vcount = static_cast<int>(vc);
    *out_tcount = t;
    return 0;
}

} // anonymous namespace

// =============================================================================
// All exported FRAHAN_GEOGRAM_API functions in ONE extern "C" block.
// =============================================================================

extern "C" {

FRAHAN_GEOGRAM_API const char* frahan_geogram_version(void) {
    return "Frahan-Geogram 0.1 (Geogram 1.9.9)";
}

FRAHAN_GEOGRAM_API const char* frahan_geogram_last_error(void) {
    return g_lastError.c_str();
}

FRAHAN_GEOGRAM_API int frahan_geogram_decimate_mesh(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    int     nb_bins,
    int     mode_flags,
    double** out_verts,  int* out_vcount,
    int**    out_tris,   int* out_tcount) {
    if (out_verts == nullptr || out_vcount == nullptr ||
        out_tris  == nullptr || out_tcount == nullptr) {
        set_error("null output pointer");
        return -100;
    }
    *out_verts = nullptr; *out_vcount = 0;
    *out_tris  = nullptr; *out_tcount = 0;

    if (nb_bins < 2) {
        set_error("nb_bins must be >= 2 (typical range 50..300)");
        return -101;
    }

    try {
        SehScope seh_translator;
        ensure_geogram_initialized();

        GEO::Mesh m(3, false);  // 3D, no ints attached
        if (!build_mesh(m, verts, vcount, tris, tcount)) return -102;

        const GEO::MeshDecimateMode mode =
            static_cast<GEO::MeshDecimateMode>(mode_flags);
        GEO::mesh_decimate_vertex_clustering(
            m,
            static_cast<GEO::index_t>(nb_bins),
            mode,
            nullptr /* vertex flags - none preserved */);

        return extract_mesh(m, out_verts, out_vcount, out_tris, out_tcount);
    } catch (const std::exception& e) {
        set_error(e.what());
        return -110;
    } catch (...) {
        set_error("unknown C++ exception in decimate_mesh");
        return -111;
    }
}

// ---------------------------------------------------------------------------
// Mesh repair
// ---------------------------------------------------------------------------

FRAHAN_GEOGRAM_API int frahan_geogram_repair_mesh(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    int     mode_flags,
    double  colocate_epsilon,
    double** out_verts,  int* out_vcount,
    int**    out_tris,   int* out_tcount) {
    if (out_verts == nullptr || out_vcount == nullptr ||
        out_tris  == nullptr || out_tcount == nullptr) {
        set_error("null output pointer");
        return -150;
    }
    *out_verts = nullptr; *out_vcount = 0;
    *out_tris  = nullptr; *out_tcount = 0;

    try {
        SehScope seh_translator;
        ensure_geogram_initialized();

        GEO::Mesh m(3, false);
        if (!build_mesh(m, verts, vcount, tris, tcount)) return -151;

        GEO::mesh_repair(m,
            static_cast<GEO::MeshRepairMode>(mode_flags),
            colocate_epsilon);

        return extract_mesh(m, out_verts, out_vcount, out_tris, out_tcount);
    } catch (const std::exception& e) { set_error(e.what()); return -160; }
      catch (...) { set_error("unknown C++ exception in repair_mesh"); return -161; }
}

// ---------------------------------------------------------------------------
// Fill holes
// ---------------------------------------------------------------------------

FRAHAN_GEOGRAM_API int frahan_geogram_fill_holes(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    double  max_hole_area,
    int     max_hole_edges,
    int     repair_after,
    double** out_verts,  int* out_vcount,
    int**    out_tris,   int* out_tcount) {
    if (out_verts == nullptr || out_vcount == nullptr ||
        out_tris  == nullptr || out_tcount == nullptr) {
        set_error("null output pointer");
        return -290;
    }
    *out_verts = nullptr; *out_vcount = 0;
    *out_tris  = nullptr; *out_tcount = 0;
    if (max_hole_area < 0.0) {
        set_error("max_hole_area must be >= 0");
        return -291;
    }

    try {
        SehScope seh_translator;
        ensure_geogram_initialized();

        GEO::Mesh m(3, false);
        if (!build_mesh(m, verts, vcount, tris, tcount)) return -292;

        // Geogram convention: max_edges == max_index_t() means "no limit".
        const GEO::index_t max_edges =
            (max_hole_edges <= 0)
                ? GEO::max_index_t()
                : static_cast<GEO::index_t>(max_hole_edges);

        GEO::fill_holes(m, max_hole_area, max_edges, repair_after != 0);

        return extract_mesh(m, out_verts, out_vcount, out_tris, out_tcount);
    } catch (const std::exception& e) { set_error(e.what()); return -293; }
      catch (...) { set_error("unknown C++ exception in fill_holes"); return -294; }
}

// ---------------------------------------------------------------------------
// OBB via PrincipalAxes3d
// ---------------------------------------------------------------------------

FRAHAN_GEOGRAM_API int frahan_geogram_obb_3d(
    const double* verts, int vcount,
    const int* tris, int tcount,
    double out_obb[15]) {
    (void)tris; (void)tcount;
    if (verts == nullptr || vcount <= 0 || out_obb == nullptr) {
        set_error("null buffer or non-positive vertex count");
        return -170;
    }
    try {
        SehScope seh_translator;
        ensure_geogram_initialized();

        GEO::PrincipalAxes3d pca;
        pca.begin();
        for (int i = 0; i < vcount; ++i) {
            pca.add_point(GEO::vec3(
                verts[3 * i + 0], verts[3 * i + 1], verts[3 * i + 2]));
        }
        pca.end();

        const GEO::vec3 cx = pca.axis(0);
        const GEO::vec3 cy = pca.axis(1);
        const GEO::vec3 cz = pca.axis(2);

        // Project all points onto each axis (relative to centroid) to
        // find the [min,max] extent along each.
        const GEO::vec3 ctr = pca.center();
        double xmin =  std::numeric_limits<double>::infinity();
        double xmax = -std::numeric_limits<double>::infinity();
        double ymin = xmin, ymax = xmax, zmin = xmin, zmax = xmax;
        for (int i = 0; i < vcount; ++i) {
            const GEO::vec3 p(
                verts[3 * i + 0] - ctr.x,
                verts[3 * i + 1] - ctr.y,
                verts[3 * i + 2] - ctr.z);
            const double dx = GEO::dot(p, cx);
            const double dy = GEO::dot(p, cy);
            const double dz = GEO::dot(p, cz);
            xmin = std::min(xmin, dx); xmax = std::max(xmax, dx);
            ymin = std::min(ymin, dy); ymax = std::max(ymax, dy);
            zmin = std::min(zmin, dz); zmax = std::max(zmax, dz);
        }

        const double extX = xmax - xmin;
        const double extY = ymax - ymin;
        const double extZ = zmax - zmin;
        if (extX < 1e-15 || extY < 1e-15 || extZ < 1e-15) {
            set_error("degenerate OBB (zero-length axis)");
            return -171;
        }

        // Origin = centroid + (xmin*cx + ymin*cy + zmin*cz). Box runs
        // from origin along (+cx, +cy, +cz) for extents (extX, extY, extZ).
        const GEO::vec3 origin = ctr + xmin * cx + ymin * cy + zmin * cz;

        out_obb[0]  = origin.x; out_obb[1]  = origin.y; out_obb[2]  = origin.z;
        out_obb[3]  = cx.x;     out_obb[4]  = cx.y;     out_obb[5]  = cx.z;
        out_obb[6]  = cy.x;     out_obb[7]  = cy.y;     out_obb[8]  = cy.z;
        out_obb[9]  = cz.x;     out_obb[10] = cz.y;     out_obb[11] = cz.z;
        out_obb[12] = extX;     out_obb[13] = extY;     out_obb[14] = extZ;
        return 0;
    } catch (const std::exception& e) { set_error(e.what()); return -172; }
      catch (...) { set_error("unknown C++ exception in obb_3d"); return -173; }
}

// ---------------------------------------------------------------------------
// Surface remesh (uniform)
// ---------------------------------------------------------------------------

FRAHAN_GEOGRAM_API int frahan_geogram_remesh_uniform(
    const double* verts, int vcount,
    const int* tris, int tcount,
    int     nb_points,
    int     nb_lloyd,
    int     nb_newton,
    double** out_verts, int* out_vcount,
    int**    out_tris,  int* out_tcount) {
    if (out_verts == nullptr || out_vcount == nullptr ||
        out_tris  == nullptr || out_tcount == nullptr) {
        set_error("null output pointer");
        return -180;
    }
    *out_verts = nullptr; *out_vcount = 0;
    *out_tris  = nullptr; *out_tcount = 0;
    if (nb_points < 4) { set_error("nb_points must be >= 4"); return -181; }
    if (nb_lloyd  < 0) nb_lloyd  = 5;
    if (nb_newton < 0) nb_newton = 30;

    try {
        SehScope seh_translator;
        ensure_geogram_initialized();

        GEO::Mesh m_in(3, false), m_out(3, false);
        if (!build_mesh(m_in, verts, vcount, tris, tcount)) return -182;

        GEO::remesh_smooth(m_in, m_out,
            static_cast<GEO::index_t>(nb_points),
            /*dim*/ 3,
            static_cast<GEO::index_t>(nb_lloyd),
            static_cast<GEO::index_t>(nb_newton));

        return extract_mesh(m_out, out_verts, out_vcount, out_tris, out_tcount);
    } catch (const std::exception& e) { set_error(e.what()); return -183; }
      catch (...) { set_error("unknown C++ exception in remesh_uniform"); return -184; }
}

// ---------------------------------------------------------------------------
// Tetrahedralize (requires GEOGRAM_WITH_TETGEN=ON)
// ---------------------------------------------------------------------------

FRAHAN_GEOGRAM_API int frahan_geogram_tetrahedralize(
    const double* verts, int vcount,
    const int* tris, int tcount,
    int     preprocess,
    int     refine,
    double  quality,
    int     keep_regions,
    double** out_verts, int* out_vcount,
    int**    out_tets,  int* out_tcount) {
    if (out_verts == nullptr || out_vcount == nullptr ||
        out_tets  == nullptr || out_tcount == nullptr) {
        set_error("null output pointer");
        return -200;
    }
    *out_verts = nullptr; *out_vcount = 0;
    *out_tets  = nullptr; *out_tcount = 0;

    try {
        SehScope seh_translator;
        ensure_geogram_initialized();

        GEO::Mesh m(3, false);
        if (!build_mesh(m, verts, vcount, tris, tcount)) return -201;

        GEO::MeshTetrahedralizeParameters params;
        params.preprocess    = (preprocess != 0);
        params.refine        = (refine != 0);
        params.refine_quality = quality;
        params.keep_regions  = (keep_regions != 0);
        params.verbose       = false;

        const bool ok = GEO::mesh_tetrahedralize(m, params);
        if (!ok) {
            set_error("Tetrahedralize unavailable: this Geogram build "
                      "was compiled with GEOGRAM_WITH_TETGEN=OFF (the "
                      "intentional default for this shim, because "
                      "TetGen is licensed for NON-COMMERCIAL use only). "
                      "If commercial use is not a concern for your "
                      "deployment, rebuild the shim with "
                      "-DGEOGRAM_WITH_TETGEN=ON. Otherwise, use the "
                      "Mesh Decompose (CoACD) component for "
                      "approximate convex decomposition instead.");
            return -210;
        }

        // Output: verts + tet cells. Tet indices = 4 * cells.nb().
        const int vc = static_cast<int>(m.vertices.nb());
        const int tc = static_cast<int>(m.cells.nb());
        *out_vcount = vc;
        *out_tcount = tc;
        if (vc == 0 || tc == 0) return 0;

        *out_verts = static_cast<double*>(std::malloc(sizeof(double) * 3 * vc));
        *out_tets  = static_cast<int*>(std::malloc(sizeof(int) * 4 * tc));
        if (*out_verts == nullptr || *out_tets == nullptr) {
            std::free(*out_verts); *out_verts = nullptr;
            std::free(*out_tets);  *out_tets  = nullptr;
            *out_vcount = 0; *out_tcount = 0;
            set_error("malloc failure (tetrahedralize output)");
            return -211;
        }
        for (int v = 0; v < vc; ++v) {
            const double* p = m.vertices.point_ptr(static_cast<GEO::index_t>(v));
            (*out_verts)[3 * v + 0] = p[0];
            (*out_verts)[3 * v + 1] = p[1];
            (*out_verts)[3 * v + 2] = p[2];
        }
        for (int t = 0; t < tc; ++t) {
            const GEO::index_t c = static_cast<GEO::index_t>(t);
            (*out_tets)[4 * t + 0] = static_cast<int>(m.cells.vertex(c, 0));
            (*out_tets)[4 * t + 1] = static_cast<int>(m.cells.vertex(c, 1));
            (*out_tets)[4 * t + 2] = static_cast<int>(m.cells.vertex(c, 2));
            (*out_tets)[4 * t + 3] = static_cast<int>(m.cells.vertex(c, 3));
        }
        return 0;
    } catch (const std::exception& e) { set_error(e.what()); return -220; }
      catch (...) { set_error("unknown C++ exception in tetrahedralize"); return -221; }
}

// ---------------------------------------------------------------------------
// Centroidal Voronoi Tessellation (CVT)
// ---------------------------------------------------------------------------

FRAHAN_GEOGRAM_API int frahan_geogram_cvt_compute(
    const double* verts, int vcount,
    const int* tris, int tcount,
    int     nb_points,
    int     nb_lloyd,
    int     nb_newton,
    double** out_points, int* out_pcount) {
    if (out_points == nullptr || out_pcount == nullptr) {
        set_error("null output pointer");
        return -230;
    }
    *out_points = nullptr; *out_pcount = 0;
    if (nb_points < 4) { set_error("nb_points must be >= 4"); return -231; }
    if (nb_lloyd  < 0) nb_lloyd  = 5;
    if (nb_newton < 0) nb_newton = 30;

    try {
        SehScope seh_translator;
        ensure_geogram_initialized();

        GEO::Mesh m(3, false);
        if (!build_mesh(m, verts, vcount, tris, tcount)) return -232;

        GEO::CentroidalVoronoiTesselation cvt(&m, 3);
        if (!cvt.compute_initial_sampling(static_cast<GEO::index_t>(nb_points))) {
            set_error("CVT compute_initial_sampling failed");
            return -233;
        }
        if (nb_lloyd  > 0) cvt.Lloyd_iterations(static_cast<GEO::index_t>(nb_lloyd));
        if (nb_newton > 0) cvt.Newton_iterations(static_cast<GEO::index_t>(nb_newton));

        const int n = static_cast<int>(cvt.nb_points());
        *out_pcount = n;
        if (n == 0) return 0;
        *out_points = static_cast<double*>(std::malloc(sizeof(double) * 3 * n));
        if (*out_points == nullptr) {
            *out_pcount = 0;
            set_error("malloc failure (CVT output)");
            return -234;
        }
        for (int i = 0; i < n; ++i) {
            const GEO::vec3& p = cvt.R3_embedding(static_cast<GEO::index_t>(i));
            (*out_points)[3 * i + 0] = p.x;
            (*out_points)[3 * i + 1] = p.y;
            (*out_points)[3 * i + 2] = p.z;
        }
        return 0;
    } catch (const std::exception& e) { set_error(e.what()); return -235; }
      catch (...) { set_error("unknown C++ exception in cvt_compute"); return -236; }
}

// ---------------------------------------------------------------------------
// Restricted Voronoi Diagram (3D surface, given seeds)
// ---------------------------------------------------------------------------

FRAHAN_GEOGRAM_API int frahan_geogram_rvd_compute(
    const double* mesh_verts, int mesh_vcount,
    const int* mesh_tris,    int mesh_tcount,
    const double* seed_points, int seed_count,
    double** out_verts,    int* out_vcount,
    int**    out_tris,     int* out_tcount,
    int**    out_seed_ids, int* out_idcount) {
    if (out_verts    == nullptr || out_vcount  == nullptr ||
        out_tris     == nullptr || out_tcount  == nullptr ||
        out_seed_ids == nullptr || out_idcount == nullptr) {
        set_error("null output pointer");
        return -250;
    }
    *out_verts = nullptr; *out_vcount = 0;
    *out_tris  = nullptr; *out_tcount = 0;
    *out_seed_ids = nullptr; *out_idcount = 0;
    if (seed_points == nullptr || seed_count < 1) {
        set_error("need at least 1 seed point");
        return -251;
    }

    try {
        SehScope seh_translator;
        ensure_geogram_initialized();

        GEO::Mesh m(3, false);
        if (!build_mesh(m, mesh_verts, mesh_vcount, mesh_tris, mesh_tcount)) return -252;

        GEO::Delaunay_var delaunay = GEO::Delaunay::create(3, "default");
        delaunay->set_vertices(static_cast<GEO::index_t>(seed_count), seed_points);

        GEO::RestrictedVoronoiDiagram_var rvd =
            GEO::RestrictedVoronoiDiagram::create(delaunay, &m);
        rvd->set_volumetric(false);

        GEO::Mesh result(3, false);
        // Args: output_mesh, dim, cell_borders_only, integration_simplices.
        // integration_simplices MUST be false — true emits tiny triangles
        // meant for numerical integration over the cells (one vertex
        // coincident with the seed), not the Voronoi cell surface
        // patches. The seed_id "region" attribute on facets is populated
        // either way, so we get cell ownership without the simplex
        // explosion.
        rvd->compute_RVD(
            result,
            /*dim*/ 0,
            /*cell_borders_only*/ false,
            /*integration_simplices*/ false);

        return extract_rvd_result(result,
                                  out_verts, out_vcount,
                                  out_tris, out_tcount,
                                  out_seed_ids, out_idcount);
    } catch (const std::exception& e) { set_error(e.what()); return -260; }
      catch (...) { set_error("unknown C++ exception in rvd_compute"); return -261; }
}

// ---------------------------------------------------------------------------
// Surface RVD with anti-sawtooth pre-remesh.
// ---------------------------------------------------------------------------

FRAHAN_GEOGRAM_API int frahan_geogram_rvd_compute_smooth(
    const double* mesh_verts, int mesh_vcount,
    const int* mesh_tris,    int mesh_tcount,
    const double* seed_points, int seed_count,
    int     remesh_nb_points,
    int     remesh_nb_lloyd,
    int     remesh_nb_newton,
    double** out_verts,    int* out_vcount,
    int**    out_tris,     int* out_tcount,
    int**    out_seed_ids, int* out_idcount) {
    if (out_verts    == nullptr || out_vcount  == nullptr ||
        out_tris     == nullptr || out_tcount  == nullptr ||
        out_seed_ids == nullptr || out_idcount == nullptr) {
        set_error("null output pointer");
        return -270;
    }
    *out_verts = nullptr; *out_vcount = 0;
    *out_tris  = nullptr; *out_tcount = 0;
    *out_seed_ids = nullptr; *out_idcount = 0;
    if (seed_points == nullptr || seed_count < 1) {
        set_error("need at least 1 seed point");
        return -271;
    }

    try {
        SehScope seh_translator;
        ensure_geogram_initialized();

        GEO::Mesh m_in(3, false);
        if (!build_mesh(m_in, mesh_verts, mesh_vcount, mesh_tris, mesh_tcount)) return -272;

        // Optional pre-RVD uniform remesh. Same call as
        // frahan_geogram_remesh_uniform - the smoothing knob the user
        // would otherwise wire by hand upstream of the Voronoi
        // partition. Skipped when remesh_nb_points <= 0.
        GEO::Mesh m_remeshed(3, false);
        GEO::Mesh* rvd_input = &m_in;
        if (remesh_nb_points > 0) {
            GEO::remesh_smooth(m_in, m_remeshed,
                static_cast<GEO::index_t>(remesh_nb_points),
                /*dim*/ 3,
                /*nb_lloyd*/  (remesh_nb_lloyd  >= 0 ? remesh_nb_lloyd  : 5),
                /*nb_newton*/ (remesh_nb_newton >= 0 ? remesh_nb_newton : 30));
            if (m_remeshed.vertices.nb() == 0 || m_remeshed.facets.nb() == 0) {
                set_error("pre-RVD remesh produced an empty mesh");
                return -273;
            }
            rvd_input = &m_remeshed;
        }

        GEO::Delaunay_var delaunay = GEO::Delaunay::create(3, "default");
        delaunay->set_vertices(static_cast<GEO::index_t>(seed_count), seed_points);

        GEO::RestrictedVoronoiDiagram_var rvd =
            GEO::RestrictedVoronoiDiagram::create(delaunay, rvd_input);
        rvd->set_volumetric(false);

        GEO::Mesh result(3, false);
        rvd->compute_RVD(
            result,
            /*dim*/ 0,
            /*cell_borders_only*/ false,
            /*integration_simplices*/ false);

        return extract_rvd_result(result,
                                  out_verts, out_vcount,
                                  out_tris, out_tcount,
                                  out_seed_ids, out_idcount);
    } catch (const std::exception& e) { set_error(e.what()); return -274; }
      catch (...) { set_error("unknown C++ exception in rvd_compute_smooth"); return -275; }
}

// ---------------------------------------------------------------------------
// Volumetric Voronoi block decomposition (closed polyhedral cells).
//
// Requires GEOGRAM_WITH_TETGEN=ON (CMake option FRAHAN_WITH_TETGEN).
// Returns -283 with a clear error message when TetGen is not linked.
// ---------------------------------------------------------------------------

FRAHAN_GEOGRAM_API int frahan_geogram_voronoi_blocks_compute(
    const double* mesh_verts, int mesh_vcount,
    const int* mesh_tris,    int mesh_tcount,
    const double* seed_points, int seed_count,
    double** out_verts,    int* out_vcount,
    int**    out_tris,     int* out_tcount,
    int**    out_seed_ids, int* out_idcount) {
    if (out_verts    == nullptr || out_vcount  == nullptr ||
        out_tris     == nullptr || out_tcount  == nullptr ||
        out_seed_ids == nullptr || out_idcount == nullptr) {
        set_error("null output pointer");
        return -280;
    }
    *out_verts = nullptr; *out_vcount = 0;
    *out_tris  = nullptr; *out_tcount = 0;
    *out_seed_ids = nullptr; *out_idcount = 0;
    if (seed_points == nullptr || seed_count < 1) {
        set_error("need at least 1 seed point");
        return -281;
    }

    try {
        SehScope seh_translator;
        ensure_geogram_initialized();

        // Step 1: build the input solid as a triangle mesh, then
        // tetrahedralize in place. preprocess=1 fixes minor non-manifold
        // issues that would otherwise abort TetGen; refine=0 preserves
        // the surface vertex set as much as possible.
        GEO::Mesh m(3, false);
        if (!build_mesh(m, mesh_verts, mesh_vcount, mesh_tris, mesh_tcount)) return -282;

        GEO::MeshTetrahedralizeParameters tet_params;
        tet_params.preprocess    = true;
        tet_params.refine        = false;
        tet_params.refine_quality= 1.0;
        tet_params.keep_regions  = false;
        tet_params.verbose       = false;

        const bool tet_ok = GEO::mesh_tetrahedralize(m, tet_params);
        if (!tet_ok) {
            set_error("Volumetric blocks unavailable: this Geogram build "
                      "was compiled with GEOGRAM_WITH_TETGEN=OFF. Rebuild "
                      "the shim with -DFRAHAN_WITH_TETGEN=ON (accepts "
                      "TetGen's AGPL terms).");
            return -283;
        }
        if (m.cells.nb() == 0) {
            set_error("Tetrahedralize succeeded but produced 0 cells "
                      "(input solid is empty or degenerate)");
            return -284;
        }

        // Step 2: Delaunay over the seeds. Seeds outside the solid are
        // tolerated by Geogram - their cells are simply pruned by the
        // intersection with the tet mesh.
        GEO::Delaunay_var delaunay = GEO::Delaunay::create(3, "default");
        delaunay->set_vertices(static_cast<GEO::index_t>(seed_count), seed_points);

        // Step 3: volumetric RVD with cell_borders_only=true. Each output
        // facet belongs to exactly one cell's CLOSED boundary - the
        // input surface clipped to the cell, plus the planar separator
        // faces between this cell and its Voronoi neighbours. Adjacent
        // cells therefore each emit their own copy of the shared
        // separator (with opposite orientation), so SplitBySeedId
        // upstream produces one closed mesh per cell with no shared
        // vertices across cells.
        GEO::RestrictedVoronoiDiagram_var rvd =
            GEO::RestrictedVoronoiDiagram::create(delaunay, &m);
        rvd->set_volumetric(true);

        GEO::Mesh result(3, false);
        rvd->compute_RVD(
            result,
            /*dim*/ 0,
            /*cell_borders_only*/ true,
            /*integration_simplices*/ false);

        return extract_rvd_result(result,
                                  out_verts, out_vcount,
                                  out_tris, out_tcount,
                                  out_seed_ids, out_idcount);
    } catch (const std::exception& e) { set_error(e.what()); return -285; }
      catch (...) { set_error("unknown C++ exception in voronoi_blocks_compute"); return -286; }
}

FRAHAN_GEOGRAM_API void frahan_geogram_free_pdouble(double* p) {
    if (p != nullptr) std::free(p);
}

FRAHAN_GEOGRAM_API void frahan_geogram_free_pint(int* p) {
    if (p != nullptr) std::free(p);
}

/* =============================================================================
 * Phase H — Poisson surface reconstruction. Wraps Misha Kazhdan's screened-
 * Poisson (PoissonRecon) that ships bundled inside Geogram, via the
 * GEO::PoissonReconstruction wrapper (third_party/PoissonRecon/poisson_geogram.h).
 * Gated by FRAHAN_GEOGRAM_ENABLE_POISSON (set ON in CMakeLists). The input is
 * an oriented point set; `depth` is the octree depth (default 8, higher = finer).
 * ============================================================================= */
FRAHAN_GEOGRAM_API int frahan_geogram_poisson_reconstruct(
    const double* points,  int pcount,
    const double* normals,
    int     depth,
    double  samples_per_node,
    double**out_verts,     int* out_vcount,
    int**   out_tris,      int* out_tcount)
{
    *out_verts = nullptr; *out_tris = nullptr;
    *out_vcount = 0; *out_tcount = 0;
    if (!points || !normals || pcount < 8) {
        set_error("poisson_reconstruct: need >=8 oriented points");
        return -461;
    }
#if defined(FRAHAN_GEOGRAM_ENABLE_POISSON)
    try {
        SehScope seh_translator;
        ensure_geogram_initialized();

        const int d = (depth > 0) ? depth : 8;

        // PoissonReconstruction's input contract: a GEO::Mesh holding the
        // sample points as vertices, with a dim-3 "normal" vector attribute
        // attached to the vertices (see geogram mesh_io.cpp:2697 and
        // points/co3ne.cpp:2062 for the same idiom).
        GEO::Mesh points_mesh(3, false);
        points_mesh.vertices.create_vertices(static_cast<GEO::index_t>(pcount));
        for (int i = 0; i < pcount; ++i) {
            double* p = points_mesh.vertices.point_ptr(static_cast<GEO::index_t>(i));
            p[0] = points[3 * i + 0];
            p[1] = points[3 * i + 1];
            p[2] = points[3 * i + 2];
        }
        GEO::Attribute<double> nrm;
        nrm.create_vector_attribute(
            points_mesh.vertices.attributes(), "normal", 3);
        for (int i = 0; i < pcount; ++i) {
            nrm[3 * i + 0] = normals[3 * i + 0];
            nrm[3 * i + 1] = normals[3 * i + 1];
            nrm[3 * i + 2] = normals[3 * i + 2];
        }

        // Kazhdan screened-Poisson. The bundled wrapper exposes octree depth
        // (default 8; 10-11 for fine detail); samples_per_node is fixed
        // internally at 1.5 in this bundle, so we honour only depth here.
        GEO::PoissonReconstruction poisson;
        poisson.set_depth(static_cast<GEO::index_t>(d));
        (void)samples_per_node;

        GEO::Mesh surface(3, false);
        poisson.reconstruct(&points_mesh, &surface);

        if (surface.vertices.nb() == 0 || surface.facets.nb() == 0) {
            set_error("poisson_reconstruct: empty surface "
                      "(check oriented normals / increase depth)");
            return -465;
        }
        // Poisson emits polygons (create_polygon); fan-triangulate on extract.
        return extract_mesh_triangulated(
            surface, out_verts, out_vcount, out_tris, out_tcount);
    } catch (const std::exception& e) { set_error(e.what()); return -463; }
      catch (...) { set_error("unknown C++ exception in poisson_reconstruct"); return -464; }
#else
    set_error("poisson_reconstruct: not enabled (build with -DFRAHAN_GEOGRAM_ENABLE_POISSON=ON)");
    return -460;
#endif
}

/* =============================================================================
 * Phase I.6-I15 — Voxel downsample (pure-C++, no optional dep) +
 * KD-tree NN query via Geogram's NearestNeighborSearch.
 * ============================================================================= */
FRAHAN_GEOGRAM_API int frahan_geogram_voxel_downsample(
    const double* points, int pcount,
    double  voxel_size,
    double**out_centroids, int* out_count)
{
    *out_centroids = nullptr; *out_count = 0;
    if (!points || pcount < 1) { set_error("voxel_downsample: empty input"); return -471; }
    if (!(voxel_size > 0.0)) { set_error("voxel_downsample: voxel_size must be > 0"); return -472; }
    try {
        // Single-pass spatial hash. Map each input point to a quantised
        // cell key; accumulate sum + count per cell; emit centroids.
        struct Acc { double sx, sy, sz; long n; };
        std::map<std::tuple<long,long,long>, Acc> cells;
        for (int i = 0; i < pcount; ++i) {
            double x = points[3*i + 0];
            double y = points[3*i + 1];
            double z = points[3*i + 2];
            long ix = static_cast<long>(std::floor(x / voxel_size));
            long iy = static_cast<long>(std::floor(y / voxel_size));
            long iz = static_cast<long>(std::floor(z / voxel_size));
            auto key = std::make_tuple(ix, iy, iz);
            auto it = cells.find(key);
            if (it == cells.end()) cells[key] = Acc{ x, y, z, 1 };
            else { it->second.sx += x; it->second.sy += y; it->second.sz += z; ++it->second.n; }
        }
        *out_count = static_cast<int>(cells.size());
        *out_centroids = static_cast<double*>(std::malloc(3 * cells.size() * sizeof(double)));
        if (!*out_centroids) { set_error("voxel_downsample: malloc failed"); return -473; }
        int j = 0;
        for (auto& kv : cells) {
            double inv = 1.0 / static_cast<double>(kv.second.n);
            (*out_centroids)[3*j + 0] = kv.second.sx * inv;
            (*out_centroids)[3*j + 1] = kv.second.sy * inv;
            (*out_centroids)[3*j + 2] = kv.second.sz * inv;
            ++j;
        }
        return 0;
    } catch (const std::exception& e) { set_error(e.what()); return -474; }
      catch (...) { set_error("unknown C++ exception in voxel_downsample"); return -475; }
}

FRAHAN_GEOGRAM_API int frahan_geogram_kdtree_query(
    const double* tree_points,  int tree_count,
    const double* query_points, int query_count,
    int**   out_indices,
    double**out_sq_distances)
{
    *out_indices = nullptr;
    if (out_sq_distances) *out_sq_distances = nullptr;
    if (!tree_points || tree_count < 1) { set_error("kdtree_query: empty tree"); return -481; }
    if (!query_points || query_count < 1) { set_error("kdtree_query: empty queries"); return -482; }
    try {
        GEO::NearestNeighborSearch_var nn = GEO::NearestNeighborSearch::create(3);
        nn->set_points(tree_count, tree_points);

        *out_indices = static_cast<int*>(std::malloc(query_count * sizeof(int)));
        if (!*out_indices) { set_error("kdtree_query: malloc indices failed"); return -483; }
        if (out_sq_distances) {
            *out_sq_distances = static_cast<double*>(std::malloc(query_count * sizeof(double)));
            if (!*out_sq_distances) { set_error("kdtree_query: malloc distances failed"); return -484; }
        }
        // Per-query 1-NN search.
        for (int q = 0; q < query_count; ++q) {
            GEO::index_t idx;
            double sqd;
            nn->get_nearest_neighbors(1, &query_points[3*q], &idx, &sqd);
            (*out_indices)[q] = static_cast<int>(idx);
            if (out_sq_distances) (*out_sq_distances)[q] = sqd;
        }
        return 0;
    } catch (const std::exception& e) { set_error(e.what()); return -485; }
      catch (...) { set_error("unknown C++ exception in kdtree_query"); return -486; }
}

} // extern "C"
