using System;
using System.Collections.Generic;
using Xunit;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Interfaces;
using Frahan.Masonry.Solvers;

namespace Frahan.Verification.Tests
{
    /// <summary>
    /// Verifies the masonry CRA / RBE equilibrium stack
    /// (<c>MasonryStabilityChecker</c> + <c>EquilibriumMatrixBuilder</c> +
    /// <c>FrictionConeBuilder</c> + <c>RbeQpFormulation</c> + the managed QP
    /// solvers, Kao et al. 2022, CAD 146:103216 / compas_cra) against the Lean
    /// theorems in <c>frahan_proofs/FrahanProofs/TierThree.lean</c>:
    ///
    ///  * <c>cra_farkas</c> (<c>thm:cra</c>): stability = feasibility of the set
    ///    <c>{ f : A f = g, f in K }</c> where K is the Coulomb friction cone.
    ///    THE CERTIFICATE PROPERTY: when the solver returns a STABLE verdict with
    ///    contact forces f, those forces must INDEPENDENTLY be a feasible point of
    ///    that set — (a) equilibrium <c>A f = g</c> and (b) admissibility
    ///    <c>f in K</c>. Both are re-derived here from the assembly's interface
    ///    geometry + gravity + the decoded per-vertex forces, NEVER from the
    ///    solver's own residual/verdict flag (independent oracle).
    ///  * <c>admissibleSet_convex</c>: the admissible set is convex, so the
    ///    midpoint of two admissible force certificates is itself admissible.
    ///
    /// Equilibrium sign convention (verified against the shipping code, 2026-07-25):
    /// the checker solves <c>RbeQpFormulation.BuildPhysicsCorrected</c>, whose
    /// equality RHS is <c>+B</c> (it flips the legacy <c>Build</c>'s <c>-B</c> so
    /// f_n &gt;= 0 means compression). The satisfied per-free-block balance is
    /// therefore <c>Aeq f = B</c> with <c>B_z = mass * gravityZ</c> (negative), i.e.
    /// the independent residual is <c>Aeq f - B</c>. The reconstruction below rebuilds
    /// <c>Aeq f</c> from each interface's (n, t1, t2) frame, the contact-vertex world
    /// positions, and the block centre of mass (exact box centre), with its own
    /// cross-product arithmetic — it never multiplies the solver's matrix.
    ///
    /// The MANAGED solver lane is forced (<c>MasonrySolverRegistry.Default = null</c>)
    /// so the native OSQP path (only taken when a caller pre-registers an
    /// <c>OsqpQpSolver</c>) is never touched; the checker then uses its
    /// closed-form LS-first KKT certificate or the pure-managed ADMM, both
    /// deterministic and IEEE-754-identical across Windows/Linux CI. This mirrors
    /// NesterTests forcing FRAHAN_NFP_NATIVE=0.
    ///
    /// Characterization evidence (probe, 2026-07-25): the stable fixtures below
    /// solve to force residual &lt;= 8e-7 rel, moment residual &lt;= 5.5e-7 rel,
    /// tension = 0, cone violation = 0. Gates sit at 1e-4 rel (&gt;100x margin,
    /// and below the checker's own 1e-3 equilibrium-audit gate).
    /// </summary>
    public class CraStabilityTests
    {
        const double Density = 2400.0;      // matches CraStabilityCheckerTests
        const double G = -9.80665;          // gravity Z

