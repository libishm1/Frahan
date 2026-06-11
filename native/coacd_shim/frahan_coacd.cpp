// =============================================================================
// frahan_coacd — CoACD-backed approximate convex decomposition.
//
// Builds against SarahWeiii/CoACD (linked as static `coacd` target by
// CMakeLists.txt). FRAHAN_COACD_BUILDING is defined by CMake via
// target_compile_definitions; do NOT redefine it here.
//
// Structural invariants (mirror frahan_cgal):
//
//   - All C++-only helpers live in ONE anonymous namespace at file scope,
//     BEFORE the extern "C" block. Anything that returns or accepts a CoACD
//     C++ type must have C++ linkage.
//   - All exported FRAHAN_COACD_API entry points live in ONE extern "C"
//     block at the bottom. They reference the anonymous-namespace helpers
//     by name.
//   - No exception escapes the C boundary. Every entry point body is
//     wrapped in try/catch with negative return codes.
// =============================================================================

#include "frahan_coacd.h"

// CoACD's public C++ API. Header path under the upstream repo is
// `public/coacd.h`. CMake's target_link_libraries(... coacd) propagates
// the include directory.
#include <coacd.h>

#include <array>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <exception>
#include <stdexcept>
#include <string>
#include <vector>

// SEH-to-C++-exception translator. Requires /EHa (set by CMakeLists.txt
// for MSVC). Without this, native crashes inside CoACD (access
// violations, stack overflow, FP exceptions) bypass `catch (...)` and
// surface at the .NET boundary as System.Runtime.InteropServices.
// SEHException with no diagnostic detail. With it, the SEH code is
// embedded in a std::runtime_error that our existing catch handlers
// pick up and forward through frahan_coacd_last_error.
#ifdef _MSC_VER
#  include <eh.h>
#  include <windows.h>
#endif

// =============================================================================
// All C++-only helpers in ONE anonymous namespace, OUTSIDE extern "C".
// =============================================================================

namespace {

thread_local std::string g_lastError;

void set_error(const char* msg) { g_lastError = msg ? msg : ""; }
void set_error(const std::string& msg) { g_lastError = msg; }

// RAII installer for the per-thread SEH translator. Construct at the
// top of each entry point. Restores the previous translator on scope
// exit. SEH thrown inside CoACD becomes std::runtime_error("SEH 0x...")
// which our existing catch (const std::exception&) handles.
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
    SehScope() {
        prev = _set_se_translator([](unsigned int code, EXCEPTION_POINTERS* /*ep*/) {
            char buf[96];
            std::snprintf(buf, sizeof(buf),
                "SEH 0x%08X (%s) inside CoACD",
                code, seh_name(code));
            throw std::runtime_error(buf);
        });
    }
    ~SehScope() {
        _set_se_translator(prev);
    }
};
#else
struct SehScope { SehScope() {} ~SehScope() {} };
#endif

// Validate input buffers. Triangle indices are checked against vc.
bool validate_input(const double* verts, int vc, const int* tris, int tc) {
    if (verts == nullptr || tris == nullptr || vc < 0 || tc < 0) {
        set_error("null buffer or negative count");
        return false;
    }
    for (int i = 0; i < tc; ++i) {
        int a = tris[3 * i + 0], b = tris[3 * i + 1], c = tris[3 * i + 2];
        if (a < 0 || a >= vc || b < 0 || b >= vc || c < 0 || c >= vc) {
            set_error("triangle index out of range");
            return false;
        }
    }
    return true;
}

// Apply per-parameter "use CoACD default" semantics. Defaults match the
// header-declared defaults of `coacd::CoACD(...)` in public/coacd.h
// (verified against tag 1.0.11). preprocess_mode and apx_mode use the
// constexpr int constants from coacd.h:
//   preprocess_auto = 0, preprocess_on = 1, preprocess_off = 2
//   apx_ch = 0, apx_box = 1
struct CoacdParams {
    double       threshold;
    int          max_convex_hull;
    int          preprocess_mode;
    int          prep_resolution;
    int          sample_resolution;
    int          mcts_nodes;
    int          mcts_iteration;
    int          mcts_max_depth;
    bool         pca;
    bool         merge;
    int          max_ch_vertex;
    unsigned int seed;
    bool         real_metric;
};

