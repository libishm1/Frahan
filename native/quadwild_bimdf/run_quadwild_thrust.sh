#!/usr/bin/env bash
# =============================================================================
# run_quadwild_thrust.sh -- reliable THRUST-ALIGNED quad remesh of a triangle
# mesh, by feeding OUR thrust-potential cross-field into QuadWild + Bi-MDF.
#
#   ours   : frahan_quadremesh --rosy   (potential field E1 = grad phi, per face)
#   theirs : quadwild (do_remesh 0, our .rosy) -> quad_from_patches (Bi-MDF/LEMON)
#
# The single-chart Poisson remesher (frahan_quadremesh --remesh/--potential) is
# exact on a clean disk but folds on a multiply-connected / high-genus-boundary
# vault (the Park Guell portico: 8 boundary loops). QuadWild's patch tracing +
# Bi-MDF integer quantization place the singularities and match patch sides so the
# output is ONE watertight 100%-quad mesh -- and with our field it follows the
# compression flow, not just curvature. Bi-MDF replaces Gurobi (LEMON/satsuma),
# so this is license-clean for an open build.
#
# Usage: run_quadwild_thrust.sh <mesh.bin> <out.obj> [supportFrac=0.30]
#   mesh.bin  : frahan_quadremesh mesh-only blob (int32 nv; nv*3 f64 xyz;
#               int32 nf; nf*3 int32 tris; f64 edgeLen)
#
# Requires QUADWILD_HOME to point at an extracted quadwild-bimdf release
# (bin/quadwild.exe, bin/quad_from_patches.exe, config/). Get it from
# https://github.com/cgg-bern/quadwild-bimdf/releases (GPL-3.0; NOT vendored here
# -- arm's-length, like frahan_instantmesh). See README.md.
# =============================================================================
set -euo pipefail
MESH_BIN="${1:?mesh.bin}"; OUT="${2:?out.obj}"; FRAC="${3:-0.30}"
QW="${QUADWILD_HOME:?set QUADWILD_HOME to the extracted quadwild-bimdf dir}"
HERE="$(cd "$(dirname "$0")" && pwd)"
QREMESH="${FRAHAN_QUADREMESH:-$HERE/../quadremesh_shim/frahan_quadremesh.exe}"

WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
cp -r "$QW/config" "$WORK/config"
# our mesh -> OBJ + our thrust field -> .rosy
python - "$MESH_BIN" "$WORK/m.obj" <<'PY'
import struct,sys
f=open(sys.argv[1],'rb'); nv=struct.unpack('<i',f.read(4))[0]
V=[struct.unpack('<3d',f.read(24)) for _ in range(nv)]
nf=struct.unpack('<i',f.read(4))[0]; T=struct.unpack('<%di'%(nf*3),f.read(nf*3*4))
o=open(sys.argv[2],'w')
for v in V: o.write('v %.8g %.8g %.8g\n'%v)
for i in range(nf): o.write('f %d %d %d\n'%(T[3*i]+1,T[3*i+1]+1,T[3*i+2]+1))
PY
"$QREMESH" --rosy "$MESH_BIN" "$WORK/m.rosy" "$FRAC"
printf 'do_remesh 0\nsharp_feature_thr 35\nalpha 0.01\nscaleFact 1\n' > "$WORK/config/prep_config/thrust_setup.txt"

cd "$WORK"
"$QW/bin/quadwild.exe" m.obj 2 config/prep_config/thrust_setup.txt m.rosy
"$QW/bin/quad_from_patches.exe" m_rem_p0.obj 123 config/main_config/flow_noalign_lemon.txt
cp m_rem_p0_123_quadrangulation_smooth.obj "$OUT"
echo "thrust-aligned quad mesh -> $OUT"