        // ---- Fact 1 (PRIORITY): the safe theorem's certificate. ----------------
        [Fact]
        public void StableVerdict_ForceCertificate_SatisfiesEquilibriumAndCone()
        {
            MasonrySolverRegistry.Default = null; // force the managed/deterministic lane

            // Three RBE-stable assemblies of increasing character:
            //  * a 2-box stack and a 3-box tower (load flows straight down: the
            //    friction cone is slack, exercises the equilibrium oracle);
            //  * Kao's H-model beam bridging two columns through vertical faces —
            //    RBE (force-only) accepts it via a self-equilibrated squeeze whose
            //    friction carries the beam, so the returned certificate rides the
            //    friction cone (util ~0.92): exercises the admissibility oracle
            //    near saturation. (That this assembly is physically unstable is a
            //    separate CRA/kinematic fact; the force state IS a valid feasible
            //    point of {A f = g, f in K}, which is exactly what cra_farkas asks.)
            var cases = new (string name, List<BoxDef> boxes, double mu)[]
            {
                ("stack2", Stack2(), 0.7),
                ("tower3", Tower3(), 0.7),
                ("hmodel", HModel(), 0.7),
            };

            double worstForceRel = 0, worstMomentRel = 0, worstTensionRel = 0, worstCone = 0;
            foreach (var (name, boxes, mu) in cases)
            {
                var asm = BuildAssembly(boxes, out var info);
                var detail = MasonryStabilityChecker.CheckDetailed(asm, mu: mu, gravityZ: G);
                Assert.True(detail.Result.IsStable,
                    $"[{name}] expected an RBE-stable verdict but got {detail.Result.Status}: {detail.Result.Message}");

                var fmap = Decode(detail);

                // (a) equilibrium: independent A f - B residual (force + moment).
                Equilibrium(asm, info, fmap, out double fResAbs, out double mResAbs,
                            out double forceScale, out double momScale);
                double fRel = fResAbs / forceScale, mRel = mResAbs / momScale;
                if (fRel > worstForceRel) worstForceRel = fRel;
                if (mRel > worstMomentRel) worstMomentRel = mRel;

                // (b) admissibility: no tension + inside the true Coulomb cone.
                Admissibility(fmap, mu, out double maxTension, out double maxCone,
                              out double maxNegNormal, out double maxCompression);
                double tRel = maxTension / Math.Max(1e-9, maxCompression);
                if (tRel > worstTensionRel) worstTensionRel = tRel;
                double coneRel = maxCone / Math.Max(1e-9, maxCompression);
                if (coneRel > worstCone) worstCone = coneRel;

                Assert.True(fRel <= 1e-4,
                    $"[{name}] cra_farkas equilibrium (force) violated: ||Aeq f - B|| = {fResAbs:G4} " +
                    $"({fRel:G3} rel, scale {forceScale:G4}) exceeds 1e-4.");
                Assert.True(mRel <= 1e-4,
                    $"[{name}] cra_farkas equilibrium (moment) violated: ||moment(Aeq f)|| = {mResAbs:G4} " +
                    $"({mRel:G3} rel, scale {momScale:G4}) exceeds 1e-4.");
                Assert.True(maxTension <= 1e-3 * Math.Max(1e-9, maxCompression),
                    $"[{name}] admissibility violated: a STABLE certificate carries tension f_n- = {maxTension:G4} " +
                    $"(> 1e-3 * maxCompression {maxCompression:G4}).");
                Assert.True(maxNegNormal <= 1e-3 * Math.Max(1e-9, maxCompression),
                    $"[{name}] admissibility violated: net normal force went negative ({maxNegNormal:G4}) — the cone requires f_n >= 0.");
                Assert.True(maxCone <= 1e-6 * Math.Max(1e-9, maxCompression),
                    $"[{name}] admissibility violated: contact force outside the friction cone, " +
                    $"max(|f_t| - mu*f_n) = {maxCone:G4} (mu={mu}).");
            }

            // A single roll-up guard so the worst-across-fixtures numbers are visible on failure.
            Assert.True(worstForceRel <= 1e-4 && worstMomentRel <= 1e-4 && worstTensionRel <= 1e-3 && worstCone <= 1e-6 * 1e6,
                $"worst force {worstForceRel:G3}, moment {worstMomentRel:G3}, tension {worstTensionRel:G3}, cone {worstCone:G3}.");
        }

