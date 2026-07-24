import FrahanProofs.Common

/-!
Frahan StonePack — identical-machines list scheduling (cut-station / batch
load balancing).

Mechanizes the makespan side of tex Theorem `thm:lpt` (Graham 1969, "Graham
LPT bound"): assigning cut/finish jobs to `m` identical machines, how far can
a simple rule land above the optimum makespan `C*`.

Formalized here (PROVED, no sorry):
  * `load` / `totalWork` / `makespan` for an assignment `a : J → Fin m`.
  * The two OPTIMUM LOWER BOUNDS that hold for ANY assignment (hence for the
    optimum, and hence lower-bound `C*`):
      - `opt_ge_avg`      : `(∑ p) / m ≤ makespan a`   (max load ≥ average load)
      - `opt_ge_max_job`  : `p j ≤ makespan a`         (the machine running `j`)
  * `greedy_makespan_bound` — the clean, fully provable relative of Graham's
    theorem: the GREEDY / list-scheduling `(2 − 1/m)` bound, from the explicit
    list-schedule certificate (`makespan = start + q`, `m·start ≤ total − q`,
    `total ≤ m·C*`, `q ≤ C*`). `makespan_greedy_le` restates it on `makespan`.
  * `lpt_makespan_bound` — the arithmetic CORE of the tight `4/3 − 1/(3m)`
    ratio, from the same certificate PLUS the LPT counting hypothesis
    (`0 < start → 3q ≤ C*`: a machine with ≥2 jobs has a small last job).

Left as `proof_wanted` (statements exact; proofs need the execution-trace
layer, staged like the other Tier-1 `[D]` rows of `Roadmap.lean`):
  * `list_schedule_decomposition` — an actual greedy list schedule realizes the
    `greedy_makespan_bound` certificate (`makespan = start + q`, `m·start ≤ …`).
  * `lpt_tight_bound` — the headline tex `thm:lpt`: the LPT schedule's makespan
    is `≤ (4/3 − 1/(3m))·C*` for the true optimum `C*` (Graham 1969). Discharged
    by composing `lpt_makespan_bound` with the trace + counting facts.

Nothing here uses `sorry` or a new `axiom`.
-/

namespace Frahan

section ListScheduling

variable {m : ℕ} {J : Type*} [Fintype J]

/-- Load of machine `k` under assignment `a`: total processing time of the jobs
sent to `k`. -/
def load (p : J → ℝ) (a : J → Fin m) (k : Fin m) : ℝ :=
  ∑ j ∈ Finset.univ.filter (fun j => a j = k), p j

/-- Total processing time over all jobs (`= m · average load`). -/
def totalWork (p : J → ℝ) : ℝ := ∑ j, p j

/-- Makespan of an assignment: the largest machine load. Needs `0 < m` so at
least one machine exists (the `Finset.sup'` nonempty witness). -/
noncomputable def makespan (hm : 0 < m) (p : J → ℝ) (a : J → Fin m) : ℝ :=
  Finset.univ.sup' ⟨⟨0, hm⟩, Finset.mem_univ _⟩ (load p a)

/-- Each job's own machine carries at least that job's time (all times ≥ 0). -/
theorem le_load (p : J → ℝ) (hp : ∀ j, 0 ≤ p j) (a : J → Fin m) (j : J) :
    p j ≤ load p a (a j) := by
  unfold load
  refine Finset.single_le_sum (fun i _ => hp i) ?_
  simp

/-- Every machine load is ≤ the makespan (it is a `sup'`). -/
theorem load_le_makespan (hm : 0 < m) (p : J → ℝ) (a : J → Fin m) (k : Fin m) :
    load p a k ≤ makespan hm p a := by
  unfold makespan
  exact Finset.le_sup' (load p a) (Finset.mem_univ k)

/-- tex `thm:lpt`, first optimum lower bound (`max-job`): for ANY assignment the
makespan is at least each job's processing time, so the OPTIMUM makespan is too.
`C* ≥ max_j p_j`. -/
theorem opt_ge_max_job (hm : 0 < m) (p : J → ℝ) (hp : ∀ j, 0 ≤ p j)
    (a : J → Fin m) (j : J) : p j ≤ makespan hm p a :=
  le_trans (le_load p hp a j) (load_le_makespan hm p a (a j))

/-- The machine loads partition the total work: `∑ₖ load k = ∑ⱼ pⱼ`. -/
theorem sum_load (p : J → ℝ) (a : J → Fin m) :
    (∑ k, load p a k) = totalWork p :=
  Finset.sum_fiberwise Finset.univ a p

/-- tex `thm:lpt`, second optimum lower bound (`average`): for ANY assignment
the makespan is at least the average load `total / m`, so the OPTIMUM makespan
is too. `C* ≥ (∑ p) / m`. -/
theorem opt_ge_avg (hm : 0 < m) (p : J → ℝ) (a : J → Fin m) :
    totalWork p / (m : ℝ) ≤ makespan hm p a := by
  have hm_pos : (0 : ℝ) < m := by exact_mod_cast hm
  have hbound :
      (∑ k, load p a k) ≤
        (Finset.univ : Finset (Fin m)).card • makespan hm p a :=
    Finset.sum_le_card_nsmul _ _ _ (fun k _ => load_le_makespan hm p a k)
  rw [Finset.card_fin, sum_load p a, nsmul_eq_mul] at hbound
  -- hbound : totalWork p ≤ ↑m * makespan
  rw [div_le_iff₀ hm_pos]
  linarith [hbound, mul_comm (m : ℝ) (makespan hm p a)]

/-- The GREEDY / list-scheduling `(2 − 1/m)` bound, from an explicit
list-schedule certificate. This is the honest, fully-formalized relative of
Graham's LPT theorem: model the makespan machine `k*` as `load k* = start + q`,
where `q` is the last job placed on it and `start` its load just before. A list
schedule places `j*` on a least-loaded machine, so every machine had load
≥ `start`, giving `m·start ≤ total − q`. With `total ≤ m·C*` and `q ≤ C*`:

`C_greedy = start + q ≤ total/m + (1 − 1/m)·q ≤ (2 − 1/m)·C*`. -/
theorem greedy_makespan_bound (hm : 0 < m)
    {cGreedy start q total cStar : ℝ}
    (hmk : cGreedy = start + q)
    (hstart : (m : ℝ) * start ≤ total - q)
    (htot : total ≤ (m : ℝ) * cStar)
    (hq : q ≤ cStar) :
    cGreedy ≤ (2 - 1 / (m : ℝ)) * cStar := by
  have hm_pos : (0 : ℝ) < m := by exact_mod_cast hm
  have hm_ne : (m : ℝ) ≠ 0 := ne_of_gt hm_pos
  have hM1 : (1 : ℝ) ≤ m := by exact_mod_cast hm
  have heq : (2 - 1 / (m : ℝ)) * cStar = (2 * m - 1) * cStar / m := by
    field_simp
  rw [hmk, heq, le_div_iff₀ hm_pos]
  nlinarith [hstart, htot, hq, hM1,
    mul_nonneg (by linarith : (0:ℝ) ≤ cStar - q) (by linarith : (0:ℝ) ≤ (m:ℝ) - 1)]

/-- `greedy_makespan_bound` restated directly on `makespan`: an assignment whose
makespan machine decomposes as `start + p j*` under a list schedule satisfies
`makespan ≤ (2 − 1/m)·C*`, using the optimum lower bounds `total ≤ m·C*`
(from `opt_ge_avg`) and `p j* ≤ C*` (from `opt_ge_max_job`). -/
theorem makespan_greedy_le (hm : 0 < m) (p : J → ℝ) (a : J → Fin m)
    {jstar : J} {start cStar : ℝ}
    (hmk : makespan hm p a = start + p jstar)
    (hstart : (m : ℝ) * start ≤ totalWork p - p jstar)
    (hopt_avg : totalWork p ≤ (m : ℝ) * cStar)
    (hopt_job : p jstar ≤ cStar) :
    makespan hm p a ≤ (2 - 1 / (m : ℝ)) * cStar :=
  greedy_makespan_bound hm hmk hstart hopt_avg hopt_job

/-- The arithmetic CORE of tex `thm:lpt`'s tight `4/3 − 1/(3m)` ratio, from the
list-schedule certificate PLUS the LPT counting hypothesis `hlpt`: on the
critical machine, if it runs more than one job (`0 < start`) then its last —
hence (by LPT ordering) smallest — job is small, `3q ≤ C*`. Graham 1969. Two
cases: single job (`start = 0`, `C_LPT = q ≤ C*`) or `3q ≤ C*`, both landing
under `(4/3 − 1/(3m))·C*`. The hypothesis `hlpt` is exactly the counting lemma
`proof_wanted` below discharges from the execution trace. -/
theorem lpt_makespan_bound (hm : 0 < m)
    {cLPT start q total cStar : ℝ}
    (hmk : cLPT = start + q)
    (hstart : (m : ℝ) * start ≤ total - q)
    (htot : total ≤ (m : ℝ) * cStar)
    (hq : q ≤ cStar) (hq_nn : 0 ≤ q) (hstart_nn : 0 ≤ start)
    (hlpt : 0 < start → 3 * q ≤ cStar) :
    cLPT ≤ (4 / 3 - 1 / (3 * (m : ℝ))) * cStar := by
  have hm_pos : (0 : ℝ) < m := by exact_mod_cast hm
  have hm_ne : (m : ℝ) ≠ 0 := ne_of_gt hm_pos
  have hM1 : (1 : ℝ) ≤ m := by exact_mod_cast hm
  have h3m : (0 : ℝ) < 3 * m := mul_pos (by norm_num) hm_pos
  have hcStar_nn : 0 ≤ cStar := le_trans hq_nn hq
  have heq : (4 / 3 - 1 / (3 * (m : ℝ))) * cStar = (4 * m - 1) * cStar / (3 * m) := by
    field_simp
  rw [hmk, heq, le_div_iff₀ h3m]
  rcases hstart_nn.eq_or_lt with hs0 | hspos
  · -- single job on the critical machine: start = 0
    rw [← hs0]
    nlinarith [hq, hcStar_nn, hM1,
      mul_nonneg hcStar_nn (by linarith : (0:ℝ) ≤ (m:ℝ) - 1),
      mul_nonneg (sub_nonneg.mpr hq) hm_pos.le]
  · -- ≥ 2 jobs: the LPT counting hypothesis gives 3q ≤ C*
    have h3q : 3 * q ≤ cStar := hlpt hspos
    nlinarith [hstart, htot, h3q, hM1, hm_pos,
      mul_nonneg (by linarith : (0:ℝ) ≤ cStar - 3 * q) (by linarith : (0:ℝ) ≤ (m:ℝ) - 1)]

/-! ### The execution-trace layer (`proof_wanted`)

The two bounds above take the list-schedule certificate as hypotheses. What
remains — building that certificate from an actual greedy run, and the LPT
counting lemma — is the execution-trace work staged across `Roadmap.lean`'s
Tier-1 `[D]` rows. We state it exactly, over a faithful list-schedule model.
-/

/-- Processing time on machine `k` of the jobs in the prefix list `js` (the jobs
already placed, in the schedule's processing order). -/
def listLoad (p : J → ℝ) (a : J → Fin m) (js : List J) (k : Fin m) : ℝ :=
  (js.filterMap (fun j => if a j = k then some (p j) else none)).sum

/-- `a` is a LIST SCHEDULE for order `js`: `js` enumerates every job once, and
each job is placed on a machine of minimal load among the jobs before it
(greedy least-loaded). -/
def IsListSchedule (p : J → ℝ) (a : J → Fin m) (js : List J) : Prop :=
  js.Nodup ∧ (∀ j, j ∈ js) ∧
    ∀ (i : ℕ) (hi : i < js.length) (k : Fin m),
      listLoad p a (js.take i) (a (js.get ⟨i, hi⟩)) ≤ listLoad p a (js.take i) k

/-- `a` is the LPT (longest-processing-time-first) schedule: a list schedule
whose order is non-increasing in processing time. Graham 1969. -/
def IsLPTSchedule (p : J → ℝ) (a : J → Fin m) (js : List J) : Prop :=
  IsListSchedule p a js ∧ js.Pairwise (fun i j => p j ≤ p i)

/-! #### `listLoad` bookkeeping lemmas (support for the trace layer below) -/

/-- `listLoad` of a cons: split off the head's contribution. -/
private theorem listLoad_cons (p : J → ℝ) (a : J → Fin m) (j : J) (l : List J)
    (k : Fin m) :
    listLoad p a (j :: l) k = (if a j = k then p j else 0) + listLoad p a l k := by
  by_cases h : a j = k <;> simp [listLoad, h]

/-- `listLoad` distributes over list append. -/
private theorem listLoad_append (p : J → ℝ) (a : J → Fin m) (l₁ l₂ : List J)
    (k : Fin m) :
    listLoad p a (l₁ ++ l₂) k = listLoad p a l₁ k + listLoad p a l₂ k := by
  unfold listLoad
  rw [List.filterMap_append, List.sum_append]

/-- A machine that receives none of the listed jobs carries zero load. -/
private theorem listLoad_eq_zero (p : J → ℝ) (a : J → Fin m) (l : List J) (k : Fin m)
    (h : ∀ j ∈ l, a j ≠ k) : listLoad p a l k = 0 := by
  induction l with
  | nil => simp [listLoad]
  | cons j t ih =>
    rw [listLoad_cons, if_neg (h j (by simp)), zero_add]
    exact ih (fun x hx => h x (List.mem_cons_of_mem j hx))

/-- Summing every machine's `listLoad` over a prefix recovers that prefix's
total processing time. -/
private theorem sum_listLoad (p : J → ℝ) (a : J → Fin m) (l : List J) :
    (∑ k, listLoad p a l k) = (l.map p).sum := by
  induction l with
  | nil => simp [listLoad]
  | cons j t ih =>
    have hcongr : (∑ k, listLoad p a (j :: t) k)
        = ∑ k : Fin m, ((if a j = k then p j else 0) + listLoad p a t k) :=
      Finset.sum_congr rfl (fun k _ => listLoad_cons p a j t k)
    rw [hcongr, Finset.sum_add_distrib, ih, List.map_cons, List.sum_cons]
    have hite : (∑ k : Fin m, if a j = k then p j else 0) = p j := by
      simp [Finset.sum_ite_eq]
    rw [hite]

/-- On a nodup list, `listLoad` equals the Finset sum over the jobs sent to `k`. -/
private theorem listLoad_eq_finsetSum [DecidableEq J] (p : J → ℝ) (a : J → Fin m)
    (k : Fin m) :
    ∀ {l : List J}, l.Nodup →
      listLoad p a l k = ∑ j ∈ l.toFinset.filter (fun j => a j = k), p j
  | [], _ => by simp [listLoad]
  | j :: t, hl => by
    rw [List.nodup_cons] at hl
    obtain ⟨hjt, ht⟩ := hl
    have hjnotin : j ∉ t.toFinset.filter (fun j => a j = k) := by
      simp only [Finset.mem_filter, List.mem_toFinset, not_and]
      exact fun hc => absurd hc hjt
    rw [listLoad_cons, listLoad_eq_finsetSum p a k ht, List.toFinset_cons,
      Finset.filter_insert]
    by_cases h : a j = k
    · rw [if_pos h, if_pos h, Finset.sum_insert hjnotin]
    · rw [if_neg h, if_neg h, zero_add]

/-- On the full (nodup, exhaustive) job list, `listLoad` equals `load`. -/
private theorem listLoad_full [DecidableEq J] (p : J → ℝ) (a : J → Fin m) (k : Fin m)
    {js : List J} (hnodup : js.Nodup) (hall : ∀ j, j ∈ js) :
    listLoad p a js k = load p a k := by
  rw [listLoad_eq_finsetSum p a k hnodup]
  unfold load
  apply Finset.sum_congr _ (fun _ _ => rfl)
  ext j
  simp [Finset.mem_filter, List.mem_toFinset, hall j]

/-- Trace fact for the greedy bound: an actual list schedule realizes the
`greedy_makespan_bound` certificate — its makespan is the last job placed on the
critical machine, and every machine was at least as loaded when that job landed
(`m·start ≤ total − q`). Composed with `makespan_greedy_le` this yields the
end-to-end `(2 − 1/m)` guarantee. Needs the greedy-run induction (staged). -/
theorem list_schedule_decomposition (hm : 0 < m) (p : J → ℝ)
    (hp : ∀ j, 0 ≤ p j) (a : J → Fin m) (js : List J)
    (hls : IsListSchedule p a js) (hne : js ≠ []) :
    ∃ (jstar : J) (start : ℝ),
      makespan hm p a = start + p jstar ∧
        (m : ℝ) * start ≤ totalWork p - p jstar := by
  classical
  obtain ⟨hnodup, hall, hmin⟩ := hls
  -- A critical machine `kstar` attains the makespan.
  have hne_univ : (Finset.univ : Finset (Fin m)).Nonempty := ⟨⟨0, hm⟩, Finset.mem_univ _⟩
  obtain ⟨kstar, -, hks⟩ := Finset.exists_mem_eq_sup' hne_univ (load p a)
  have hmake : makespan hm p a = load p a kstar := by unfold makespan; exact hks
  -- Indices of jobs that land on `kstar`.
  set T : Finset (Fin js.length) :=
    Finset.univ.filter (fun i => a (js.get i) = kstar) with hT_def
  by_cases hTne : T.Nonempty
  · -- `kstar` carries at least one job: take the LAST one placed on it.
    set i₀ := T.max' hTne with hi0_def
    have hi0_mem : i₀ ∈ T := T.max'_mem hTne
    have hi0lt : (i₀ : ℕ) < js.length := i₀.isLt
    have hkstar : a (js.get i₀) = kstar := by
      have h := hi0_mem; rw [hT_def] at h
      simpa using (Finset.mem_filter.mp h).2
    set jstar : J := js.get i₀ with hjstar_def
    set start : ℝ := listLoad p a (js.take (i₀ : ℕ)) kstar with hstart_def
    -- No job placed strictly after `i₀` lands on `kstar` (maximality of `i₀`).
    have hlast : ∀ (n : ℕ) (hn : n < js.length),
        (i₀ : ℕ) < n → a (js.get ⟨n, hn⟩) ≠ kstar := by
      intro n hn hlt hcontra
      have hmemT : (⟨n, hn⟩ : Fin js.length) ∈ T := by
        rw [hT_def]; simp only [Finset.mem_filter, Finset.mem_univ, true_and]; exact hcontra
      have hle : (⟨n, hn⟩ : Fin js.length) ≤ i₀ := by
        rw [hi0_def]; exact Finset.le_max' T _ hmemT
      have hn_le : n ≤ (i₀ : ℕ) := hle
      omega
    -- `js.drop (i₀+1)` sends nothing to `kstar`.
    have havoid_drop : ∀ j ∈ js.drop ((i₀ : ℕ) + 1), a j ≠ kstar := by
      intro j hj
      obtain ⟨t, htlt, hgt⟩ := List.mem_iff_getElem.mp hj
      rw [List.getElem_drop] at hgt
      have hnlt : (i₀ : ℕ) + 1 + t < js.length := by
        rw [List.length_drop] at htlt; omega
      have hne_k : a (js.get ⟨(i₀ : ℕ) + 1 + t, hnlt⟩) ≠ kstar :=
        hlast ((i₀ : ℕ) + 1 + t) hnlt (by omega)
      rw [List.get_eq_getElem] at hne_k
      rw [← hgt]; exact hne_k
    -- Split the full list at `i₀`.
    have hload_split : ∀ k, listLoad p a js k
        = listLoad p a (js.take (i₀ : ℕ)) k + listLoad p a (js.drop (i₀ : ℕ)) k := by
      intro k; rw [← listLoad_append, List.take_append_drop]
    have hdrop : js.drop (i₀ : ℕ) = jstar :: js.drop ((i₀ : ℕ) + 1) := by
      rw [hjstar_def, List.get_eq_getElem]; exact List.drop_eq_getElem_cons hi0lt
    have haj : a jstar = kstar := by rw [hjstar_def]; exact hkstar
    -- makespan = start + p jstar
    have hmk : makespan hm p a = start + p jstar := by
      rw [hmake, ← listLoad_full p a kstar hnodup hall, hload_split kstar, hdrop,
        listLoad_cons, if_pos haj,
        listLoad_eq_zero p a (js.drop ((i₀ : ℕ) + 1)) kstar havoid_drop, hstart_def]
      ring
    -- `start` is a lower bound on every machine's load just before `jstar` landed.
    have hmin_kstar : ∀ k, start ≤ listLoad p a (js.take (i₀ : ℕ)) k := by
      intro k
      have h2 : listLoad p a (js.take (i₀ : ℕ)) (a jstar)
          ≤ listLoad p a (js.take (i₀ : ℕ)) k := hmin (i₀ : ℕ) hi0lt k
      rw [haj] at h2
      rw [hstart_def]; exact h2
    -- Sum that bound over the `m` machines.
    have hsum_min : (m : ℝ) * start ≤ ∑ k, listLoad p a (js.take (i₀ : ℕ)) k := by
      have h1 : (∑ _k : Fin m, start) ≤ ∑ k, listLoad p a (js.take (i₀ : ℕ)) k :=
        Finset.sum_le_sum (fun k _ => hmin_kstar k)
      rwa [Finset.sum_const, Finset.card_univ, Fintype.card_fin, nsmul_eq_mul] at h1
    -- Total work as the whole list's sum, and the prefix bound.
    have htot_eq : totalWork p = (js.map p).sum := by
      rw [← sum_load p a, ← sum_listLoad p a js]
      exact Finset.sum_congr rfl (fun k _ => (listLoad_full p a k hnodup hall).symm)
    have hmap_split : ((js.take (i₀ : ℕ)).map p).sum + ((js.drop (i₀ : ℕ)).map p).sum
        = (js.map p).sum := by
      rw [← List.sum_append, ← List.map_append, List.take_append_drop]
    have hdrop_map : ((js.drop (i₀ : ℕ)).map p).sum
        = p jstar + ((js.drop ((i₀ : ℕ) + 1)).map p).sum := by
      rw [hdrop]; simp
    have hrest_nonneg : 0 ≤ ((js.drop ((i₀ : ℕ) + 1)).map p).sum := by
      apply List.sum_nonneg
      intro x hx
      simp only [List.mem_map] at hx
      obtain ⟨j, -, rfl⟩ := hx; exact hp j
    have hsum_take : (∑ k, listLoad p a (js.take (i₀ : ℕ)) k)
        = ((js.take (i₀ : ℕ)).map p).sum := sum_listLoad p a _
    refine ⟨jstar, start, hmk, ?_⟩
    have htake_le : ((js.take (i₀ : ℕ)).map p).sum ≤ totalWork p - p jstar := by
      have key : ((js.take (i₀ : ℕ)).map p).sum + ((js.drop (i₀ : ℕ)).map p).sum
          = totalWork p := by rw [hmap_split, htot_eq]
      rw [hdrop_map] at key
      linarith [hrest_nonneg]
    calc (m : ℝ) * start ≤ ∑ k, listLoad p a (js.take (i₀ : ℕ)) k := hsum_min
      _ = ((js.take (i₀ : ℕ)).map p).sum := hsum_take
      _ ≤ totalWork p - p jstar := htake_le
  · -- `kstar` carries no job: the makespan is `0` and every job has zero time.
    rw [Finset.not_nonempty_iff_eq_empty] at hTne
    have havoid : ∀ j ∈ js, a j ≠ kstar := by
      intro j hj hcontra
      obtain ⟨i, hi, hji⟩ := List.mem_iff_getElem.mp hj
      have hmemT : (⟨i, hi⟩ : Fin js.length) ∈ T := by
        rw [hT_def]; simp only [Finset.mem_filter, Finset.mem_univ, true_and]
        rw [List.get_eq_getElem, hji]; exact hcontra
      rw [hTne] at hmemT; simp at hmemT
    have hmake0 : makespan hm p a = 0 := by
      rw [hmake, ← listLoad_full p a kstar hnodup hall,
        listLoad_eq_zero p a js kstar havoid]
    obtain ⟨jstar, rest, hjs⟩ := List.exists_cons_of_ne_nil hne
    have hpj0 : p jstar = 0 :=
      le_antisymm (by have := opt_ge_max_job hm p hp a jstar; rw [hmake0] at this; exact this)
        (hp jstar)
    refine ⟨jstar, 0, ?_, ?_⟩
    · rw [hmake0, hpj0]; ring
    · rw [hpj0]; simp only [mul_zero, sub_zero]
      exact Finset.sum_nonneg (fun j _ => hp j)

/-- tex Theorem `thm:lpt` (Graham 1969), headline statement: the LPT list
schedule's makespan is within `4/3 − 1/(3m)` of the OPTIMUM makespan `cStar`
(characterized as a lower bound on every assignment's makespan that is itself
achieved). Discharged by feeding `lpt_makespan_bound` the certificate of
`list_schedule_decomposition` together with the LPT counting lemma
(`0 < start → 3q ≤ cStar`). The tight LPT-specific counting argument is the long
part deferred here. -/
proof_wanted lpt_tight_bound (hm : 0 < m) (p : J → ℝ) (hp : ∀ j, 0 ≤ p j)
    (a : J → Fin m) (js : List J) (hlpt : IsLPTSchedule p a js)
    (cStar : ℝ) (hcstar_lb : ∀ b : J → Fin m, cStar ≤ makespan hm p b)
    (hcstar_ach : ∃ b : J → Fin m, makespan hm p b = cStar) :
    makespan hm p a ≤ (4 / 3 - 1 / (3 * (m : ℝ))) * cStar

end ListScheduling

end Frahan
