#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Masonry.Tna
{
    // =========================================================================
    // TnaFormDiagram — plan-view graph that the TNA solver operates on.
    //
    // Nodes hold plan positions (XY) and, for boundary nodes, prescribed
    // heights (Z).  Edges carry force densities q.  Loads are optional;
    // when omitted the solver uses zero (form-only, no self-weight).
    // =========================================================================
    public sealed class TnaFormDiagram
    {
        public List<Point3d>  Nodes    { get; } = new List<Point3d>();
        public List<bool>     IsFixed  { get; } = new List<bool>();
        public List<int[]>    Edges    { get; } = new List<int[]>();   // [i, j]
        public List<double>   Q        { get; } = new List<double>();  // per edge
        public List<double>   LoadZ    { get; } = new List<double>();  // per node, N downward
        public List<double>   LoadX    { get; } = new List<double>();  // per node, N horizontal
        public List<double>   LoadY    { get; } = new List<double>();  // per node, N horizontal

        public int NodeCount => Nodes.Count;
        public int EdgeCount => Edges.Count;

        // Add a free node (height will be solved).
        public int AddFreeNode(double x, double y, double pz = 0, double px = 0, double py = 0)
        {
            int idx = Nodes.Count;
            Nodes.Add(new Point3d(x, y, 0));
            IsFixed.Add(false);
            LoadZ.Add(pz); LoadX.Add(px); LoadY.Add(py);
            return idx;
        }

        // Add a boundary node with known height.
        public int AddFixedNode(double x, double y, double z, double pz = 0)
        {
            int idx = Nodes.Count;
            Nodes.Add(new Point3d(x, y, z));
            IsFixed.Add(true);
            LoadZ.Add(pz); LoadX.Add(0); LoadY.Add(0);
            return idx;
        }

        // Add an edge between two nodes with a force density.
        public void AddEdge(int i, int j, double q = 1.0)
        {
            Edges.Add(new[] { i, j });
            Q.Add(q > 0 ? q : 1e-6);
        }
    }

    // =========================================================================
    // TnaResult — output from the TNA solver.
    // =========================================================================
    public sealed class TnaResult
    {
        public bool     Success      { get; internal set; }
        public string   Error        { get; internal set; }
        public double[] Heights      { get; internal set; }  // all nodes, same order
        public double[] ReactionX    { get; internal set; }  // fixed nodes only (others = 0)
        public double[] ReactionY    { get; internal set; }
        public double[] ReactionZ    { get; internal set; }
        public double[] BranchForce  { get; internal set; }  // horizontal thrust per edge (N)
    }

    // =========================================================================
    // TnaPlanner — managed entry point.
    //
    // Call Solve() with a TnaFormDiagram to get a TnaResult.
    // Use GenerateBarrelVaultGrid() to build a Park Güell-style form diagram.
    // =========================================================================
    public static class TnaPlanner
    {
        // Check whether the native DLL is present.
        public static bool NativeAvailable => TnaNative.Available;
        public static string NativeVersion  => TnaNative.Version() ?? "(not found)";

        // ---------------------------------------------------------------------
        // Solve: call the native TNA solver.
        // ---------------------------------------------------------------------
        public static TnaResult Solve(TnaFormDiagram form)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            if (!TnaNative.Available)
                return new TnaResult { Success = false, Error = "frahan_tna.dll not available." };

            int nn = form.NodeCount;
            int ne = form.EdgeCount;

            var nodes  = new TnaNodeFlat[nn];
            var edges  = new TnaEdgeFlat[ne];
            var loads  = new TnaLoadFlat[nn];

            for (int i = 0; i < nn; i++) {
                nodes[i] = new TnaNodeFlat {
                    X = form.Nodes[i].X, Y = form.Nodes[i].Y, Z = form.Nodes[i].Z,
                    Fixed = form.IsFixed[i] ? 1 : 0
                };
                loads[i] = new TnaLoadFlat {
                    Pz = form.LoadZ[i], Px = form.LoadX[i], Py = form.LoadY[i]
                };
            }
            for (int e = 0; e < ne; e++) {
                edges[e] = new TnaEdgeFlat {
                    I = form.Edges[e][0], J = form.Edges[e][1], Q = form.Q[e]
                };
            }

            var zOut   = new double[nn];
            var rxOut  = new double[nn];
            var ryOut  = new double[nn];
            var rzOut  = new double[nn];
            var bForce = new double[ne];

            int code = TnaNative.frahan_tna_solve(
                nn, nodes, ne, edges, loads,
                zOut, rxOut, ryOut, rzOut, bForce);

            if (code != 0)
                return new TnaResult { Success = false, Error = TnaNative.LastError() };

            return new TnaResult {
                Success     = true,
                Heights     = zOut,
                ReactionX   = rxOut,
                ReactionY   = ryOut,
                ReactionZ   = rzOut,
                BranchForce = bForce
            };
        }

        // ---------------------------------------------------------------------
        // GenerateBarrelVaultGrid — Park Güell–style barrel vault form diagram.
        //
        // Creates a rectangular grid in plan:
        //   Y direction = arch span (from retaining wall to column line).
        //   X direction = vault length (along the hillside tunnel).
        //
        // Left boundary  (y=0)      : retaining wall, height = zLeft.
        // Right boundary (y=span)   : column tops, height = zRight.
        // End boundaries (x=0, x=L) : end walls, height interpolated zLeft→zRight.
        // Interior nodes            : free (solved by TNA).
        //
        // Parameters:
        //   span       : vault width (m), Y direction.
        //   length     : vault tunnel length (m), X direction.
        //   zLeft      : retaining wall top height (m).
        //   zRight     : column top height (m).
        //   ny         : nodes across span (min 3 — more = smoother arch).
        //   nx         : nodes along length (min 2 — more = allows non-prismatic).
        //   q          : uniform force density (N/m).
        //   unitWeight : stone self-weight (kN/m³ → convert to N/m² per tributary area).
        //   thickness  : vault mean thickness (m), used for tributary load.
        //   kaGamma    : K_a × γ_soil (kN/m³) for earth pressure on left wall.
        //                Pass 0 to omit earth pressure.
        // ---------------------------------------------------------------------
        public static TnaFormDiagram GenerateBarrelVaultGrid(
            double span,
            double length,
            double zLeft,
            double zRight,
            int    ny          = 7,
            int    nx          = 5,
            double q           = 1.0,
            double unitWeight  = 25.0,   // kN/m³ granite
            double thickness   = 0.4,    // m
            double kaGamma     = 0.0)
        {
            if (ny < 3) ny = 3;
            if (nx < 2) nx = 2;

            var form    = new TnaFormDiagram();
            var nodeIdx = new int[nx, ny];

            double dx = length / (nx - 1);
            double dy = span   / (ny - 1);
            double tribArea = dx * dy;              // tributary area per node (m²)
            double pz_node  = unitWeight            // kN/m³
                            * thickness * tribArea; // kN per node (keep consistent with q in kN/m)

            // Build nodes.
            for (int ix = 0; ix < nx; ix++) {
                double x = ix * dx;
                for (int iy = 0; iy < ny; iy++) {
                    double y = iy * dy;
                    bool onLeft   = (iy == 0);
                    bool onRight  = (iy == ny - 1);
                    bool onEnd    = (ix == 0 || ix == nx - 1);
                    bool isFixed  = onLeft || onRight || onEnd;

                    double t   = (double)iy / (ny - 1);  // 0 → left wall, 1 → columns
                    double zBoundary = zLeft * (1 - t) + zRight * t;

                    // Earth pressure horizontal load on left-wall boundary nodes.
                    double px = 0.0;
                    if (onLeft && kaGamma > 0)
                        px = kaGamma * zLeft * tribArea; // simplified K_a·γ·h·A, kN units

                    if (isFixed) {
                        nodeIdx[ix, iy] = form.AddFixedNode(x, y, zBoundary, pz_node);
                    } else {
                        nodeIdx[ix, iy] = form.AddFreeNode(x, y, pz_node, px, 0.0);
                    }
                }
            }

            // Build edges.
            // Transverse edges (Y direction — arch direction, high q).
            for (int ix = 0; ix < nx; ix++)
                for (int iy = 0; iy < ny - 1; iy++)
                    form.AddEdge(nodeIdx[ix, iy], nodeIdx[ix, iy + 1], q);

            // Longitudinal edges (X direction — barrel direction, lower q; affects 3D shape).
            for (int ix = 0; ix < nx - 1; ix++)
                for (int iy = 0; iy < ny; iy++)
                    form.AddEdge(nodeIdx[ix, iy], nodeIdx[ix + 1, iy], q * 0.1);

            return form;
        }

        // ---------------------------------------------------------------------
        // ToMesh — convert TNA result + form diagram to a Rhino mesh.
        // ---------------------------------------------------------------------
        public static Mesh ToMesh(TnaFormDiagram form, TnaResult result, int nx, int ny)
        {
            if (!result.Success) return null;

            var mesh = new Mesh();
            var nodeIdx = new int[nx, ny];

            // Add vertices in the same order as GenerateBarrelVaultGrid.
            double dx = (form.Nodes[form.NodeCount - 1].X) / (nx - 1);
            double dy = (form.Nodes[form.NodeCount - 1].Y) / (ny - 1);
            for (int ix = 0; ix < nx; ix++) {
                for (int iy = 0; iy < ny; iy++) {
                    int gi = nodeIdx[ix, iy] = ix * ny + iy;
                    double z = result.Heights[gi];
                    mesh.Vertices.Add(form.Nodes[gi].X, form.Nodes[gi].Y, z);
                }
            }

            // Quad faces.
            for (int ix = 0; ix < nx - 1; ix++) {
                for (int iy = 0; iy < ny - 1; iy++) {
                    int a = nodeIdx[ix,     iy    ];
                    int b = nodeIdx[ix + 1, iy    ];
                    int c = nodeIdx[ix + 1, iy + 1];
                    int d = nodeIdx[ix,     iy + 1];
                    mesh.Faces.AddFace(a, b, c, d);
                }
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }
    }
}
