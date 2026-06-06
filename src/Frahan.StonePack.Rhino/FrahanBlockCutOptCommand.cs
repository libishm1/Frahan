#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Quarry.BlockCutOpt;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace Frahan.Rhino;

// Command-line entry point for BlockCutOpt. Pipeline:
//   1. user picks one Mesh as the tested-area host (its AABB = the tested area)
//   2. user picks one or more Meshes as fracture geometry
//   3. user provides block size (X, Y, Z, mm) and kerf
//   4. command runs BlockCutOptSolver.Solve over psi search, prints result
//
// Phase 1 scope: psi-only search (theta = phi = 0), single tested area, single
// block size. Matches Core's BlockCutOptOptions Phase-1 constructor. Recovery
// percentage uses Elkarmoty Eq. 7-1.

public sealed class FrahanBlockCutOptCommand : Command
{
    public override string EnglishName => "FrahanBlockCutOpt";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var testedMesh = PickSingleMesh("Select tested-area mesh (its AABB defines the tested area)");
        if (testedMesh == null) return Result.Cancel;

        var fractureMeshes = PickMultipleMeshes("Select fracture meshes (Enter when done)");
        if (fractureMeshes == null || fractureMeshes.Count == 0) return Result.Cancel;

        if (!PromptDouble("BlockSizeX (mm)", 500.0, 1.0, 1e6, out double blockX)) return Result.Cancel;
        if (!PromptDouble("BlockSizeY (mm)", 500.0, 1.0, 1e6, out double blockY)) return Result.Cancel;
        if (!PromptDouble("BlockSizeZ (mm)", 500.0, 1.0, 1e6, out double blockZ)) return Result.Cancel;
        if (!PromptDouble("Kerf (mm)", 5.0, 0.0, 1e4, out double kerf)) return Result.Cancel;
        if (!PromptDouble("PsiStepDeg", 5.0, 0.1, 90.0, out double psiStepDeg)) return Result.Cancel;

        var testedArea = FrahanCommandInterop.BoxToBbox(testedMesh.GetBoundingBox(true));
        var fractures = FrahanCommandInterop.CombineMeshesToPly(fractureMeshes);

        double psiStepRad = psiStepDeg * Math.PI / 180.0;
        double dxMax = blockX;
        double dyMax = blockY;
        double dxStep = blockX * 0.1;
        double dyStep = blockY * 0.1;
        var options = new BlockCutOptOptions(
            blockX, blockY, blockZ,
            kerf,
            psiStartRad: 0.0,
            psiStopRad: Math.PI / 2.0,
            psiStepRad: psiStepRad,
            dxMax: dxMax,
            dxStep: dxStep,
            dyMax: dyMax,
            dyStep: dyStep);

        RhinoApp.WriteLine($"FrahanBlockCutOpt: solving over psi=[0..90 deg, step {psiStepDeg}], block=({blockX}x{blockY}x{blockZ}) mm, kerf={kerf} mm");
        BlockCutOptResult result;
        try
        {
            result = BlockCutOptSolver.Solve(testedArea, fractures, options);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"FrahanBlockCutOpt FAILED: {ex.Message}");
            return Result.Failure;
        }

        RhinoApp.WriteLine($"FrahanBlockCutOpt: {result}");
        RhinoApp.WriteLine($"  non-intersected blocks: {result.NonIntersectedCount}");
        RhinoApp.WriteLine($"  recovery: {result.RecoveryPercent:0.00}%");
        RhinoApp.WriteLine($"  optimal psi: {result.BestPsiDeg:0.0} deg");
        RhinoApp.WriteLine($"  optimal dx, dy: ({result.BestDx:0.00}, {result.BestDy:0.00}) mm");
        RhinoApp.WriteLine($"  evaluations: {result.TotalEvaluations}");
        RhinoApp.WriteLine($"  elapsed: {result.Elapsed.TotalMilliseconds:0} ms");
        return Result.Success;
    }

    private static Mesh PickSingleMesh(string prompt)
    {
        var go = new GetObject();
        go.SetCommandPrompt(prompt);
        go.GeometryFilter = ObjectType.Mesh;
        go.SubObjectSelect = false;
        go.AcceptNothing(false);
        if (go.Get() != GetResult.Object) return null;
        return go.Object(0).Mesh();
    }

    private static List<Mesh> PickMultipleMeshes(string prompt)
    {
        var go = new GetObject();
        go.SetCommandPrompt(prompt);
        go.GeometryFilter = ObjectType.Mesh;
        go.SubObjectSelect = false;
        go.GroupSelect = true;
        go.AcceptNothing(true);
        var meshes = new List<Mesh>();
        while (true)
        {
            var r = go.GetMultiple(1, 0);
            if (r == GetResult.Nothing && meshes.Count > 0) break;
            if (r != GetResult.Object) return null;
            for (int i = 0; i < go.ObjectCount; i++)
            {
                var m = go.Object(i).Mesh();
                if (m != null) meshes.Add(m);
            }
            break;
        }
        return meshes;
    }

    private static bool PromptDouble(string prompt, double defaultValue, double lower, double upper, out double value)
    {
        var gn = new GetNumber();
        gn.SetCommandPrompt(prompt);
        gn.SetDefaultNumber(defaultValue);
        gn.SetLowerLimit(lower, false);
        gn.SetUpperLimit(upper, false);
        if (gn.Get() != GetResult.Number)
        {
            value = defaultValue;
            return false;
        }
        value = gn.Number();
        return true;
    }
}