CoacdParams resolve_params(double threshold,
                           int preprocess_mode,
                           int prep_resolution,
                           int sample_resolution,
                           int mcts_nodes,
                           int mcts_iteration,
                           int mcts_max_depth,
                           int pca,
                           int merge,
                           int max_convex_hull,
                           unsigned int seed,
                           int real_metric) {
    CoacdParams p;
    p.threshold         = (threshold        < 0.0) ? 0.05  : threshold;
    p.max_convex_hull   = (max_convex_hull  <= 0)  ? -1    : max_convex_hull;
    p.preprocess_mode   = (preprocess_mode  < 0)   ? preprocess_auto : preprocess_mode;
    p.prep_resolution   = (prep_resolution  <= 0)  ? 50    : prep_resolution;
    p.sample_resolution = (sample_resolution <= 0) ? 2000  : sample_resolution;
    p.mcts_nodes        = (mcts_nodes       <= 0)  ? 20    : mcts_nodes;
    p.mcts_iteration    = (mcts_iteration   <= 0)  ? 150   : mcts_iteration;
    p.mcts_max_depth    = (mcts_max_depth   <= 0)  ? 3     : mcts_max_depth;
    p.pca               = (pca   != 0);
    p.merge             = (merge != 0);
    // CoACD's default for max_ch_vertex is 256; expose it as the
    // "max convex-hull vertex count per piece" knob via the same arg
    // slot the Frahan ABI uses for max-pieces (see header comment).
    // We keep separate fields here since the C ABI distinguishes them.
    p.max_ch_vertex     = 256;
    p.seed              = seed;
    p.real_metric       = (real_metric != 0);
    return p;
}

// Pack a CoACD_MeshArray into the flat output layout (concatenated verts
// + tris with per-part start arrays). Triangle indices stay LOCAL to each
// piece, so downstream code can lift each piece into its own mesh without
// reindexing.
int pack_output(const CoACD_MeshArray& arr,
                int*     out_part_count,
                double** out_verts,       int** out_vert_starts, int* out_vert_count,
                int**    out_tris,        int** out_tri_starts,  int* out_tri_count) {
    const int n = static_cast<int>(arr.meshes_count);
    *out_part_count = n;

    long long total_v = 0, total_t = 0;
    for (uint64_t i = 0; i < arr.meshes_count; ++i) {
        total_v += static_cast<long long>(arr.meshes_ptr[i].vertices_count);
        total_t += static_cast<long long>(arr.meshes_ptr[i].triangles_count);
    }
    *out_vert_count = static_cast<int>(total_v);
    *out_tri_count  = static_cast<int>(total_t);

    *out_verts        = nullptr;
    *out_vert_starts  = nullptr;
    *out_tris         = nullptr;
    *out_tri_starts   = nullptr;

    if (n == 0) return 0;

    *out_verts = (total_v > 0)
        ? static_cast<double*>(std::malloc(sizeof(double) * 3 * total_v))
        : nullptr;
    *out_tris  = (total_t > 0)
        ? static_cast<int*>(std::malloc(sizeof(int) * 3 * total_t))
        : nullptr;
    *out_vert_starts = static_cast<int*>(std::malloc(sizeof(int) * (n + 1)));
    *out_tri_starts  = static_cast<int*>(std::malloc(sizeof(int) * (n + 1)));

    if ((total_v > 0 && *out_verts == nullptr) ||
        (total_t > 0 && *out_tris  == nullptr) ||
        *out_vert_starts == nullptr ||
        *out_tri_starts  == nullptr) {
        std::free(*out_verts);       *out_verts       = nullptr;
        std::free(*out_tris);        *out_tris        = nullptr;
        std::free(*out_vert_starts); *out_vert_starts = nullptr;
        std::free(*out_tri_starts);  *out_tri_starts  = nullptr;
        *out_part_count = 0; *out_vert_count = 0; *out_tri_count = 0;
        set_error("malloc failure (decompose output)");
        return -101;
    }

    int v_off = 0, t_off = 0;
    for (int i = 0; i < n; ++i) {
        (*out_vert_starts)[i] = v_off;
        (*out_tri_starts)[i]  = t_off;

        const CoACD_Mesh& part = arr.meshes_ptr[i];
        const int part_v = static_cast<int>(part.vertices_count);
        const int part_t = static_cast<int>(part.triangles_count);
        if (part_v > 0 && part.vertices_ptr != nullptr) {
            std::memcpy(*out_verts + 3 * v_off, part.vertices_ptr,
                        sizeof(double) * 3 * part_v);
        }
        if (part_t > 0 && part.triangles_ptr != nullptr) {
            std::memcpy(*out_tris + 3 * t_off, part.triangles_ptr,
                        sizeof(int) * 3 * part_t);
        }
        v_off += part_v;
        t_off += part_t;
    }
    (*out_vert_starts)[n] = v_off;
    (*out_tri_starts)[n]  = t_off;
    return 0;
}

} // anonymous namespace

// =============================================================================
// All exported FRAHAN_COACD_API functions in ONE extern "C" block.
// =============================================================================