        // ---- Fact 2: the negative direction (no admissible state -> Infeasible). --
        [Fact]
        public void UnstableCantilever_ReportedInfeasible_NoCertificate()
        {
            MasonrySolverRegistry.Default = null;

            // Upper box shifted so its CoM (x=1.3) overhangs the support edge (x=1.0):
            // no non-negative normal-force distribution over the contact can put the
            // resultant under the CoM, so {A f = g, f in K} is empty. The API exposes
            // this cleanly: IsStable=false AND Status=Infeasible (a primal
            // infeasibility certificate, OSQP-style, from the managed ADMM lane).
            var asm = BuildAssembly(Cantilever(), out _);
            var detail = MasonryStabilityChecker.CheckDetailed(asm, mu: 0.7, gravityZ: G);

            Assert.False(detail.Result.IsStable,
                $"a cantilever with CoM beyond the support must NOT be certified stable: {detail.Result.Message}");
            Assert.Equal(ConvexQpStatus.Infeasible, detail.Result.Status);
        }

        // ---- Fact 3: admissibleSet_convex (midpoint of two certificates). --------
        [Fact]
        public void AdmissibleSet_Convex_MidpointOfTwoCertificates_IsAdmissible()
        {
            MasonrySolverRegistry.Default = null;

            // The admissible set K(mu) = {A f = g} ∩ Coulomb-cone(mu) is convex.
            // Two distinct admissible certificates are obtained on the H-model — the
            // one assembly whose friction cone is ACTIVE, so different mu yield
            // genuinely different min-norm force states (gravity-only stacks are
            // shear-determinate and return one solution regardless of mu). f(muLo)
            // lies inside cone(muLo) ⊂ cone(muHi), f(muHi) inside cone(muHi), and
            // both satisfy the SAME mu-independent equilibrium A f = g. Their
            // midpoint must then be admissible in cone(muHi): equilibrium (affine)
            // + cone (convex).
            const double muLo = 1.0, muHi = 2.0;
            var asm = BuildAssembly(HModel(), out var info);

            var loD = MasonryStabilityChecker.CheckDetailed(asm, mu: muLo, gravityZ: G);
            var hiD = MasonryStabilityChecker.CheckDetailed(asm, mu: muHi, gravityZ: G);
            Assert.True(loD.Result.IsStable && hiD.Result.IsStable,
                "both mu solves of the H-model must be RBE-stable for the convexity test.");

            var fLo = Decode(loD);
            var fHi = Decode(hiD);

            // Non-triviality: the two certificates must actually differ, else the
            // midpoint test degenerates. (Probe: ||d||/||f|| ~ 0.38 at these mu.)
            double diff = 0, norm = 0;
            foreach (var kv in fLo)
            {
                var a = kv.Value; var b = fHi[kv.Key];
                double dn = (a[0] - a[1]) - (b[0] - b[1]);
                diff += dn * dn + (a[2] - b[2]) * (a[2] - b[2]) + (a[3] - b[3]) * (a[3] - b[3]);
                norm += (a[0] - a[1]) * (a[0] - a[1]) + a[2] * a[2] + a[3] * a[3];
            }
            diff = Math.Sqrt(diff); norm = Math.Sqrt(norm);
            Assert.True(diff > 1e-3 * Math.Max(1e-9, norm),
                $"the two certificates coincided (||d||/||f|| = {diff / Math.Max(1e-9, norm):G3}); midpoint test would be trivial.");

            // Midpoint of the two decoded certificates (component-wise average).
            var mid = new Dictionary<long, double[]>();
            foreach (var kv in fLo)
            {
                var a = kv.Value; var b = fHi[kv.Key];
                mid[kv.Key] = new[] { 0.5 * (a[0] + b[0]), 0.5 * (a[1] + b[1]), 0.5 * (a[2] + b[2]), 0.5 * (a[3] + b[3]) };
            }

            Equilibrium(asm, info, mid, out double fResAbs, out double mResAbs, out double forceScale, out double momScale);
            Admissibility(mid, muHi, out double maxTension, out double maxCone, out double maxNegNormal, out double maxCompression);

            Assert.True(fResAbs / forceScale <= 1e-4,
                $"admissibleSet_convex: midpoint breaks equilibrium (force) — {fResAbs:G4} ({fResAbs / forceScale:G3} rel).");
            Assert.True(mResAbs / momScale <= 1e-4,
                $"admissibleSet_convex: midpoint breaks equilibrium (moment) — {mResAbs:G4} ({mResAbs / momScale:G3} rel).");
            Assert.True(maxTension <= 1e-3 * Math.Max(1e-9, maxCompression) && maxNegNormal <= 1e-3 * Math.Max(1e-9, maxCompression),
                $"admissibleSet_convex: midpoint carries tension (f_n- = {maxTension:G4}, negNormal = {maxNegNormal:G4}).");
            Assert.True(maxCone <= 1e-6 * Math.Max(1e-9, maxCompression),
                $"admissibleSet_convex: midpoint leaves the cone(mu={muHi}), max(|f_t| - mu*f_n) = {maxCone:G4}.");
        }

