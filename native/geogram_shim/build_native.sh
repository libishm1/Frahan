#!/usr/bin/env bash
# =============================================================================
# build_native.sh - one-shot Linux/macOS build for libfrahan_geogram.{so,dylib}.
#
# Prerequisites (Linux):
#   sudo apt install cmake g++ git
# Prerequisites (macOS):
#   brew install cmake git
#
# Usage:
#   ./build_native.sh                                  # Release build
#   ./build_native.sh Debug                            # Debug build
#   ./build_native.sh Release clean                    # wipe build/ first
#   GEOGRAM_ROOT=/src/geogram ./build_native.sh        # local checkout
#   FRAHAN_GEOGRAM_TAG=v1.9.9 ./build_native.sh        # pin upstream tag
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
if [ -n "${FRAHAN_GEOGRAM_TAG:-}" ]; then
    cmake_args+=( -DFRAHAN_GEOGRAM_TAG="$FRAHAN_GEOGRAM_TAG" )
fi
if [ -n "${GEOGRAM_ROOT:-}" ]; then
    if [ ! -f "$GEOGRAM_ROOT/CMakeLists.txt" ]; then
        echo "GEOGRAM_ROOT '$GEOGRAM_ROOT' does not contain a CMakeLists.txt." >&2
        exit 1
    fi
    cmake_args+=( -DGEOGRAM_ROOT="$GEOGRAM_ROOT" )
fi

echo "==> cmake configure ($CONFIG)"
cmake "${cmake_args[@]}"

echo "==> cmake build"
cmake --build build --config "$CONFIG" --target frahan_geogram -- -j"$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 4)"

if [ "$(uname)" = "Darwin" ]; then
    lib="build/libfrahan_geogram.dylib"
else
    lib="build/libfrahan_geogram.so"
fi

if [ ! -f "$lib" ]; then
    echo "build succeeded but library not found at $lib" >&2
    exit 1
fi
echo "Built: $lib  ($(stat -c%s "$lib" 2>/dev/null || stat -f%z "$lib") bytes)"
echo ""
echo "Deploy: copy $lib alongside your Frahan.StonePack.gha, or onto"
echo "LD_LIBRARY_PATH (Linux) / DYLD_LIBRARY_PATH (macOS)."
