"""Machine-verification of published equation instances (SMT / Z3).

Complements the Lean formalization plan (LEAN_PLAN.md): where full
mechanization is pending, decidable instances of the published theorems are
proved with Z3 (quantified linear real arithmetic). Each check encodes the
NEGATION of the instance; `unsat` = the instance is proved.

Run:  pip install z3-solver && python verify_instances.py
Expected output: two lines ending in PROVED.
"""
from z3 import Reals, And, Or, Exists, ForAll, Implies, Not, Solver, unsat


def check(name, formula):
    s = Solver()
    s.add(Not(formula))
    verdict = s.check()
    status = "PROVED (negation unsat)" if verdict == unsat else f"FAILED: {s.model()}"
    print(f"{name}: {status}")
    return verdict == unsat


def nfp_unit_squares():
    """EQUATIONS.md 1.2 instance: unit squares -> NFP = [-1,1]^2.

    forall p: (exists a in A, b in B: a = b + p)  <->  p in [-1,1]^2
    """
    px, py, ax, ay, bx, by = Reals("px py ax ay bx by")
    in_a = And(0 <= ax, ax <= 1, 0 <= ay, ay <= 1)
    in_b = And(0 <= bx, bx <= 1, 0 <= by, by <= 1)
    overlap = Exists([ax, ay, bx, by], And(in_a, in_b, ax == bx + px, ay == by + py))
    in_nfp = And(-1 <= px, px <= 1, -1 <= py, py <= 1)
    return ForAll([px, py], overlap == in_nfp)


def ifp_erosion():
    """EQUATIONS.md 1.3 instance: [0,5]^2 erode [0,1]^2 = [0,4]^2.

    forall p: (forall b in B: b + p in S)  <->  p in [0,4]^2
    """
    px, py, bx, by = Reals("px py bx by")
    in_b = And(0 <= bx, bx <= 1, 0 <= by, by <= 1)
    shifted_in_s = And(0 <= bx + px, bx + px <= 5, 0 <= by + py, by + py <= 5)
    containment = ForAll([bx, by], Implies(in_b, shifted_in_s))
    in_ifp = And(0 <= px, px <= 4, 0 <= py, py <= 4)
    return ForAll([px, py], containment == in_ifp)


def inscribed_pyramid_in_cone():
    """MASONRY_STABILITY: the shipping INNER friction pyramid is a subset of the
    true Coulomb cone (so an RBE certificate on it is conservative).

    For K=4, mu_eff = mu*cos(pi/4) gives c^2 = (mu^2/2) fn^2 per axis. Squaring
    to stay in polynomial arithmetic (ft1s = ft1^2, ft2s = ft2^2 >= 0):
    (|ft1|<=c and |ft2|<=c)  =>  ft1^2 + ft2^2 <= mu^2 fn^2.
    """
    ft1s, ft2s, fn, mu, c2 = Reals("ft1s ft2s fn mu c2")
    hyp = And(fn >= 0, mu >= 0, ft1s >= 0, ft2s >= 0,
              c2 == (mu * mu / 2) * fn * fn, ft1s <= c2, ft2s <= c2)
    concl = ft1s + ft2s <= mu * mu * fn * fn
    return ForAll([ft1s, ft2s, fn, mu, c2], Implies(hyp, concl))


def blf_lexmin_at_vertex():
    """EQUATIONS.md 1.5: the bottom-left lexicographic (y, x) minimum over a box
    feasible region is attained at the corner vertex (x0, y0).

    forall p in [x0,x1]x[y0,y1]:  y0 < p.y  OR  (y0 = p.y AND x0 <= p.x).
    """
    px, py, x0, x1, y0, y1 = Reals("px py x0 x1 y0 y1")
    box = And(x0 <= x1, y0 <= y1)
    in_f = And(x0 <= px, px <= x1, y0 <= py, py <= y1)
    minimal = Or(y0 < py, And(y0 == py, x0 <= px))
    return ForAll([px, py, x0, x1, y0, y1], Implies(And(box, in_f), minimal))


def inscribed_pyramid_in_cone_k8_shipping():
    """MASONRY_STABILITY: the SHIPPING configuration — the K=8 inscribed
    pyramid emitted by FrictionConeBuilder (MasonryStabilityChecker default)
    is a subset of the true Coulomb cone, so the RBE verdict is conservative.

    Facet normals at theta_k = 2*pi*k/8 use cos/sin in {0, +-1, +-c4} with
    2*c4^2 = 1 (cos pi/4); mu_eff = mu*c8 with 2*c8^2 = 1 + c4 (the half-angle
    identity for cos pi/8). Quantifier-free QF_NRA: hypotheses AND negated
    conclusion, unsat = the implication holds for all values.
    """
    from z3 import Reals as R
    ft1, ft2, fn, mu, c4, c8, e = R("ft1 ft2 fn mu c4 c8 e")
    hyp = And(
        c4 > 0, 2 * c4 * c4 == 1,
        c8 > 0, 2 * c8 * c8 == 1 + c4,
        fn >= 0, mu >= 0, e == mu * c8 * fn,
        ft1 <= e, c4 * (ft1 + ft2) <= e, ft2 <= e, c4 * (-ft1 + ft2) <= e,
        -ft1 <= e, c4 * (-ft1 - ft2) <= e, -ft2 <= e, c4 * (ft1 - ft2) <= e,
    )
    # returned as a closed implication for the shared negation-unsat checker
    concl = ft1 * ft1 + ft2 * ft2 <= mu * mu * fn * fn
    return ForAll([ft1, ft2, fn, mu, c4, c8, e], Implies(hyp, concl))


if __name__ == "__main__":
    ok = True
    ok &= check("NFP unit-square instance (EQUATIONS.md 1.2)", nfp_unit_squares())
    ok &= check("IFP erosion instance (EQUATIONS.md 1.3)", ifp_erosion())
    ok &= check("Inscribed friction pyramid subset-of-cone, K=4 (MASONRY_STABILITY)",
                inscribed_pyramid_in_cone())
    ok &= check("Inscribed friction pyramid subset-of-cone, K=8 SHIPPING config",
                inscribed_pyramid_in_cone_k8_shipping())
    ok &= check("BLF lex-min at box vertex (EQUATIONS.md 1.5)", blf_lexmin_at_vertex())
    raise SystemExit(0 if ok else 1)
