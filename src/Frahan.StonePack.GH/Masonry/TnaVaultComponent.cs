#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Frahan.GH;
using Frahan.Masonry.Tna;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.StonePack.GH.Masonry
{
    // =========================================================================
    // TNA Vault (Gaudí) — form-finds a compression-only funicular vault surface
    // using the force-density method (Block & Ochsendorf 2007 TNA).
    //
    // Generates a barrel-vault grid form diagram, calls the native frahan_tna.dll
    // solver (Eigen SimplicialLDLT), and outputs the funicular mesh + force data.
    //
    // Park Güell usage: left boundary = retaining wall (high z), right boundary
    // = inclined column tops (lower z).  The asymmetric supports tilt the
    // funicular automatically — no manual adjustment needed.
    //
    // The output mesh is the structural template for downstream rubble packing
    // (RubblePack) and CRA block-wise validation (Masonry Stability Check).
    //
    // Algorithm: Block & Ochsendorf 2007, TNA §3; force-density height solve.
    // =========================================================================
    public sealed class TnaVaultComponent : FrahanComponentBase
    {
        public TnaVaultComponent()
            : base("TNA Vault (Gaudí)", "TNAVault",
                "Form-find a compression-only funicular vault surface using Thrust Network " +
                "Analysis (Block & Ochsendorf 2007). Generates a barrel-vault form diagram, " +
                "solves the force-density height system D_nn·z = p − D_nf·z_fixed via a " +
                "sparse Cholesky factorisation (Eigen SimplicialLDLT), and returns the " +
                "funicular mesh + branch forces. Left boundary = retaining wall; right " +
                "boundary = column tops. Asymmetric supports produce the Park Güell lean " +
                "automatically. Use as structural template for RubblePack + Stability Check.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("D5F10044-0BA0-4ED9-A044-0BA00BA00044");
        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override Bitmap Icon => IconProvider.Load("TnaVault.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddNumberParameter("Span",        "W",  "Vault width — arch direction (m).",                  GH_ParamAccess.item, 4.0);
            p.AddNumberParameter("Length",      "L",  "Vault length — tunnel direction (m).",               GH_ParamAccess.item, 12.0);
            p.AddNumberParameter("Z Left",      "Zl", "Retaining wall top height, left boundary (m).",      GH_ParamAccess.item, 3.5);
            p.AddNumberParameter("Z Right",     "Zr", "Column top height, right boundary (m).",             GH_ParamAccess.item, 0.0);
            p.AddIntegerParameter("Nodes Span", "Ny", "Grid resolution across span (min 3).",               GH_ParamAccess.item, 9);
            p.AddIntegerParameter("Nodes Len",  "Nx", "Grid resolution along length (min 2).",              GH_ParamAccess.item, 6);
            p.AddNumberParameter("Force Density","Q", "Uniform force density q (kN/m). Higher = flatter arch, lower = more curvature. " +
                "Scale to match loads: typical 50–300 kN/m for granite rubble vaults. " +
                "Default 120 kN/m gives ~1.5m rise on a 4m span with 26 kN/m³ granite.",GH_ParamAccess.item, 120.0);
            p.AddNumberParameter("Unit Weight", "Uw", "Stone unit weight (kN/m³). Granite ≈ 25–27.",        GH_ParamAccess.item, 26.0);
            p.AddNumberParameter("Thickness",   "T",  "Vault mean thickness (m).",                          GH_ParamAccess.item, 0.35);
            p.AddNumberParameter("K_a × γ",     "Ka", "Rankine K_a × soil unit weight (kN/m³) for left-wall earth pressure. 0 = omit.", GH_ParamAccess.item, 0.0);
            p.AddBooleanParameter("Run",        "R",  "Execute the TNA solve.",                             GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter(   "Vault",    "V",  "Funicular vault surface mesh.",                    GH_ParamAccess.item);
            p.AddLineParameter(   "Branches", "B",  "Form diagram edges coloured by horizontal thrust.", GH_ParamAccess.list);
            p.AddNumberParameter( "Thrust",   "H",  "Horizontal thrust per branch (kN).",               GH_ParamAccess.list);
            p.AddVectorParameter( "Reactions","Rx",  "Support reactions at boundary nodes (kN, XYZ).",  GH_ParamAccess.list);
            p.AddPointParameter(  "BoundPts", "Bp",  "Boundary node positions (world XYZ).",            GH_ParamAccess.list);
            p.AddTextParameter(   "Report",   "Rp",  "Solve summary.",                                  GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            double span = 4.0, length = 12.0, zLeft = 3.5, zRight = 0.0;
            int ny = 9, nx = 6;
            double q = 120.0, uw = 26.0, thick = 0.35, kaGamma = 0.0;
            bool run = false;

            da.GetData(0,  ref span);
            da.GetData(1,  ref length);
            da.GetData(2,  ref zLeft);
            da.GetData(3,  ref zRight);
            da.GetData(4,  ref ny);
            da.GetData(5,  ref nx);
            da.GetData(6,  ref q);
            da.GetData(7,  ref uw);
            da.GetData(8,  ref thick);
            da.GetData(9,  ref kaGamma);
            da.GetData(10, ref run);

            if (!run)
            {
                da.SetData(5, "Run = false. Toggle to solve.");
                return;
            }

            if (!TnaPlanner.NativeAvailable)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "frahan_tna.dll not found. Build native/tna_solver and redeploy.");
                da.SetData(5, "frahan_tna.dll unavailable.");
                return;
            }

            if (ny < 3) { ny = 3; AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Ny clamped to 3."); }
            if (nx < 2) { nx = 2; AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Nx clamped to 2."); }

            // Build form diagram.
            var form = TnaPlanner.GenerateBarrelVaultGrid(
                span, length, zLeft, zRight, ny, nx, q, uw, thick, kaGamma);

            // Solve.
            var result = TnaPlanner.Solve(form);
            if (!result.Success)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "TNA solve failed: " + result.Error);
                da.SetData(5, "Error: " + result.Error);
                return;
            }

            // --- Vault mesh ---
            var mesh = BuildMesh(form, result, nx, ny);

            // --- Branch lines + thrust ---
            var lines  = new List<Line>(form.EdgeCount);
            var thrust = new List<double>(form.EdgeCount);
            for (int e = 0; e < form.EdgeCount; e++)
            {
                int gi = form.Edges[e][0];
                int gj = form.Edges[e][1];
                var pi = new Point3d(form.Nodes[gi].X, form.Nodes[gi].Y, result.Heights[gi]);
                var pj = new Point3d(form.Nodes[gj].X, form.Nodes[gj].Y, result.Heights[gj]);
                lines.Add(new Line(pi, pj));
                thrust.Add(result.BranchForce[e]);
            }

            // --- Support reactions + boundary points ---
            var reactions = new List<Vector3d>();
            var boundPts  = new List<Point3d>();
            for (int i = 0; i < form.NodeCount; i++)
            {
                if (!form.IsFixed[i]) continue;
                var pt = new Point3d(form.Nodes[i].X, form.Nodes[i].Y, result.Heights[i]);
                boundPts.Add(pt);
                reactions.Add(new Vector3d(result.ReactionX[i], result.ReactionY[i], result.ReactionZ[i]));
            }

            // --- Report ---
            double maxH = 0, minH = double.MaxValue;
            double maxThrust = 0;
            for (int i = 0; i < form.NodeCount; i++) {
                if (result.Heights[i] > maxH)    maxH    = result.Heights[i];
                if (result.Heights[i] < minH)    minH    = result.Heights[i];
            }
            foreach (var h in result.BranchForce)
                if (h > maxThrust) maxThrust = h;

            var rep = new StringBuilder();
            rep.AppendLine($"TNA Vault (Gaudí) solved via frahan_tna v{TnaPlanner.NativeVersion}.");
            rep.AppendLine($"Grid: {nx}×{ny} = {nx * ny} nodes, {form.EdgeCount} edges.");
            rep.AppendLine($"Span {span:F2} m × Length {length:F2} m.");
            rep.AppendLine($"Z range: {minH:F3} – {maxH:F3} m  (rise = {maxH - Math.Min(zLeft, zRight):F3} m above lower support).");
            rep.AppendLine($"Max horizontal branch thrust: {maxThrust:F2} kN.");
            rep.AppendLine($"Self-weight: {uw:F0} kN/m³ × {thick:F3} m thickness (q in kN/m, loads in kN).");
            if (kaGamma > 0) rep.AppendLine($"Earth pressure: K_a×γ = {kaGamma:F2} kN/m³ (left wall).");
            rep.AppendLine($"Boundary nodes: {boundPts.Count}  |  Free nodes: {form.NodeCount - boundPts.Count}.");

            Message = $"{nx}×{ny} | rise {maxH - Math.Min(zLeft, zRight):F2}m | H_max {maxThrust:F1}kN";

            da.SetData    (0, mesh);
            da.SetDataList(1, lines);
            da.SetDataList(2, thrust);
            da.SetDataList(3, reactions);
            da.SetDataList(4, boundPts);
            da.SetData    (5, rep.ToString());
        }

        // Build a quad mesh from the grid node order used in GenerateBarrelVaultGrid.
        // Node order: outer loop ix (0..nx-1), inner loop iy (0..ny-1).
        private static Mesh BuildMesh(TnaFormDiagram form, TnaResult result, int nx, int ny)
        {
            var mesh = new Mesh();

            for (int ix = 0; ix < nx; ix++)
                for (int iy = 0; iy < ny; iy++) {
                    int gi = ix * ny + iy;
                    mesh.Vertices.Add(form.Nodes[gi].X, form.Nodes[gi].Y, result.Heights[gi]);
                }

            for (int ix = 0; ix < nx - 1; ix++)
                for (int iy = 0; iy < ny - 1; iy++) {
                    int a = ix * ny + iy;
                    int b = (ix + 1) * ny + iy;
                    int c = (ix + 1) * ny + (iy + 1);
                    int d = ix * ny + (iy + 1);
                    mesh.Faces.AddFace(a, b, c, d);
                }

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }
    }
}
