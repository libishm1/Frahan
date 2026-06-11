cmake_minimum_required(VERSION 3.16)
project(frahan_cgal CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# CGAL ships a CMake config since 5.0+. With vcpkg + the
# CMake toolchain file, find_package picks it up directly.
find_package(CGAL REQUIRED)

# Eigen3 is OPTIONAL — only required for CGAL::oriented_bounding_box.
# When absent, the OBB entry point is excluded from the build (other
# entry points remain available). vcpkg's `cgal[core]` does not pull
# Eigen automatically; install via `vcpkg install eigen3` when OBB is
# wanted.
find_package(Eigen3 QUIET CONFIG)

add_library(frahan_cgal SHARED
    frahan_cgal.cpp
    frahan_cgal.h)

target_link_libraries(frahan_cgal PRIVATE CGAL::CGAL)

# Force C++17 even when CGAL is configured with a different default.
target_compile_features(frahan_cgal PRIVATE cxx_std_17)

# Symbol export. Single source of truth: set here only; the .cpp must
# NOT redefine FRAHAN_CGAL_BUILDING. (Audit findings in
# wiki/specs/20_frahan_cgal_audit.md "Build-time issues found and resolved".)
target_compile_definitions(frahan_cgal PRIVATE FRAHAN_CGAL_BUILDING)

# Phase H reconstruction (alpha shape, advancing front, estimate normals).
# The entry points always EXPORT; their bodies compile only when this macro
# is defined (they otherwise return a "not enabled" error). ON by default.
# Requires CGAL + Boost.Multiprecision; estimate_normals also needs Eigen
# (CGAL pca_estimate_normals). Turn OFF for a minimal boolean-only build.
option(FRAHAN_CGAL_ENABLE_RECONSTRUCTION "Build alpha shape / advancing front / estimate normals" ON)
if(FRAHAN_CGAL_ENABLE_RECONSTRUCTION)
    message(STATUS "frahan_cgal: reconstruction ENABLED (alpha shape + advancing front + estimate normals)")
    target_compile_definitions(frahan_cgal PRIVATE FRAHAN_CGAL_ENABLE_RECONSTRUCTION)
endif()

# OBB conditional — define FRAHAN_CGAL_HAVE_EIGEN only when Eigen3 is
# present. The .cpp and .h gate the OBB declaration + implementation
# on this macro. Builds without Eigen still compile cleanly; OBB is
# simply not exposed.
if(Eigen3_FOUND)
    message(STATUS "Eigen3 found - enabling CGAL OBB (oriented_bounding_box)")
    target_link_libraries(frahan_cgal PRIVATE Eigen3::Eigen)
    target_compile_definitions(frahan_cgal PRIVATE FRAHAN_CGAL_HAVE_EIGEN)
else()
    message(STATUS "Eigen3 not found - frahan_cgal_obb_3d will be omitted. "
                   "Install Eigen3 (vcpkg/apt/brew) and reconfigure to enable.")
endif()

# On Windows, ensure the .lib import library is also produced.
if(WIN32)
    set_target_properties(frahan_cgal PROPERTIES
        WINDOWS_EXPORT_ALL_SYMBOLS OFF
        VS_GLOBAL_RunCodeAnalysis false)
endif()

# Install layout: dll + import lib + header into a single folder for
# easy bundling alongside the Frahan plugin.
install(TARGETS frahan_cgal
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib)
install(FILES frahan_cgal.h DESTINATION include)
