#nullable disable
using System;
using System.Collections.Generic;
using BulletSharp;
using BulletSharp.Math;

namespace Frahan.Masonry.Physics
{
    // =========================================================================
    // BulletSettleService -- rigid-body physics settle for irregular stone piles,
    // backed by Bullet (BulletSharp.x64, zlib). Rhino-free: operates on plain
    // double arrays (convex-piece points in world coords) + a box container, and
    // returns each body's final world transform. The GH component is a thin
    // adapter that converts Rhino meshes <-> these arrays.
    //
    // Verdict (BULLET_PHYSICS_BACKEND.md): Bullet > Kangaroo for stone settling
    // (Kangaroo rigid-body has no friction, author-confirmed unsuitable for
    // stacking). Same engine as the pybullet dev harness.
    //
    // Collision proxy: each stone is given as one or more CONVEX pieces (the
    // caller decomposes concave stones via CoacdMeshDecompose). Pieces are
    // wrapped as ConvexHullShape children of a per-stone CompoundShape. Bodies
    // are seeded at their (already placed) world positions, lifted slightly to
    // clear convex-proxy overlap, then gravity is ramped gentle->full so they
    // settle into real contact without exploding (the lesson from the dev run).
    //
    // Needs native libbulletc.dll beside the assembly (shipped by BulletSharp.x64).
    // Single-threaded: callers run this on a background Task (heavy-component
    // pattern), never on the GH UI thread.
    // =========================================================================

    /// <summary>One stone to settle: its convex pieces (world coords) + mass.</summary>
    public sealed class SettleStone
    {
        /// <summary>Convex pieces; each is a flat [x0,y0,z0,x1,y1,z1,...] point list in WORLD coords.</summary>
        public IReadOnlyList<double[]> ConvexPieces { get; set; }
        /// <summary>Mass (proportional to volume). Floored internally.</summary>
        public double Mass { get; set; } = 1.0;
        /// <summary>Optional CENTER OF MASS in world coords [x,y,z]. When set, the body rotates about
        /// this point (the physical CoM). Leave null to use the hull's VERTEX centroid -- which for an
        /// irregular stone is offset from the true CoM, so gravity applies a false torque and the stone
        /// tumbles even when it is actually stable. Callers with the mesh should pass the volume centroid.</summary>
        public double[] CenterOfMass { get; set; }
        /// <summary>When true, this stone is a FIXED (static, immovable) support: it holds its placed
        /// pose and the dynamic stones settle ONTO it. Used by the incremental settle-as-placed loop
        /// (already-built courses are fixed while the next stone seats), which avoids the whole-wall
        /// mutual push-apart that scatters an un-settled point-contact wall. Not lifted.</summary>
        public bool Fixed { get; set; }
    }

    /// <summary>Axis-aligned box container [0,W] x [0,D] x [0,H] (open top).</summary>
    public sealed class SettleContainer
    {
        public double Width { get; set; }
        public double Depth { get; set; }
        public double Height { get; set; }
    }

    public sealed class SettleOptions
    {
        public double GravityZ { get; set; } = -9.81;
        public double Friction { get; set; } = 0.85;
        public int SettleSteps { get; set; } = 1500;
        public int SolverIterations { get; set; } = 80;
        public double TimeStep { get; set; } = 1.0 / 600.0;
        /// <summary>Sub-steps per gravity-ramp phase (4 phases gentle-&gt;full). Lower = faster, fine
        /// for a single body dropping onto a FIXED base (little initial overlap to resolve gently).</summary>
        public int RampStepsPerPhase { get; set; } = 250;
        /// <summary>Lift applied to every body at seed time to clear convex-proxy overlap.</summary>
        public double Lift { get; set; } = 0.06;
        /// <summary>Vertical tamp rounds (strong-gravity bursts) to densify after settling.</summary>
        public int TampRounds { get; set; } = 1;
    }

    /// <summary>Result for one stone: rigid delta to apply to the ORIGINAL mesh as
    /// v' = R * (v - Centroid) + Translation, where R is row-major 3x3.</summary>
    public sealed class SettleStoneResult
    {
        public double[] Centroid { get; set; }     // 3
        public double[] Rotation { get; set; }      // 9, row-major 3x3 (world = R * local)
        public double[] Translation { get; set; }   // 3
        public bool InContainer { get; set; }
    }

    public sealed class SettleResult
    {
        public List<SettleStoneResult> Stones { get; } = new List<SettleStoneResult>();
        public int Settled { get; set; }
    }

    public static class BulletSettleService
    {
        public static bool IsAvailable
        {
            get
            {
                try { using (var c = new DefaultCollisionConfiguration()) return true; }
                catch { return false; }
            }
        }

