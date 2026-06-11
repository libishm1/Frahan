#!/usr/bin/env bash
# =============================================================================
# build_native.sh — one-shot Linux/macOS build for libfrahan_coacd.{so,dylib}.
#
# Prerequisites (Linux):
#   sudo apt install cmake g++ git
# Prerequisites (macOS):
#   brew install cmake git
#
# Usage:
#   ./build_native.sh                              # Release build
#   ./build_native.sh Debug                        # Debug build
#   ./build_native.sh Release clean                # wipe build/ first
#   COACD_ROOT=/src/CoACD ./build_native.sh        # use local CoACD checkout
#   FRAHAN_COACD_TAG=v1.0.11 ./build_native.sh     # pin upstream tag
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

cmake_args=(
    -DCMAKE_BUILD_TYPE="$CONFIG"
    -S . -B build
)
if [ -n "${FRAHAN_COACD_TAG:-}" ]; then
    cmake_args+=( -DFRAHAN_COACD_TAG="$FRAHAN_COACD_TAG" )
fi
if [ -n "${COACD_ROOT:-}" ]; then
    if [ ! -f "$COACD_ROOT/CMakeLists.txt" ]; then
        echo "COACD_ROOT '$COACD_ROOT' does not contain a CMakeLists.txt." >&2
        exit 1
    fi
    cmake_args+=( -DCOACD_ROOT="$COACD_ROOT" )
fi

echo "==> cmake configure ($CONFIG)"
cmake "${cmake_args[@]}"

echo "==> cmake build"
cmake --build build --config "$CONFIG" -- -j"$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 4)"

if [ "$(uname)" = "Darwin" ]; then
    lib="build/libfrahan_coacd.dylib"
else
    lib="build/libfrahan_coacd.so"
fi

if [ ! -f "$lib" ]; then
    echo "build succeeded but library not found at $lib" >&2
    exit 1
fi
echo "Built: $lib  ($(stat -c%s "$lib" 2>/dev/null || stat -f%z "$lib") bytes)"
echo ""
echo "Deploy: copy $lib alongside your Frahan.StonePack.gha, or onto"
echo "LD_LIBRARY_PATH (Linux) / DYLD_LIBRARY_PATH (macOS)."
