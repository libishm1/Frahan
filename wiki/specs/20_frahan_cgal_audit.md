# 20 — Frahan CGAL shim audit + extension

## Purpose

Cross-check the shipped `native/cgal_shim/` (frahan_cgal.h, .cpp,
CMakeLists.txt) for ABI consistency, exception-safety at the boundary,
and GPL license footprint. Also document the C ABI extension landed in
the same pass: oriented bounding box (OBB), 2D straight skeleton, 2D
polygon partition.

## Confirmed facts

### ABI consistency (mesh-boolean baseline) — PASS

- `extern "C"` with `__declspec(dllexport)` on Windows /
  `__attribute__((visibility("default")))` elsewhere. Symbol export
  controlled by `FRAHAN_CGAL_BUILDING` macro. Managed side uses
  `CallingConvention.Cdecl` matching the native default.
- Flat array I/O: `const double*` vertices (3 * N), `const int*` triangles
  (3 * T). Matches `MeshSnapshot` convention used elsewhere in Frahan.
- Output buffers via `double**` / `int**`. Native side mallocs; managed
  side `Marshal.Copy`s into a managed array, then calls
  `frahan_cgal_free_buffers` on both pointers. `IntPtr.Zero` safe.
- Error reporting: negative return code + thread-local
  `g_lastError` accessed via `frahan_cgal_last_error()`. Codes: -10,
  -11 input parse; -12 corefinement; -20, -21 std/unknown exception.
- Version string: static const, reads `CGAL_VERSION_STR` macro.

### Exception safety — PASS

- Every native entry point wraps the implementation in
  `try { ... } catch (const std::exception&) { ... } catch (...) { ... }`
- No C++ exceptions cross the C boundary.
- malloc failure handled: outputs zeroed, `set_error("malloc failure")`,
  -1 return.
- Defensive triangle-count check in `extract_mesh` (-2 path) catches
  CGAL emitting a non-triangle face after `triangulate_faces`. Should
  never fire in practice; safety net.

### Memory model — PASS

- Native allocations: `std::malloc` only (not `new[]`); managed side
  releases via `frahan_cgal_free_buffers` which calls `std::free`.
  No type / allocator mismatch.
- One free function for the boolean output shape (`double*` verts,
  `int*` tris). Extended ABI adds `frahan_cgal_free_pdouble` and
  `frahan_cgal_free_pint` for variable-shape outputs (skeleton,
  partition).

### GPL license footprint — UNDERSTOOD AND CORRECTLY HANDLED

Headers used by the mesh boolean baseline:
- `Exact_predicates_inexact_constructions_kernel` — LGPL
- `Surface_mesh` — LGPL
- `Polygon_mesh_processing/corefinement` — **GPL**
- `Polygon_mesh_processing/triangulate_faces` — **GPL**

Extended ABI adds GPL packages:
- `optimal_bounding_box` — **GPL**
- `Straight_skeleton_2` — **GPL**
- `Partition_2` (`approx_convex_partition_2`,
  `optimal_convex_partition_2`, `y_monotone_partition_2`) — **GPL**

Conclusion: the shim is GPL once compiled. Distribution model in
`BUILD.md` is the recommended path: ship `Frahan.StonePack.gha`
without the shim, let the user install CGAL themselves and build
the `.dll` locally. The managed front-end (`CgalMeshBoolean`,
`CgalGeometry`) `IsAvailable` probe means the .gha runs cleanly
regardless of whether the user opted in.

### Minor issues / nits

1. **thread_local last_error cross-thread read.** If native code A
   sets the error on thread T1, and managed code reads
   `frahan_cgal_last_error()` from thread T2, T2 sees an empty
   string. Managed wrappers always read from the same thread that
   made the failing native call (synchronous P/Invoke), so this is
   correct in practice; documented for clarity.
2. **Unused includes (pre-extension)**: `orientation.h` and
   `repair.h` were included but not referenced. Removed in the same
   commit as the ABI extension to avoid redundant compile cost.