        // ---- Fact 4: the coupled CRA analysis (Kao 2022 kinematic refinement). ---
        [Fact]
        public void CraCoupledAnalysis_CertifiesRealStack_RejectsSelfStressHModel()
        {
            MasonrySolverRegistry.Default = null;

            // thm:cra (the coupling): force-feasibility {A f = g, f in K} is NECESSARY
            // but not SUFFICIENT — a state can be force-admissible yet demand a
            // kinematically impossible virtual motion (self-stress). CraStabilityChecker
            // adds the kinematic-compatibility certificate (Kao 2022 Eqs. 8-11).
            // This fact pins the paper's headline behaviour on the permanent suite:
            //   * a genuine 2-box stack is CRA-certified stable (a consistent virtual
            //     motion exists);
            //   * the CoM-overhang cantilever is CRA-unstable;
            //   * the H-model is ACCEPTED by force-only RBE (independently a valid
            //     {A f = g, f in K} point — Fact 1 verifies exactly that certificate)
            //     but REJECTED by CRA, because engaging both vertical joints needs the
            //     beam to virtually penetrate both columns at once.
            // (Verdict-level behavioral pin of the coupled path; the independent
            // equilibrium+cone oracle lives in Fact 1.)
            var stack = CraStabilityChecker.Check(BuildAssembly(Stack2(), out _), gravityZ: G);
            Assert.True(stack.IsStable && stack.Certified,
                $"a 2-box stack must be CRA-certified stable: stable={stack.IsStable} certified={stack.Certified}: {stack.Message}");

            var canti = CraStabilityChecker.Check(BuildAssembly(Cantilever(), out _), gravityZ: G);
            Assert.False(canti.IsStable, $"a CoM-overhang cantilever must be CRA-unstable: {canti.Message}");

            var hAsm = BuildAssembly(HModel(), out _);
            var rbe = MasonryStabilityChecker.Check(hAsm, gravityZ: G);
            Assert.True(rbe.IsStable,
                $"pin: force-only RBE is expected to accept the H-model via self-stress, got: {rbe.Message}");
            var craH = CraStabilityChecker.Check(hAsm, gravityZ: G);
            Assert.False(craH.IsStable,
                $"CRA must reject the H-model (the squeeze is kinematically impossible): {craH.Message}");
        }

        // =========================================================================
        // Independent oracles + fixtures (no solver internals used below).
        // =========================================================================

        // key -> [f_n_pos, f_n_neg, f_t1, f_t2] per (interface, vertex).
        static Dictionary<long, double[]> Decode(DetailedStabilityResult d)
        {
            var m = new Dictionary<long, double[]>();
            foreach (var vf in d.VertexForces)
            {
                long key = ((long)vf.InterfaceIndex << 32) | (uint)vf.VertexIndex;
                m[key] = new[] { vf.FnPos, vf.FnNeg, vf.Ft1, vf.Ft2 };
            }
            return m;
        }

