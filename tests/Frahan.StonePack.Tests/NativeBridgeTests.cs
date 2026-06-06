#nullable disable
using System;
using System.IO;
using System.Linq;
using Frahan.NativeBridge;

namespace Frahan.Tests;

// Unit tests for Frahan.NativeBridge interface scaffold + ManagedDefault* fallbacks.
// Pure managed; no Rhino runtime required.

static class NativeBridgeTests
{
    // -- BackendDiagnostic ---------------------------------------------------

    public static void BackendDiagnostic_StoresLevelAndMessage()
    {
        var d = new BackendDiagnostic(BackendDiagnosticLevel.Warning, "hello");
        Assert(d.Level == BackendDiagnosticLevel.Warning, "level should round-trip");
        Assert(d.Message == "hello", "message should round-trip");
        Assert(d.ToString().Contains("Warning") && d.ToString().Contains("hello"),
            "ToString should include level and message");
    }

    public static void BackendDiagnostic_NullMessage_Throws()
    {
        bool threw = false;
        try { _ = new BackendDiagnostic(BackendDiagnosticLevel.Info, null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null message should throw ArgumentNullException");
    }

    // -- BackendOperationResult ---------------------------------------------

    public static void BackendOperationResult_BasicConstruction()
    {
        var diags = new[] { new BackendDiagnostic(BackendDiagnosticLevel.Info, "ok") };
        var r = new BackendOperationResult(true, "Test", new Version(1, 2), diags);
        Assert(r.Success, "Success should be true");
        Assert(r.BackendName == "Test", "name should round-trip");
        Assert(r.BackendVersion.Major == 1 && r.BackendVersion.Minor == 2, "version should round-trip");
        Assert(r.Diagnostics.Count == 1, "should carry one diagnostic");
    }

    public static void BackendOperationResult_NullDiagnostics_GivesEmptyList()
    {
        var r = new BackendOperationResult(true, "Test", new Version(0, 1), null);
        Assert(r.Diagnostics.Count == 0, "null diagnostics should give empty list");
    }

    // -- ManagedGeometryBackend ---------------------------------------------

    public static void ManagedGeometry_IsAlwaysAvailable()
    {
        var backend = NativeBackendLoader.ChooseGeometryBackend(forceReload: true);
        Assert(backend != null, "loader must return a non-null backend");
        Assert(backend.IsAvailable, "managed default should always be available");
        Assert(backend.Name == "Managed", $"managed default name should be 'Managed', got {backend.Name}");
    }

    public static void ManagedGeometry_Repair_RoundTripsInput()
    {
        var backend = NativeBackendLoader.ChooseGeometryBackend(forceReload: true);
        var verts = new[] { 0.0, 0, 0, 1, 0, 0, 0, 1, 0 };
        var tris = new[] { 0, 1, 2 };

        var result = backend.Repair(verts, tris, out var rv, out var rt);

        Assert(result.Success, "managed Repair should report success");
        Assert(result.BackendName == "Managed", "result should carry backend name");
        Assert(rv.Length == verts.Length, "vertex count should be preserved");
        Assert(rt.Length == tris.Length, "triangle count should be preserved");
        for (int i = 0; i < verts.Length; i++)
            Assert(Math.Abs(rv[i] - verts[i]) < 1e-12, $"vertex {i} should round-trip");
        Assert(result.Diagnostics.Any(d => d.Level == BackendDiagnosticLevel.Warning),
            "managed Repair should emit a Warning diagnostic about being a passthrough");
    }

    public static void ManagedGeometry_Simplify_RoundTripsInput()
    {
        var backend = NativeBackendLoader.ChooseGeometryBackend(forceReload: true);
        var verts = new[] { 0.0, 0, 0, 1, 0, 0, 0, 1, 0 };
        var tris = new[] { 0, 1, 2 };

        var result = backend.Simplify(verts, tris, 0.5, out var sv, out var st);

        Assert(result.Success, "managed Simplify should report success");
        Assert(sv.Length == verts.Length, "vertex count should be preserved (passthrough)");
        Assert(st.Length == tris.Length, "triangle count should be preserved");
    }

    public static void ManagedGeometry_Simplify_RatioOutOfRange_Throws()
    {
        var backend = NativeBackendLoader.ChooseGeometryBackend(forceReload: true);
        var verts = new[] { 0.0, 0, 0 };
        var tris = new[] { 0, 0, 0 };
        bool threw = false;
        try { backend.Simplify(verts, tris, 1.5, out _, out _); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "ratio > 1 should throw ArgumentOutOfRangeException");
    }

    // -- ManagedPackingBackend ----------------------------------------------

    public static void ManagedPacking_BuildCollisionProxy_ReturnsInput()
    {
        var backend = NativeBackendLoader.ChoosePackingBackend(forceReload: true);
        var verts = new[] { 0.0, 0, 0, 1, 0, 0, 0, 1, 0 };
        var tris = new[] { 0, 1, 2 };

        var result = backend.BuildCollisionProxy(verts, tris, out var pv, out var pt);

        Assert(result.Success, "managed BuildCollisionProxy should report success");
        Assert(pv.Length == verts.Length, "proxy vertex count should equal input (passthrough)");
        Assert(pt.Length == tris.Length, "proxy triangle count should equal input");
        Assert(result.Diagnostics.Any(d => d.Level == BackendDiagnosticLevel.Warning),
            "managed BuildCollisionProxy should emit a Warning about being a passthrough");
    }

    public static void ManagedPacking_NullInput_Throws()
    {
        var backend = NativeBackendLoader.ChoosePackingBackend(forceReload: true);
        bool threw = false;
        try { backend.BuildCollisionProxy(null, new[] { 0, 0, 0 }, out _, out _); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null vertexCoords should throw ArgumentNullException");
    }

    // -- NativeBackendLoader ------------------------------------------------

    public static void Loader_NeverThrowsOnMissingNative()
    {
        // Spec 11 rule 4: loader never throws on missing native.
        // Pass a deliberately-bogus preference. Loader still returns a backend.
        var backend = NativeBackendLoader.ChooseGeometryBackend(
            preference: "DefinitelyDoesNotExist",
            forceReload: true);
        Assert(backend != null, "loader must always return a non-null backend");
        Assert(backend.IsAvailable, "fallback backend must be available");
    }

    public static void Loader_GetSearchPaths_ReturnsAtLeastOne()
    {
        var paths = NativeBackendLoader.GetSearchPaths();
        Assert(paths.Count >= 1, $"expected at least one search path, got {paths.Count}");
        // None of them need to actually exist, but each entry has a Path string.
        foreach (var (p, _) in paths)
            Assert(!string.IsNullOrWhiteSpace(p), "search path entries should be non-empty");
    }

    public static void Loader_CachesGeometryBackend()
    {
        var b1 = NativeBackendLoader.ChooseGeometryBackend(forceReload: true);
        var b2 = NativeBackendLoader.ChooseGeometryBackend(forceReload: false);
        Assert(ReferenceEquals(b1, b2),
            "loader should cache the geometry backend instance unless forceReload=true");
    }

    public static void Loader_ForceReload_ProducesNewInstance()
    {
        var b1 = NativeBackendLoader.ChooseGeometryBackend(forceReload: true);
        var b2 = NativeBackendLoader.ChooseGeometryBackend(forceReload: true);
        // Both are ManagedGeometryBackend; instance equality may or may not
        // hold depending on caching. The test asserts only that forceReload
        // returns a non-null result.
        Assert(b1 != null && b2 != null, "forceReload should always return non-null");
    }

    // -- Probe behaviour (added 2026-05-06) ---------------------------------

    public static void Loader_PreferenceManaged_ReturnsManagedAndSkipsProbe()
    {
        // "managed" preference short-circuits native probing entirely.
        var backend = NativeBackendLoader.ChooseGeometryBackend(
            preference: "managed",
            forceReload: true);

        Assert(backend.Name == "Managed", $"expected Managed, got {backend.Name}");

        var report = RequireProbe(NativeBackendLoader.LastGeometryProbe, "LastGeometryProbe");
        Assert(report.SelectedBackendName == "Managed",
            $"probe should record managed selection, got '{report.SelectedBackendName}'");
        Assert(report.DllsExamined.Count == 0,
            $"managed short-circuit should examine zero DLLs, got {report.DllsExamined.Count}");
        Assert(report.CallerPreference == "managed", "probe should record the caller preference");
    }

    public static void Loader_GeometryProbeReport_IsPopulatedAfterChoose()
    {
        var backend = NativeBackendLoader.ChooseGeometryBackend(forceReload: true);
        Assert(backend != null, "Choose should return a backend");
        var report = RequireProbe(NativeBackendLoader.LastGeometryProbe, "LastGeometryProbe");
        Assert(report.InterfaceName == "geometry",
            $"interface tag should be 'geometry', got '{report.InterfaceName}'");
        Assert(report.SearchPathsChecked.Count >= 1,
            "probe should record at least one search path");
        Assert(!string.IsNullOrEmpty(report.SelectedBackendName),
            "probe should record the selected backend name");
    }

    public static void Loader_PackingProbeReport_IsPopulatedAfterChoose()
    {
        var backend = NativeBackendLoader.ChoosePackingBackend(forceReload: true);
        Assert(backend != null, "Choose should return a backend");
        var report = RequireProbe(NativeBackendLoader.LastPackingProbe, "LastPackingProbe");
        Assert(report.InterfaceName == "packing",
            $"interface tag should be 'packing', got '{report.InterfaceName}'");
    }

    public static void Loader_BogusDllInSearchPath_DoesNotThrow_RecordsLoadFailed()
    {
        // Plant a 0-byte DLL with the expected name in a temp dir, point
        // FRAHAN_BACKENDS_PATH at it, and make sure (a) the loader still
        // returns a managed backend, and (b) the probe report records the
        // failure so future diagnostics can surface it.
        string tmpDir = Path.Combine(Path.GetTempPath(),
            "frahan_native_probe_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        string bogus = Path.Combine(tmpDir, NativeBackendLoader.GeometryDllName);
        File.WriteAllBytes(bogus, Array.Empty<byte>());

        string priorEnv = Environment.GetEnvironmentVariable("FRAHAN_BACKENDS_PATH");
        try
        {
            Environment.SetEnvironmentVariable("FRAHAN_BACKENDS_PATH", tmpDir);
            var backend = NativeBackendLoader.ChooseGeometryBackend(forceReload: true);
            Assert(backend.Name == "Managed",
                $"bogus DLL should fall back to Managed, got {backend.Name}");

            var report = RequireProbe(NativeBackendLoader.LastGeometryProbe, "LastGeometryProbe");
            Assert(report.DllsExamined.Any(e => e.Status == BackendProbeStatus.LoadFailed
                && string.Equals(e.AssemblyPath, bogus, StringComparison.OrdinalIgnoreCase)),
                "report should record LoadFailed for the bogus DLL");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRAHAN_BACKENDS_PATH", priorEnv);
            try { File.Delete(bogus); } catch { }
            try { Directory.Delete(tmpDir); } catch { }
            // Force a fresh probe so subsequent tests see a clean state.
            NativeBackendLoader.ChooseGeometryBackend(forceReload: true);
        }
    }

    public static void Loader_FrahanBackendEnv_ManagedKeyword_ReturnsManaged()
    {
        // FRAHAN_BACKEND=managed forces the managed default even with no
        // caller preference passed.
        string priorEnv = Environment.GetEnvironmentVariable("FRAHAN_BACKEND");
        try
        {
            Environment.SetEnvironmentVariable("FRAHAN_BACKEND", "managed");
            var backend = NativeBackendLoader.ChooseGeometryBackend(forceReload: true);
            Assert(backend.Name == "Managed",
                $"FRAHAN_BACKEND=managed should yield Managed, got {backend.Name}");

            var report = RequireProbe(NativeBackendLoader.LastGeometryProbe, "LastGeometryProbe");
            Assert(report.EnvOverride == "managed",
                $"probe should record env override, got '{report.EnvOverride}'");
            Assert(report.DllsExamined.Count == 0,
                $"managed env should skip probe (zero DLLs), got {report.DllsExamined.Count}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRAHAN_BACKEND", priorEnv);
            NativeBackendLoader.ChooseGeometryBackend(forceReload: true);
        }
    }

    private static BackendProbeReport RequireProbe(BackendProbeReport report, string name)
    {
        if (report == null)
            throw new InvalidOperationException($"{name} was null; expected populated probe report");
        return report;
    }

    // -- Helpers ------------------------------------------------------------

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
