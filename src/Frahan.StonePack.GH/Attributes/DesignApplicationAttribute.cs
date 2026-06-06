#nullable disable
using System;

namespace Frahan.GH.Attributes;

// =============================================================================
// DesignApplicationAttribute -- declarative architect/artist-facing design
// problem + tolerance + design-flow tag on a Frahan GH component.
//
// Sibling to [AlgorithmAttribute]. Where [Algorithm] tags the COMPUTATIONAL
// citation (the algorithm + its paper), [DesignApplication] tags the DESIGN
// CITATION (the architect-facing application phrase + the named precedent
// project / building / fabricator that grounds the workflow).
//
// Added 2026-05-31 per Libish directive:
//   "components themselves can have the algorithms references and the small
//    application phrase"
//
// The attribute serves three audiences:
//   1. Architects/artists examining a Frahan canvas cold -- the Description
//      hover text reads from this attribute so they understand what real-
//      world workflow the component serves.
//   2. The Frahan-internal _FrahanWhichAlgorithm reflection report -- this
//      attribute joins the [Algorithm] citation chain in the report.
//   3. Yak / CloudZoo package metadata -- the design-flow tag surfaces in
//      the plugin discovery UI (top-down vs bottom-up vs bridges-both).
//
// One [DesignApplication] per component. Multiple would dilute the contract
// (a component should serve ONE primary design problem; if it serves more,
// split it). For components that genuinely bridge two flows, use
// DesignFlow.Bridges.
//
// Design discipline reference: wiki/specs/frahan_design_philosophy.md §5.1
// (component classification by design flow) + memory feedback_hitl_cards_design_grounded.
// =============================================================================

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DesignApplicationAttribute : Attribute
{
    public DesignApplicationAttribute(string phrase, DesignFlow flow)
    {
        Phrase = phrase ?? throw new ArgumentNullException(nameof(phrase));
        Flow = flow;
    }

    /// <summary>
    /// One-line architect-facing application phrase. Plain English, present-
    /// tense imperative, ~6-15 words. Example: "Match panels along a rail for
    /// live-edge wood flooring." Read into the GH Description hover text so
    /// the user sees it on canvas without opening the wiki.
    /// </summary>
    public string Phrase { get; }

    /// <summary>
    /// Top-down (form sovereign), Bottom-up (material sovereign), or Bridges
    /// (accepts either input as sovereign). See
    /// wiki/specs/frahan_design_philosophy.md §0 + §5.1 for the framing.
    /// </summary>
    public DesignFlow Flow { get; }

    /// <summary>
    /// Optional named precedent the workflow demonstrates -- a paper, project,
    /// building, or fabricator. Examples: "IAAC MRAC re(al)form 2022-23",
    /// "Clifford-McGee 2017 Cyclopean Cannibalism (ACADIA pp. 404-413)",
    /// "Quarra Parallel Nature JPMC 270 Park Ave", "UCL Devadass 2025
    /// three-legged limestone arch". Per AGENTS.md SS9 no invented citations.
    /// </summary>
    public string Precedent { get; set; }

    /// <summary>
    /// Optional numeric tolerance the architect can read at-a-glance.
    /// Example: "<= 3 mm joint residual at 500 mm fragment" or "<= 50 mm
    /// endpoint deviation at 2.5 m arch span". Pass criterion derivative.
    /// </summary>
    public string Tolerance { get; set; }

    /// <summary>
    /// Optional pointer to the HITL card-set under wiki/research/hitl_cards/
    /// demonstrating the workflow. Path-style, e.g.
    /// "wiki/research/hitl_cards/em_2d_panel_match_rail/".
    /// </summary>
    public string CardSet { get; set; }
}

/// <summary>
/// Per wiki/specs/frahan_design_philosophy.md SS5.1: every Frahan component
/// classifies as one of three design flows.
/// </summary>
public enum DesignFlow
{
    /// <summary>
    /// Form-first. The designer's geometry is sovereign and immutable;
    /// the component matches material to fit the form. Examples: Voussoir
    /// Ingest, Scan to Block Inventory (the form-finder's lookup), Template
    /// Panel Match, BlockCutOpt v2. Material relationship = imposition.
    /// </summary>
    TopDown = 1,

    /// <summary>
    /// Material-first. The material inventory is sovereign and immutable;
    /// the form emerges from the matching. Examples: EdgeMatch Solve /
    /// Trencadis Assembly Solve, Panel Match Along Rail, Random Rubble Pack,
    /// Rubble Drop-Settle, Cyclopean Recipe Coursing. Material relationship
    /// = negotiation.
    /// </summary>
    BottomUp = 2,

    /// <summary>
    /// Accepts either form or material as the sovereign input; the workflow
    /// author picks per call. Examples: Stone-Aware Cut Export (metadata
    /// flows in either direction), Fabrication Prep Report, Frahan Geo
    /// Import, EdgeMatch Options DTO (configures both flows), Mesh Sanitize
    /// (pure pre-processing). Most options / utility components live here.
    /// </summary>
    Bridges = 3,
}
