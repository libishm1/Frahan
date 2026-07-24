import FrahanProofs.Machines

/-!
Frahan StonePack — Graham LPT `4/3 − 1/(3m)` bound, Stage 1 of 3.

Mechanizes Stage 1 of the design in
`proofs/DESIGN_lpt_optimality.md`: the STATIC PAIR LEMMA that replaces the
classical exchange-induction (the step that previously stalled the last
`proof_wanted` of `Machines.lean`).

Setting (the LPT prefix of the schedule, all jobs large): a nodup list `l : List J`
sorted non-increasingly by processing time `p`, every job `> C/3`
(`hlarge : C < 3 * p j`), assigned by `b : J → Fin m` with every machine's prefix
load `≤ C` (`hb`). Over this data Stage 1 delivers, with NO trace and NO exchange
induction — pure static pigeonhole:

  * `lpt_prefix_job_le`      : each job is itself `≤ C`.
  * `lpt_prefix_fiber_le_two`: each machine runs `≤ 2` of these jobs (three larges
    would exceed `C`). Re-proves the shape of `Machines.card_jobs_le_two_of_large`
    (which is `private`) over the list model.
  * `lpt_prefix_length_le`   : `l.length ≤ 2 * m` (sum the fibers).
  * `lpt_prefix_pair_le`     : THE PAIR LEMMA. For `1 ≤ i` and `m + i ≤ l.length`,
    the `(m−i)`-th and `(m+i−1)`-th jobs (0-based) satisfy
    `p (l.get ⟨m−i,_⟩) + p (l.get ⟨m+i−1,_⟩) ≤ C`. The heart is a counting
    argument: among the first `m+i` jobs some machine holding two of them has a
    member at position `≤ m−i`; sortedness then bounds the pair.

The vocabulary (`load`, `listLoad`, `IsListSchedule`, `IsLPTSchedule`,
descending-sort convention `l.Pairwise (fun j j' => p j' ≤ p j)` — the exact
phrasing `IsLPTSchedule` uses) matches `Machines.lean` so Stage 2 (the trace
invariant) and Stage 3 (assembly into `lpt_tight_bound`) compose on top. Nothing
here uses `sorry` or a new `axiom`.
-/

namespace Frahan

/-! ### Generic counting helpers (no `l`/`p` structure) -/

/-- If every job on a set has processing time `> C/3` and their total is `≤ C`,
the set has at most two jobs. The list-model twin of the `private`
`Machines.card_jobs_le_two_of_large`. -/
private theorem card_le_two_of_sum {J : Type*} (p : J → ℝ) (hp : ∀ j, 0 ≤ p j)
    {C : ℝ} (S : Finset J) (hlg : ∀ j ∈ S, C < 3 * p j) (hsum : ∑ j ∈ S, p j ≤ C) :
    S.card ≤ 2 := by
  by_contra hcon
  have h3 : 3 ≤ S.card := by omega
  have hSne : S.Nonempty := Finset.card_pos.mp (by omega)
  have hlt : ∑ _j ∈ S, (C / 3) < ∑ j ∈ S, p j := by
    apply Finset.sum_lt_sum_of_nonempty hSne
    intro j hj
    linarith [hlg j hj]
  rw [Finset.sum_const, nsmul_eq_mul] at hlt
  have hcard3 : (3 : ℝ) ≤ (S.card : ℝ) := by exact_mod_cast h3
  have hcnn : 0 ≤ C := le_trans (Finset.sum_nonneg (fun j _ => hp j)) hsum
  nlinarith [hlt, hsum,
    mul_nonneg (by linarith : (0:ℝ) ≤ (S.card : ℝ) - 3) (by linarith : (0:ℝ) ≤ C / 3)]

/-- Length of a filtered cons: the head contributes one iff it lands on `k`. -/
private theorem filter_length_cons {m : ℕ} {J : Type*} (b : J → Fin m) (j : J)
    (t : List J) (k : Fin m) :
    ((j :: t).filter (fun j' => b j' = k)).length
      = (t.filter (fun j' => b j' = k)).length + (if b j = k then 1 else 0) := by
  by_cases h : b j = k <;> simp [h]

/-- The `m` machine fibers partition the list: their lengths sum to `l.length`. -/
private theorem sum_filter_length {m : ℕ} {J : Type*} (b : J → Fin m) (l : List J) :
    ∑ k : Fin m, (l.filter (fun j => b j = k)).length = l.length := by
  induction l with
  | nil => simp
  | cons j t ih =>
    rw [Finset.sum_congr rfl (fun k _ => filter_length_cons b j t k),
      Finset.sum_add_distrib, ih]
    have h1 : ∑ k : Fin m, (if b j = k then (1 : ℕ) else 0) = 1 := by
      simp [Finset.sum_ite_eq]
    rw [h1, List.length_cons]

/-! ### The abstract pair-position lemma (pure combinatorics over `Fin`)