3. **`extract_mesh` uses `std::map<Vh, int>`** for vertex-handle →
   index lookup — O(log N). For large meshes a `std::vector<int>`
   indexed by `vh.id()` would be O(1). Defer until perf measures
   show it matters.
4. **CMake export macro mismatch (FIXED in extension commit)**: the
   shipped `CMakeLists.txt` did not pass `-DFRAHAN_CGAL_BUILDING`
   when compiling the .dll, so on Windows the symbols would be
   imported (dllimport) rather than exported (dllexport). The
   `frahan_cgal.cpp` file `#define`s this macro before the include,
   which DOES work, but only because the header check goes through
   `#ifdef`. Adding the define explicitly via
   `target_compile_definitions(frahan_cgal PRIVATE FRAHAN_CGAL_BUILDING)`
   in CMakeLists.txt makes the intent explicit and avoids reliance
   on header ordering.

## HYBRID kernel mode (added 2026-05-08, COMPAS_CGAL pattern)

Reference: `https://discourse.mcneel.com/t/best-mesh-boolean-method-in-the-west/218547`
(Petras Vestartas, 2026-04-30) recommends the COMPAS_CGAL approach for
Rhino mesh booleans on real-world inputs that get cut multiple times.
Source pattern from `BlockResearchGroup/compas_cgal/src/booleans.cpp`.

### Why hybrid

EPICK alone (the original shipped behaviour) fails on numerically
fragile inputs: near-tangent contacts, repeated cuts that accumulate
rounding error, coplanar boundaries. EPECK alone is robust but 50–100×
slower than EPICK because every face-traversal touches lazy exact
arithmetic. HYBRID gets the best of both:

- Mesh storage and traversal stay in EPICK doubles (fast cache).
- A parallel EPECK property map carries exact coordinates per vertex.
- A custom property map (`ExactVertexPointMap`) is passed to PMP's
  corefinement so intersection vertices are CONSTRUCTED in EPECK and
  written back to BOTH the EPECK store (for downstream corefinement
  to see exact arithmetic) AND the inexact mesh point (so the mesh
  remains usable as a regular `Surface_mesh<EPICK::Point_3>`).
- `Cartesian_converter<EPICK, EPECK>` and `<EPECK, EPICK>` round-trip
  on read / write through the property map.

Empirically: COMPAS_CGAL reports 2–5× slowdown vs EPICK-only with
same robustness as full EPECK.

### ABI surface added

```c
int frahan_cgal_mesh_boolean_hybrid(
    int op_kind,        // 0=union, 1=intersection, 2=difference
    const double* a_verts, int a_vcount,
    const int* a_tris,    int a_tcount,
    const double* b_verts, int b_vcount,
    const int* b_tris,    int b_tcount,
    double** out_verts,    int* out_vcount,
    int**    out_tris,     int* out_tcount);
```

Single entry point because the three op kinds share 95% of the
implementation; one switch routes to PMP::corefine_and_compute_*.
Output ownership and free contract identical to the EPICK entry
points (`frahan_cgal_free_buffers`).

### Key implementation primitives (verbatim from COMPAS_CGAL)

```cpp
typedef CGAL::Exact_predicates_inexact_constructions_kernel K;
typedef CGAL::Exact_predicates_exact_constructions_kernel   EKernel;
typedef EKernel::Point_3                                    EPoint;
typedef CGAL::Cartesian_converter<K, EKernel>               ToExact;
typedef CGAL::Cartesian_converter<EKernel, K>               ToInexact;
typedef Mesh::Property_map<Mesh::Vertex_index, EPoint>      EPointMap;

struct ExactVertexPointMap {
    Mesh* mesh; EPointMap epm;
    typedef Mesh::Vertex_index            key_type;
    typedef EPoint                        value_type;
    typedef value_type                    reference;
    typedef boost::read_write_property_map_tag category;

    friend value_type get(const ExactVertexPointMap& m, key_type k) {
        return m.epm[k];
    }
    friend void put(const ExactVertexPointMap& m, key_type k, const value_type& v) {
        m.epm[k] = v;
        ToInexact to_inexact;
        m.mesh->point(k) = to_inexact(v);
    }
};

PMP::corefine_and_compute_union(a, b, out,
    PMP::parameters::vertex_point_map(vpmA),
    PMP::parameters::vertex_point_map(vpmB),
    PMP::parameters::vertex_point_map(vpmO));
```

