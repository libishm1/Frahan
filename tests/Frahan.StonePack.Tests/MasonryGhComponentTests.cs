using System;
using Frahan.GH.Masonry;
using Frahan.Masonry.Solvers;

namespace Frahan.Tests;

// Phase A.4 — smoke tests for the Masonry GH components (MasonryBlock,
// MasonryAssembly, MasonryStability(RBE)) and MasonrySolverRegistry.
//
// Tests 1-9 instantiate GH_Component subclasses; that triggers
// PostConstructor which calls RegisterInputParams/RegisterOutputParams.
// Both code paths require Grasshopper.dll, so the headless test runner
// will SKIP them via Program.cs IsNativeRhinoException (FileNotFoundException
// for Grasshopper / GH_IO / RhinoCommon).
//
// Test 10 is pure managed: MasonrySolverRegistry has no Rhino dependency,
// so it should PASS not SKIP.

static class MasonryGhComponentTests
{
    // -- MasonryBlockComponent ----------------------------------------------

    public static void MasonryBlockComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new MasonryBlockComponent();
        var expected = new Guid("D4F8A1B2-2C3D-4E5F-9A1B-3C4D5E6F7A8B");
        Assert(c.ComponentGuid == expected,
            $"MasonryBlockComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void MasonryBlockComponent_Metadata_IsCorrect()
    {
        var c = new MasonryBlockComponent();
        Assert(c.Name == "Masonry Block",
            $"Name should be 'Masonry Block', got '{c.Name}'");
        Assert(c.NickName == "MasBlk",
            $"NickName should be 'MasBlk', got '{c.NickName}'");
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan', got '{c.Category}'");
        Assert(c.SubCategory == "Masonry",
            $"SubCategory should be 'Masonry', got '{c.SubCategory}'");
    }

    public static void MasonryBlockComponent_HasExpectedInputAndOutputCount()
    {
        var c = new MasonryBlockComponent();
        Assert(c.Params.Input.Count == 3,
            $"MasonryBlockComponent input count should be 3, got {c.Params.Input.Count}");
        // Output[0] = Block (DTO), Output[1] = Id (text). Wiring the Id back
        // into Assembly.FixedBlockIds keeps block identity consistent.
        Assert(c.Params.Output.Count == 2,
            $"MasonryBlockComponent output count should be 2, got {c.Params.Output.Count}");
    }

    public static void MasonryBlockComponent_SecondOutputIsIdText()
    {
        var c = new MasonryBlockComponent();
        var second = c.Params.Output[1];
        Assert(second.Name == "Id",
            $"second output Name should be 'Id', got '{second.Name}'");
        Assert(second.NickName == "Id",
            $"second output NickName should be 'Id', got '{second.NickName}'");
    }

    // -- AssemblyPreviewComponent -------------------------------------------

    public static void AssemblyPreviewComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new AssemblyPreviewComponent();
        var expected = new Guid("12345678-9ABC-DEF0-1234-56789ABCDEF0");
        Assert(c.ComponentGuid == expected,
            $"AssemblyPreviewComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void AssemblyPreviewComponent_Metadata_IsCorrect()
    {
        var c = new AssemblyPreviewComponent();
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan', got '{c.Category}'");
        Assert(c.SubCategory == "Masonry",
            $"SubCategory should be 'Masonry', got '{c.SubCategory}'");
    }

    public static void AssemblyPreviewComponent_HasExpectedInputAndOutputCount()
    {
        var c = new AssemblyPreviewComponent();
        Assert(c.Params.Input.Count == 1,
            $"AssemblyPreviewComponent input count should be 1, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 8,
            $"AssemblyPreviewComponent output count should be 8, got {c.Params.Output.Count}");
    }

    // -- PickPlaceFramesComponent -------------------------------------------

    public static void PickPlaceFramesComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new PickPlaceFramesComponent();
        var expected = new Guid("456789AB-CDEF-0123-4567-89ABCDEF0123");
        Assert(c.ComponentGuid == expected,
            $"PickPlaceFramesComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void PickPlaceFramesComponent_Metadata_IsCorrect()
    {
        var c = new PickPlaceFramesComponent();
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan', got '{c.Category}'");
        Assert(c.SubCategory == "Masonry",
            $"SubCategory should be 'Masonry', got '{c.SubCategory}'");
    }

    public static void PickPlaceFramesComponent_HasExpectedInputAndOutputCount()
    {
        var c = new PickPlaceFramesComponent();
        Assert(c.Params.Input.Count == 5,
            $"PickPlaceFramesComponent input count should be 5, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 5,
            $"PickPlaceFramesComponent output count should be 5, got {c.Params.Output.Count}");
    }

    public static void PickPlaceFramesComponent_OptionalInputs()
    {
        var c = new PickPlaceFramesComponent();
        // [0] Place Transforms required; [1..4] all optional.
        Assert(!c.Params.Input[0].Optional, "Place Transforms must be required");
        Assert(c.Params.Input[1].Optional, "Pick Plane must be optional");
        Assert(c.Params.Input[2].Optional, "Approach Vector must be optional");
        Assert(c.Params.Input[3].Optional, "Approach Distance must be optional");
        Assert(c.Params.Input[4].Optional, "Retract Distance must be optional");
    }

    // -- BuildStepPreviewComponent ------------------------------------------

    public static void BuildStepPreviewComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new BuildStepPreviewComponent();
        var expected = new Guid("56789ABC-DEF0-1234-5678-9ABCDEF01234");
        Assert(c.ComponentGuid == expected,
            $"BuildStepPreviewComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void BuildStepPreviewComponent_Metadata_IsCorrect()
    {
        var c = new BuildStepPreviewComponent();
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan', got '{c.Category}'");
        Assert(c.SubCategory == "Masonry",
            $"SubCategory should be 'Masonry', got '{c.SubCategory}'");
    }

    public static void BuildStepPreviewComponent_HasExpectedInputAndOutputCount()
    {
        var c = new BuildStepPreviewComponent();
        Assert(c.Params.Input.Count == 2,
            $"BuildStepPreviewComponent input count should be 2, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 4,
            $"BuildStepPreviewComponent output count should be 4, got {c.Params.Output.Count}");
    }

    // -- BuildSequenceJsonComponent -----------------------------------------

    public static void BuildSequenceJsonComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new BuildSequenceJsonComponent();
        var expected = new Guid("6789ABCD-EF01-2345-6789-ABCDEF012345");
        Assert(c.ComponentGuid == expected,
            $"BuildSequenceJsonComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void BuildSequenceJsonComponent_Metadata_IsCorrect()
    {
        var c = new BuildSequenceJsonComponent();
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan', got '{c.Category}'");
        Assert(c.SubCategory == "Masonry",
            $"SubCategory should be 'Masonry', got '{c.SubCategory}'");
    }

    public static void BuildSequenceJsonComponent_HasExpectedInputAndOutputCount()
    {
        var c = new BuildSequenceJsonComponent();
        Assert(c.Params.Input.Count == 4,
            $"BuildSequenceJsonComponent input count should be 4, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 1,
            $"BuildSequenceJsonComponent output count should be 1, got {c.Params.Output.Count}");
    }

    // -- ReadPlyMeshComponent -----------------------------------------------

    public static void ReadPlyMeshComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new ReadPlyMeshComponent();
        var expected = new Guid("789ABCDE-F012-3456-789A-BCDEF0123456");
        Assert(c.ComponentGuid == expected,
            $"ReadPlyMeshComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void ReadPlyMeshComponent_Metadata_IsCorrect()
    {
        // Phase F moved this Masonry → Mesh (scan-prep tool); the 2026-07-07
        // ribbon pass (blueprint #9) moved the whole scan/cloud loader family
        // Mesh → Ingest so users find loaders in ONE place. GUID preserved
        // per AGENTS.md §8 throughout.
        var c = new ReadPlyMeshComponent();
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan', got '{c.Category}'");
        Assert(c.SubCategory == "Ingest",
            $"SubCategory should be 'Ingest' after the #9 ribbon pass, got '{c.SubCategory}'");
    }

    public static void ReadPlyMeshComponent_HasExpectedInputAndOutputCount()
    {
        // Phase F1+F2 rescue-extend (UX architecture report §9.14, 2026-05-19):
        // - inputs grew from 1 (File Path) to 2 (File Path + Format enum)
        // - outputs grew from 3 (Mesh, V, T) to 5 (Meshes[], Names[], V[], T[],
        //   Detected Format) to support multi-group OBJ files.
        var c = new ReadPlyMeshComponent();
        Assert(c.Params.Input.Count == 2,
            $"ReadPlyMeshComponent input count should be 2 after Phase F, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 5,
            $"ReadPlyMeshComponent output count should be 5 after Phase F, got {c.Params.Output.Count}");
    }

    // -- MatchBlockTransformComponent ---------------------------------------

    public static void MatchBlockTransformComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new MatchBlockTransformComponent();
        var expected = new Guid("89ABCDEF-0123-4567-89AB-CDEF01234567");
        Assert(c.ComponentGuid == expected,
            $"MatchBlockTransformComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void MatchBlockTransformComponent_Metadata_IsCorrect()
    {
        var c = new MatchBlockTransformComponent();
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan', got '{c.Category}'");
        Assert(c.SubCategory == "Masonry",
            $"SubCategory should be 'Masonry', got '{c.SubCategory}'");
    }

    public static void MatchBlockTransformComponent_HasExpectedInputAndOutputCount()
    {
        var c = new MatchBlockTransformComponent();
        Assert(c.Params.Input.Count == 3,
            $"MatchBlockTransformComponent input count should be 3, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 4,
            $"MatchBlockTransformComponent output count should be 4, got {c.Params.Output.Count}");
    }

    // -- MasonryAssemblyComponent -------------------------------------------

    public static void MasonryAssemblyComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new MasonryAssemblyComponent();
        var expected = new Guid("E5A9B2C3-3D4E-4F60-AB2C-4D5E6F7A8B9C");
        Assert(c.ComponentGuid == expected,
            $"MasonryAssemblyComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void MasonryAssemblyComponent_Metadata_IsCorrect()
    {
        var c = new MasonryAssemblyComponent();
        Assert(c.Name == "Masonry Assembly",
            $"Name should be 'Masonry Assembly', got '{c.Name}'");
        Assert(c.NickName == "MasAsm",
            $"NickName should be 'MasAsm', got '{c.NickName}'");
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan', got '{c.Category}'");
        Assert(c.SubCategory == "Masonry",
            $"SubCategory should be 'Masonry', got '{c.SubCategory}'");
    }

    public static void MasonryAssemblyComponent_HasExpectedInputAndOutputCount()
    {
        var c = new MasonryAssemblyComponent();
        Assert(c.Params.Input.Count == 3,
            $"MasonryAssemblyComponent input count should be 3, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 1,
            $"MasonryAssemblyComponent output count should be 1, got {c.Params.Output.Count}");
    }

    // -- MasonryStabilityRbeComponent ---------------------------------------

    public static void MasonryStabilityRbeComponent_ComponentGuid_IsExpectedValue()
    {
        var c = new MasonryStabilityRbeComponent();
        var expected = new Guid("F6BAC3D4-4E5F-4071-BC3D-5E6F7A8B9CAD");
        Assert(c.ComponentGuid == expected,
            $"MasonryStabilityRbeComponent ComponentGuid should be {expected}, got {c.ComponentGuid}");
    }

    public static void MasonryStabilityRbeComponent_Metadata_IsCorrect()
    {
        var c = new MasonryStabilityRbeComponent();
        Assert(c.Name == "Masonry Stability (RBE)",
            $"Name should be 'Masonry Stability (RBE)', got '{c.Name}'");
        Assert(c.NickName == "MasRBE",
            $"NickName should be 'MasRBE', got '{c.NickName}'");
        Assert(c.Category == "Frahan",
            $"Category should be 'Frahan', got '{c.Category}'");
        Assert(c.SubCategory == "Masonry",
            $"SubCategory should be 'Masonry', got '{c.SubCategory}'");
    }

    public static void MasonryStabilityRbeComponent_HasExpectedInputAndOutputCount()
    {
        var c = new MasonryStabilityRbeComponent();
        Assert(c.Params.Input.Count == 5,
            $"MasonryStabilityRbeComponent input count should be 5, got {c.Params.Input.Count}");
        Assert(c.Params.Output.Count == 5,
            $"MasonryStabilityRbeComponent output count should be 5, got {c.Params.Output.Count}");
    }

    // -- MasonrySolverRegistry (pure managed; should PASS, not SKIP) --------

    public static void MasonrySolverRegistry_DefaultIsNullByDefault()
    {
        // The registry is process-wide static state, so other tests (or a
        // future plugin OnLoad) might assign it. To keep this smoke test
        // self-contained we snapshot, force null, assert, then restore.
        var snapshot = MasonrySolverRegistry.Default;
        try
        {
            MasonrySolverRegistry.Default = null;
            Assert(MasonrySolverRegistry.Default == null,
                "MasonrySolverRegistry.Default should return null when nothing is registered.");
        }
        finally
        {
            MasonrySolverRegistry.Default = snapshot;
        }
    }

    public static void MasonrySolverRegistry_EnsureDefaultSolver_AssignsManagedSolverWhenEmpty()
    {
        // When Default is null (the no-solver-registered baseline state),
        // EnsureDefaultSolver assigns a fresh ManagedQpSolver and reports true.
        var snapshot = MasonrySolverRegistry.Default;
        try
        {
            MasonrySolverRegistry.Default = null;
            bool assigned = MasonrySolverRegistry.EnsureDefaultSolver();
            Assert(assigned, "EnsureDefaultSolver should report true when it newly registers a solver");
            Assert(MasonrySolverRegistry.Default is ManagedQpSolver,
                $"expected ManagedQpSolver, got {MasonrySolverRegistry.Default?.GetType().FullName ?? "null"}");
        }
        finally
        {
            MasonrySolverRegistry.Default = snapshot;
        }
    }

    public static void MasonrySolverRegistry_EnsureDefaultSolver_PreservesExistingSolver()
    {
        // EnsureDefaultSolver must NOT overwrite an already-registered solver
        // (so external init code or tests can pre-register a different solver
        // and expect it to survive plugin OnLoad).
        var snapshot = MasonrySolverRegistry.Default;
        try
        {
            var sentinel = new ManagedQpSolver(tolerance: 1e-3, maxIterations: 7);
            MasonrySolverRegistry.Default = sentinel;
            bool assigned = MasonrySolverRegistry.EnsureDefaultSolver();
            Assert(!assigned, "EnsureDefaultSolver should report false when a solver is already registered");
            Assert(ReferenceEquals(MasonrySolverRegistry.Default, sentinel),
                "EnsureDefaultSolver must not overwrite an existing solver instance");
        }
        finally
        {
            MasonrySolverRegistry.Default = snapshot;
        }
    }

    public static void MasonrySolverRegistry_EnsureDefaultSolver_IsIdempotent()
    {
        // Calling EnsureDefaultSolver twice is safe; second call is a no-op
        // and the same instance is returned both times.
        var snapshot = MasonrySolverRegistry.Default;
        try
        {
            MasonrySolverRegistry.Default = null;
            bool first = MasonrySolverRegistry.EnsureDefaultSolver();
            var afterFirst = MasonrySolverRegistry.Default;
            bool second = MasonrySolverRegistry.EnsureDefaultSolver();
            var afterSecond = MasonrySolverRegistry.Default;
            Assert(first, "first EnsureDefaultSolver call should report true");
            Assert(!second, "second EnsureDefaultSolver call should report false (no-op)");
            Assert(ReferenceEquals(afterFirst, afterSecond),
                "second EnsureDefaultSolver call must not replace the instance from the first");
        }
        finally
        {
            MasonrySolverRegistry.Default = snapshot;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
