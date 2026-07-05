# frahan_quadremesh

Out-of-process **thrust-following quad remesher** — the native port of our C#
reference (`Frahan.Masonry.Vault.FieldAlignedParam` + `QuadExtract`, validated in
Rhino). Given a triangle mesh and a per-vertex cross-field (E1 tangent + N normal,
computed upstream from the TNA thrust network), it produces a quad mesh whose
edges follow the field.

This is our replacement for the vendored `frahan_instantmesh.exe`: Instant Meshes
computes a *smoothness* field and cannot take our thrust field; here the field is
an **input**, so the quads align to the compression flow.

## Pipeline (mirrors the C# reference, cross-validated)

- **Stage A.5** comb + light Gauss-Seidel smooth of the field (the C# side already
  combs; this is a re-smooth hook).
- **Stage B** cotangent-Poisson parametrization `min |grad u - rho*E1|^2`, solved
  **matrix-free** by Jacobi-preconditioned conjugate gradient. This is the native
  win over the C# dense Cholesky (O(n^3) → sparse CG), so full-resolution meshes
  stay fast and memory-light.
- **Stage C** lift the integer `(u,v)` lattice back onto the surface → quads, with
  a signed-area fold count as a single-chart injectivity check.

Single chart only (no interior singularity). The seam cut for interior cones
(three-prong hub) is Stage B2, added later on both the C# and native sides.

## Build / test

    bash build_mingw.sh          # static mingw64 g++ -O3, no external libraries
    ./frahan_quadremesh.exe --selftest

Self-test reproduces the exact cases validated in Rhino:

    TEST1 flat/const  : residual 3.4e-22, u = rho*x exact (cg ~106 it)   PASS
    TEST2 paraboloid  : residual 9.5e-06, 344 quads, 0 flips (cg ~170 it) PASS

`344 quads` is bit-identical to the C# `QuadExtract` result — the native port
matches the reference.

## Usage

    frahan_quadremesh --selftest
    frahan_quadremesh --remesh <in.bin> <out.obj>

`in.bin` (little-endian): `int32 nv; nv*3 f64 verts; nv*3 f64 E1; nv*3 f64 N;
int32 nf; nf*3 int32 tris; f64 edgeLen`. The GH side (async, like the CRA worker)
writes the blob, shells out, and reads the quad OBJ back.

The `.exe` is gitignored (built from source, deployed to `install/plugin/`).