### Managed surface added

`CgalMeshBoolean` adds a `CsgKernelMode` enum (`Inexact` = current
EPICK, `Hybrid` = new) and three new kernel-mode-aware overloads:

```csharp
MeshSnapshot Union(MeshSnapshot a, MeshSnapshot b, CsgKernelMode kernel, out CsgBackend backend);
MeshSnapshot Intersection(MeshSnapshot a, MeshSnapshot b, CsgKernelMode kernel, out CsgBackend backend);
MeshSnapshot Difference(MeshSnapshot a, MeshSnapshot b, CsgKernelMode kernel, out CsgBackend backend);
```

Existing 0-arg / 1-out overloads remain (backward compat). Default
behaviour = `Inexact`. When the native shim is absent the call falls
back transparently to managed BSP CSG regardless of requested kernel
(BSP has no kernel-mode equivalent).

### When to use which

| Input character | Recommended kernel |
|---|---|
| Well-conditioned masonry blocks, single-cut operations | Inexact |
| Multi-cut chains, near-tangent contacts, repeated repair cycles | Hybrid |
| Production fabrication outputs where one bad bool ruins the run | Hybrid |
| Throwaway exploration where speed matters, manual repair OK | Inexact |

## Build-time issues found and resolved (2026-05-08, post-HYBRID landing)

After the HYBRID kernel + extended-ABI commits, three concrete MSVC
build errors surfaced when the user attempted the local CMake build.
Fixed in the same pass.

### B1: Duplicate FRAHAN_CGAL_BUILDING define

**Symptom:** `frahan_cgal.cpp` had `#define FRAHAN_CGAL_BUILDING`
near the top AND `CMakeLists.txt` set the same macro via
`target_compile_definitions`. Compiler warning C4005 (macro
redefinition); some toolchains escalate to error.

**Root cause:** the original .cpp predated the CMakeLists.txt
addition. Both were correct in isolation; together, redundant.

**Fix:** removed the `#define` from `frahan_cgal.cpp`. CMakeLists.txt
is now the single source of truth. Header comment in the .cpp
explicitly forbids re-adding it.

### B2: ExactVertexPointMap inside extern "C"

**Symptom:** MSVC error C2526 / C7595 "C linkage function
returning C++ class". The `friend value_type get(...)` and `friend
void put(...)` of `ExactVertexPointMap` return / accept
`EKernel::Point_3` — a CGAL C++ type — but the struct lived inside
the `extern "C"` block, putting its friends under C linkage.

**Root cause:** the HYBRID-kernel commit appended the new code
INSIDE the existing `extern "C" { ... }` block at the bottom of the
file, including a nested anonymous namespace. That worked under GCC
(more lenient) but MSVC enforces the C-linkage check on friend
functions returning non-trivial C++ types.

**Fix:** restructured `frahan_cgal.cpp` so:
- ALL C++-only helpers (typedefs, anonymous-namespace functions,
  `ExactVertexPointMap` and similar structs, `BoolOp` enum,
  `build_mesh*`, `extract_mesh`, `run_op*`, `build_polygon2*`)
  live in ONE anonymous namespace at file scope, BEFORE the
  `extern "C"` block.
- ALL exported `FRAHAN_CGAL_API` entry points live in ONE
  `extern "C"` block at the bottom. They reference the
  anonymous-namespace helpers by name; that's legal because the
  helpers have C++ linkage and the entry-point body is C++ code
  that just happens to be visible with C linkage to the linker.
- Removed the nested `namespace { ... }` and nested `extern "C"`
  blocks the HYBRID and skeleton commits had introduced.

This is now an explicit invariant in the file's header comment.

### B3: oriented_bounding_box requires Eigen3

**Symptom:** static_assert failure during compilation of the OBB
entry point. Trace pointed at
`<CGAL/optimal_bounding_box.h>` requiring Eigen3 internally
(the OBB optimisation step solves a small SDP via Eigen).

**Root cause:** `vcpkg install cgal:x64-windows` installs CGAL but
does NOT pull Eigen3 as a transitive dependency. CGAL's CMake
config doesn't enforce Eigen presence at find_package time; the
static_assert fires deep inside the include chain.

**Fix:**
- Wrapped the `#include <CGAL/optimal_bounding_box.h>` in
  `#ifdef FRAHAN_CGAL_HAVE_EIGEN`.