extern "C" {

FRAHAN_COACD_API const char* frahan_coacd_version(void) {
    // WITH_3RD_PARTY_LIBS is propagated by CoACD's PUBLIC compile
    // definitions when the `coacd` static target was built with
    // WITH_3RD_PARTY_LIBS=ON (=1) or =OFF (=0). Embedded in the
    // version string so the GH "Backend" output is honest about
    // whether OpenVDB-based manifold preprocessing is available.
#if defined(WITH_3RD_PARTY_LIBS) && WITH_3RD_PARTY_LIBS
    return "Frahan-CoACD 0.1 (CoACD 1.0.11)";
#else
    return "Frahan-CoACD 0.1 (CoACD 1.0.11; no manifold preprocess; input must be 2-manifold)";
#endif
}

FRAHAN_COACD_API const char* frahan_coacd_last_error(void) {
    return g_lastError.c_str();
}

FRAHAN_COACD_API void frahan_coacd_set_log_level(const char* level) {
    if (level == nullptr) return;
    try {
        // Use the C ABI variant — takes char const*, no string_view arg.
        CoACD_setLogLevel(level);
    } catch (const std::exception& e) {
        set_error(e.what());
    } catch (...) {
        set_error("unknown C++ exception in set_log_level");
    }
}

FRAHAN_COACD_API int frahan_coacd_decompose(
    const double* verts, int vcount,
    const int*    tris,  int tcount,
    double  threshold,
    int     preprocess_mode,
    int     preprocess_resolution,
    int     sample_resolution,
    int     mcts_nodes,
    int     mcts_iters,
    int     mcts_max_depth,
    int     pca,
    int     merge,
    int     max_convex_hull,
    unsigned int seed,
    int     real_metric,
    int*     out_part_count,
    double** out_verts,       int** out_vert_starts, int* out_vert_count,
    int**    out_tris,        int** out_tri_starts,  int* out_tri_count) {

    if (out_part_count == nullptr ||
        out_verts      == nullptr || out_vert_starts == nullptr || out_vert_count == nullptr ||
        out_tris       == nullptr || out_tri_starts  == nullptr || out_tri_count  == nullptr) {
        set_error("null output pointer");
        return -100;
    }
    *out_part_count   = 0;
    *out_verts        = nullptr;
    *out_vert_starts  = nullptr;
    *out_vert_count   = 0;
    *out_tris         = nullptr;
    *out_tri_starts   = nullptr;
    *out_tri_count    = 0;

    try {
        SehScope seh_translator;  // SEH -> std::runtime_error for catch below
        if (!validate_input(verts, vcount, tris, tcount)) return -110;

        const CoacdParams p = resolve_params(
            threshold, preprocess_mode, preprocess_resolution,
            sample_resolution, mcts_nodes, mcts_iters, mcts_max_depth,
            pca, merge, max_convex_hull, seed, real_metric);

        // CoACD_Mesh holds non-owning pointers + 3-tuple counts (NOT
        // raw element counts — verified against public/coacd.cpp at
        // tag 1.0.11). Passing const_cast is safe: CoACD_run reads
        // only and copies into its internal coacd::Mesh.
        CoACD_Mesh input;
        input.vertices_ptr    = const_cast<double*>(verts);
        input.vertices_count  = static_cast<uint64_t>(vcount);
        input.triangles_ptr   = const_cast<int*>(tris);
        input.triangles_count = static_cast<uint64_t>(tcount);

        // CoACD_run signature (verified against tag 1.0.11):
        //   CoACD_run(input, threshold, max_convex_hull, preprocess_mode,
        //             prep_resolution, sample_resolution, mcts_nodes,
        //             mcts_iteration, mcts_max_depth, pca, merge,
        //             decimate, max_ch_vertex, extrude, extrude_margin,
        //             apx_mode, seed, real_metric)
        CoACD_MeshArray result = CoACD_run(
            input,
            p.threshold,
            p.max_convex_hull,
            p.preprocess_mode,
            p.prep_resolution,
            p.sample_resolution,
            p.mcts_nodes,
            p.mcts_iteration,
            p.mcts_max_depth,
            p.pca,
            p.merge,
            /*decimate*/ false,
            p.max_ch_vertex,
            /*extrude*/ false,
            /*extrude_margin*/ 0.01,
            /*apx_mode*/ apx_ch,
            p.seed,
            p.real_metric);

        if (result.meshes_count == 0) {
            CoACD_freeMeshArray(result);
            set_error("CoACD returned zero pieces");
            return -111;
        }

        const int rc = pack_output(result,
                                   out_part_count,
                                   out_verts, out_vert_starts, out_vert_count,
                                   out_tris,  out_tri_starts,  out_tri_count);
        CoACD_freeMeshArray(result);
        return rc;
    } catch (const std::exception& e) {
        set_error(e.what());
        return -120;
    } catch (...) {
        set_error("unknown C++ exception in decompose");
        return -121;
    }
}

FRAHAN_COACD_API void frahan_coacd_free_pdouble(double* p) {
    if (p != nullptr) std::free(p);
}

FRAHAN_COACD_API void frahan_coacd_free_pint(int* p) {
    if (p != nullptr) std::free(p);
}

} // extern "C"
