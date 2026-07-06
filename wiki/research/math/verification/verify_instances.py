"""Machine-verification of published equation instances (SMT / Z3).

Complements the Lean formalization plan (LEAN_PLAN.md): where full
mechanization is pending, decidable instances of the published theorems are
proved with Z3 (quantified linear real arithmetic). Each check encodes the
NEGATION of the instance; `unsat` = the instance is proved.

Run:  pip install z3-solver && python verify_instances.py
Expected output: two lines ending in PROVED.
"""
from z3 import Reals, And, Exists, ForAll, Implies, Not, Solver, unsat


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


if __name__ == "__main__":
    ok = True
    ok &= check("NFP unit-square instance (EQUATIONS.md 1.2)", nfp_unit_squares())
    ok &= check("IFP erosion instance (EQUATIONS.md 1.3)", ifp_erosion())
    raise SystemExit(0 if ok else 1)
