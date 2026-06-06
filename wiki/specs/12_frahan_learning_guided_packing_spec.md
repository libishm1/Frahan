# 12 - Frahan Learning-Guided Packing Spec

**Spec version:** 0.1 (proposed-only; gated)
**Sources:** runbook §§ 4 (item 10) and 15.5,
`Template-General/wiki/local_ai_workflow/frahan_agent_rules.md`,
`frahan/Frahan_MASTER_RESEARCH_KNOWLEDGE_BASE_v0_2_20260503.md` (ML
chapters), `Template-General/wiki/graphml_baseline_comparison/`.

## 1. Gate

Learning-guided packing **only proceeds after** the deterministic
heuristics in specs **05–10** are stable, tested (V2 evidence per
spec 13), and shipping in a tagged release.

Until that gate is passed, this module is **proposed-only** and the
solver always uses the deterministic path.

## 2. Goal

Replace or augment the **ordering** and **candidate-pruning** stages
of Frahan packing solvers with a learned model. The model is **never**
the final arbiter - every learned suggestion is validated by the
deterministic placement check.

## 3. Where learning is allowed

| Pipeline stage | Learning role | Why |
| --- | --- | --- |
| extract descriptors | feature pipeline only (no learning yet) | deterministic, reproducible |
| match compatible features | candidate-pair scoring | a learned scorer can prune by 10–100× |
| generate candidates | candidate ordering | learned priority over yield-first heuristic |
| validate with original geometry | **never** learned | safety-critical |
| refine or rank | learned ranker | secondary to deterministic yield |
| suggest trims or cuts | optional learned suggestion | deterministic feasibility check still mandatory |

## 4. Frameworks (per AGENTS.md GraphML rules)

- **PyTorch Geometric (PyG)** is the default 2026 framework.
- **DGL** is supported only for existing pipelines (no major release
  since late 2024).
- **SVM baseline** is mandatory for every learned model (per AGENTS.md
  GraphML rules).
- **Reproducibility** - split, features, metric, seed, hardware
  recorded with every experiment summary.

## 5. Where the learned model lives (deployment)

- Training runs **outside** Frahan (Python, PyG, or DGL).
- Inference runs **inside** Frahan via:
  - Option A - `Microsoft.ML.OnnxRuntime` consuming an exported ONNX
    model (preferred; pure managed; license MIT).
  - Option B - embedded Python via `Python.NET` or pythonnet (ruled
    out today: deployment friction with Rhino's CPython 3 in
    ScriptEditor; logged as future work).
- Frahan exposes a `LearnedScorer` interface in
  `Frahan.Core.Learning` (proposed) that returns a `double` per
  candidate.

## 6. Frahan-owned DTOs (proposed)

```csharp
public sealed class FeatureVector { /* fixed-length double[] */ }
public sealed class CandidateScore { /* CandidateId, Score, ModelVersion */ }
public interface ILearnedScorer
{
    string ModelName { get; }
    Version ModelVersion { get; }
    CandidateScore Score(FeatureVector features);
}
```

## 7. Acceptance contract for any learned scorer

- Reproducibility metadata file (`.meta.json`) ships with every
  `.onnx`: training data SHA, dataset version, framework, hardware,
  seed, and split.
- `LearnedScorer` returns a deterministic score given the same input
  (no internal randomness).
- A `Frahan Native Backend Status`-style component reports the
  loaded scorer for diagnostic purposes.

## 8. Validation rules

- The learned scorer is **never** the final arbiter - the
  deterministic placement check (`MinDistPolys` /
  `Frahan.NativeBridge.PackingBackend.Validate`) still runs.
- A learned scorer is only enabled if its baseline-comparison report
  shows **no regression** vs the deterministic ordering on the
  benchmark suite (spec **13**).
- An audit log records every learned-scorer decision so a human can
  trace any unexpected packing result.

## 9. Performance targets

- Inference ≤ 1 ms per candidate on a 4-core CPU (ONNX Runtime).
- Inference ≤ 0.1 ms per candidate on a GPU (proposed; not required
  for v1).

## 10. Tests required

- Unit: `LearnedScorer.Score` returns the same `Score` for the same
  `FeatureVector`.
- Integration: full pack run with learned scorer enabled produces a
  `PackResult.FillRatio` ≥ deterministic baseline on the benchmark
  fixture (no regression).
- Regression: baseline comparison run on every commit that touches
  the scorer.

## 11. Out of scope for v1

- Online / on-policy training (offline only).
- Federated training across users.
- Reinforcement-learning solver-as-policy (not before deterministic
  heuristics are stable; not before this spec is unblocked).
