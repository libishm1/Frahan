# 11 - Frahan Mesh and Native Backend Spec

**Spec version:** 0.1
**Sources:** runbook § 16.6, the nested `frahan_*_backend_starter*.zip`
files (catalogued, not extracted),
`Agent-orchestration-main/Agent-orchestration-main/RhinoCommon_CSharp_Portability_Knowledge_Wiki.md`,
and live `Frahan.StonePack.Core.SurfacePacking.MeshCleanup` /
`MeshObjIO`.

## 1. Goal

Provide a Frahan-owned mesh utility surface and a lazy native backend
loader so heavy mesh operations (repair, simplification, slicing,
remeshing, collision proxy) can be served by either the managed core
or an optional native binary, transparently.

## 2. Frahan.Mesh (proposed managed surface)

Pure-managed entry points over the existing `Rhino.Geometry.Mesh` type.
Live equivalent today: `Frahan.StonePack.Core.SurfacePacking.MeshCleanup`
+ `MeshObjIO`. Future home: `Frahan.Mesh` assembly (split out of
`Frahan.Core` per refactor R1).

```
Frahan.Mesh.MeshDiagnostics  : bool IsManifold(Mesh), int FaceCount(Mesh), …
Frahan.Mesh.MeshRepair       : Mesh Repair(Mesh, RepairOptions)
Frahan.Mesh.MeshSimplify     : Mesh Simplify(Mesh, double targetReductionRatio)
Frahan.Mesh.MeshSlice        : List<Polyline> Slice(Mesh, Plane[])
Frahan.Mesh.Remesh           : Mesh Uniform(Mesh, double edgeLength)
Frahan.Mesh.CollisionProxy   : Mesh BuildVoxelProxy(Mesh, double cellSize)
```

## 3. Frahan.NativeBridge (proposed)

Boundary interfaces:

```csharp
public interface IGeometryBackend
{
    string Name { get; }                       // "Managed", "GeometryCore", "Geogram", "CGAL"
    Version Version { get; }
    bool IsAvailable { get; }
    Mesh Repair(Mesh mesh, RepairOptions options);
    Mesh Simplify(Mesh mesh, double ratio);
    Mesh Remesh(Mesh mesh, double edgeLength);
}

public interface IPackingBackend
{
    string Name { get; }                       // "Managed", "Packing"
    Version Version { get; }
    bool IsAvailable { get; }
    PackResult Pack(IReadOnlyList<MeshPackItem> items, IrregularMeshContainer container, MeshPackSettings settings, CancellationToken token);
}

public static class NativeBackendLoader
{
    public static IGeometryBackend ChooseGeometryBackend(string preference);
    public static IPackingBackend  ChoosePackingBackend(string preference);
}
```

## 4. Frahan.Native.* (proposed concrete implementations)

| Assembly | Backs | Source | Risk |
| --- | --- | --- | --- |
| `Frahan.Native.GeometryCore` | mesh repair, simplification, remesh | `frahan_geometrycore_backend_starter (2).zip` (research bundle) | medium - license unknown until extracted |
| `Frahan.Native.Geogram` | remesh, mesh repair | research-mention only | medium - license check required |
| `Frahan.Native.CGAL` | boolean ops, mesh repair, polyhedral | `frahan_cgal_backend_starter (1).zip` (research bundle) | high - GPL/LGPL per CGAL package; per-package license review required |
| `Frahan.Native.Packing` | 3D collision proxies (VHACD / CoACD), packing acceleration | `frahan_packing_backend_starter (1).zip` (research bundle) | low–medium |

## 5. Lazy loading rules

1. The default `Frahan.GH` install ships with **no** native DLLs.
2. On first use of a component that requires a native backend, the
   loader probes a fixed search path (`%APPDATA%\Frahan\backends\` and
   the plugin install folder).
3. If no native backend is available, the loader returns the managed
   default and the component emits a `Remark` ("running on managed
   backend; native backend not found").
4. The loader never throws on missing native; it returns the managed
   backend.
5. The loader honours an environment variable `FRAHAN_BACKEND` to
   force a specific backend for testing.

## 6. Public-API discipline

No native type ever appears in a `Frahan.*` public signature.
Backend implementations marshal at the assembly boundary only.

## 7. Acceptance contract

A `Frahan.Native.*` assembly is **acceptance-tested** if:

- It implements one of the `Frahan.NativeBridge.I*Backend` interfaces.
- It returns deterministic results across two consecutive runs on
  the same input.
- It returns an error / throws only at the boundary; internal native
  failures are wrapped in `FrahanBackendException`.

## 8. Validation rules

- Native DLLs are signed (or their SHA-256 is recorded) in
  `THIRD_PARTY_NOTICES.md` so a tampered DLL can be detected.
- The `Frahan Native Backend Status` GH component lists the loaded
  backends and their versions for diagnostic purposes.

## 9. Failure modes

- Native DLL missing → fallback to managed, `Remark` emitted.
- Native DLL throws → wrapped in `FrahanBackendException`, surfaced
  as `Error` on the GH component.
- Native version mismatch with `Frahan.NativeBridge` → `Error`,
  loader refuses to load.

## 10. Tests required

- Unit: `NativeBackendLoader.ChooseGeometryBackend("Managed")` always
  returns a non-null backend.
- Unit: missing DLL → loader returns managed + `Remark`.
- Integration: round-trip a fixture mesh through the managed and
  native backends; deterministic equal results within 1e-6.

## 11. Out of scope for v1

- macOS / Linux native variants (Windows-x64 only).
- Native C++ build pipeline integration (vcpkg, Nuget native, etc.).
- Hot-swap backend at runtime without component re-evaluation.