- Wrapped the `frahan_cgal_obb_3d` declaration in `frahan_cgal.h`
  with the same guard.
- Wrapped the implementation in `frahan_cgal.cpp` with the same
  guard.
- `CMakeLists.txt`: `find_package(Eigen3 QUIET CONFIG)`. If found,
  `target_link_libraries(... Eigen3::Eigen)` and
  `target_compile_definitions(... FRAHAN_CGAL_HAVE_EIGEN)`. If
  not found, emit a status message: "Eigen3 not found —
  frahan_cgal_obb_3d will be omitted. Install Eigen3 (vcpkg/apt/brew)
  and reconfigure to enable."

**Managed-side handling:** added `CgalGeometry.IsObbAvailable`
property that probes for the symbol on first call. The
`OrientedBoundingBox` method now throws
`InvalidOperationException` with the message "shim built without
Eigen3" when the symbol is missing, distinct from the generic
"shim not loaded" message. Builds without Eigen still produce a
working .dll; OBB is the only feature affected.

### Test gate after fix

Managed compile + unit tests: 425 PASS / 0 FAIL / 39 SKIP. The
.NET side does not depend on the native compile state — `IsAvailable`
and `IsObbAvailable` probes mean managed code degrades gracefully
when the .dll is absent or built without Eigen.

Native compile: user task. With these three fixes the structural
errors are resolved; remaining build success depends on the local
CGAL + Eigen install state.

## Implementation notes (extended ABI)

### OBB (3D)

- Native function: `frahan_cgal_obb_3d(verts, vc, tris, tc, out_obb[15])`.
- Output is a fixed 15-double layout (origin × 3, X-axis × 3, Y-axis × 3,
  Z-axis × 3, extents × 3). No malloc; caller pre-allocates.
- Triangle topology accepted but unused — CGAL's
  `oriented_bounding_box` works on point sets.
- Managed wrapper: `CgalGeometry.OrientedBoundingBox(...)` returns
  an `ObbResult` struct.
- License: `optimal_bounding_box.h` is GPL.

### Straight skeleton (2D, interior)

- Native: `frahan_cgal_straight_skeleton_2d(outer, ..., holes, ..., out_verts, out_edges, out_times)`.
- Outer ring CCW, holes CW. Native side reverses if input winding
  is wrong (`Polygon2.is_clockwise_oriented()` check + `reverse_orientation`).
- Output graph: vertex array (2 * N doubles), edge pair array
  (2 * E ints), per-vertex time-of-arrival (N doubles, boundary verts
  have time 0). Caller frees three buffers via
  `frahan_cgal_free_pdouble` / `frahan_cgal_free_pint`.
- Each undirected edge is emitted once (canonical: `src < tgt`
  index pair) to avoid the 2x duplicate from CGAL's halfedge data
  structure.
- Managed wrapper: `CgalGeometry.StraightSkeleton2D(...)` returns
  `StraightSkeletonResult`.
- License: `Straight_skeleton_2` is GPL.

### Polygon partition (2D)

- Native: `frahan_cgal_polygon_partition_2d(verts, vc, kind, ...)`.
- Kinds: 0 = Hertel-Mehlhorn approx convex, 1 = Greene optimal
  convex (O(n^4)), 2 = y-monotone.
- Output: deduplicated vertex array + flat indices array + start
  offsets array. Polygon i = `indices[starts[i] .. starts[i+1])`.
  Three malloc'd buffers, freed via the per-type helpers.
