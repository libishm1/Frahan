#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Frahan.NativeBridge;

// =============================================================================
// Frahan.NativeBridge - lazy native-backend boundary
//
// This file is the proposed-only Frahan.NativeBridge module from Spec 11
// implemented as a real, pure-managed scaffold. No native DLLs are required.
// All Frahan-owned interfaces live here so future Frahan.Native.* assemblies
// can plug in without changing any Frahan public API.
//
// Five rules from Spec 11 enforced:
//   1. Default install ships no native DLLs.
//   2. On first use, NativeBackendLoader probes a fixed search path.
//   3. Missing native -> fall back to managed default + emit Remark.
//   4. Loader never throws on missing native.
//   5. FRAHAN_BACKEND environment variable forces a specific backend for testing.
// =============================================================================

/// <summary>
/// Severity of a backend diagnostic message.
/// </summary>
public enum BackendDiagnosticLevel
{
    Info,
    Warning,
    Error
}

/// <summary>
/// One diagnostic message from a backend operation.
/// </summary>
public sealed class BackendDiagnostic
{
    public BackendDiagnostic(BackendDiagnosticLevel level, string message)
    {
        Level = level;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public BackendDiagnosticLevel Level { get; }
    public string Message { get; }

    public override string ToString() => $"[{Level}] {Message}";
}

/// <summary>
/// Outcome from a backend operation. Carries success flag, diagnostics, and a
/// human-readable backend name (so callers can surface "ran on Managed" or
/// "ran on Geogram 1.7.0" in the GH UI).
/// </summary>
public sealed class BackendOperationResult
{
    public BackendOperationResult(
        bool success,
        string backendName,
        Version backendVersion,
        IReadOnlyList<BackendDiagnostic> diagnostics)
    {
        Success = success;
        BackendName = backendName ?? throw new ArgumentNullException(nameof(backendName));
        BackendVersion = backendVersion ?? throw new ArgumentNullException(nameof(backendVersion));
        Diagnostics = diagnostics ?? Array.Empty<BackendDiagnostic>();
    }

    public bool Success { get; }
    public string BackendName { get; }
    public Version BackendVersion { get; }
    public IReadOnlyList<BackendDiagnostic> Diagnostics { get; }

    public override string ToString() =>
        $"BackendOperationResult(Success={Success}, {BackendName} {BackendVersion}, {Diagnostics.Count} diag)";
}

/// <summary>
/// Common surface for all Frahan native (or managed) backends. Concrete
/// implementations live in Frahan.Native.* assemblies (proposed) or in
/// ManagedDefaultBackends below (the lowest-common-denominator fallback).
/// </summary>
public interface IFrahanBackend
{
    string Name { get; }
    Version Version { get; }
    bool IsAvailable { get; }
}

/// <summary>
/// Geometry backend (mesh repair, simplification, remesh, slicing). All input
/// and output types are Frahan-owned (callers convert from RhinoCommon at the
/// component edge). The managed default returns NotSupported diagnostics for
/// every operation; a future Frahan.Native.GeometryCore implementation
/// supersedes it on machines that have the DLL.
/// </summary>
public interface IGeometryBackend : IFrahanBackend
{
    /// <summary>Repair a mesh (close holes, weld vertices, unify normals).</summary>
    BackendOperationResult Repair(
        IReadOnlyList<double> vertexCoordsXyz,   // length = 3 * vertexCount
        IReadOnlyList<int> triangleIndices,      // length = 3 * triangleCount
        out double[] repairedVertexCoordsXyz,
        out int[] repairedTriangleIndices,
        CancellationToken cancellationToken = default);

