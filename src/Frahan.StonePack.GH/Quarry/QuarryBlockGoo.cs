#nullable disable
using System;
using Frahan.Core.Quarry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Quarry;

// =============================================================================
// QuarryBlockGoo — IGH_Goo wrapper that flows a QuarryBlock value through GH
// wires. Pairs with Param_QuarryBlock so downstream components can declare
// the type as their input parameter.
//
// Cast policy: a raw Mesh source is auto-converted into a minimal QuarryBlock
// (Bounds = UsableVolume = mesh, Frame = world XY at mesh bbox centre,
// Dimensions = bbox extents, Volume = bbox product). This is the raw-Mesh
// fallback wire path agreed in HITL #2 of v1_consolidated_plan.md §7.
//
// Param GUID: F2D0BC21-1A2B-4F2D-A0B0-7E60CADA20A1 (next after the
// ScanToBlockInventoryComponent GUID F2D0BC20-…).
// =============================================================================

public sealed class QuarryBlockGoo : GH_Goo<QuarryBlock>
{
    public QuarryBlockGoo() { Value = null; }

    public QuarryBlockGoo(QuarryBlock value) : base(value) { }

    public override bool IsValid =>
        Value != null && Value.Bounds != null && Value.Bounds.IsValid;

    public override string TypeName => "QuarryBlock";

    public override string TypeDescription =>
        "A scanned quarry block: bounds + usable volume + frame + dimensions + label.";

    public override IGH_Goo Duplicate()
    {
        // Shallow duplicate. Meshes are reference types but we are flowing
        // them, not mutating them; downstream consumers only read.
        return new QuarryBlockGoo(Value);
    }

    public override string ToString()
    {
        if (Value == null) return "QuarryBlock (null)";
        if (!string.IsNullOrEmpty(Value.Label)) return Value.Label;
        return "QuarryBlock";
    }

    public override bool CastFrom(object source)
    {
        if (source == null) return false;

        // Direct: QuarryBlock value.
        if (source is QuarryBlock qb)
        {
            Value = qb;
            return true;
        }

        // Through another QuarryBlockGoo (e.g. from a wire conversion).
        if (source is QuarryBlockGoo gooFromGoo)
        {
            Value = gooFromGoo.Value;
            return true;
        }

        // Unwrap a GH_Mesh wrapper.
        if (source is GH_Mesh ghMesh && ghMesh.Value != null)
        {
            return CastFromMesh(ghMesh.Value);
        }

        // Raw Mesh fallback (HITL #2 — raw-Mesh wire path).
        if (source is Mesh mesh)
        {
            return CastFromMesh(mesh);
        }

        return false;
    }

    private bool CastFromMesh(Mesh mesh)
    {
        if (mesh == null || !mesh.IsValid) return false;
        var bbox = mesh.GetBoundingBox(true);
        if (!bbox.IsValid) return false;

        var dims = new Vector3d(
            bbox.Max.X - bbox.Min.X,
            bbox.Max.Y - bbox.Min.Y,
            bbox.Max.Z - bbox.Min.Z);
        var centre = (bbox.Min + bbox.Max) * 0.5;
        var frame = new Plane(centre, Vector3d.XAxis, Vector3d.YAxis);
        double vol = dims.X * dims.Y * dims.Z;

        Value = new QuarryBlock(mesh, mesh, frame, dims, vol, "", "RawMesh");
        return true;
    }
}

// -----------------------------------------------------------------------------
// Param_QuarryBlock — persistent parameter so downstream components can use
// QuarryBlock as an input type via pManager.AddParameter(new Param_QuarryBlock(), ...).
// -----------------------------------------------------------------------------
public sealed class Param_QuarryBlock : GH_PersistentParam<QuarryBlockGoo>
{
    public Param_QuarryBlock()
        : base("QuarryBlock", "QB",
            "A scanned quarry block: bounds + usable volume + frame + dimensions + label.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D0BC21-1A2B-4F2D-A0B0-7E60CADA20A1");
        protected override System.Drawing.Bitmap Icon => Frahan.GH.IconProvider.Load("BlockCutOpt.png");

    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override GH_GetterResult Prompt_Singular(ref QuarryBlockGoo value)
    {
        value = new QuarryBlockGoo();
        return GH_GetterResult.success;
    }

    protected override GH_GetterResult Prompt_Plural(ref System.Collections.Generic.List<QuarryBlockGoo> values)
    {
        values = new System.Collections.Generic.List<QuarryBlockGoo>();
        return GH_GetterResult.success;
    }
}