- Vertex deduplication uses `std::map<pair<double,double>, int>`
  keyed on coordinate equality. Partition_2 introduces no Steiner
  points so coordinate equality is sufficient.
- Managed wrapper: `CgalGeometry.PolygonPartition2D(...)` returns
  `PolygonPartitionResult` with a `GetPolygon(i)` helper.
- License: `Partition_2` is GPL.

## Code or command patterns

### Native build (Windows)

```powershell
cd Template-General/outputs/2026-05-01/frahan_stonepack/native/cgal_shim
.\build_native.ps1                          # Release x64 + auto-deploy
.\build_native.ps1 -Config Debug -Clean     # full Debug rebuild
.\build_native.ps1 -NoDeploy                # build only
```

### Native build (Linux / macOS)

```bash
cd Template-General/outputs/2026-05-01/frahan_stonepack/native/cgal_shim
./build_native.sh                # Release
./build_native.sh Debug clean    # full Debug rebuild
```

### Managed probe + use

```csharp
if (CgalGeometry.IsAvailable)
{
    var obb = CgalGeometry.OrientedBoundingBox(meshVerts, meshTris);
    // obb.OriginX, obb.XAxisX, obb.ExtentX, ...
}
else
{
    // Fall back to AABB or whatever your code path needs.
}
```

## Risks

- **CGAL version drift.** API used here (PMP corefinement, OBB,
  Partition_2, Straight_skeleton_2) is stable from CGAL 5.0+ and
  current as of CGAL 6.0 (released 2025). vcpkg ships a recent
  version on every install. If a future CGAL deprecates one of
  these, the shim's `BUILD.md` notes which package each function
  uses for targeted fix.
- **GPL infection of the shipped binary.** Confirmed: ship the
  `.gha` without the `.dll`. The managed code probes for the .dll
  at runtime; absent → throws `InvalidOperationException` on
  call (CgalGeometry) or transparently falls back to MeshCsg
  (CgalMeshBoolean). User installs CGAL themselves.
- **Single-consumer of MeshCsg.** Auditing the Core source tree
  found `CgalMeshBoolean.ManagedFallback` is the ONLY caller of
  `MeshCsg.{Union, Intersection, Difference}`. There are no other
  sites to wire into CgalMeshBoolean — the wrapper exists but no
  upstream code currently invokes it. New OBB / skeleton / partition
  wrappers are equally dormant pending GH-component consumers.

## Open questions

- Should `CgalMeshBoolean` add an `Available` mode that throws
  rather than falling back? Current behaviour is silent fallback;
  for benchmarks where you're explicitly comparing CGAL vs BSP, a
  strict-mode toggle would help.
- Should the CMakeLists.txt detect missing GPL packages
  (Optimal_bounding_box, etc.) and emit a clear error rather than
  failing later in the .cpp compile? CGAL's CMake config does this
  to some extent but the message isn't great.
- Do the new ABI functions need versioning, or is the version
  string sufficient? Suggest add `FRAHAN_CGAL_API_VERSION` integer
  exposed via `frahan_cgal_api_version()` so managed code can
  verify the shim matches its expectations.

## Related raw files

- `Template-General/raw/2026-05-04/audit_extracts/nested_zips/frahan_cgal_backend_starter_1_/`
  — original starter that informed the shim design.

## Related wiki pages

- [`19_frahan_source_relocation_plan.md`](19_frahan_source_relocation_plan.md)
  — when this happens, native/ moves with the rest.
- [`21_frahan_cgal_build_setup.md`](21_frahan_cgal_build_setup.md)
  — Windows build procedure that produced the first successful
  frahan_cgal.dll on 2026-05-08. Documents dependency install
  order, cmake-gui workflow, and the three code-level fixes
  (committed 07de6ea) needed during the build pass.
- ``../algorithms/cgal_shim/validation_log.md`` (internal log, not published)
  — HitL passes against the build configuration in spec 21.

## Last updated

2026-05-08