Given a machine assignment `mach : Fin (m+i) → Fin m` with every fiber `≤ 2`, a
"pair-summable" value function whose same-machine pairs sum to `≤ C`, and a value
function that is non-increasing along positions, the `(m−i)`-th and `(m+i−1)`-th
values sum to `≤ C`. This isolates the counting core of the pair lemma. -/
private theorem pair_bound_abstract {m i : ℕ} (hi1 : 1 ≤ i)
    (hile : i ≤ m) {C : ℝ} (mach : Fin (m + i) → Fin m) (val : Fin (m + i) → ℝ)
    (hfib2 : ∀ k : Fin m,
      (Finset.univ.filter (fun x : Fin (m + i) => mach x = k)).card ≤ 2)
    (hpair : ∀ x y : Fin (m + i), x ≠ y → mach x = mach y → val x + val y ≤ C)
    (hmono : ∀ x y : Fin (m + i), (x : ℕ) ≤ (y : ℕ) → val y ≤ val x) :
    val ⟨m - i, by omega⟩ + val ⟨m + i - 1, by omega⟩ ≤ C := by
  classical
  let cnt : Fin m → ℕ :=
    fun k => (Finset.univ.filter (fun x : Fin (m + i) => mach x = k)).card
  have hsum : ∑ k : Fin m, cnt k = m + i := by
    have h := Finset.card_eq_sum_card_fiberwise
      (s := (Finset.univ : Finset (Fin (m + i)))) (t := (Finset.univ : Finset (Fin m)))
      (f := mach) (fun x _ => Finset.mem_univ (mach x))
    rw [Finset.card_univ, Fintype.card_fin] at h
    exact h.symm
  have hcnt_ge1 : ∀ x : Fin (m + i), 1 ≤ cnt (mach x) := by
    intro x
    apply Finset.card_pos.mpr
    exact ⟨x, by simp⟩
  -- Some position `≤ m − i` shares its machine with another of the first `m+i` jobs.
  have key : ∃ x : Fin (m + i), (x : ℕ) ≤ m - i ∧ 1 < cnt (mach x) := by
    by_contra hcon
    push_neg at hcon
    set lowPos : Finset (Fin (m + i)) :=
      Finset.univ.filter (fun x => (x : ℕ) ≤ m - i) with hlowPos
    set LowM : Finset (Fin m) := lowPos.image mach with hLowM
    have hinj : Set.InjOn mach ↑lowPos := by
      intro x hx x' hx' hxx'
      have hxle : (x : ℕ) ≤ m - i := by
        have hx2 : x ∈ lowPos := Finset.mem_coe.mp hx
        rw [hlowPos, Finset.mem_filter] at hx2
        exact hx2.2
      have hle : cnt (mach x) ≤ 1 := hcon x hxle
      have hxin : x ∈ Finset.univ.filter (fun z : Fin (m + i) => mach z = mach x) := by
        simp
      have hx'in : x' ∈ Finset.univ.filter (fun z : Fin (m + i) => mach z = mach x) := by
        simp only [Finset.mem_filter, Finset.mem_univ, true_and]
        exact hxx'.symm
      exact Finset.card_le_one.mp hle x hxin x' hx'in
    have hLowM_card : LowM.card = lowPos.card := Finset.card_image_of_injOn hinj
    have hlow_ge : m - i + 1 ≤ lowPos.card := by
      have hle_mi : m - i + 1 ≤ m + i := by omega
      have hmap : (Finset.univ : Finset (Fin (m - i + 1))).card ≤ lowPos.card := by
        apply Finset.card_le_card_of_injOn (Fin.castLE hle_mi)
        · intro j _
          rw [Finset.mem_coe, hlowPos, Finset.mem_filter]
          refine ⟨Finset.mem_univ _, ?_⟩
          change (j : ℕ) ≤ m - i
          have hj := j.isLt
          omega
        · exact (Fin.castLE_injective hle_mi).injOn
      simpa using hmap
    have hcnt1 : ∀ k ∈ LowM, cnt k = 1 := by
      intro k hk
      rw [hLowM, Finset.mem_image] at hk
      obtain ⟨x, hxlp, hxk⟩ := hk
      rw [hlowPos, Finset.mem_filter] at hxlp
      have hxle : (x : ℕ) ≤ m - i := hxlp.2
      have hle : cnt k ≤ 1 := by rw [← hxk]; exact hcon x hxle
      have hge : 1 ≤ cnt k := by rw [← hxk]; exact hcnt_ge1 x
      omega
    have hsplit : (∑ k ∈ LowM, cnt k) + (∑ k ∈ LowMᶜ, cnt k) = ∑ k : Fin m, cnt k :=
      Finset.sum_add_sum_compl LowM cnt
    have hlowsum : ∑ k ∈ LowM, cnt k = LowM.card := by
      rw [Finset.sum_congr rfl hcnt1]; simp
    have hcomplsum : ∑ k ∈ LowMᶜ, cnt k ≤ LowMᶜ.card * 2 := by
      have h := Finset.sum_le_card_nsmul LowMᶜ cnt 2 (fun k _ => hfib2 k)
      simpa [smul_eq_mul] using h
    have hcardadd : LowM.card + LowMᶜ.card = m := by
      have h := Finset.card_add_card_compl LowM
      rwa [Fintype.card_fin] at h
    rw [hlowsum, hsum] at hsplit
    omega
  obtain ⟨x, hxlow, hxcard⟩ := key
  set Sx : Finset (Fin (m + i)) := Finset.univ.filter (fun z => mach z = mach x) with hSx
  have hSxcard : 1 < Sx.card := hxcard
  have hexy : ∃ y : Fin (m + i), y ∈ Sx ∧ y ≠ x := by
    by_contra hc
    push_neg at hc
    have hsub : Sx ⊆ {x} := fun y hy => Finset.mem_singleton.mpr (hc y hy)
    have hle := Finset.card_le_card hsub
    rw [Finset.card_singleton] at hle
    omega
  obtain ⟨y, hySx, hyx⟩ := hexy
  have hmachy : mach y = mach x := by
    rw [hSx, Finset.mem_filter] at hySx; exact hySx.2
  have hlow_le : val ⟨m - i, by omega⟩ ≤ val x :=
    hmono x ⟨m - i, by omega⟩ hxlow
  have hyle : (y : ℕ) ≤ m + i - 1 := by have := y.isLt; omega
  have hhigh_le : val ⟨m + i - 1, by omega⟩ ≤ val y :=
    hmono y ⟨m + i - 1, by omega⟩ hyle
  have hpairxy : val x + val y ≤ C := hpair x y (Ne.symm hyx) hmachy.symm
  linarith [hlow_le, hhigh_le, hpairxy]

/-! ### Stage-1 deliverables over the LPT prefix -/

section Prefix

variable {m : ℕ} {J : Type*} {p : J → ℝ} {b : J → Fin m} {C : ℝ} {l : List J}

