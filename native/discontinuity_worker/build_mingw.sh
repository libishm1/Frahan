#!/usr/bin/env bash
# Clean-room discontinuity worker — static mingw64 build (no GPL, no external libs).
export PATH=/c/msys64/mingw64/bin:/c/msys64/usr/bin:$PATH
# g++ driver writes scratch to the Windows %TMP% dir; if that points at
# C:\WINDOWS the build dies with "Cannot create temporary file ... Permission
# denied". Force a writable Windows-style temp dir.
TMPWIN="$(cd "$(dirname "$0")" && pwd -W 2>/dev/null || echo "$PWD")/_tmpbuild"
mkdir -p "$TMPWIN"
export TMP="$(echo "$TMPWIN" | sed 's#/#\\#g')" TEMP="$TMP" TMPDIR="$TMPWIN"
set -e
GPP="/c/msys64/mingw64/bin/g++.exe"
"$GPP" -O3 -fopenmp -std=c++17 -D_USE_MATH_DEFINES \
  -static -static-libgcc -static-libstdc++ \
  -o frahan_discontinuity_worker.exe frahan_discontinuity_worker.cpp -lgomp
echo "built: $(ls -la frahan_discontinuity_worker.exe | awk '{print $5}') bytes"
