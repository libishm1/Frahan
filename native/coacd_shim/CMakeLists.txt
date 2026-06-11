cmake_minimum_required(VERSION 3.24)

# CoACD's vendored zlib uses cmake_minimum_required(VERSION <3.5) which
# CMake 4.x refuses by default. Set the policy floor BEFORE project() so
# every nested CMakeLists (including FetchContent'd deps) picks it up.
set(CMAKE_POLICY_VERSION_MINIMUM 3.5 CACHE STRING
    "Compat shim for CoACD's vendored zlib on CMake 4.x" FORCE)

# Match Rhino's MSVC runtime (/MD, dynamic). Without this, when
# WITH_3RD_PARTY=ON the OpenVDB sub-build picks up /MT (static) via
# USE_STATIC_DEPENDENCIES=ON, which then collides with frahan_coacd's
# /MD at link time (LNK2038). Set FORCE so CoACD's openvdb.cmake
# can't override it.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreadedDLL" CACHE STRING
    "Use /MD to match Rhino and the rest of the Frahan build" FORCE)

project(frahan_coacd CXX)

# CoACD's CMakeLists.txt (line ~137) calls
#   configure_package_config_file(${CMAKE_SOURCE_DIR}/cmake/Config.cmake.in ...)
# which resolves to OUR project root when CoACD is consumed via
# add_subdirectory / FetchContent rather than as the top-level project.
# We mirror their template at coacd_shim/cmake/Config.cmake.in so the
# configure step succeeds. The install path is never invoked (we link
# `coacd` static into our shared lib instead of installing CoACD).

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# =============================================================================
# CoACD source acquisition.
#
# Two paths:
#   1. -DCOACD_ROOT=<path>  Use a pre-cloned CoACD checkout. Faster, offline.
#   2. (default)            FetchContent pulls CoACD from upstream and pins
#                           to a known-good tag. Requires network on first
#                           configure.
#
# CoACD bundles its own deps (CGAL, libigl, OpenVDB-style sources). It links
# as a static library named `coacd` with public header `public/coacd.h`.
# Reference: https://github.com/SarahWeiii/CoACD
# =============================================================================

set(FRAHAN_COACD_TAG "1.0.11" CACHE STRING
    "Upstream CoACD tag to fetch when COACD_ROOT is not provided. Tags
     are unprefixed (1.0.11, not v1.0.11).")

# CoACD's heavy preprocess pipeline pulls boost + openvdb + spdlog + zlib.
# Disable for fast iteration when input meshes are already 2-manifold —
# CoACD will throw `runtime_error("The mesh is not a 2-manifold!")` at
# decompose time on bad inputs in this mode.
option(FRAHAN_COACD_WITH_3RD_PARTY "Build CoACD with OpenVDB-based manifold preprocess (heavy deps)" ON)
set(WITH_3RD_PARTY_LIBS ${FRAHAN_COACD_WITH_3RD_PARTY} CACHE BOOL
    "Forwarded to CoACD's WITH_3RD_PARTY_LIBS option" FORCE)

if(DEFINED COACD_ROOT AND EXISTS "${COACD_ROOT}/CMakeLists.txt")
    message(STATUS "Using user-provided CoACD at ${COACD_ROOT}")
    add_subdirectory("${COACD_ROOT}" coacd_build EXCLUDE_FROM_ALL)
else()
    include(FetchContent)
    FetchContent_Declare(coacd
        GIT_REPOSITORY    https://github.com/SarahWeiii/CoACD.git
        GIT_TAG           ${FRAHAN_COACD_TAG}
        GIT_SHALLOW       TRUE
        GIT_SUBMODULES_RECURSE TRUE)
    message(STATUS "Fetching CoACD ${FRAHAN_COACD_TAG} from upstream "
                   "(WITH_3RD_PARTY_LIBS=${WITH_3RD_PARTY_LIBS})")
    FetchContent_MakeAvailable(coacd)
endif()

add_library(frahan_coacd SHARED
    frahan_coacd.cpp
    frahan_coacd.h)

# CoACD upstream exposes a static target named `coacd`. PRIVATE link keeps
# CoACD's symbols out of the shim's exported surface — only the
# FRAHAN_COACD_API entry points are visible to .NET.
target_link_libraries(frahan_coacd PRIVATE coacd)

target_compile_features(frahan_coacd PRIVATE cxx_std_17)

# Symbol export. Single source of truth: defined here only; the .cpp must
# NOT redefine FRAHAN_COACD_BUILDING. (Same convention as frahan_cgal.)
target_compile_definitions(frahan_coacd PRIVATE FRAHAN_COACD_BUILDING)

if(WIN32)
    set_target_properties(frahan_coacd PROPERTIES
        WINDOWS_EXPORT_ALL_SYMBOLS OFF
        VS_GLOBAL_RunCodeAnalysis  false)
endif()

# /EHa = "asynchronous exceptions". Without it, MSVC catch(...) does NOT
# trap SEH (access violations, stack overflow, FPU traps). With it, those
# crashes from inside CoACD become catchable in our extern "C" entry
# points and surface as negative return codes + frahan_coacd_last_error
# instead of SEHException at the .NET boundary.
if(MSVC)
    target_compile_options(frahan_coacd PRIVATE /EHa)
endif()

install(TARGETS frahan_coacd
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib)
install(FILES frahan_coacd.h DESTINATION include)