/-- Deliverable 1: each prefix job is itself `≤ C` — its own machine load contains
it and every load is `≤ C`. -/
theorem lpt_prefix_job_le (hp : ∀ j, 0 ≤ p j) (hnodup : l.Nodup)
    (hb : ∀ k : Fin m, ((l.filter (fun j => b j = k)).map p).sum ≤ C) :
    ∀ j ∈ l, p j ≤ C := by
  classical
  intro j hj
  have hjmem : j ∈ (l.filter (fun j' => b j' = b j)).toFinset := by
    rw [List.mem_toFinset, List.mem_filter]
    exact ⟨hj, by simp⟩
  calc p j
      ≤ ∑ j' ∈ (l.filter (fun j' => b j' = b j)).toFinset, p j' :=
        Finset.single_le_sum (fun j' _ => hp j') hjmem
    _ = ((l.filter (fun j' => b j' = b j)).map p).sum :=
        List.sum_toFinset p (hnodup.filter _)
    _ ≤ C := hb (b j)

/-- Deliverable 2: each machine runs at most two prefix jobs — three jobs each
`> C/3` would exceed the load bound `C`. -/
theorem lpt_prefix_fiber_le_two (hp : ∀ j, 0 ≤ p j) (hnodup : l.Nodup)
    (hlarge : ∀ j ∈ l, C < 3 * p j)
    (hb : ∀ k : Fin m, ((l.filter (fun j => b j = k)).map p).sum ≤ C)
    (k : Fin m) : (l.filter (fun j => b j = k)).length ≤ 2 := by
  classical
  have hLnodup : (l.filter (fun j => b j = k)).Nodup := hnodup.filter _
  rw [← List.toFinset_card_of_nodup hLnodup]
  apply card_le_two_of_sum p hp
  · intro j hj
    exact hlarge j (List.mem_of_mem_filter (List.mem_toFinset.mp hj))
  · rw [List.sum_toFinset p hLnodup]
    exact hb k

/-- Deliverable 3: the prefix has at most `2m` jobs — sum the `≤ 2`-per-machine
fibers over the `m` machines. -/
theorem lpt_prefix_length_le (hp : ∀ j, 0 ≤ p j) (hnodup : l.Nodup)
    (hlarge : ∀ j ∈ l, C < 3 * p j)
    (hb : ∀ k : Fin m, ((l.filter (fun j => b j = k)).map p).sum ≤ C) :
    l.length ≤ 2 * m := by
  classical
  have hsum : ∑ k : Fin m, (l.filter (fun j => b j = k)).length = l.length :=
    sum_filter_length b l
  have hle : ∑ k : Fin m, (l.filter (fun j => b j = k)).length ≤ ∑ _k : Fin m, 2 :=
    Finset.sum_le_sum (fun k _ => lpt_prefix_fiber_le_two hp hnodup hlarge hb k)
  rw [hsum] at hle
  simp only [Finset.sum_const, Finset.card_univ, Fintype.card_fin, smul_eq_mul] at hle
  omega