        // Rebuild the per-free-block equilibrium residual Aeq f - B from geometry:
        // for each interface incident to a free block, sum sign*(f_n*n + f_t1*t1 +
        // f_t2*t2) into the force rows and sign*(r x that) into the moment rows
        // (r = contact vertex - block CoM), then subtract gravity B_z = mass*g.
        // sign = +1 for block A, -1 for block B (EquilibriumMatrixBuilder convention).
        static void Equilibrium(
            MasonryAssembly asm, Dictionary<string, (double cx, double cy, double cz, double mass)> info,
            Dictionary<long, double[]> fmap,
            out double maxForceAbs, out double maxMomentAbs, out double forceScale, out double momScale)
        {
            maxForceAbs = 0; maxMomentAbs = 0; forceScale = 1e-9; double charLen = 1e-9;
            foreach (var kv in info)
            {
                string id = kv.Key;
                if (asm.BoundaryConditions.IsFixed(id)) continue;
                var (cx, cy, cz, mass) = kv.Value;
                double w = Math.Abs(mass * G);
                if (w > forceScale) forceScale = w;

                double Fx = 0, Fy = 0, Fz = 0, Mx = 0, My = 0, Mz = 0;
                for (int ii = 0; ii < asm.Interfaces.Count; ii++)
                {
                    var iface = asm.Interfaces[ii];
                    double sign;
                    if (iface.BlockAId == id) sign = +1.0;
                    else if (iface.BlockBId == id) sign = -1.0;
                    else continue;
                    double nx = iface.NormalX, ny = iface.NormalY, nz = iface.NormalZ;
                    double t1x = iface.Tangent1X, t1y = iface.Tangent1Y, t1z = iface.Tangent1Z;
                    double t2x = iface.Tangent2X, t2y = iface.Tangent2Y, t2z = iface.Tangent2Z;
                    for (int v = 0; v < iface.VertexCount; v++)
                    {
                        long key = ((long)ii << 32) | (uint)v;
                        if (!fmap.TryGetValue(key, out var f)) continue;
                        double fn = f[0] - f[1];
                        double fvx = fn * nx + f[2] * t1x + f[3] * t2x;
                        double fvy = fn * ny + f[2] * t1y + f[3] * t2y;
                        double fvz = fn * nz + f[2] * t1z + f[3] * t2z;
                        Fx += sign * fvx; Fy += sign * fvy; Fz += sign * fvz;
                        var p = iface.ContactPolygon[v];
                        double rx = p.X - cx, ry = p.Y - cy, rz = p.Z - cz;
                        double rlen = Math.Sqrt(rx * rx + ry * ry + rz * rz);
                        if (rlen > charLen) charLen = rlen;
                        Mx += sign * (ry * fvz - rz * fvy);
                        My += sign * (rz * fvx - rx * fvz);
                        Mz += sign * (rx * fvy - ry * fvx);
                    }
                }
                Fz -= mass * G; // residual = Aeq f - B, B_z = mass*g (BuildPhysicsCorrected)
                double fres = Math.Sqrt(Fx * Fx + Fy * Fy + Fz * Fz);
                double mres = Math.Sqrt(Mx * Mx + My * My + Mz * Mz);
                if (fres > maxForceAbs) maxForceAbs = fres;
                if (mres > maxMomentAbs) maxMomentAbs = mres;
            }
            momScale = forceScale * charLen; // moment scale = force * lever arm
        }

        // No tension (f_n- ~ 0, net f_n >= 0) and inside the TRUE Coulomb cone
        // |f_t| <= mu*f_n. The checker builds an INSCRIBED polyhedral pyramid, so a
        // feasible tangential force never exceeds the true cone of the ORIGINAL mu.
        static void Admissibility(
            Dictionary<long, double[]> fmap, double mu,
            out double maxTension, out double maxCone, out double maxNegNormal, out double maxCompression)
        {
            maxTension = 0; maxCone = 0; maxNegNormal = 0; maxCompression = 0;
            foreach (var f in fmap.Values) if (f[0] > maxCompression) maxCompression = f[0];
            foreach (var f in fmap.Values)
            {
                double fnPos = f[0], fnNeg = f[1];
                double fn = fnPos - fnNeg;
                if (fnNeg > maxTension) maxTension = fnNeg;
                if (-fn > maxNegNormal) maxNegNormal = -fn;
                double ft = Math.Sqrt(f[2] * f[2] + f[3] * f[3]);
                double viol = ft - mu * fnPos;
                if (viol > maxCone) maxCone = viol;
            }
        }

        struct BoxDef { public double x0, y0, z0, x1, y1, z1; }

        static BoxDef B(double x0, double y0, double z0, double x1, double y1, double z1) =>
            new BoxDef { x0 = x0, y0 = y0, z0 = z0, x1 = x1, y1 = y1, z1 = z1 };

