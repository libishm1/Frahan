#!/usr/bin/env bash
# =============================================================================
# build_native.sh — one-shot Linux/macOS build for libfrahan_cgal.{so,dylib}.
#
# Prerequisites (Linux):
#   sudo apt install libcgal-dev cmake g++
# Prerequisites (macOS):
#   brew install cgal cmake
#
# Usage:
#   ./build_native.sh                  # Release build
#   ./build_native.sh Debug            # Debug build
#   ./build_native.sh Release clean    # wipe build/ first
# =============================================================================

set -euo pipefail

CONFIG="${1:-Release}"
CLEAN="${2:-}"

cd "$(dirname "$0")"

if [ "$CLEAN" = "clean" ] && [ -d build ]; then
    echo "Cleaning build/"
    rm -rf build
fi
mkdir -p build

echo "==> cmake configure ($CONFIG)"
cmake -DCMAKE_BUILD_TYPE="$CONFIG" -S . -B build

echo "==> cmake build"
cmake --build build --config "$CONFIG" -- -j"$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 4)"

# Locate the built shared library.
if [ "$(uname)" = "Darwin" ]; then
    lib="build/libfrahan_cgal.dylib"
else
    lib="build/libfrahan_cgal.so"
fi

if [ ! -f "$lib" ]; then
    echo "build succeeded but library not found at $lib" >&2
    exit 1
fi
echo "Built: $lib  ($(stat -c%s "$lib" 2>/dev/null || stat -f%z "$lib") bytes)"
echo ""
echo "Deploy: copy $lib alongside your Frahan.StonePack.gha, or onto"
echo "LD_LIBRARY_PATH (Linux) / DYLD_LIBRARY_PATH (macOS)."