    /// <summary>Simplify a mesh by a target reduction ratio (0..1).</summary>
    BackendOperationResult Simplify(
        IReadOnlyList<double> vertexCoordsXyz,
        IReadOnlyList<int> triangleIndices,
        double targetReductionRatio,
        out double[] simplifiedVertexCoordsXyz,
        out int[] simplifiedTriangleIndices,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Packing backend. Today the Frahan managed solvers (GreedyHeightmapPacker
/// etc.) live in Frahan.Core directly; this interface is the pluggable
/// boundary for a future Frahan.Native.Packing acceleration backend
/// (VHACD/CoACD-based collision proxies, native NFP, etc.).
/// </summary>
public interface IPackingBackend : IFrahanBackend
{
    /// <summary>Build a convex collision proxy of an arbitrary mesh.</summary>
    BackendOperationResult BuildCollisionProxy(
        IReadOnlyList<double> vertexCoordsXyz,
        IReadOnlyList<int> triangleIndices,
        out double[] proxyVertexCoordsXyz,
        out int[] proxyTriangleIndices,
        CancellationToken cancellationToken = default);
}

// -----------------------------------------------------------------------------
// Managed default implementations (the always-available fallback)
// -----------------------------------------------------------------------------

internal sealed class ManagedGeometryBackend : IGeometryBackend
{
    public string Name => "Managed";
    public Version Version => new Version(0, 1, 0);
    public bool IsAvailable => true;

    public BackendOperationResult Repair(
        IReadOnlyList<double> vertexCoordsXyz,
        IReadOnlyList<int> triangleIndices,
        out double[] repairedVertexCoordsXyz,
        out int[] repairedTriangleIndices,
        CancellationToken cancellationToken = default)
    {
        // The managed default simply round-trips the input; real repair (close
        // holes, weld, unify normals) lives in Frahan.Native.GeometryCore when
        // installed. Caller sees a Warning so they know to install the native
        // backend if they need actual repair.
        if (vertexCoordsXyz == null) throw new ArgumentNullException(nameof(vertexCoordsXyz));
        if (triangleIndices == null) throw new ArgumentNullException(nameof(triangleIndices));

        repairedVertexCoordsXyz = new double[vertexCoordsXyz.Count];
        for (int i = 0; i < vertexCoordsXyz.Count; i++)
            repairedVertexCoordsXyz[i] = vertexCoordsXyz[i];

        repairedTriangleIndices = new int[triangleIndices.Count];
        for (int i = 0; i < triangleIndices.Count; i++)
            repairedTriangleIndices[i] = triangleIndices[i];

        return new BackendOperationResult(
            success: true,
            backendName: Name,
            backendVersion: Version,
            diagnostics: new[]
            {
                new BackendDiagnostic(BackendDiagnosticLevel.Warning,
                    "Managed Repair is a passthrough. Install Frahan.Native.GeometryCore for actual mesh repair.")
            });
    }

    public BackendOperationResult Simplify(
        IReadOnlyList<double> vertexCoordsXyz,
        IReadOnlyList<int> triangleIndices,
        double targetReductionRatio,
        out double[] simplifiedVertexCoordsXyz,
        out int[] simplifiedTriangleIndices,
        CancellationToken cancellationToken = default)
    {
        if (vertexCoordsXyz == null) throw new ArgumentNullException(nameof(vertexCoordsXyz));
        if (triangleIndices == null) throw new ArgumentNullException(nameof(triangleIndices));
        if (targetReductionRatio < 0.0 || targetReductionRatio > 1.0)
            throw new ArgumentOutOfRangeException(nameof(targetReductionRatio), "must be in [0, 1]");

        simplifiedVertexCoordsXyz = new double[vertexCoordsXyz.Count];
        for (int i = 0; i < vertexCoordsXyz.Count; i++)
            simplifiedVertexCoordsXyz[i] = vertexCoordsXyz[i];
        simplifiedTriangleIndices = new int[triangleIndices.Count];
        for (int i = 0; i < triangleIndices.Count; i++)
            simplifiedTriangleIndices[i] = triangleIndices[i];

        return new BackendOperationResult(
            success: true,
            backendName: Name,
            backendVersion: Version,
            diagnostics: new[]
            {
                new BackendDiagnostic(BackendDiagnosticLevel.Warning,
                    "Managed Simplify is a passthrough. Install Frahan.Native.GeometryCore for actual mesh simplification.")
            });
    }
}

internal sealed class ManagedPackingBackend : IPackingBackend
{
    public string Name => "Managed";
    public Version Version => new Version(0, 1, 0);
    public bool IsAvailable => true;

    public BackendOperationResult BuildCollisionProxy(
        IReadOnlyList<double> vertexCoordsXyz,
        IReadOnlyList<int> triangleIndices,
        out double[] proxyVertexCoordsXyz,
        out int[] proxyTriangleIndices,
        CancellationToken cancellationToken = default)
    {
        if (vertexCoordsXyz == null) throw new ArgumentNullException(nameof(vertexCoordsXyz));
        if (triangleIndices == null) throw new ArgumentNullException(nameof(triangleIndices));

        // Default proxy = the input mesh itself (no convex decomposition without
        // the native VHACD/CoACD backend).
        proxyVertexCoordsXyz = new double[vertexCoordsXyz.Count];
        for (int i = 0; i < vertexCoordsXyz.Count; i++)
            proxyVertexCoordsXyz[i] = vertexCoordsXyz[i];
        proxyTriangleIndices = new int[triangleIndices.Count];
        for (int i = 0; i < triangleIndices.Count; i++)
            proxyTriangleIndices[i] = triangleIndices[i];

        return new BackendOperationResult(
            success: true,
            backendName: Name,
            backendVersion: Version,
            diagnostics: new[]
            {
                new BackendDiagnostic(BackendDiagnosticLevel.Warning,
                    "Managed BuildCollisionProxy returned the input mesh unchanged. Install Frahan.Native.Packing (VHACD/CoACD) for true convex decomposition.")
            });
    }
}

// -----------------------------------------------------------------------------
// Probe report (Spec 11 § 5: surface what the loader did, for diagnostics)
// -----------------------------------------------------------------------------

/// <summary>
/// What the loader concluded about one DLL it considered during a probe.
/// </summary>
public enum BackendProbeStatus
{
    /// <summary>The DLL did not load (file not found, bad image, etc.).</summary>
    LoadFailed,
    /// <summary>The DLL loaded, but no public type implements the target backend interface.</summary>
    NoMatchingType,
    /// <summary>A candidate type was found, but its <c>IsAvailable</c> returned false, or a name preference excluded it.</summary>
    Skipped,
    /// <summary>A backend was instantiated and selected. Only one entry per probe report carries this status.</summary>
    Instantiated,
}

/// <summary>
/// One entry in a <see cref="BackendProbeReport"/>.
/// </summary>
public sealed class BackendProbeEntry
{
    public BackendProbeEntry(string assemblyPath, BackendProbeStatus status, string detail)
    {
        AssemblyPath = assemblyPath ?? throw new ArgumentNullException(nameof(assemblyPath));
        Status = status;
        Detail = detail ?? string.Empty;
    }

    public string AssemblyPath { get; }
    public BackendProbeStatus Status { get; }
    public string Detail { get; }

    public override string ToString() =>
        $"{Status}: {AssemblyPath}{(string.IsNullOrEmpty(Detail) ? "" : " (" + Detail + ")")}";
}

/// <summary>
/// Snapshot of one backend probe — what env override was active, which search
/// paths were checked, what each DLL did, and which backend was finally
/// selected. Useful for the proposed "Frahan Native Backend Status" GH
/// component and for debugging native-backend rollout. All fields are
/// immutable after construction.
/// </summary>
public sealed class BackendProbeReport
{
    public BackendProbeReport(
        string interfaceName,
        string envOverride,
        string callerPreference,
        IReadOnlyList<(string Path, bool Existed)> searchPathsChecked,
        IReadOnlyList<BackendProbeEntry> dllsExamined,
        string selectedBackendName)
    {
        InterfaceName = interfaceName ?? throw new ArgumentNullException(nameof(interfaceName));
        EnvOverride = envOverride ?? string.Empty;
        CallerPreference = callerPreference ?? string.Empty;
        SearchPathsChecked = searchPathsChecked ?? Array.Empty<(string, bool)>();
        DllsExamined = dllsExamined ?? Array.Empty<BackendProbeEntry>();
        SelectedBackendName = selectedBackendName ?? string.Empty;
    }

    /// <summary>Short interface tag (e.g. "geometry", "packing").</summary>
    public string InterfaceName { get; }

    /// <summary>Value of FRAHAN_BACKEND at probe time (empty if unset).</summary>
    public string EnvOverride { get; }

    /// <summary>Preference passed by the caller (empty if none).</summary>
    public string CallerPreference { get; }

    public IReadOnlyList<(string Path, bool Existed)> SearchPathsChecked { get; }
    public IReadOnlyList<BackendProbeEntry> DllsExamined { get; }

    /// <summary>The Name of the backend that ChooseXxxBackend ultimately returned (e.g. "Managed").</summary>
    public string SelectedBackendName { get; }

    public override string ToString() =>
        $"Probe({InterfaceName}, env='{EnvOverride}', pref='{CallerPreference}', " +
        $"selected='{SelectedBackendName}', paths={SearchPathsChecked.Count}, " +
        $"dlls={DllsExamined.Count})";
}

// -----------------------------------------------------------------------------
// Lazy backend loader (Spec 11 § 5)
// -----------------------------------------------------------------------------

/// <summary>
/// Probes a search path for native backend assemblies and returns the first
/// available implementation. Falls back to the managed default if no native
/// backend is found. Honours the FRAHAN_BACKEND environment variable as a
/// per-process override.
///
/// Search path (in order):
///   1. %FRAHAN_BACKENDS_PATH% (if set)
///   2. %APPDATA%\Frahan\backends\
///   3. Directory containing the calling assembly + "\backends"
///
/// FRAHAN_BACKEND values:
///   - empty / unset       -> probe native, fall back to managed
///   - "managed" (any case)-> skip native probe, return managed
///   - path ending ".dll"  -> load that exact DLL only
///   - any other string    -> probe but require backend Name to contain it
///
/// The loader never throws on missing native. It always returns a usable
/// backend, even if that backend is the managed passthrough.
/// </summary>
public static class NativeBackendLoader
{
    private const string EnvOverride = "FRAHAN_BACKEND";
    private const string EnvSearchPath = "FRAHAN_BACKENDS_PATH";

    /// <summary>Native geometry backend assembly file name probed in each search dir.</summary>
    public const string GeometryDllName = "Frahan.Native.GeometryCore.dll";

    /// <summary>Native packing backend assembly file name probed in each search dir.</summary>
    public const string PackingDllName = "Frahan.Native.Packing.dll";

    private static readonly object _gate = new object();
    private static IGeometryBackend _geometry;
    private static IPackingBackend _packing;
    private static BackendProbeReport _lastGeometryProbe;
    private static BackendProbeReport _lastPackingProbe;

    /// <summary>
    /// Snapshot of the most recent geometry-backend probe. Null until
    /// <see cref="ChooseGeometryBackend"/> has been called at least once.
    /// </summary>
    public static BackendProbeReport LastGeometryProbe
    {
        get { lock (_gate) return _lastGeometryProbe; }
    }

    /// <summary>
    /// Snapshot of the most recent packing-backend probe. Null until
    /// <see cref="ChoosePackingBackend"/> has been called at least once.
    /// </summary>
    public static BackendProbeReport LastPackingProbe
    {
        get { lock (_gate) return _lastPackingProbe; }
    }

    /// <summary>
    /// Resolve the geometry backend. The result is cached per process; pass
    /// forceReload=true to re-probe (useful in tests).
    /// </summary>
    public static IGeometryBackend ChooseGeometryBackend(string preference = null, bool forceReload = false)
    {
        if (!forceReload && _geometry != null) return _geometry;
        lock (_gate)
        {
            if (!forceReload && _geometry != null) return _geometry;
            _geometry = ResolveGeometry(preference);
            return _geometry;
        }
    }

    /// <summary>
    /// Resolve the packing backend. The result is cached per process; pass
    /// forceReload=true to re-probe.
    /// </summary>
    public static IPackingBackend ChoosePackingBackend(string preference = null, bool forceReload = false)
    {
        if (!forceReload && _packing != null) return _packing;
        lock (_gate)
        {
            if (!forceReload && _packing != null) return _packing;
            _packing = ResolvePacking(preference);
            return _packing;
        }
    }

    /// <summary>
    /// Returns the backend search paths in probe order, including which exist.
    /// Useful for the Frahan Native Backend Status component.
    /// </summary>
    public static IReadOnlyList<(string Path, bool Exists)> GetSearchPaths()
    {
        var paths = new List<(string, bool)>();
        string envPath = Environment.GetEnvironmentVariable(EnvSearchPath);
        if (!string.IsNullOrWhiteSpace(envPath))
            paths.Add((envPath, Directory.Exists(envPath)));

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            string p = Path.Combine(appData, "Frahan", "backends");
            paths.Add((p, Directory.Exists(p)));
        }

        try
        {
            string asmDir = Path.GetDirectoryName(typeof(NativeBackendLoader).Assembly.Location);
            if (!string.IsNullOrWhiteSpace(asmDir))
            {
                string p = Path.Combine(asmDir, "backends");
                paths.Add((p, Directory.Exists(p)));
            }
        }
        catch
        {
            // Some hosts return null/empty for Assembly.Location; ignore.
        }

        return paths;
    }

    /// <summary>
    /// Reset the cached backend instances and probe reports. Intended for tests only.
    /// </summary>
    internal static void ResetCacheForTests()
    {
        lock (_gate)
        {
            _geometry = null;
            _packing = null;
            _lastGeometryProbe = null;
            _lastPackingProbe = null;
        }
    }

    private static IGeometryBackend ResolveGeometry(string preference)
    {
        var managed = new ManagedGeometryBackend();
        var native = ProbeNativeBackend<IGeometryBackend>(
            interfaceName: "geometry",
            defaultDllName: GeometryDllName,
            callerPreference: preference,
            out var report);
        var selected = native ?? managed;
        _lastGeometryProbe = new BackendProbeReport(
            interfaceName: report.InterfaceName,
            envOverride: report.EnvOverride,
            callerPreference: report.CallerPreference,
            searchPathsChecked: report.SearchPathsChecked,
            dllsExamined: report.DllsExamined,
            selectedBackendName: selected.Name);
        return selected;
    }

    private static IPackingBackend ResolvePacking(string preference)
    {
        var managed = new ManagedPackingBackend();
        var native = ProbeNativeBackend<IPackingBackend>(
            interfaceName: "packing",
            defaultDllName: PackingDllName,
            callerPreference: preference,
            out var report);
        var selected = native ?? managed;
        _lastPackingProbe = new BackendProbeReport(
            interfaceName: report.InterfaceName,
            envOverride: report.EnvOverride,
            callerPreference: report.CallerPreference,
            searchPathsChecked: report.SearchPathsChecked,
            dllsExamined: report.DllsExamined,
            selectedBackendName: selected.Name);
        return selected;
    }

    /// <summary>
    /// Walk the search paths, reflection-load the named DLL (or a caller-
    /// supplied path), look for a public type implementing <typeparamref name="T"/>
    /// with a public parameterless constructor, instantiate it, and return it.
    /// Returns null on any failure; caller is expected to fall back to the
    /// managed default. Always records what happened in <paramref name="report"/>.
    /// </summary>
    private static T ProbeNativeBackend<T>(
        string interfaceName,
        string defaultDllName,
        string callerPreference,
        out BackendProbeReport report)
        where T : class, IFrahanBackend
    {
        string env = Environment.GetEnvironmentVariable(EnvOverride) ?? string.Empty;
        string pref = callerPreference ?? string.Empty;

        // Caller preference takes precedence over env var.
        string effective = !string.IsNullOrWhiteSpace(pref) ? pref : env;

        var paths = GetSearchPaths();
        var dllReport = new List<BackendProbeEntry>();

        // Short-circuit: explicit "managed" forces fallback (no probe).
        if (string.Equals(effective, "managed", StringComparison.OrdinalIgnoreCase))
        {
            report = new BackendProbeReport(
                interfaceName: interfaceName,
                envOverride: env,
                callerPreference: pref,
                searchPathsChecked: paths,
                dllsExamined: dllReport,
                selectedBackendName: string.Empty);
            return null;
        }

        // Build the candidate DLL path list.
        bool effectiveIsDllPath = !string.IsNullOrWhiteSpace(effective)
            && effective.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

        var candidatePaths = new List<string>();
        if (effectiveIsDllPath)
        {
            // Caller asked for a specific DLL — probe only that.
            candidatePaths.Add(effective);
        }
        else
        {
            foreach (var (path, existed) in paths)
            {
                if (!existed) continue;
                string candidate;
                try { candidate = Path.Combine(path, defaultDllName); }
                catch { continue; }
                candidatePaths.Add(candidate);
            }
        }

        // If preference is something like "Geogram" (not a path, not "managed"),
        // we still probe the default-named DLL; the name filter applies after
        // instantiation.
        string nameFilter = (!effectiveIsDllPath
            && !string.IsNullOrWhiteSpace(effective)
            && !string.Equals(effective, "auto", StringComparison.OrdinalIgnoreCase))
            ? effective
            : null;

        foreach (string dllPath in candidatePaths)
        {
            if (!File.Exists(dllPath))
            {
                // Don't log every default search path that simply doesn't have
                // the DLL — only log if the caller asked for a specific path.
                if (effectiveIsDllPath)
                    dllReport.Add(new BackendProbeEntry(
                        dllPath, BackendProbeStatus.LoadFailed, "file not found"));
                continue;
            }

            Assembly asm;
            try
            {
                asm = Assembly.LoadFrom(dllPath);
            }
            catch (Exception ex)
            {
                dllReport.Add(new BackendProbeEntry(
                    dllPath, BackendProbeStatus.LoadFailed, ex.GetType().Name + ": " + ex.Message));
                continue;
            }

            Type[] exported;
            try { exported = asm.GetExportedTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                exported = ex.Types?.Where(t => t != null).ToArray() ?? Array.Empty<Type>();
            }
            catch (Exception ex)
            {
                dllReport.Add(new BackendProbeEntry(
                    dllPath, BackendProbeStatus.LoadFailed, "GetExportedTypes: " + ex.Message));
                continue;
            }

            T instantiated = null;
            foreach (Type t in exported)
            {
                if (t == null || t.IsAbstract || t.IsInterface) continue;
                if (!typeof(T).IsAssignableFrom(t)) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;

                T candidate;
                try
                {
                    candidate = (T)Activator.CreateInstance(t);
                }
                catch (Exception ex)
                {
                    dllReport.Add(new BackendProbeEntry(
                        dllPath, BackendProbeStatus.LoadFailed,
                        "ctor " + t.FullName + " threw " + ex.GetType().Name));
                    continue;
                }

                if (candidate == null) continue;

                if (!candidate.IsAvailable)
                {
                    dllReport.Add(new BackendProbeEntry(
                        dllPath, BackendProbeStatus.Skipped,
                        t.FullName + " IsAvailable=false"));
                    continue;
                }

                if (nameFilter != null
                    && (candidate.Name == null
                        || candidate.Name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    dllReport.Add(new BackendProbeEntry(
                        dllPath, BackendProbeStatus.Skipped,
                        $"name '{candidate.Name}' does not contain '{nameFilter}'"));
                    continue;
                }

                instantiated = candidate;
                dllReport.Add(new BackendProbeEntry(
                    dllPath, BackendProbeStatus.Instantiated,
                    t.FullName + " -> " + candidate.Name));
                break;
            }

            if (instantiated != null)
            {
                report = new BackendProbeReport(
                    interfaceName: interfaceName,
                    envOverride: env,
                    callerPreference: pref,
                    searchPathsChecked: paths,
                    dllsExamined: dllReport,
                    selectedBackendName: instantiated.Name);
                return instantiated;
            }

            // No type in this DLL matched.
            // Only add a NoMatchingType entry if no per-type entry was added above.
            if (!dllReport.Any(e => string.Equals(e.AssemblyPath, dllPath, StringComparison.OrdinalIgnoreCase)))
            {
                dllReport.Add(new BackendProbeEntry(
                    dllPath, BackendProbeStatus.NoMatchingType,
                    "no public type implements " + typeof(T).Name + " with a public parameterless ctor"));
            }
        }

        report = new BackendProbeReport(
            interfaceName: interfaceName,
            envOverride: env,
            callerPreference: pref,
            searchPathsChecked: paths,
            dllsExamined: dllReport,
            selectedBackendName: string.Empty);
        return null;
    }
}