/-- Deliverable 4, THE PAIR LEMMA. For `1 ≤ i` with `m + i ≤ l.length`, the
0-based `(m−i)`-th and `(m+i−1)`-th prefix jobs have processing times summing to
`≤ C`. (1-based `q_{m−i+1}` = 0-based `m−i`; 1-based `q_{m+i}` = 0-based `m+i−1`.)
Static pigeonhole, no exchange induction. -/
theorem lpt_prefix_pair_le (hm : 0 < m) (hp : ∀ j, 0 ≤ p j) (hnodup : l.Nodup)
    (hsort : l.Pairwise (fun j j' => p j' ≤ p j)) (hlarge : ∀ j ∈ l, C < 3 * p j)
    (hb : ∀ k : Fin m, ((l.filter (fun j => b j = k)).map p).sum ≤ C)
    (i : ℕ) (hi1 : 1 ≤ i) (hin : m + i ≤ l.length) :
    p (l.get ⟨m - i, by omega⟩) + p (l.get ⟨m + i - 1, by omega⟩) ≤ C := by
  classical
  have hlen : l.length ≤ 2 * m := lpt_prefix_length_le hp hnodup hlarge hb
  have hile : i ≤ m := by omega
  have hpos : ∀ x : Fin (m + i), (x : ℕ) < l.length := fun x => lt_of_lt_of_le x.isLt hin
  let job : Fin (m + i) → J := fun x => l.get ⟨(x : ℕ), hpos x⟩
  let mach : Fin (m + i) → Fin m := fun x => b (job x)
  let val : Fin (m + i) → ℝ := fun x => p (job x)
  have hget_inj : Function.Injective l.get := List.nodup_iff_injective_get.mp hnodup
  have hjob_inj : Function.Injective job := by
    intro x y hxy
    have h1 : (⟨(x : ℕ), hpos x⟩ : Fin l.length) = ⟨(y : ℕ), hpos y⟩ := hget_inj hxy
    exact Fin.ext (by simpa using congrArg Fin.val h1)
  -- Same-machine value sums are ≤ C (restricting the load bound to a prefix fiber).
  have fiber_sum_le : ∀ (k : Fin m) (S : Finset (Fin (m + i))),
      (∀ z ∈ S, mach z = k) → ∑ z ∈ S, p (job z) ≤ C := by
    intro k S hSk
    have hsub : S.image job ⊆ (l.filter (fun j => b j = k)).toFinset := by
      intro j hj
      rw [Finset.mem_image] at hj
      obtain ⟨z, hz, rfl⟩ := hj
      rw [List.mem_toFinset, List.mem_filter]
      refine ⟨List.get_mem l _, ?_⟩
      have hmz : b (job z) = k := hSk z hz
      simpa using hmz
    calc ∑ z ∈ S, p (job z)
        = ∑ j ∈ S.image job, p j :=
          (Finset.sum_image (fun a _ c _ h => hjob_inj h)).symm
      _ ≤ ∑ j ∈ (l.filter (fun j => b j = k)).toFinset, p j :=
          Finset.sum_le_sum_of_subset_of_nonneg hsub (fun j _ _ => hp j)
      _ = ((l.filter (fun j => b j = k)).map p).sum :=
          List.sum_toFinset p (hnodup.filter _)
      _ ≤ C := hb k
  -- Fibers of `mach` have ≤ 2 members.
  have hfib2 : ∀ k : Fin m,
      (Finset.univ.filter (fun x : Fin (m + i) => mach x = k)).card ≤ 2 := by
    intro k
    have himg_card :
        (Finset.univ.filter (fun x : Fin (m + i) => mach x = k)).card
          = ((Finset.univ.filter (fun x : Fin (m + i) => mach x = k)).image job).card :=
      (Finset.card_image_of_injOn hjob_inj.injOn).symm
    rw [himg_card]
    apply card_le_two_of_sum p hp
    · intro j hj
      rw [Finset.mem_image] at hj
      obtain ⟨z, _, rfl⟩ := hj
      exact hlarge (job z) (List.get_mem l _)
    · rw [Finset.sum_image (fun a _ c _ h => hjob_inj h)]
      exact fiber_sum_le k _ (fun z hz => (Finset.mem_filter.mp hz).2)
  -- Same-machine pairs sum to ≤ C.
  have hpair : ∀ x y : Fin (m + i), x ≠ y → mach x = mach y → val x + val y ≤ C := by
    intro x y hxy hmm
    have hsum2 : ∑ z ∈ ({x, y} : Finset (Fin (m + i))), p (job z) ≤ C := by
      apply fiber_sum_le (mach x)
      intro z hz
      rcases Finset.mem_insert.mp hz with h | h
      · rw [h]
      · have hzy : z = y := Finset.mem_singleton.mp h
        rw [hzy]; exact hmm.symm
    rwa [Finset.sum_pair hxy] at hsum2
  -- Descending sort ⇒ value non-increasing along positions.
  have hmono : ∀ x y : Fin (m + i), (x : ℕ) ≤ (y : ℕ) → val y ≤ val x := by
    intro x y hxy
    rcases lt_or_eq_of_le hxy with hlt | heq
    · exact List.Pairwise.rel_get_of_lt hsort
        (a := ⟨(x : ℕ), hpos x⟩) (b := ⟨(y : ℕ), hpos y⟩) hlt
    · have hxy' : x = y := Fin.ext heq
      rw [hxy']
  exact pair_bound_abstract hi1 hile mach val hfib2 hpair hmono

end Prefix

/-! ### Stage 2 — the trace invariant: every greedy prefix load stays `≤ C`

Mechanizes Stage 2 of `proofs/DESIGN_lpt_optimality.md`. Greedy least-loaded
placement of the large, descending-sorted jobs keeps EVERY machine load `≤ C`
after every step. Proved by the stronger induction
`∀ t ≤ l.length, ∀ k, listLoad p a (l.take t) k ≤ C`. The step places job `t`
on a machine of minimal current load `s`; only that load changes, so it suffices
that `s + p jₜ ≤ C`. If some machine is empty, `s ≤ 0` and `p jₜ ≤ C`
(`lpt_prefix_job_le` on the WITNESS `b`). Otherwise every machine runs `≤ 2`
prefix jobs (Stage-1 fiber bound on the GREEDY prefix), so with `u := 2m − t`
single-job machines the minimal single load is `≤ q_{u-1}`, giving
`s ≤ p (l.get ⟨2m−t−1, _⟩)`; the Stage-1 pair lemma on `b` with `i := t+1−m`
then closes `s + p jₜ ≤ C`. Consumed by Stage 3 (assembly into `lpt_tight_bound`).

The conclusion is phrased in `Machines.listLoad` vocabulary; the greedy
hypothesis `hgreedy` is exactly the third conjunct of `Machines.IsListSchedule`.
Nothing here uses `sorry` or a new `axiom`. -/

/-- `listLoad` as a plain map-and-sum with an `if`-guard (no `filterMap`). The
local twin of `Machines.listLoad_cons`/`listLoad_append` bookkeeping. -/
private theorem listLoad_eq_mapIte_sum {m : ℕ} {J : Type*} (p : J → ℝ) (a : J → Fin m)
    (L : List J) (k : Fin m) :
    listLoad p a L k = (L.map (fun j => if a j = k then p j else 0)).sum := by
  induction L with
  | nil => simp [listLoad]
  | cons j t ih =>
    have hcons : listLoad p a (j :: t) k
        = (if a j = k then p j else 0) + listLoad p a t k := by
      by_cases h : a j = k <;> simp [listLoad, h]
    rw [hcons, ih, List.map_cons, List.sum_cons]

/-- Placing the `t`-th job adds its time to exactly its own machine's prefix load:
`listLoad` over `take (t+1)` is `take t` plus a single guarded term. -/
private theorem listLoad_take_succ {m : ℕ} {J : Type*} (p : J → ℝ) (a : J → Fin m)
    (l : List J) (t : ℕ) (ht : t < l.length) (k : Fin m) :
    listLoad p a (l.take (t + 1)) k
      = listLoad p a (l.take t) k
        + (if a (l.get ⟨t, ht⟩) = k then p (l.get ⟨t, ht⟩) else 0) := by
  have hsplit : l.take (t + 1) = l.take t ++ [l.get ⟨t, ht⟩] := by
    rw [List.get_eq_getElem]; exact List.take_succ_eq_append_getElem ht
  rw [listLoad_eq_mapIte_sum p a (l.take (t + 1)) k, hsplit, List.map_append, List.sum_append,
    List.map_singleton, List.sum_singleton, ← listLoad_eq_mapIte_sum p a (l.take t) k]

/-- Stage-2 deliverable, THE TRACE INVARIANT. On the LPT prefix `l` (nodup,
descending-sorted, all jobs large) with a witness assignment `b` keeping every
machine `≤ C` (`hb`), any GREEDY least-loaded list schedule `a` for `l`
(`hgreedy`, the minimality conjunct of `Machines.IsListSchedule p a l`) keeps
every machine load `≤ C` on the full prefix: `∀ k, listLoad p a l k ≤ C`.
Stage 3 consumes this verbatim. -/
theorem lpt_prefix_loads_le {m : ℕ} {J : Type*} {p : J → ℝ} {a b : J → Fin m} {C : ℝ}
    {l : List J} (hm : 0 < m) (hp : ∀ j, 0 ≤ p j) (hnodup : l.Nodup)
    (hsort : l.Pairwise (fun j j' => p j' ≤ p j)) (hlarge : ∀ j ∈ l, C < 3 * p j)
    (hb : ∀ k : Fin m, ((l.filter (fun j => b j = k)).map p).sum ≤ C)
    (hgreedy : ∀ (i : ℕ) (hi : i < l.length) (k : Fin m),
        listLoad p a (l.take i) (a (l.get ⟨i, hi⟩)) ≤ listLoad p a (l.take i) k) :
    ∀ k, listLoad p a l k ≤ C := by
  classical
  -- `0 ≤ C` from any machine's nonnegative witness load.
  have hC : 0 ≤ C := by
    refine le_trans ?_ (hb ⟨0, hm⟩)
    apply List.sum_nonneg
    intro x hx
    rw [List.mem_map] at hx
    obtain ⟨j, -, rfl⟩ := hx
    exact hp j
  have hlen : l.length ≤ 2 * m := lpt_prefix_length_le hp hnodup hlarge hb
  -- Stronger induction: every prefix keeps all loads `≤ C`.
  have main : ∀ t, t ≤ l.length → ∀ k, listLoad p a (l.take t) k ≤ C := by
    intro t
    induction t with
    | zero => intro _ k; simpa [listLoad] using hC
    | succ t ih =>
      intro hsucc k
      have ht : t < l.length := by omega
      have htle : t ≤ l.length := by omega
      have iht : ∀ k', listLoad p a (l.take t) k' ≤ C := ih htle
      rw [listLoad_take_succ p a l t ht k]
      by_cases hk : a (l.get ⟨t, ht⟩) = k
      · -- the placed job lands on machine `k`; only this load changes
        rw [if_pos hk]
        subst hk
        have hjt_mem : l.get ⟨t, ht⟩ ∈ l := List.get_mem l _
        have hmin : ∀ k', listLoad p a (l.take t) (a (l.get ⟨t, ht⟩))
            ≤ listLoad p a (l.take t) k' := hgreedy t ht
        -- positions of the greedy prefix as `Fin t`
        have hlt : ∀ r : Fin t, (r : ℕ) < l.length := fun r => lt_of_lt_of_le r.isLt htle
        set job : Fin t → J := fun r => l.get ⟨(r : ℕ), hlt r⟩ with hjob
        have hjob_inj : Function.Injective job := by
          have hget : Function.Injective l.get := List.nodup_iff_injective_get.mp hnodup
          intro r r' hrr
          simp only [hjob] at hrr
          have h1 : (⟨(r : ℕ), hlt r⟩ : Fin l.length) = ⟨(r' : ℕ), hlt r'⟩ := hget hrr
          exact Fin.ext (by simpa using congrArg Fin.val h1)
        -- `l.take t = ofFn job`, giving the position/list load bridge
        have hofFn : l.take t = List.ofFn job := by
          apply List.ext_getElem
          · rw [List.length_ofFn, List.length_take]; omega
          · intro i h₁ h₂
            rw [List.getElem_take, List.getElem_ofFn]
            simp only [hjob, List.get_eq_getElem]
        have bridge : ∀ c : Fin m, listLoad p a (l.take t) c
            = ∑ r ∈ Finset.univ.filter (fun r : Fin t => a (job r) = c), p (job r) := by
          intro c
          rw [listLoad_eq_mapIte_sum, hofFn,
            show ((List.ofFn job).map (fun j => if a j = c then p j else 0))
                = List.ofFn (fun r => if a (job r) = c then p (job r) else 0) from List.map_ofFn,
            List.sum_ofFn, Finset.sum_filter]
        -- per-machine prefix job counts
        set cnt : Fin m → ℕ :=
          fun c => (Finset.univ.filter (fun r : Fin t => a (job r) = c)).card with hcnt
        have hcnt_eq : ∀ c : Fin m,
            cnt c = (Finset.univ.filter (fun r : Fin t => a (job r) = c)).card :=
          fun c => by simp only [hcnt]
        have hcnt_sum : ∑ c : Fin m, cnt c = t := by
          simp only [hcnt_eq]
          have h := Finset.card_eq_sum_card_fiberwise (f := fun r : Fin t => a (job r))
            (s := (Finset.univ : Finset (Fin t))) (t := (Finset.univ : Finset (Fin m)))
            (fun x _ => Finset.mem_univ _)
          rw [Finset.card_univ, Fintype.card_fin] at h
          exact h.symm
        have hcnt_le2 : ∀ c : Fin m, cnt c ≤ 2 := by
          intro c
          have himg : cnt c
              = ((Finset.univ.filter (fun r : Fin t => a (job r) = c)).image job).card := by
            rw [hcnt_eq c]
            exact (Finset.card_image_of_injOn hjob_inj.injOn).symm
          rw [himg]
          apply card_le_two_of_sum p hp
          · intro j hj
            rw [Finset.mem_image] at hj
            obtain ⟨x, -, rfl⟩ := hj
            exact hlarge (job x) (by simp only [hjob]; exact List.get_mem l _)
          · rw [Finset.sum_image (fun x _ y _ h => hjob_inj h), ← bridge c]
            exact iht c
        -- case split: some machine has an empty prefix fiber?
        by_cases hEmpty : ∃ c : Fin m, cnt c = 0
        · obtain ⟨k0, hk0⟩ := hEmpty
          have hk0' : (Finset.univ.filter (fun r : Fin t => a (job r) = k0)).card = 0 := by
            rw [← hcnt_eq k0]; exact hk0
          have hfib0 : Finset.univ.filter (fun r : Fin t => a (job r) = k0) = ∅ :=
            Finset.card_eq_zero.mp hk0'
          have hload0 : listLoad p a (l.take t) k0 = 0 := by
            rw [bridge k0, hfib0, Finset.sum_empty]
          have hs0 : listLoad p a (l.take t) (a (l.get ⟨t, ht⟩)) ≤ 0 := by
            rw [← hload0]; exact hmin k0
          have hpjt : p (l.get ⟨t, ht⟩) ≤ C := lpt_prefix_job_le hp hnodup hb _ hjt_mem
          linarith
        · -- every machine nonempty ⇒ every fiber ∈ {1,2}
          push_neg at hEmpty
          have hcnt_ge1 : ∀ c, 1 ≤ cnt c := fun c => Nat.one_le_iff_ne_zero.mpr (hEmpty c)
          have hpart : ∀ c, cnt c = 1 ∨ cnt c = 2 := by
            intro c; have := hcnt_le2 c; have := hcnt_ge1 c; omega
          set Ones : Finset (Fin m) := Finset.univ.filter (fun c => cnt c = 1) with hOnes
          -- |Ones| = 2m − t
          have hindic : (∑ c : Fin m, (if cnt c = 1 then 1 else 0)) = Ones.card := by
            rw [hOnes, Finset.card_filter]
          have heach : ∀ c : Fin m, cnt c + (if cnt c = 1 then 1 else 0) = 2 := by
            intro c; rcases hpart c with h | h <;> simp [h]
          have hsum2 :
              (∑ c : Fin m, cnt c) + (∑ c : Fin m, (if cnt c = 1 then 1 else 0)) = 2 * m := by
            rw [← Finset.sum_add_distrib, Finset.sum_congr rfl (fun c _ => heach c),
              Finset.sum_const, Finset.card_univ, Fintype.card_fin, smul_eq_mul, Nat.mul_comm]
          have hOnes_card : Ones.card = 2 * m - t := by
            rw [hindic, hcnt_sum] at hsum2; omega
          -- t ≥ m (all fibers ≥ 1)
          have htm : m ≤ t := by
            have h : (∑ _c : Fin m, (1 : ℕ)) ≤ ∑ c : Fin m, cnt c :=
              Finset.sum_le_sum (fun c _ => hcnt_ge1 c)
            simp only [Finset.sum_const, Finset.card_univ, Fintype.card_fin, smul_eq_mul,
              mul_one] at h
            rw [hcnt_sum] at h; exact h
          -- singleton positions: |SP| = |Ones| = 2m − t
          set SP : Finset (Fin t) := Finset.univ.filter (fun r => cnt (a (job r)) = 1) with hSP
          have hSP_card : SP.card = Ones.card := by
            have H : ∀ r ∈ SP, a (job r) ∈ Ones := by
              intro r hr
              simp only [hSP, Finset.mem_filter, Finset.mem_univ, true_and] at hr
              simp only [hOnes, Finset.mem_filter, Finset.mem_univ, true_and]
              exact hr
            rw [Finset.card_eq_sum_card_fiberwise (s := SP) (t := Ones)
              (f := fun r : Fin t => a (job r)) H]
            have hterm : ∀ c ∈ Ones, (SP.filter (fun r => a (job r) = c)).card = 1 := by
              intro c hc
              simp only [hOnes, Finset.mem_filter, Finset.mem_univ, true_and] at hc
              have hfe : SP.filter (fun r => a (job r) = c)
                  = Finset.univ.filter (fun r : Fin t => a (job r) = c) := by
                ext r
                simp only [hSP, Finset.mem_filter, Finset.mem_univ, true_and]
                constructor
                · rintro ⟨-, hr2⟩; exact hr2
                · intro hr2; exact ⟨by rw [hr2]; exact hc, hr2⟩
              rw [hfe, ← hcnt_eq c]; exact hc
            rw [Finset.sum_congr rfl hterm, Finset.sum_const, smul_eq_mul, mul_one]
          have hSPne : SP.Nonempty := by
            rw [← Finset.card_pos, hSP_card, hOnes_card]; omega
          set rmax : Fin t := SP.max' hSPne with hrmax
          have hrmax_mem : rmax ∈ SP := by rw [hrmax]; exact SP.max'_mem hSPne
          have hcnt1 : cnt (a (job rmax)) = 1 := by
            have h := hrmax_mem
            simp only [hSP, Finset.mem_filter, Finset.mem_univ, true_and] at h
            exact h
          -- rmax ≥ 2m − t − 1 (|SP| distinct positions all `≤ rmax`)
          have hcard_le : SP.card ≤ (rmax : ℕ) + 1 := by
            have himg : SP.image Fin.val ⊆ Finset.range ((rmax : ℕ) + 1) := by
              intro x hx
              rw [Finset.mem_image] at hx
              obtain ⟨r, hrSP, rfl⟩ := hx
              rw [Finset.mem_range]
              have hle : r ≤ rmax := by rw [hrmax]; exact Finset.le_max' SP r hrSP
              have hle' : (r : ℕ) ≤ (rmax : ℕ) := hle
              omega
            calc SP.card = (SP.image Fin.val).card :=
                  (Finset.card_image_of_injective SP Fin.val_injective).symm
              _ ≤ (Finset.range ((rmax : ℕ) + 1)).card := Finset.card_le_card himg
              _ = (rmax : ℕ) + 1 := Finset.card_range _
          have hrmax_ge : 2 * m - t - 1 ≤ (rmax : ℕ) := by
            rw [hSP_card, hOnes_card] at hcard_le; omega
          -- the singleton machine at rmax carries exactly its own job
          have hfib_single :
              Finset.univ.filter (fun x : Fin t => a (job x) = a (job rmax)) = {rmax} := by
            obtain ⟨w, hw⟩ := Finset.card_eq_one.mp
              (by rw [← hcnt_eq (a (job rmax))]; exact hcnt1 :
                (Finset.univ.filter (fun x : Fin t => a (job x) = a (job rmax))).card = 1)
            have hrmem : rmax ∈ ({w} : Finset (Fin t)) := by
              rw [← hw]; exact Finset.mem_filter.mpr ⟨Finset.mem_univ _, rfl⟩
            rw [Finset.mem_singleton] at hrmem
            rw [hw, hrmem]
          have hload_single : listLoad p a (l.take t) (a (job rmax)) = p (job rmax) := by
            rw [bridge (a (job rmax)), hfib_single, Finset.sum_singleton]
          -- the minimal single load is `≤ p (l.get ⟨2m−t−1, _⟩)`
          have hq : 2 * m - t - 1 < l.length := by omega
          have hmono_le : p (job rmax) ≤ p (l.get ⟨2 * m - t - 1, hq⟩) := by
            simp only [hjob]
            rcases eq_or_lt_of_le hrmax_ge with heq | hlt2
            · exact le_of_eq (congrArg p (congrArg l.get (Fin.ext heq.symm)))
            · exact List.Pairwise.rel_get_of_lt hsort
                (a := ⟨2 * m - t - 1, hq⟩) (b := ⟨(rmax : ℕ), hlt rmax⟩) hlt2
          have hs_le : listLoad p a (l.take t) (a (l.get ⟨t, ht⟩))
              ≤ p (l.get ⟨2 * m - t - 1, hq⟩) := by
            refine le_trans (hmin (a (job rmax))) ?_
            rw [hload_single]; exact hmono_le
          -- the Stage-1 pair lemma on the WITNESS closes `s + p jₜ ≤ C`
          have hi1 : 1 ≤ t + 1 - m := by omega
          have hin : m + (t + 1 - m) ≤ l.length := by omega
          have hpair := lpt_prefix_pair_le hm hp hnodup hsort hlarge hb (t + 1 - m) hi1 hin
          have hpair' : p (l.get ⟨2 * m - t - 1, hq⟩) + p (l.get ⟨t, ht⟩) ≤ C := by
            have e1 : (⟨2 * m - t - 1, hq⟩ : Fin l.length) = ⟨m - (t + 1 - m), by omega⟩ :=
              Fin.ext (show (2 * m - t - 1 : ℕ) = m - (t + 1 - m) by omega)
            have e2 : (⟨t, ht⟩ : Fin l.length) = ⟨m + (t + 1 - m) - 1, by omega⟩ :=
              Fin.ext (show (t : ℕ) = m + (t + 1 - m) - 1 by omega)
            rw [e1, e2]; exact hpair
          linarith
      · rw [if_neg hk, add_zero]; exact iht k
  have hfull := main l.length (le_refl _)
  intro k
  have hk := hfull k
  rwa [List.take_length] at hk

/-! ### Stage 3 — assembly of the tight LPT `4/3 − 1/(3m)` bound

Mechanizes Stage 3 of `proofs/DESIGN_lpt_optimality.md`, discharging the last
`proof_wanted` of `Machines.lean`. Split on the critical (last-placed) job `jstar`
that `Machines.list_schedule_decomposition'` exposes together with its prefix index
`i₀`:

  * SMALL case `3·p jstar ≤ cStar`: the greedy certificate alone closes the bound —
    `Machines.lpt_tight_bound_small_case`.
  * LARGE case `cStar < 3·p jstar`: every job in the prefix `P = js.take (i₀+1)` is
    `> cStar/3` (LPT sortedness, `jstar` being `P`'s last and smallest job). The
    optimum achiever `bopt` restricted to `P` keeps every machine `≤ cStar`, so the
    Stage-1/2 pigeonhole + trace invariant (`lpt_prefix_loads_le`) forces the GREEDY
    prefix loads `≤ cStar`; the critical machine's load is `start + p jstar` (Stage-2
    `listLoad_take_succ`) `= makespan`, hence `makespan ≤ cStar`, and the ratio's
    `≥ 1` slack (`Machines.le_tight_bound_of_le_opt`) finishes.

Nothing here uses `sorry` or a new `axiom`. -/

section Assembly

variable {m : ℕ} {J : Type*} [Fintype J]

/-- tex Theorem `thm:lpt` (Graham 1969), headline statement, PROVED: the LPT list
schedule's makespan is within `4/3 − 1/(3m)` of the OPTIMUM makespan `cStar`
(characterized as a lower bound on every assignment's makespan that is itself
achieved). Assembles the index-exposing greedy certificate
(`Machines.list_schedule_decomposition'`), the small-critical-job arithmetic
(`Machines.lpt_tight_bound_small_case`), and — for the large-critical-job case — the
Stage-1 static pair lemma and the Stage-2 trace invariant (`lpt_prefix_loads_le`)
that together replace the classical `≤2 jobs/machine ⇒ LPT optimal` exchange
induction. -/
theorem lpt_tight_bound (hm : 0 < m) (p : J → ℝ) (hp : ∀ j, 0 ≤ p j)
    (a : J → Fin m) (js : List J) (hlpt : IsLPTSchedule p a js)
    (cStar : ℝ) (hcstar_lb : ∀ b : J → Fin m, cStar ≤ makespan hm p b)
    (hcstar_ach : ∃ b : J → Fin m, makespan hm p b = cStar) :
    makespan hm p a ≤ (4 / 3 - 1 / (3 * (m : ℝ))) * cStar := by
  classical
  obtain ⟨hls, hsort_js⟩ := hlpt
  rcases eq_or_ne js [] with hnil | hne
  · -- degenerate empty case: no jobs, every makespan is `0`
    subst hnil
    haveI hIE : IsEmpty J := ⟨fun j => by have := hls.2.1 j; simp at this⟩
    have hmk_zero : ∀ c : J → Fin m, makespan hm p c = 0 := by
      intro c
      have hload0 : ∀ k, load p c k = 0 := by
        intro k; simp [load, Finset.univ_eq_empty]
      apply le_antisymm
      · unfold makespan
        rw [Finset.sup'_le_iff]
        intro k _
        exact le_of_eq (hload0 k)
      · rw [← hload0 ⟨0, hm⟩]
        exact load_le_makespan hm p c ⟨0, hm⟩
    obtain ⟨b, hb⟩ := hcstar_ach
    have hcs0 : cStar = 0 := hb.symm.trans (hmk_zero b)
    have hle : makespan hm p a ≤ cStar := le_of_eq (by rw [hmk_zero a, hcs0])
    exact le_tight_bound_of_le_opt hm hle (le_of_eq hcs0.symm)
  · -- main case: expose the critical index and job
    obtain ⟨i₀, hi₀, start, hmk, hstart, hstart_eq⟩ :=
      list_schedule_decomposition' hm p hp a js hls hne
    set jstar : J := js.get ⟨i₀, hi₀⟩ with hjstar_def
    by_cases hcase : 3 * p jstar ≤ cStar
    · -- Case A: small critical job — greedy certificate closes it
      exact lpt_tight_bound_small_case hm p a cStar hcstar_ach jstar start hmk hstart hcase
    · -- Case B: large critical job — Stage-1/2 machinery on the LPT prefix
      have hlt : cStar < 3 * p jstar := not_le.mp hcase
      set P : List J := js.take (i₀ + 1) with hP
      obtain ⟨bopt, hbopt⟩ := hcstar_ach
      have hnodupP : P.Nodup := by
        rw [hP]; exact List.Nodup.sublist (List.take_sublist (i₀ + 1) js) hls.1
      have hsortP : P.Pairwise (fun j j' => p j' ≤ p j) := by
        rw [hP]; exact List.Pairwise.sublist (List.take_sublist (i₀ + 1) js) hsort_js
      have hPeq : P = js.take i₀ ++ [jstar] := by
        rw [hP, hjstar_def, List.get_eq_getElem]
        exact List.take_succ_eq_append_getElem hi₀
      have hlargeP : ∀ j ∈ P, cStar < 3 * p j := by
        intro j hj
        have hsort_app : (js.take i₀ ++ [jstar]).Pairwise (fun j j' => p j' ≤ p j) := by
          rw [← hPeq]; exact hsortP
        rw [List.pairwise_append] at hsort_app
        have hpjstar : p jstar ≤ p j := by
          rw [hPeq, List.mem_append] at hj
          rcases hj with hjf | hjl
          · exact hsort_app.2.2 j hjf jstar (List.mem_singleton.mpr rfl)
          · rw [List.mem_singleton] at hjl
            exact le_of_eq (congrArg p hjl.symm)
        calc cStar < 3 * p jstar := hlt
          _ ≤ 3 * p j := by linarith
      have hbnd : ∀ k : Fin m, ((P.filter (fun j => bopt j = k)).map p).sum ≤ cStar := by
        intro k
        have hPfilt_nodup : (P.filter (fun j => bopt j = k)).Nodup := hnodupP.filter _
        have hsub : (P.filter (fun j => bopt j = k)).toFinset ⊆
            Finset.univ.filter (fun j => bopt j = k) := by
          intro x hx
          rw [List.mem_toFinset, List.mem_filter] at hx
          rw [Finset.mem_filter]
          exact ⟨Finset.mem_univ x, by simpa using hx.2⟩
        calc ((P.filter (fun j => bopt j = k)).map p).sum
            = ∑ j ∈ (P.filter (fun j => bopt j = k)).toFinset, p j :=
              (List.sum_toFinset p hPfilt_nodup).symm
          _ ≤ ∑ j ∈ Finset.univ.filter (fun j => bopt j = k), p j :=
              Finset.sum_le_sum_of_subset_of_nonneg hsub (fun j _ _ => hp j)
          _ = load p bopt k := rfl
          _ ≤ cStar := by rw [← hbopt]; exact load_le_makespan hm p bopt k
      have hgreedyP : ∀ (i : ℕ) (hi : i < P.length) (k : Fin m),
          listLoad p a (P.take i) (a (P.get ⟨i, hi⟩)) ≤ listLoad p a (P.take i) k := by
        intro i hi k
        have hilt : i < i₀ + 1 := by
          have hPl : P.length = min (i₀ + 1) js.length := by rw [hP, List.length_take]
          omega
        have hijs : i < js.length := by omega
        have htt : P.take i = js.take i := by
          rw [hP, List.take_take]; congr 1; omega
        have hgetP : P.get ⟨i, hi⟩ = js.get ⟨i, hijs⟩ := by
          simp only [List.get_eq_getElem, hP, List.getElem_take]
        rw [htt, hgetP]
        exact hls.2.2 i hijs k
      have hloadsP : ∀ k, listLoad p a P k ≤ cStar :=
        lpt_prefix_loads_le hm hp hnodupP hsortP hlargeP hbnd hgreedyP
      have hcrit : listLoad p a P (a jstar) ≤ cStar := hloadsP (a jstar)
      have hPsucc : listLoad p a P (a jstar) = start + p jstar := by
        have h := listLoad_take_succ p a js i₀ hi₀ (a jstar)
        rw [hP, h, ← hjstar_def, if_pos rfl, ← hstart_eq]
      have hle_opt : makespan hm p a ≤ cStar := by
        rw [hmk, ← hPsucc]; exact hcrit
      have hcnn : 0 ≤ cStar := by
        rw [← hbopt]
        refine le_trans ?_ (load_le_makespan hm p bopt ⟨0, hm⟩)
        unfold load; exact Finset.sum_nonneg (fun j _ => hp j)
      exact le_tight_bound_of_le_opt hm hle_opt hcnn

end Assembly

end Frahan
