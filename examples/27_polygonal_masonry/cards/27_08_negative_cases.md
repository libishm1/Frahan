# Card 08 — Negative cases (chain-monotonicity + crossing)

## Component

`Frahan > Masonry > Polygonal Masonry Sequence (2D)`

## Fixture

**Easiest**: open `08_negative_cases.gh`. Both 8a and 8b are wired up
side by side; both component instances should display red runtime
errors.

**From scratch**: open `08_negative_cases.3dm`. Two sub-tests live in
this file, each with its own rectangle and chains.

### 8a — non-monotone chain

- **Wall_8a**: rectangle at x in [0, 10], y in [0, 8].
- **NEG_8a_non_monotone**: a single polyline whose x dips backwards.

Wire that rectangle to `Wall` and that polyline to `Chains`.

Expected: a red runtime-error bubble on the component reading
something like `"chain 0 is not monotone in x or y"`. No output
geometry produced.

### 8b — crossing chains

- **Wall_8b**: rectangle at x in [12, 22], y in [0, 8].
- **NEG_8b_crossing**: two polylines that form an X.

Wire the second rectangle to `Wall` and BOTH crossing polylines
to `Chains`.

Expected: either a runtime-error bubble (rule (8) violation) OR an
incorrect-looking arrangement on the canvas. The component MUST NOT
silently emit a plausible-looking but wrong install order.

## Pass / fail

```
Date: ____________
8a verdict: PASS / FAIL (error surfaced as expected?)
8b verdict: PASS / FAIL (error surfaced OR clear failure visible?)
Notes:
```

## Notes

TWO separate runs in this card. (8a) Wire only the non-monotone chain and the rectangle. Expected: the component raises an error 'chain is not monotone in x or y'. (8b) Wire both crossing chains and the rectangle. Expected: either a rule (8) violation error OR an incorrect arrangement; either way the component should NOT silently produce nonsense.