        public static SettleResult Settle(IReadOnlyList<SettleStone> stones, SettleContainer box, SettleOptions opt)
        {
            if (stones == null) throw new ArgumentNullException(nameof(stones));
            if (box == null) throw new ArgumentNullException(nameof(box));
            opt = opt ?? new SettleOptions();

            var col = new DefaultCollisionConfiguration();
            var disp = new CollisionDispatcher(col);
            var bp = new DbvtBroadphase();
            var solver = new SequentialImpulseConstraintSolver();
            var world = new DiscreteDynamicsWorld(disp, bp, solver, col);
            world.SolverInfo.NumIterations = opt.SolverIterations;

            double w = box.Width, d = box.Depth, h = box.Height, wall = 0.05;
            AddStatic(world, new BoxShape(w / 2, d / 2, wall), new Vector3(w / 2, d / 2, -wall));            // floor
            AddStatic(world, new BoxShape(wall, d / 2, h / 2), new Vector3(-wall, d / 2, h / 2));            // x-
            AddStatic(world, new BoxShape(wall, d / 2, h / 2), new Vector3(w + wall, d / 2, h / 2));         // x+
            AddStatic(world, new BoxShape(w / 2, wall, h / 2), new Vector3(w / 2, -wall, h / 2));            // y-
            AddStatic(world, new BoxShape(w / 2, wall, h / 2), new Vector3(w / 2, d + wall, h / 2));         // y+

            var bodies = new List<RigidBody>(stones.Count);
            var centroids = new List<double[]>(stones.Count);
            foreach (var s in stones)
            {
                // Rotate about the physical CoM when the caller supplies it; the hull vertex-centroid
                // (the fallback) is offset for irregular stones and tumbles them under a false torque.
                var c = (s.CenterOfMass != null && s.CenterOfMass.Length == 3)
                        ? s.CenterOfMass
                        : Centroid(s.ConvexPieces);
                centroids.Add(c);
                var compound = new CompoundShape();
                foreach (var piece in s.ConvexPieces)
                {
                    var hull = new ConvexHullShape();
                    for (int i = 0; i + 2 < piece.Length; i += 3)
                        hull.AddPoint(new Vector3(piece[i] - c[0], piece[i + 1] - c[1], piece[i + 2] - c[2]), false);
                    // Bullet's default 0.04 m collision margin INFLATES the hull and rounds its contact
                    // faces -- a stone then rests on rounded edges and rolls off even a face it is stable
                    // on. Shrink it to a thin skin so the hull matches the real stone (~mm scale).
                    hull.Margin = 0.0015;
                    hull.RecalcLocalAabb();
                    compound.AddChildShape(Matrix.Identity, hull);
                }
                // A Fixed stone is a STATIC support (mass 0): it never moves and is not lifted, so the
                // dynamic stones settle onto the already-built wall with no whole-wall push-apart cascade.
                double mass = s.Fixed ? 0.0 : Math.Max(s.Mass, 1e-4);
                var inertia = Vector3.Zero;
                if (!s.Fixed) compound.CalculateLocalInertia(mass, out inertia);
                var seed = Matrix.Translation(c[0], c[1], c[2] + (s.Fixed ? 0.0 : opt.Lift));
                var ms = new DefaultMotionState(seed);
                var body = new RigidBody(new RigidBodyConstructionInfo(mass, ms, compound, inertia))
                { Friction = opt.Friction, Restitution = 0.0 };
                if (s.Fixed) body.CollisionFlags |= CollisionFlags.StaticObject;
                else body.SetDamping(0.4, 0.4);
                world.AddRigidBody(body);
                bodies.Add(body);
            }

            // Ramp gravity gentle -> full so seeded near-contacts resolve softly.
            int rampSteps = Math.Max(1, opt.RampStepsPerPhase);
            foreach (var g in new[] { -0.5, -2.0, -5.0, opt.GravityZ })
            {
                world.Gravity = new Vector3(0, 0, g);
                for (int i = 0; i < rampSteps; i++) world.StepSimulation(opt.TimeStep, 4, opt.TimeStep);
            }
            for (int i = 0; i < opt.SettleSteps; i++) world.StepSimulation(opt.TimeStep, 4, opt.TimeStep);
            for (int t = 0; t < opt.TampRounds; t++)
            {
                world.Gravity = new Vector3(0, 0, opt.GravityZ * 3.5);
                for (int i = 0; i < 200; i++) world.StepSimulation(opt.TimeStep, 4, opt.TimeStep);
                world.Gravity = new Vector3(0, 0, opt.GravityZ);
                for (int i = 0; i < 400; i++) world.StepSimulation(opt.TimeStep, 4, opt.TimeStep);
            }

            var result = new SettleResult();
            for (int k = 0; k < bodies.Count; k++)
            {
                var m = bodies[k].WorldTransform;     // BulletSharp Matrix: row-vector basis in M11..M33, origin M41..M43
                var rsr = new SettleStoneResult
                {
                    Centroid = centroids[k],
                    // world = R * local (column-vector). BulletSharp stores the basis row-wise for
                    // row-vector math, so the column-vector rotation is its transpose.
                    Rotation = new[] { (double)m.M11, m.M21, m.M31, m.M12, m.M22, m.M32, m.M13, m.M23, m.M33 },
                    Translation = new[] { (double)m.M41, m.M42, m.M43 },
                };
                rsr.InContainer = rsr.Translation[2] > -0.3;
                if (rsr.InContainer) result.Settled++;
                result.Stones.Add(rsr);
            }
            world.Dispose();
            return result;
        }

        private static void AddStatic(DiscreteDynamicsWorld w, CollisionShape s, Vector3 pos)
        {
            var ms = new DefaultMotionState(Matrix.Translation(pos));
            var body = new RigidBody(new RigidBodyConstructionInfo(0, ms, s, Vector3.Zero)) { Friction = 0.9 };
            w.AddRigidBody(body);
        }

        private static double[] Centroid(IReadOnlyList<double[]> pieces)
        {
            double sx = 0, sy = 0, sz = 0; long n = 0;
            foreach (var p in pieces)
                for (int i = 0; i + 2 < p.Length; i += 3) { sx += p[i]; sy += p[i + 1]; sz += p[i + 2]; n++; }
            if (n == 0) return new double[] { 0, 0, 0 };
            return new[] { sx / n, sy / n, sz / n };
        }
    }
}
