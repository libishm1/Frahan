#!/usr/bin/env bash
# frahan_quadremesh -- static mingw64 build (no external libraries).
export PATH=/c/msys64/mingw64/bin:/c/msys64/usr/bin:$PATH
TMPWIN="$(cd "$(dirname "$0")" && pwd -W 2>/dev/null || echo "$PWD")/_tmpbuild"
mkdir -p "$TMPWIN"
export TMP="$(echo "$TMPWIN" | sed 's#/#\\#g')" TEMP="$TMP" TMPDIR="$TMPWIN"
set -e
GPP="/c/msys64/mingw64/bin/g++.exe"
"$GPP" -O3 -std=c++17 -D_USE_MATH_DEFINES \
  -static -static-libgcc -static-libstdc++ \
  -o frahan_quadremesh.exe frahan_quadremesh.cpp
echo "built: $(ls -la frahan_quadremesh.exe | awk '{print $5}') bytes"
