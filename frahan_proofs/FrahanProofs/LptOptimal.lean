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

end Frahan
