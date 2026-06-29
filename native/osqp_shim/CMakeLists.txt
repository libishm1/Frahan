cmake_minimum_required(VERSION 3.16)
project(frahan_osqp CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# ---- OSQP ----
# vcpkg installs two targets depending on whether it built shared or static:
#   osqp::osqp            (usual)
#   osqp::osqp-static     (when VCPKG_LIBRARY_LINKAGE=static)
# We want the static flavour so frahan_osqp.dll has no runtime osqp.dll dep.
# Install with: vcpkg install osqp:x64-windows-static-md
# (uses the MSVC /MD runtime, compatible with Rhino's Grasshopper host).
#
# If you used the default x64-windows triplet (shared), change the target to
# osqp::osqp and ship osqp.dll alongside frahan_osqp.dll.
find_package(osqp REQUIRED CONFIG)

# Try static target first; fall back to shared.
if(TARGET osqp::osqp-static)
    set(OSQP_TARGET osqp::osqp-static)
elseif(TARGET osqp::osqp)
    set(OSQP_TARGET osqp::osqp)
    message(STATUS "frahan_osqp: linking shared OSQP (ship osqp.dll alongside frahan_osqp.dll)")
else()
    message(FATAL_ERROR "Neither osqp::osqp-static nor osqp::osqp target found.")
endif()

add_library(frahan_osqp SHARED
    frahan_osqp.cpp
    frahan_osqp.h)

target_link_libraries(frahan_osqp PRIVATE ${OSQP_TARGET})
target_compile_features(frahan_osqp PRIVATE cxx_std_17)
target_compile_definitions(frahan_osqp PRIVATE FRAHAN_OSQP_BUILDING)

if(MSVC)
    target_compile_options(frahan_osqp PRIVATE
        $<$<CONFIG:Release>:/O2 /GL /DNDEBUG>
        $<$<CONFIG:Debug>:/Od /Zi>)
    target_link_options(frahan_osqp PRIVATE
        $<$<CONFIG:Release>:/LTCG>)
    set_target_properties(frahan_osqp PROPERTIES
        WINDOWS_EXPORT_ALL_SYMBOLS OFF)
endif()

install(TARGETS frahan_osqp
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib)
install(FILES frahan_osqp.h DESTINATION include)