        // Two-box vertical stack: ground (fixed) + one free block on top.
        static List<BoxDef> Stack2() => new List<BoxDef> {
            B(0, 0, 0.0, 1, 1, 0.5), B(0, 0, 0.5, 1, 1, 1.0) };

        // Three-box tower: two free blocks.
        static List<BoxDef> Tower3() => new List<BoxDef> {
            B(0, 0, 0.0, 1, 1, 0.5), B(0, 0, 0.5, 1, 1, 1.0), B(0, 0, 1.0, 1, 1, 1.5) };

        // Cantilever: upper box CoM (x=1.3) overhangs the support edge (x=1.0).
        static List<BoxDef> Cantilever() => new List<BoxDef> {
            B(0.0, 0, 0.0, 1.0, 1, 0.5), B(0.8, 0, 0.5, 1.8, 1, 1.0) };

        // Kao 2022 H-model: a beam bridging two columns through vertical faces only.
        static List<BoxDef> HModel() => new List<BoxDef> {
            B(0.0, 0, 0.0, 0.4, 0.4, 1.2), B(1.0, 0, 0.0, 1.4, 0.4, 1.2), B(0.4, 0, 0.6, 1.0, 0.4, 0.9) };

        static void Box(BoxDef b, out List<double> coords, out List<int> tris)
        {
            coords = new List<double>
            {
                b.x0,b.y0,b.z0,  b.x1,b.y0,b.z0,  b.x1,b.y1,b.z0,  b.x0,b.y1,b.z0,
                b.x0,b.y0,b.z1,  b.x1,b.y0,b.z1,  b.x1,b.y1,b.z1,  b.x0,b.y1,b.z1,
            };
            tris = new List<int>
            {
                0,2,1, 0,3,2,   4,5,6, 4,6,7,
                0,1,5, 0,5,4,   2,3,7, 2,7,6,
                0,4,7, 0,7,3,   1,2,6, 1,6,5,
            };
        }

        // Build the assembly via the shipping proximity contact detector (the
        // proven CraStabilityCheckerTests pattern), fixing the lowest course, and
        // return each block's exact CoM + mass (box centre, density*volume) for the
        // independent oracle — computed from the box definition, not any kernel.
        static MasonryAssembly BuildAssembly(
            List<BoxDef> boxes, out Dictionary<string, (double cx, double cy, double cz, double mass)> info,
            double fixBelowZ = 1e-3)
        {
            var snaps = new List<MeshSnapshot>();
            var ids = new List<string>();
            var coordsList = new List<List<double>>();
            var trisList = new List<List<int>>();
            var minZ = new List<double>();
            double globalMin = double.MaxValue;
            for (int i = 0; i < boxes.Count; i++)
            {
                Box(boxes[i], out var c, out var t);
                coordsList.Add(c); trisList.Add(t);
                snaps.Add(new MeshSnapshot(c, t));
                ids.Add("blk_" + i.ToString("00"));
                double mz = Math.Min(boxes[i].z0, boxes[i].z1);
                minZ.Add(mz);
                if (mz < globalMin) globalMin = mz;
            }
            var ifaces = MeshContactDetector.Detect(snaps, ids);
            var blocks = new List<MasonryBlock>();
            var fixedIds = new List<string>();
            info = new Dictionary<string, (double, double, double, double)>();
            for (int i = 0; i < boxes.Count; i++)
            {
                blocks.Add(new MasonryBlock(ids[i], coordsList[i], trisList[i], Density));
                if (minZ[i] <= globalMin + fixBelowZ) fixedIds.Add(ids[i]);
                var b = boxes[i];
                double vol = Math.Abs((b.x1 - b.x0) * (b.y1 - b.y0) * (b.z1 - b.z0));
                info[ids[i]] = ((b.x0 + b.x1) / 2, (b.y0 + b.y1) / 2, (b.z0 + b.z1) / 2, Density * vol);
            }
            return new MasonryAssembly(blocks, ifaces, new BoundaryConditions(fixedIds));
        }
    }
}
