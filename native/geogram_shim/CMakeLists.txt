cmake_minimum_required(VERSION 3.24)

# Match Rhino's MSVC runtime (/MD, dynamic). Same rationale as
# coacd_shim — without this, mixing /MT (Geogram default for some
# configurations) with /MD (our shim, Rhino) causes LNK2038.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreadedDLL" CACHE STRING
    "Use /MD to match Rhino and the rest of the Frahan build" FORCE)

# CMake 4.x policy floor for any vendored deps that use
# cmake_minimum_required(<3.5). Geogram itself declares
# cmake_minimum_required(VERSION 3.5...3.30) so it's clean, but its
# third-party folder may include older code.
set(CMAKE_POLICY_VERSION_MINIMUM 3.5 CACHE STRING
    "Compat shim for Geogram's vendored third-party on CMake 4.x" FORCE)

project(frahan_geogram CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# =============================================================================
# Geogram source acquisition.
#
# Two paths:
#   1. -DGEOGRAM_ROOT=<path>  Use a pre-cloned Geogram checkout.
#   2. (default)              FetchContent pulls Geogram from upstream.
#
# Geogram is BSD-3 licensed throughout (no GPL infection unlike CGAL's
# PMP / Triangle), making it the better long-term home for features we
# want to ship inside a binary plugin without the GPL ceremony.
# Reference: https://github.com/BrunoLevy/geogram
# =============================================================================

set(FRAHAN_GEOGRAM_TAG "v1.9.9" CACHE STRING
    "Upstream Geogram tag to fetch when GEOGRAM_ROOT is not provided.")

# Strip every optional Geogram component we don't need. This keeps build
# time + DLL size + license footprint minimal.
#   - GRAPHICS / LUA / EXPLORAGRAM: unused, drag in OpenGL / GLFW / Lua.
#   - LEGACY_NUMERICS: pre-OpenNL stack, not needed.
#   - HLBFGS: nonlinear solver, decimation does not use it.
#   - TRIANGLE: GPL / non-commercial; off for license cleanliness.
#   - TETGEN: AGPL. Enabled here because volumetric Voronoi block
#     decomposition (frahan_geogram_voronoi_blocks_compute) requires
#     tet-meshing the input solid. The combined binary is therefore
#     subject to AGPL terms when redistributed externally. For an
#     AGPL-free build, set -DFRAHAN_WITH_TETGEN=OFF on the cmake line;
#     the volumetric block export will then return an error and the
#     surface RVD path keeps working.
#   - GARGANTUA: 64-bit indices, not needed.
#   - LIB_ONLY: skip example programs and viewers.
option(FRAHAN_WITH_TETGEN "Enable TetGen (AGPL) for volumetric Voronoi blocks" ON)
set(GEOGRAM_WITH_GRAPHICS         OFF CACHE BOOL "" FORCE)
set(GEOGRAM_WITH_LEGACY_NUMERICS  OFF CACHE BOOL "" FORCE)
set(GEOGRAM_WITH_HLBFGS           OFF CACHE BOOL "" FORCE)
set(GEOGRAM_WITH_TETGEN           ${FRAHAN_WITH_TETGEN} CACHE BOOL "" FORCE)
set(GEOGRAM_WITH_TRIANGLE         OFF CACHE BOOL "" FORCE)
set(GEOGRAM_WITH_LUA              OFF CACHE BOOL "" FORCE)
set(GEOGRAM_WITH_EXPLORAGRAM      OFF CACHE BOOL "" FORCE)
set(GEOGRAM_WITH_GARGANTUA        OFF CACHE BOOL "" FORCE)
set(GEOGRAM_WITH_TBB              OFF CACHE BOOL "" FORCE)
set(GEOGRAM_LIB_ONLY              ON  CACHE BOOL "" FORCE)
set(GEOGRAM_USE_SYSTEM_GLFW3      OFF CACHE BOOL "" FORCE)

if(DEFINED GEOGRAM_ROOT AND EXISTS "${GEOGRAM_ROOT}/CMakeLists.txt")
    message(STATUS "Using user-provided Geogram at ${GEOGRAM_ROOT}")
    add_subdirectory("${GEOGRAM_ROOT}" geogram_build EXCLUDE_FROM_ALL)
else()
    include(FetchContent)
    FetchContent_Declare(geogram
        GIT_REPOSITORY https://github.com/BrunoLevy/geogram.git
        GIT_TAG        ${FRAHAN_GEOGRAM_TAG}
        GIT_SHALLOW    TRUE)
    message(STATUS "Fetching Geogram ${FRAHAN_GEOGRAM_TAG} from upstream")
    FetchContent_MakeAvailable(geogram)
endif()

add_library(frahan_geogram SHARED
    frahan_geogram.cpp
    frahan_geogram.h)

target_link_libraries(frahan_geogram PRIVATE geogram)

target_compile_features(frahan_geogram PRIVATE cxx_std_17)

# Symbol export. Single source of truth: defined here only; the .cpp must
# NOT redefine FRAHAN_GEOGRAM_BUILDING.
#
# FRAHAN_GEOGRAM_ENABLE_POISSON wires frahan_geogram_poisson_reconstruct to
# GEO::PoissonReconstruction (Kazhdan's PoissonRecon, bundled inside Geogram
# at src/lib/geogram/third_party/PoissonRecon). The header is reachable via
# the geogram target's PUBLIC include dir (src/lib), so no extra include path
# is needed when building Geogram from source (the FetchContent path here).
target_compile_definitions(frahan_geogram PRIVATE
    FRAHAN_GEOGRAM_BUILDING
    FRAHAN_GEOGRAM_ENABLE_POISSON)

# /EHa: catch SEH inside Geogram in our extern "C" entry points and
# convert to negative return codes + frahan_geogram_last_error, instead
# of letting them surface at the .NET boundary as SEHException. Same
# convention as coacd_shim.
if(MSVC)
    target_compile_options(frahan_geogram PRIVATE /EHa)
    set_target_properties(frahan_geogram PROPERTIES
        WINDOWS_EXPORT_ALL_SYMBOLS OFF
        VS_GLOBAL_RunCodeAnalysis  false)
endif()

install(TARGETS frahan_geogram
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib)
install(FILES frahan_geogram.h DESTINATION include)
