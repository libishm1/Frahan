#!/usr/bin/env bash
# Frahan StonePack - one-shot deploy (git-bash / WSL on Windows, Rhino 8).
# Copies plugin binaries, native libs, BFF, and Kintsugi weights into the
# Grasshopper Libraries folder. CLOSE RHINO before running. Style: short sentences, no em dashes.
#
# Usage:  bash install/deploy.sh
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
lib="${APPDATA:-$HOME/AppData/Roaming}/Grasshopper/Libraries"
lib="$(echo "$lib" | sed 's#\\#/#g')"
mkdir -p "$lib"

if tasklist 2>/dev/null | grep -qi "Rhino.exe"; then
  echo "Rhino is running. Close Rhino and re-run (the .gha is locked while Rhino is open)." >&2
  exit 1
fi

echo "Deploying Frahan StonePack to $lib"
cp -f "$here"/plugin/* "$lib"/ && echo "  plugin binaries + native libs copied"
cp -f "$here"/tools/bff-command-line.exe "$lib"/ && echo "  bff-command-line.exe copied"
if [ -f "$here/weights/kintsugi.bin" ]; then
  cp -f "$here/weights/kintsugi.bin" "$lib"/ && echo "  kintsugi.bin copied (~255 MB)"
else
  echo "  kintsugi.bin NOT FOUND (run: git lfs pull). Kintsugi Port mode unavailable."
fi
echo "Done. Start Rhino + Grasshopper; open examples/*/*.gh."
