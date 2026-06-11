using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Frahan.Core;
using Frahan.Tests;

// 2026-05-04 - make rhcommon_c.dll loadable so the 16 native-runtime
// SKIPs ("Unable to load DLL 'rhcommon_c'") actually run. RhinoCommon's
// managed DllImport entries use the short DLL name, so Windows resolves
// them via the loader search path. We prepend the Rhino install's System
// dir to PATH and additionally call SetDllDirectory so search succeeds
// even on locked-down builds. Override the path with the env var
// FRAHAN_RHINO_SYSTEM if Rhino is installed somewhere non-default.
//
// Set FRAHAN_SKIP_NATIVE=1 to keep the SKIP behavior (useful for CI
// environments that intentionally have no Rhino install and want to run
// only the pure-managed test subset).
ConfigureRhinoNativeLoader();

static void ConfigureRhinoNativeLoader()
{
    if (Environment.GetEnvironmentVariable("FRAHAN_SKIP_NATIVE") == "1")
    {
        Console.WriteLine("INFO Rhino native loading disabled (FRAHAN_SKIP_NATIVE=1).");
        return;
    }

    string? candidate = Environment.GetEnvironmentVariable("FRAHAN_RHINO_SYSTEM");
    if (string.IsNullOrEmpty(candidate))
    {
        foreach (var d in new[]
        {
            @"C:\Program Files\Rhino 8\System",
            @"C:\Program Files\Rhino 8 WIP\System",
            @"C:\Program Files\Rhino 7\System",
        })
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(d, "rhcommon_c.dll")))
            {
                candidate = d;
                break;
            }
        }
    }

    if (string.IsNullOrEmpty(candidate) || !System.IO.Directory.Exists(candidate))
    {
        Console.WriteLine("INFO No Rhino install found; native-runtime tests will SKIP.");
        Console.WriteLine("INFO Override with FRAHAN_RHINO_SYSTEM=<path-to-Rhino-System-dir>.");
        return;
    }

    var path = Environment.GetEnvironmentVariable("PATH") ?? "";
    if (!path.Contains(candidate))
    {
        Environment.SetEnvironmentVariable("PATH", candidate + ";" + path);
    }
    if (!SetDllDirectory(candidate))
    {
        Console.WriteLine($"WARN SetDllDirectory failed for '{candidate}'.");
    }
    Console.WriteLine($"INFO Rhino native loader: {candidate}");
}

[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SetDllDirectory(string lpPathName);

var tests = new List<(string Name, Action Body)>
{
    // R3 PRs 1+2+3+4 - IrregularSheetFill unified façade: V1, V2, V3, V506 all wired
    ("R3 ForV506 construction succeeds (Rhino)", IrregularSheetFillEquivalenceTests.ForV506_Construction_Succeeds),
    ("R3 ForV506 null sheetOutlines throws", IrregularSheetFillEquivalenceTests.ForV506_NullSheetOutlines_Throws),
    ("R3 ForV506 null sheetHoles throws", IrregularSheetFillEquivalenceTests.ForV506_NullSheetHoles_Throws),
    ("R3 ForV1 construction succeeds (Rhino)", IrregularSheetFillEquivalenceTests.ForV1_Construction_Succeeds),
    ("R3 ForV1 null sheetOutlines throws", IrregularSheetFillEquivalenceTests.ForV1_NullSheetOutlines_Throws),
    ("R3 ForV1 null sheetHoles throws", IrregularSheetFillEquivalenceTests.ForV1_NullSheetHoles_Throws),
    ("R3 ForV2 construction succeeds (Rhino)", IrregularSheetFillEquivalenceTests.ForV2_Construction_Succeeds),
    ("R3 ForV2 null sheetOutlines throws", IrregularSheetFillEquivalenceTests.ForV2_NullSheetOutlines_Throws),
    ("R3 ForV2 null sheetHoles throws", IrregularSheetFillEquivalenceTests.ForV2_NullSheetHoles_Throws),
    ("R3 ForV3 construction succeeds (Rhino)", IrregularSheetFillEquivalenceTests.ForV3_Construction_Succeeds),
    ("R3 ForV3 null sheetOutlines throws", IrregularSheetFillEquivalenceTests.ForV3_NullSheetOutlines_Throws),
    ("R3 ForV3 null sheetHoles throws", IrregularSheetFillEquivalenceTests.ForV3_NullSheetHoles_Throws),
    ("R3 V506 façade equals legacy on empty inputs (Rhino)", IrregularSheetFillEquivalenceTests.V506_Facade_Equals_Legacy_OnEmptyInputs),
    ("R3 V1 façade equals legacy on empty inputs (Rhino)", IrregularSheetFillEquivalenceTests.V1_Facade_Equals_Legacy_OnEmptyInputs),
    ("R3 V2 façade equals legacy on empty inputs (Rhino)", IrregularSheetFillEquivalenceTests.V2_Facade_Equals_Legacy_OnEmptyInputs),
    ("R3 V3 façade equals legacy on empty inputs (Rhino)", IrregularSheetFillEquivalenceTests.V3_Facade_Equals_Legacy_OnEmptyInputs),
    // R3 PR 6 foundation - ForVariant routing
    ("R3 ForVariant V1 routes to V1 façade (Rhino)", IrregularSheetFillEquivalenceTests.ForVariant_V1_Routes_To_V1_Facade),
    ("R3 ForVariant V2 routes to V2 façade (Rhino)", IrregularSheetFillEquivalenceTests.ForVariant_V2_Routes_To_V2_Facade),
    ("R3 ForVariant V3 routes to V3 façade (Rhino)", IrregularSheetFillEquivalenceTests.ForVariant_V3_Routes_To_V3_Facade),
    ("R3 ForVariant V506 routes to V506 façade (Rhino)", IrregularSheetFillEquivalenceTests.ForVariant_V506_Routes_To_V506_Facade),
    ("R3 ForVariant invalid enum throws", IrregularSheetFillEquivalenceTests.ForVariant_Invalid_Throws),
    // R3 PR 6 - IrregularSheetFillComponent smoke tests (pure managed)
    ("R3 unified component ComponentGuid is expected value", IrregularSheetFillComponentTests.Component_ComponentGuid_IsExpectedValue),
    ("R3 unified component metadata (name/nick/category)", IrregularSheetFillComponentTests.Component_Metadata_IsCorrect),
    ("R3 unified component has 16 inputs and 11 outputs", IrregularSheetFillComponentTests.Component_HasExpectedInputAndOutputCount),
    ("Boundary-aware unified component inputs 12/13/14 are BMode/BAff/DTol", IrregularSheetFillComponentTests.Component_BoundaryInputs_HaveExpectedNames),
    ("R3 unified component last input is Variant", IrregularSheetFillComponentTests.Component_LastInput_IsVariant),
    ("R3 unified component last output is Variant Used", IrregularSheetFillComponentTests.Component_LastOutput_IsVariantUsed),
    // R3 async unified component smoke tests
    ("R3 async unified component ComponentGuid is expected value", IrregularSheetFillComponentTests.AsyncComponent_ComponentGuid_IsExpectedValue),
    ("R3 async unified component has 12 inputs and 9 outputs", IrregularSheetFillComponentTests.AsyncComponent_HasExpectedInputAndOutputCount),
    ("R3 async unified component NickName is FreeNestUA", IrregularSheetFillComponentTests.AsyncComponent_NickName_IsFreeNestUA),
    // 2026-05-05 - Bug B-2D-001 SheetHolesUtil PIP-first routing (Rhino)
    ("SheetHolesUtil per-sheet branches routes by path (Rhino)", SheetHolesUtilTests.PerSheetBranches_RoutesByPath),
    ("SheetHolesUtil flat list routes by PIP (Rhino)", SheetHolesUtilTests.FlatList_RoutesByPip),
    ("SheetHolesUtil mismatched path PIP overrides (Rhino)", SheetHolesUtilTests.MismatchedPath_PipOverrides),
    ("SheetHolesUtil out-of-range path PIP recovers (Rhino)", SheetHolesUtilTests.OutOfRangePath_PipRecovers),
    ("SheetHolesUtil disjoint hole falls back to path (Rhino)", SheetHolesUtilTests.DisjointHole_FallsBackToPath),
    ("SheetHolesUtil empty tree returns empty buckets", SheetHolesUtilTests.EmptyTree_ReturnsEmptyBuckets),
    ("SheetHolesUtil single sheet any branch falls through (Rhino)", SheetHolesUtilTests.SingleSheet_AnyBranchFallsThrough),
    // 2026-05-05 - Half B boundary-aware mode
    ("Boundary-aware mode=0 preserves geometric order (Rhino)", BoundaryAwarePackingTests.ModeOff_PreservesGeometricOrder),
    ("Boundary-aware mode=1 boundary-worthy part placed first (Rhino)", BoundaryAwarePackingTests.ModeOn_BoundaryWorthyPartPlacedFirst),
    ("Boundary-aware mode=1 no affinity falls back to area sort (Rhino)", BoundaryAwarePackingTests.ModeOn_NoAffinityFallsBackToAreaSort),
    ("Boundary-aware mode=1 construction does not throw (Rhino)", BoundaryAwarePackingTests.ModeOn_Construction_DoesNotThrow),
    // Half C
    ("Boundary-aware mode=2 construction does not throw (Rhino)", BoundaryAwarePackingTests.Mode2_Construction_DoesNotThrow),
    ("Boundary-aware mode=2 boundary-worthy lands on edge (Rhino)", BoundaryAwarePackingTests.Mode2_BoundaryWorthyLandsOnEdge),
    ("Boundary-aware mode=2 falls back when boundary saturated (Rhino)", BoundaryAwarePackingTests.Mode2_FallsBackWhenBoundarySaturated),
    ("Boundary-aware mode=1 auto-tunes to part scale (Rhino)", BoundaryAwarePackingTests.Mode1_AutoTunes_ToPartScale),
    // PackingPlanReport composite (pure managed)
    ("plan report Build null metrics throws", PackingPlanReportTests.Build_NullMetrics_Throws),
    ("plan report Build all null except metrics empty", PackingPlanReportTests.Build_AllNullExceptMetrics_Empty),
    ("plan report Build sums residual void areas", PackingPlanReportTests.Build_SumsResidualVoidAreas),
    ("plan report Build flattens per-fragment edge scores and averages", PackingPlanReportTests.Build_FlattensPerFragmentEdgeScoresAndAverages),
    ("plan report Build tolerant to null void entries", PackingPlanReportTests.Build_TolerantToNullVoidEntries),
    ("plan report ToString is human readable", PackingPlanReportTests.ToString_IsHumanReadable),
    // 2026-06-04 - ReconstructionCleanup: alpha-shape weird-mesh fix (pure managed)
    ("recon cleanup drops isolated spike, keeps largest component", ReconstructionCleanupTests.Clean_DropsIsolatedSpike_KeepsLargestComponent),
    ("recon cleanup drops degenerate + duplicate triangles", ReconstructionCleanupTests.Clean_DropsDegenerateAndDuplicateTriangles),
    ("recon cleanup Translate adds centroid back", ReconstructionCleanupTests.Translate_AddsCentroidBack),
    ("recon recenter+translate round-trips at quarry scale", ReconstructionCleanupTests.RecenterTranslate_RoundTrips_AtQuarryScale),
    ("recon cleanup null/empty is safe", ReconstructionCleanupTests.Clean_EmptyOrNull_IsSafe),
    // 2026-06-04 - OverburdenVolume (cut-fill -> rock-face core A5, pure managed)
    ("overburden constant depth = area*depth", OverburdenVolumeTests.ConstantDepth_VolumeEqualsAreaTimesDepth),
    ("overburden sloped all-positive uses mean depth", OverburdenVolumeTests.SlopedAllPositive_UsesMeanDepth),
    ("overburden sign change splits cut/fill exactly", OverburdenVolumeTests.SignChange_SplitsCutAndFillExactly),
    ("overburden net = mean*area invariant", OverburdenVolumeTests.NetEqualsMeanTimesArea_Invariant),
    ("overburden quarry-scale recenter keeps area exact", OverburdenVolumeTests.QuarryScale_RecenterKeepsAreaExact),
    ("overburden two-triangle square sums both", OverburdenVolumeTests.TwoTriangleSquare_SumsBoth),
    ("overburden null/bad inputs throw", OverburdenVolumeTests.NullAndBadInputs_Throw),
    ("Overburden To Rock Face component metadata", OverburdenToRockFaceComponentTests.Metadata_IsExpectedValues),
    // Item D (2026-05-04) - PackingPlanReportComponent smoke tests after DataTree input
    ("plan report component ComponentGuid is expected value", PackingPlanReportComponentTests.Component_ComponentGuid_IsExpectedValue),
    ("plan report component metadata (name/category)", PackingPlanReportComponentTests.Component_Metadata_IsCorrect),
    ("plan report component has 4 inputs and 4 outputs", PackingPlanReportComponentTests.Component_HasFourInputsAndFourOutputs),
    ("plan report component fourth input is Edge Match Tree", PackingPlanReportComponentTests.Component_FourthInput_IsEdgeMatchTree),
    ("plan report component third input still item-access", PackingPlanReportComponentTests.Component_ThirdInput_EdgeMatchScores_StillItemAccess),
    // BoundaryRailMatcher + EdgeMatch + MatchOptions (pure managed)
    ("matcher MatchEdge null index throws", BoundaryRailMatcherTests.MatchEdge_NullIndex_Throws),
    ("matcher MatchEdge null query throws", BoundaryRailMatcherTests.MatchEdge_NullQuery_Throws),
    ("matcher MatchEdge null options throws", BoundaryRailMatcherTests.MatchEdge_NullOptions_Throws),
    ("matcher MatchEdge null converter throws", BoundaryRailMatcherTests.MatchEdge_NullConverter_Throws),
    ("matcher MatchEdge empty index returns empty", BoundaryRailMatcherTests.MatchEdge_EmptyIndex_ReturnsEmpty),
    ("matcher MatchEdge perfect match scores highest", BoundaryRailMatcherTests.MatchEdge_PerfectMatchScoresHighest),
    ("matcher MatchEdge results sorted descending", BoundaryRailMatcherTests.MatchEdge_ResultsAreSortedDescending),
    ("matcher MatchEdge preserveZone=true excludes other zones", BoundaryRailMatcherTests.MatchEdge_PreserveZoneTrue_ExcludesOtherZones),
    ("matcher MatchEdge preserveZone=false includes other zones", BoundaryRailMatcherTests.MatchEdge_PreserveZoneFalse_IncludesOtherZones),
    ("matcher MatchEdge length radius 0 excludes far length", BoundaryRailMatcherTests.MatchEdge_LengthRadiusZero_ExcludesFarLength),
    ("matcher MatchEdge MinAffinityScore filters low matches", BoundaryRailMatcherTests.MatchEdge_MinAffinityScore_FiltersLowMatches),
    ("matcher MatchEdge TopK limits results", BoundaryRailMatcherTests.MatchEdge_TopK_LimitsResults),
    ("matcher MatchEdge TopK=0 returns all", BoundaryRailMatcherTests.MatchEdge_TopKZero_ReturnsAll),
    ("matcher MatchEdge converter throws skips candidate", BoundaryRailMatcherTests.MatchEdge_ConverterThrows_SkipsCandidate),
    ("matcher MatchFragment null fragment throws", BoundaryRailMatcherTests.MatchFragment_NullFragment_Throws),
    ("matcher MatchFragment returns one list per edge", BoundaryRailMatcherTests.MatchFragment_ReturnsOneListPerEdge),
    // PackingDescriptors + EdgeAffinityScorer (pure managed)
    ("descriptors EdgeDescriptor negative length throws", PackingDescriptorsTests.EdgeDescriptor_NegativeLength_Throws),
    ("descriptors EdgeDescriptor ToEdgeKey quantises all four", PackingDescriptorsTests.EdgeDescriptor_ToEdgeKey_QuantisesAllFour),
    ("descriptors EdgeDescriptor negative angle wraps to 360", PackingDescriptorsTests.EdgeDescriptor_NegativeAngle_WrapsTo360),
    ("descriptors FragmentDescriptor null id throws", PackingDescriptorsTests.FragmentDescriptor_NullId_Throws),
    ("descriptors FragmentDescriptor null edges becomes empty", PackingDescriptorsTests.FragmentDescriptor_NullEdges_BecomesEmpty),
    ("descriptors Scorer identical edges is 1", PackingDescriptorsTests.Score_IdenticalEdges_IsOne),
    ("descriptors Scorer opposite angle is 0", PackingDescriptorsTests.Score_OppositeAngle_IsZero),
    ("descriptors Scorer different zones preserveZone=true is 0", PackingDescriptorsTests.Score_DifferentZones_PreserveZoneTrue_IsZero),
    ("descriptors Scorer different zones preserveZone=false is non-zero", PackingDescriptorsTests.Score_DifferentZones_PreserveZoneFalse_IsNonZero),
    ("descriptors Scorer null argument throws", PackingDescriptorsTests.Score_NullArgument_Throws),
    ("descriptors AngleDistance 350 vs 10 is 20", PackingDescriptorsTests.AngleDistance_350vs10_IsTwenty),
    // PackingMetrics (pure managed)
    ("metrics Compute null throws", PackingMetricsTests.Compute_Null_Throws),
    ("metrics Compute empty result returns zero metrics", PackingMetricsTests.Compute_EmptyResult_ReturnsZeroMetrics),
    ("metrics Compute mixed result computes all fields", PackingMetricsTests.Compute_MixedResult_ComputesAllFields),
    ("metrics Report ToString is human readable", PackingMetricsTests.Report_ToString_IsHumanReadable),
    // ChartFlatnessReport (pure managed)
    ("flatness Classify null list throws", ChartFlatnessReportTests.Classify_NullList_Throws),
    ("flatness Classify non-positive threshold throws", ChartFlatnessReportTests.Classify_NonPositiveThreshold_Throws),
    ("flatness Classify empty list has zero faces", ChartFlatnessReportTests.Classify_EmptyList_HasZeroFaces),
    ("flatness Classify all inside threshold none flagged", ChartFlatnessReportTests.Classify_AllInsideThreshold_NoneFlagged),
    ("flatness Classify one above threshold flagged correctly", ChartFlatnessReportTests.Classify_OneAboveThreshold_FlaggedCorrectly),
    ("flatness Classify low ratio treated as distorted too", ChartFlatnessReportTests.Classify_LowRatio_TreatedAsDistortedToo),
    ("flatness Classify worst face is highest normalised distortion", ChartFlatnessReportTests.Classify_WorstFace_IsHighestNormalisedDistortion),
    ("flatness Classify zero ratio treated as infinitely distorted", ChartFlatnessReportTests.Classify_ZeroRatio_TreatedAsInfinitelyDistorted),
    // MeshDiagnostics (mostly Rhino-runtime; null guards pure managed)
    ("mesh diag VertexCount null returns zero", MeshDiagnosticsTests.VertexCount_NullMesh_ReturnsZero),
    ("mesh diag FaceCount null returns zero", MeshDiagnosticsTests.FaceCount_NullMesh_ReturnsZero),
    ("mesh diag IsClosed null returns false", MeshDiagnosticsTests.IsClosed_NullMesh_ReturnsFalse),
    ("mesh diag IsManifold null returns false", MeshDiagnosticsTests.IsManifold_NullMesh_ReturnsFalse),
    ("mesh diag AverageEdgeLength null returns zero", MeshDiagnosticsTests.AverageEdgeLength_NullMesh_ReturnsZero),
    ("mesh diag BoundingBoxVolume null returns zero", MeshDiagnosticsTests.BoundingBoxVolume_NullMesh_ReturnsZero),
    ("mesh diag Counts single triangle returns expected (Rhino)", MeshDiagnosticsTests.Counts_SingleTriangle_ReturnsExpected),
    ("mesh diag IsClosed single triangle is false (Rhino)", MeshDiagnosticsTests.IsClosed_SingleTriangle_IsFalse),
    ("mesh diag BoundingBoxVolume flat triangle is zero (Rhino)", MeshDiagnosticsTests.BoundingBoxVolume_FlatTriangle_IsZero),
    ("mesh diag AverageEdgeLength unit triangle is expected (Rhino)", MeshDiagnosticsTests.AverageEdgeLength_UnitTriangle_IsExpected),
    // BoundaryRailBuilder + BoundaryIntervalInfo (Rhino-bound; some tests SKIP without Rhino runtime)
    ("rail builder ctor non-positive window throws", BoundaryRailBuilderTests.Builder_Ctor_NonPositiveWindow_Throws),
    ("rail builder ctor non-positive step throws", BoundaryRailBuilderTests.Builder_Ctor_NonPositiveStep_Throws),
    ("rail builder ctor non-positive length bucket throws", BoundaryRailBuilderTests.Builder_Ctor_NonPositiveLengthBucket_Throws),
    ("rail builder ctor non-positive angle bucket throws", BoundaryRailBuilderTests.Builder_Ctor_NonPositiveAngleBucket_Throws),
    ("rail builder IntervalInfo null curve throws", BoundaryRailBuilderTests.IntervalInfo_NullCurve_Throws),
    ("rail builder BucketInterval length bucket quantises", BoundaryRailBuilderTests.BucketInterval_LengthBucket_QuantisesByLengthBucketSize),
    ("rail builder BucketInterval angle horizontal is bucket 0", BoundaryRailBuilderTests.BucketInterval_AngleBucket_HorizontalLineIsBucket0),
    ("rail builder BucketInterval angle vertical is expected bucket", BoundaryRailBuilderTests.BucketInterval_AngleBucket_NinetyDegreeLineIsExpectedBucket),
    ("rail builder BucketInterval negative angle wraps to 360", BoundaryRailBuilderTests.BucketInterval_AngleBucket_NegativeAngleWrapsTo360),
    ("rail builder BucketInterval null interval throws", BoundaryRailBuilderTests.BucketInterval_NullInterval_Throws),
    ("rail builder BucketInterval deterministic for same input", BoundaryRailBuilderTests.BucketInterval_DeterministicForSameInput),
    ("rail builder BuildInterval populates fields (Rhino)", BoundaryRailBuilderTests.BuildInterval_HorizontalLine_PopulatesFields),
    ("rail builder BuildInterval null curve throws", BoundaryRailBuilderTests.BuildInterval_NullCurve_Throws),
    ("rail builder AddCurve populates index (Rhino)", BoundaryRailBuilderTests.AddCurve_PopulatesIndex_WithExpectedIntervalCount),
    ("rail builder AddCurve null boundary throws", BoundaryRailBuilderTests.AddCurve_NullBoundary_Throws),
    ("rail builder AddCurve null index throws", BoundaryRailBuilderTests.AddCurve_NullIndex_Throws),
    // ResidualVoidsDetector unit tests (pure managed; 2D void detection)
    ("residual voids ctor non-positive cell size throws", ResidualVoidsDetectorTests.Ctor_NonPositiveCellSize_Throws),
    ("residual voids ctor negative min area throws", ResidualVoidsDetectorTests.Ctor_NegativeMinArea_Throws),
    ("residual voids empty sheet returns one full void", ResidualVoidsDetectorTests.Detect_EmptySheet_ReturnsOneFullVoid),
    ("residual voids fully covered sheet returns zero voids", ResidualVoidsDetectorTests.Detect_FullyCoveredSheet_ReturnsZeroVoids),
    ("residual voids single corner hole returns one void", ResidualVoidsDetectorTests.Detect_SingleCornerHole_ReturnsOneVoid),
    ("residual voids two separated voids returns two", ResidualVoidsDetectorTests.Detect_TwoSeparatedVoids_ReturnsTwo),
    ("residual voids min area filter removes small voids", ResidualVoidsDetectorTests.Detect_FiltersOutVoidsBelowMinArea),
    ("residual voids L-shape sheet respects concave outline", ResidualVoidsDetectorTests.Detect_LShapeSheet_RespectsConcaveOutline),
    ("residual voids null sheet throws", ResidualVoidsDetectorTests.Detect_NullSheet_Throws),
    ("residual voids null placed-parts list throws", ResidualVoidsDetectorTests.Detect_NullPlacedPartsList_Throws),
    ("residual voids degenerate sheet throws", ResidualVoidsDetectorTests.Detect_DegenerateSheet_Throws),
    // NativeBridge unit tests (pure managed; managed-default + loader behaviour)
    ("native bridge BackendDiagnostic stores level and message", NativeBridgeTests.BackendDiagnostic_StoresLevelAndMessage),
    ("native bridge BackendDiagnostic null message throws", NativeBridgeTests.BackendDiagnostic_NullMessage_Throws),
    ("native bridge BackendOperationResult basic construction", NativeBridgeTests.BackendOperationResult_BasicConstruction),
    ("native bridge BackendOperationResult null diagnostics gives empty list", NativeBridgeTests.BackendOperationResult_NullDiagnostics_GivesEmptyList),
    ("native bridge ManagedGeometry is always available", NativeBridgeTests.ManagedGeometry_IsAlwaysAvailable),
    ("native bridge ManagedGeometry Repair round-trips input", NativeBridgeTests.ManagedGeometry_Repair_RoundTripsInput),
    ("native bridge ManagedGeometry Simplify round-trips input", NativeBridgeTests.ManagedGeometry_Simplify_RoundTripsInput),
    ("native bridge ManagedGeometry Simplify ratio out-of-range throws", NativeBridgeTests.ManagedGeometry_Simplify_RatioOutOfRange_Throws),
    ("native bridge ManagedPacking BuildCollisionProxy returns input", NativeBridgeTests.ManagedPacking_BuildCollisionProxy_ReturnsInput),
    ("native bridge ManagedPacking null input throws", NativeBridgeTests.ManagedPacking_NullInput_Throws),
    ("native bridge Loader never throws on missing native", NativeBridgeTests.Loader_NeverThrowsOnMissingNative),
    ("native bridge Loader GetSearchPaths returns at least one", NativeBridgeTests.Loader_GetSearchPaths_ReturnsAtLeastOne),
    ("native bridge Loader caches geometry backend", NativeBridgeTests.Loader_CachesGeometryBackend),
    ("native bridge Loader forceReload produces new instance", NativeBridgeTests.Loader_ForceReload_ProducesNewInstance),
    // Probe tests added 2026-05-06 (real native-DLL probing in NativeBackendLoader)
    ("native bridge Loader preference managed returns managed and skips probe", NativeBridgeTests.Loader_PreferenceManaged_ReturnsManagedAndSkipsProbe),
    ("native bridge Loader geometry probe report populated after Choose", NativeBridgeTests.Loader_GeometryProbeReport_IsPopulatedAfterChoose),
    ("native bridge Loader packing probe report populated after Choose", NativeBridgeTests.Loader_PackingProbeReport_IsPopulatedAfterChoose),
    ("native bridge Loader bogus DLL in search path does not throw and records LoadFailed", NativeBridgeTests.Loader_BogusDllInSearchPath_DoesNotThrow_RecordsLoadFailed),
    ("native bridge Loader FRAHAN_BACKEND managed keyword returns managed", NativeBridgeTests.Loader_FrahanBackendEnv_ManagedKeyword_ReturnsManaged),
    // Masonry data model — Phase A.1 of the compas_cra C# port (Kao 2022)
    ("masonry block null id throws", MasonryDataModelTests.MasonryBlock_NullId_Throws),
    ("masonry block null vertex coords throws", MasonryDataModelTests.MasonryBlock_NullVertexCoords_Throws),
    ("masonry block negative density throws", MasonryDataModelTests.MasonryBlock_NegativeDensity_Throws),
    ("masonry block vertex and triangle counts are consistent", MasonryDataModelTests.MasonryBlock_VertexAndTriangleCounts_AreConsistent),
    ("masonry block bad triangle index throws", MasonryDataModelTests.MasonryBlock_BadTriangleIndex_Throws),
    ("masonry block vertex coords length not multiple of 3 throws", MasonryDataModelTests.MasonryBlock_VertexCoordsLengthNotMultipleOf3_Throws),
    ("masonry contact vertex stores coordinates", MasonryDataModelTests.ContactVertex_StoresCoordinates),
    ("masonry contact vertex equality and hash are consistent", MasonryDataModelTests.ContactVertex_EqualityAndHash_AreConsistent),
    ("masonry interface null polygon throws", MasonryDataModelTests.MasonryInterface_NullPolygon_Throws),
    ("masonry interface polygon too small throws", MasonryDataModelTests.MasonryInterface_PolygonTooSmall_Throws),
    ("masonry interface same block ids throws", MasonryDataModelTests.MasonryInterface_SameBlockIds_Throws),
    ("masonry interface stores frame vectors", MasonryDataModelTests.MasonryInterface_StoresFrameVectors),
    ("masonry boundary conditions IsFixed false for unknown id", MasonryDataModelTests.BoundaryConditions_IsFixed_ReturnsFalseForUnknownId),
    ("masonry boundary conditions IsFixed true for known id", MasonryDataModelTests.BoundaryConditions_IsFixed_ReturnsTrueForKnownId),
    ("masonry boundary conditions null enumerable throws", MasonryDataModelTests.BoundaryConditions_NullEnumerable_Throws),
    ("masonry assembly stores blocks and interfaces", MasonryDataModelTests.MasonryAssembly_StoresBlocksAndInterfaces),
    ("masonry assembly duplicate block id throws", MasonryDataModelTests.MasonryAssembly_DuplicateBlockId_Throws),
    ("masonry assembly interface referencing unknown block throws", MasonryDataModelTests.MasonryAssembly_InterfaceReferencingUnknownBlock_Throws),
    ("masonry assembly null boundary conditions throws", MasonryDataModelTests.MasonryAssembly_NullBoundaryConditions_Throws),
    ("masonry assembly FreeBlocks excludes fixed", MasonryDataModelTests.MasonryAssembly_FreeBlocks_ExcludesFixed),
    // Masonry equilibrium-matrix builder — Phase A.2 of the compas_cra C# port
    ("masonry sparse COO ToDense round-trips triples", MasonryEquilibriumTests.SparseMatrixCoo_ToDense_RoundTripsTriples),
    ("masonry sparse COO Add zero does not increment nnz", MasonryEquilibriumTests.SparseMatrixCoo_AddZero_DoesNotIncrementNnz),
    ("masonry sparse COO out-of-bounds row throws", MasonryEquilibriumTests.SparseMatrixCoo_OutOfBoundsRow_Throws),
    ("masonry block COM unit cube volume-weighted is center", MasonryEquilibriumTests.BlockCenterOfMass_UnitCube_VolumeWeighted_IsCenter),
    ("masonry block COM unit cube vertex-mean is center", MasonryEquilibriumTests.BlockCenterOfMass_UnitCube_VertexMean_IsCenter),
    ("masonry block COM degenerate falls back to vertex mean", MasonryEquilibriumTests.BlockCenterOfMass_DegenerateBlock_FallsBackToVertexMean),
    ("masonry equilibrium one free block on ground Aeq shape", MasonryEquilibriumTests.EquilibriumBuilder_OneFreeBlockOnGround_AeqShape_IsCorrect),
    ("masonry equilibrium penalty shift doubles normal columns", MasonryEquilibriumTests.EquilibriumBuilder_PenaltyShift_DoublesNormalColumns),
    ("masonry equilibrium gravity populates Z-force row", MasonryEquilibriumTests.EquilibriumBuilder_GravityPopulatesZForceRow),
    ("masonry equilibrium normal forces only contribute to Z-force for flat horizontal interface", MasonryEquilibriumTests.EquilibriumBuilder_NormalForcesContributeOnlyToZForce_ForFlatHorizontalInterface),
    ("masonry equilibrium tangent moment arm is correct", MasonryEquilibriumTests.EquilibriumBuilder_TangentForceMomentArmIsCorrect),
    ("masonry equilibrium two stacked blocks both free has two block rows", MasonryEquilibriumTests.EquilibriumBuilder_TwoStackedBlocks_BothFree_HasTwoBlockRows),
    // Masonry friction-cone (Phase A.3 sanity tests)
    ("masonry friction cone null equilibrium throws", MasonryFrictionConeTests.FrictionConeBuilder_NullEquilibrium_Throws),
    ("masonry friction cone non-positive mu throws", MasonryFrictionConeTests.FrictionConeBuilder_NonPositiveMu_Throws),
    ("masonry friction cone face count below 3 throws", MasonryFrictionConeTests.FrictionConeBuilder_FaceCountBelow3_Throws),
    ("masonry friction cone one free block 4 faces has 16 rows", MasonryFrictionConeTests.FrictionConeBuilder_OneFreeBlock_4Faces_Has16Rows),
    ("masonry friction cone one free block 8 faces has 32 rows", MasonryFrictionConeTests.FrictionConeBuilder_OneFreeBlock_8Faces_Has32Rows),
    ("masonry friction cone 4-face coefficients are exact integer", MasonryFrictionConeTests.FrictionConeBuilder_4FaceCoefficients_AreExactInteger),
    ("masonry friction cone penalty mode splits normal into pair", MasonryFrictionConeTests.FrictionConeBuilder_PenaltyMode_SplitsNormalIntoPair),
    ("masonry friction cone default mu is 0.84", MasonryFrictionConeTests.FrictionConeBuilder_DefaultMu_Is084),
    // Masonry RBE QP formulation (Phase B sanity tests)
    ("masonry RBE QP null equilibrium throws", MasonryRbeFormulationTests.RbeQpFormulation_NullEquilibrium_Throws),
    ("masonry RBE QP variable count matches Aeq col count", MasonryRbeFormulationTests.RbeQpFormulation_VariableCount_MatchesAeqColCount),
    ("masonry RBE QP Hessian is scaled identity", MasonryRbeFormulationTests.RbeQpFormulation_HessianIsScaledIdentity),
    ("masonry RBE QP linear objective is zero", MasonryRbeFormulationTests.RbeQpFormulation_LinearObjective_IsZero),
    ("masonry RBE QP equality RHS is -B", MasonryRbeFormulationTests.RbeQpFormulation_EqualityRhs_IsNegB),
    ("masonry RBE QP normal columns LB=0 tangents unbounded", MasonryRbeFormulationTests.RbeQpFormulation_NormalColumnsLowerBoundIsZero_TangentsUnbounded),
    ("masonry RBE QP no friction inequality is null", MasonryRbeFormulationTests.RbeQpFormulation_NoFriction_InequalityIsNull),
    // ManagedQpSolver — Dykstra alternating projections (Phase B implementation)
    ("masonry QP solver equality only two vars finds midpoint", ManagedQpSolverTests.Solve_EqualityOnly_TwoVars_FindsMidpoint),
    ("masonry QP solver bounds only one active gives active corner", ManagedQpSolverTests.Solve_BoundsOnly_OneActive_GivesActiveCorner),
    ("masonry QP solver equality and bounds projects on simplex edge", ManagedQpSolverTests.Solve_EqualityAndBounds_ProjectsOnSimplexEdge),
    ("masonry QP solver equality clamps bound active", ManagedQpSolverTests.Solve_EqualityClampsBoundActive),
    ("masonry QP solver inequality half-space projection", ManagedQpSolverTests.Solve_Inequality_HalfSpaceProjection),
    ("masonry QP solver non-diagonal hessian returns NotImplemented", ManagedQpSolverTests.Solve_NonDiagonalHessian_ReturnsNotImplemented),
    ("masonry QP solver non-zero linear objective returns NotImplemented", ManagedQpSolverTests.Solve_NonZeroLinearObjective_ReturnsNotImplemented),
    ("masonry QP solver scaled identity hessian still works", ManagedQpSolverTests.Solve_ScaledIdentityHessian_StillWorks),
    ("masonry QP solver null problem throws", ManagedQpSolverTests.Solve_NullProblem_Throws),
    ("masonry QP solver ctor non-positive tolerance throws", ManagedQpSolverTests.Ctor_NonPositiveTolerance_Throws),
    // PolygonalWallGenerator (evolution P3) + MasonryStabilityChecker (evolution P1), 2026-06-10
    ("wallgen power diagram tiles the rectangle", PolygonalWallGeneratorTests.Generate_TilesTheRectangle),
    ("wallgen is deterministic for a fixed seed", PolygonalWallGeneratorTests.Generate_IsDeterministic),
    ("wallgen Lloyd relaxation reduces area spread", PolygonalWallGeneratorTests.Generate_LloydReducesAreaSpread),
    ("wallgen sliver cull off reports zero", PolygonalWallGeneratorTests.Generate_SliverCullOffReportsZero),
    ("wallgen interlock score in range across coursing", PolygonalWallGeneratorTests.Generate_InterlockScoreInRange_AndCoursingExtremesValid),
    ("wallgen size grading widens area distribution", PolygonalWallGeneratorTests.Generate_SizeGradingIncreasesAreaSpread),
    ("stability two-box stack is stable", MasonryStabilityCheckerTests.TwoBoxStack_IsStable),
    ("stability floating block is unstable", MasonryStabilityCheckerTests.FloatingBlock_IsUnstable),
    ("stability cantilever beyond support is unstable", MasonryStabilityCheckerTests.CantileverBeyondSupport_IsUnstable),
    ("stability inscribed friction shrinks mu by cos(pi/K)", MasonryStabilityCheckerTests.InscribedFriction_ShrinksMuByCosPiOverK),
    ("stability generated coursed wall prisms are stable", MasonryStabilityCheckerTests.GeneratedWall_PrismStones_AreStable),
    ("stability 40-stone wall benchmark (sparse ADMM)", MasonryStabilityCheckerTests.GeneratedWall_40Stones_StableAndFast),
    ("stability 40-stone wall via adjacency assembler (P1.2)", MasonryStabilityCheckerTests.GeneratedWall_AdjacencyAssembler_StableAndLean),
    // CRA coupling (P2): Kao 2022 Eqs 8-14 by alternating convex certificate
    ("CRA two-box stack is certified", CraStabilityCheckerTests.Cra_TwoBoxStack_Certified),
    ("CRA cantilever is unstable", CraStabilityCheckerTests.Cra_Cantilever_Unstable),
    ("CRA H-model: RBE accepts, CRA rejects (Kao counterexample)", CraStabilityCheckerTests.Cra_HModel_RbeAcceptsButCraRejects),
    ("CRA generated wall is certified", CraStabilityCheckerTests.Cra_GeneratedWall_Certified),
    // Stone-cell assignment / imposition index Lambda (P4)
    ("Lambda identical inventory near zero", StoneCellAssignmentTests.IdenticalInventory_LambdaNearZero),
    ("Lambda inflated inventory matches analytic + identity recovery", StoneCellAssignmentTests.InflatedInventory_AnalyticLambda_AndIdentityRecovery),
    ("Lambda monotonic in stock coarseness", StoneCellAssignmentTests.CoarserInventory_LambdaMonotonic),
    ("Lambda extra inventory reports unused stones", StoneCellAssignmentTests.ExtraInventory_ReportsUnusedStones),
    ("Lambda ETH1100 real rubble on generated wall (skips without dataset)", StoneCellAssignmentEthBenchmarkTests.Lambda_EthRubble_OnGeneratedWall),
    // exact Cyclopean carve-back (P4c)
    ("carve-back inflated inventory exact analytic", StoneCarveBackTests.CarveBack_InflatedInventory_ExactAnalytic),
    ("carve-back exact refines the voxel estimate", StoneCarveBackTests.CarveBack_ExactRefinesVoxelEstimate),
    ("carve-back identity placements exact analytic 1.25", StoneCarveBackTests.CarveBack_IdentityPlacements_ExactAnalytic125),
    // compas_cra parity (Block 3 item 1): exact ports of their parametric doc examples
    ("compas_cra parity 00_simple_cube both stable", CraCompasParityTests.Compas_SimpleCube_BothStable),
    ("compas_cra parity tutorial_cubes both stable", CraCompasParityTests.Compas_TutorialCubes_BothStable),
    ("compas_cra parity 04_stacks 20deg both stable", CraCompasParityTests.Compas_Stacks20Deg_BothStable),
    ("compas_cra parity 06_arch n=20 mu=0.7 both stable (timed)", CraCompasParityTests.Compas_Arch20_BothStable_Timed),
    ("kb9 diagnostics tilt sweep (prints, never fails)", Kb9DiagnosticsTests.Kb9_TiltSweep_EqualityConsistency),
    ("kb9 diagnostics arch forensics (prints, never fails)", Kb9DiagnosticsTests.Kb9_Arch_Forensics),
    ("kb9 diagnostics arch CLEAN joints (prints, never fails)", Kb9DiagnosticsTests.Kb9_Arch_CleanJoints),
    ("kb9 diagnostics arch coplanar-resolver (prints, never fails)", Kb9DiagnosticsTests.Kb9_Arch_CoplanarResolver),
    // compas_cra json-data fixtures (wedge type-b, bridge)
    ("compas_cra json wedge type-b rot90Y both stable", CraCompasJsonFixtureTests.Compas_Json_WedgeTypeB_Rotated90Y_BothStable),
    ("compas_cra json bridge both stable", CraCompasJsonFixtureTests.Compas_Json_Bridge_BothStable),
    // Lambda flagship study (REPORTED table; skips without ETH data)
    ("lambda study coursing-by-assigner table (Rhino-free, skips without dataset)", LambdaStudyBenchmarkTests.LambdaStudy_CoursingByAssigner_Table),
    // settle v2 (P5): Furrer/Johns candidate ranking vs legacy, real ETH stones
    ("settle v2 ETH stones not-worse stability + better seating (Rhino, skips without dataset)", RubbleSettleV2BenchmarkTests.SettleV2_EthStones_NotWorseStability_BetterSeating),
    // IFC terminal (P6): write -> reopen -> assert graph + psets (pure managed)
    ("IFC export wall round-trip (xBIM)", StoneAssemblyIfcWriterTests.IfcExport_Wall_RoundTrip),
    ("IFC export arch + cladding containers (xBIM)", StoneAssemblyIfcWriterTests.IfcExport_Arch_And_Cladding_Containers),
    ("IFC export multi-container building round-trip (xBIM, P7)", StoneAssemblyIfcWriterTests.IfcExport_MultiContainer_Building_RoundTrip),
    // Masonry GH components (Phase D smoke tests; 1-9 SKIP without Grasshopper, 10 PASS)
    ("masonry GH MasonryBlockComponent ComponentGuid is expected (Rhino)", Frahan.Tests.MasonryGhComponentTests.MasonryBlockComponent_ComponentGuid_IsExpectedValue),
    ("masonry GH MasonryBlockComponent metadata is correct (Rhino)", Frahan.Tests.MasonryGhComponentTests.MasonryBlockComponent_Metadata_IsCorrect),
    ("masonry GH MasonryBlockComponent has expected input/output count (Rhino)", Frahan.Tests.MasonryGhComponentTests.MasonryBlockComponent_HasExpectedInputAndOutputCount),
    ("masonry GH MasonryBlockComponent second output is Id text (Rhino)", Frahan.Tests.MasonryGhComponentTests.MasonryBlockComponent_SecondOutputIsIdText),
    ("masonry GH AssemblyPreviewComponent ComponentGuid (Rhino)", Frahan.Tests.MasonryGhComponentTests.AssemblyPreviewComponent_ComponentGuid_IsExpectedValue),
    ("masonry GH AssemblyPreviewComponent metadata (Rhino)", Frahan.Tests.MasonryGhComponentTests.AssemblyPreviewComponent_Metadata_IsCorrect),
    ("masonry GH AssemblyPreviewComponent has expected input/output count (Rhino)", Frahan.Tests.MasonryGhComponentTests.AssemblyPreviewComponent_HasExpectedInputAndOutputCount),
    // Rigid transform recovery (Horn QAO)
    ("RigidTransform identity recovers identity", RigidTransformRecoveryTests.Solve_Identity_RecoversIdentityRotationAndZeroTranslation),
    ("RigidTransform pure translation recovers t", RigidTransformRecoveryTests.Solve_PureTranslation_RecoversIdentityRotationAndCorrectT),
    ("RigidTransform pure rotation Z recovers 90deg", RigidTransformRecoveryTests.Solve_PureRotationZ_Recovers90DegRotation),
    ("RigidTransform rotation+translation round trips", RigidTransformRecoveryTests.Solve_RotationPlusTranslation_RoundTrips),
    ("RigidTransform null source throws", RigidTransformRecoveryTests.Solve_NullSource_Throws),
    ("RigidTransform mismatched lengths throws", RigidTransformRecoveryTests.Solve_MismatchedLengths_Throws),
    ("RigidTransform fewer than 3 pairs throws", RigidTransformRecoveryTests.Solve_FewerThanThreePairs_Throws),
    ("GH BlockGroundTransforms metadata (Rhino)", RigidTransformRecoveryTests.Gh_BlockGroundTransformsComponent_Metadata),
    ("GH BlockGroundTransforms optional inputs (Rhino)", RigidTransformRecoveryTests.Gh_BlockGroundTransformsComponent_OptionalInputs),
    // Phase I (UX report §7.7.F) — Registration / cloud-cloud ICP — easy 80%
    ("Registration SolveFromPoints identity recovers identity", RegistrationApiTests.SolveFromPoints_Identity_RecoversIdentityTransform),
    ("Registration SolveFromPoints translation recovers t", RegistrationApiTests.SolveFromPoints_PureTranslation_RecoversTranslationOnly),
    ("Registration SolveFromPoints rotation+translation round trips", RegistrationApiTests.SolveFromPoints_RotationPlusTranslation_RoundTrips),
    ("Registration SolveFromPoints noisy markers non-zero rms", RegistrationApiTests.SolveFromPoints_NoisyMarkers_ProducesNonZeroRms),
    ("Registration SolveFromPoints mismatched counts throws", RegistrationApiTests.SolveFromPoints_MismatchedCounts_Throws),
    ("Registration SolveFromPoints fewer than 3 pairs throws", RegistrationApiTests.SolveFromPoints_FewerThan3Pairs_Throws),
    ("Georef LLH ECEF round-trip preserves position", RegistrationApiTests.GeoreferenceMath_LlhEcefRoundTrip_PreservesPosition),
    ("Georef ENU ECEF round-trip preserves position", RegistrationApiTests.GeoreferenceMath_EnuEcefRoundTrip_PreservesPosition),
    ("Georef UTM LLH round-trip preserves position", RegistrationApiTests.GeoreferenceMath_UtmLlhRoundTrip_PreservesPosition),
    // Phase F (UX report §7.7.A-B) — Scan ingest multi-format reader
    ("ScanIngest OBJ single triangle parses one mesh", ScanIngestTests.Obj_SingleTriangle_ParsesOneMeshOneTriangle),
    ("ScanIngest OBJ quad fan-triangulates to two tris", ScanIngestTests.Obj_QuadFace_FanTriangulatesIntoTwoTriangles),
    ("ScanIngest OBJ triplet face syntax keeps vertex index", ScanIngestTests.Obj_TripletFaceSyntax_KeepsVertexIndex),
    ("ScanIngest OBJ two groups produces two meshes", ScanIngestTests.Obj_TwoGroups_ProducesTwoMeshes),
    ("ScanIngest OBJ negative face index resolves", ScanIngestTests.Obj_NegativeFaceIndex_ResolvesRelativeToCount),
    ("ScanIngest OBJ no faces throws", ScanIngestTests.Obj_NoFaces_Throws),
    ("ScanIngest STL ASCII tetra produces welded mesh", ScanIngestTests.Stl_AsciiTetra_ProducesWeldedMesh),
    ("ScanIngest STL binary single triangle parses", ScanIngestTests.Stl_BinarySingleTriangle_Parses),
    ("ScanIngest dispatcher detects by extension", ScanIngestTests.Dispatcher_DetectsByExtension),
    ("ScanIngest dispatcher forced format overrides extension", ScanIngestTests.Dispatcher_ForcedFormatOverridesExtension),
    ("ScanIngest dispatcher missing file throws", ScanIngestTests.Dispatcher_MissingFile_Throws),
    // Phase F3 (UX report §7.7.A) — Scan Scale Calibrate
    ("ScaleCal identity factor one", ScanPrepTests.ScaleCalibration_Identity_FactorOne),
    ("ScaleCal mm-to-m factor thousand", ScanPrepTests.ScaleCalibration_MillimetresToMetres_FactorThousand),
    ("ScaleCal km-to-m factor tenth", ScanPrepTests.ScaleCalibration_KilometresToMetres_FactorTenth),
    ("ScaleCal transform is uniform and origin-centred", ScanPrepTests.ScaleCalibration_ScaleTransform_IsUniformAndCentredAtOrigin),
    ("ScaleCal negative measured throws", ScanPrepTests.ScaleCalibration_NegativeMeasured_Throws),
    ("ScaleCal zero reference throws", ScanPrepTests.ScaleCalibration_ZeroReference_Throws),
    ("ScaleCal null curve throws", ScanPrepTests.ScaleCalibration_NullCurve_Throws),
    ("ScaleCal line curve length matches (Rhino)", ScanPrepTests.ScaleCalibration_LineCurve_LengthMatches),
    // Phase F4 (UX report §7.7.A) — Stone Prep (Scan) wrapper
    ("StonePrep null mesh throws", ScanPrepTests.StonePreparation_NullMesh_Throws),
    ("StonePrep box mesh produces descriptor (Rhino)", ScanPrepTests.StonePreparation_BoxMesh_ProducesDescriptor),
    ("StonePrep repair disabled trace says so (Rhino)", ScanPrepTests.StonePreparation_RepairDisabled_TraceSaysSo),
    ("StonePrep decimate drops triangle count (Rhino)", ScanPrepTests.StonePreparation_DecimateEnabledWithTarget_TriangleCountDrops),
    ("StonePrep batch with nulls preserves positions (Rhino)", ScanPrepTests.StonePreparation_BatchWithNulls_PositionsPreserved),
    // Phase F5 (UX report §7.7.C) — 3D Pack diagnostics
    ("PackOverlap null input throws", PackDiagnosticsTests.PerStoneOverlap_NullInput_Throws),
    ("PackOverlap single stone zero fraction (Rhino)", PackDiagnosticsTests.PerStoneOverlap_SingleStone_ZeroFraction),
    ("PackOverlap disjoint stones zero fraction (Rhino)", PackDiagnosticsTests.PerStoneOverlap_DisjointStones_ZeroFraction),
    ("PackOverlap contained stone high fraction (Rhino)", PackDiagnosticsTests.PerStoneOverlap_FullyContainedStone_HighFraction),
    ("PackComCheck null container throws", PackDiagnosticsTests.ComCheck_NullContainer_Throws),
    ("PackComCheck stone inside passes (Rhino)", PackDiagnosticsTests.ComCheck_StoneInsideContainer_PassesCheck),
    ("PackComCheck stone outside fails (Rhino)", PackDiagnosticsTests.ComCheck_StoneOutsideContainer_FailsCheck),
    ("PackStability grounded stone stable (Rhino)", PackDiagnosticsTests.PileStability_GroundedStone_IsStable),
    ("PackStability supported stone stable (Rhino)", PackDiagnosticsTests.PileStability_StoneSupportedByAnother_IsStable),
    ("PackStability floating stone falling (Rhino)", PackDiagnosticsTests.PileStability_FloatingStone_IsFalling),
    // Kim 2025 port — TreePackForest (Frahan.Core.Packing); UX report §K
    ("BlockPackTree null elements throws", TreePackForestTests.Pack_NullElements_Throws),
    ("BlockPackTree mismatched value count throws", TreePackForestTests.Pack_MismatchedValueCount_Throws),
    ("BlockPackTree empty elements throws", TreePackForestTests.Pack_EmptyElements_Throws),
    ("BlockPackTree single element fits container (Rhino)", TreePackForestTests.Pack_SingleElementFitsSingleContainer_AllPacked),
    ("BlockPackTree oversized element unpacked (Rhino)", TreePackForestTests.Pack_OversizedElement_RemainsUnpacked),
    ("BlockPackTree cheap container preferred (Rhino)", TreePackForestTests.Pack_CheapContainerPreferredWhenSufficient),
    ("BlockPackTree same seed reproduces (Rhino)", TreePackForestTests.Pack_SameSeedProducesIdenticalResult),
    ("BlockPackTree different seeds explore (Rhino)", TreePackForestTests.Pack_DifferentSeedsCanProduceDifferentResults),
    ("BlockPackTree kerf reduces packed count (Rhino)", TreePackForestTests.Pack_KerfReducesPackedCount),
    ("BlockPackTree forbidden box blocks placement (Rhino)", TreePackForestTests.Pack_ForbiddenBoxBlocksPlacement),
    ("BlockPackTree forbidden lets second container win (Rhino)", TreePackForestTests.Pack_ForbiddenBoxLetsSecondContainerWin),
    ("BlockPackTree 3-axis rotation fits elongated (Rhino)", TreePackForestTests.Pack_ThreeAxisRotation_FitsElongatedElementInFlatContainer),
    ("BlockPackTree all-packed score has bonus (Rhino)", TreePackForestTests.Pack_AllPackedScoreExceedsNotPacked),
    // Kim K2 extensions
    ("BlockPackTree parallel matches serial (Rhino)", TreePackForestTests.Pack_ParallelMatchesSerial),
    ("BlockPackTree cut surface weight lowers score (Rhino)", TreePackForestTests.Pack_CutSurfaceWeightLowersScore),
    ("BlockPackTree memory budget reduces forests (Rhino)", TreePackForestTests.Pack_MemoryBudgetReducesForestCount),
    ("BlockPackTree cut surface invariant fixed layout (Rhino)", TreePackForestTests.Pack_CutSurfaceAreaInvariantForFixedLayout),
    // Phase G — BenchBoundary
    ("BenchBoundary FromMesh null throws", BenchBoundaryTests.FromMesh_NullMesh_Throws),
    ("BenchBoundary FromBox preserves AABB (Rhino)", BenchBoundaryTests.FromBox_PreservesAabb),
    ("BenchBoundary FromMesh derives AABB (Rhino)", BenchBoundaryTests.FromMesh_DerivesAabb),
    ("BenchBoundary ContainsBoxCentre inside (Rhino)", BenchBoundaryTests.ContainsBoxCentre_InsideMeshBench_ReturnsTrue),
    ("BenchBoundary ContainsBoxCentre outside (Rhino)", BenchBoundaryTests.ContainsBoxCentre_OutsideMesh_ReturnsFalse),
    ("BenchBoundary ContainsBox all corners (Rhino)", BenchBoundaryTests.ContainsBox_AllCornersInside_PassesAtFullThreshold),
    ("BenchBoundary ContainsBox no corners (Rhino)", BenchBoundaryTests.ContainsBox_NoCornersInside_FailsAtAnyThreshold),
    ("BenchBoundary FromBoxAndMesh null fallback (Rhino)", BenchBoundaryTests.FromBoxAndMesh_NullMesh_FallsBackToFromBox),
    // Phase I.6-I15 — PointCloudIcp
    ("PointCloudIcp null source throws", PointCloudIcpTests.Register_NullSource_Throws),
    ("PointCloudIcp too few points throws (Rhino)", PointCloudIcpTests.Register_TooFewPoints_Throws),
    ("PointCloudIcp identical clouds RMS zero (Rhino)", PointCloudIcpTests.Register_IdenticalClouds_FactorOne),
    ("PointCloudIcp known translation recovers (Rhino)", PointCloudIcpTests.Register_KnownTranslation_RecoversTransform),
    ("PointCloudIcp trim drops outliers (Rhino)", PointCloudIcpTests.Register_TrimFractionDropsOutliers),
    ("PointCloudIcp voxel downsample reduces (Rhino)", PointCloudIcpTests.VoxelDownsample_ManagedFallback_ReducesCount),
    // Block build order (toposort)
    ("BuildOrder single column orders bottom to top", BlockBuildOrdererTests.Solve_SingleColumn_OrdersBottomToTop),
    ("BuildOrder two courses bottom before top", BlockBuildOrdererTests.Solve_TwoCourses_BottomBeforeTop),
    ("BuildOrder side by side both layer 0", BlockBuildOrdererTests.Solve_SideBySide_NoSupport_BothLayerZero),
    ("BuildOrder custom up axis orders along +X", BlockBuildOrdererTests.Solve_CustomUpAxis_OrdersAlongThatAxis),
    ("BuildOrder cycle throws", BlockBuildOrdererTests.Solve_Cycle_Throws),
    ("BuildOrder null assembly throws", BlockBuildOrdererTests.Solve_NullAssembly_Throws),
    ("BuildOrder degenerate up throws", BlockBuildOrdererTests.Solve_DegenerateUp_Throws),
    ("GH BlockBuildOrder metadata (Rhino)", BlockBuildOrdererTests.Gh_BlockBuildOrderComponent_Metadata),
    // Pick / Place frames
    ("masonry GH PickPlaceFrames ComponentGuid (Rhino)", Frahan.Tests.MasonryGhComponentTests.PickPlaceFramesComponent_ComponentGuid_IsExpectedValue),
    ("masonry GH PickPlaceFrames metadata (Rhino)", Frahan.Tests.MasonryGhComponentTests.PickPlaceFramesComponent_Metadata_IsCorrect),
    ("masonry GH PickPlaceFrames input/output count (Rhino)", Frahan.Tests.MasonryGhComponentTests.PickPlaceFramesComponent_HasExpectedInputAndOutputCount),
    ("masonry GH PickPlaceFrames optional inputs (Rhino)", Frahan.Tests.MasonryGhComponentTests.PickPlaceFramesComponent_OptionalInputs),
    // Build step preview
    ("masonry GH BuildStepPreview ComponentGuid (Rhino)", Frahan.Tests.MasonryGhComponentTests.BuildStepPreviewComponent_ComponentGuid_IsExpectedValue),
    ("masonry GH BuildStepPreview metadata (Rhino)", Frahan.Tests.MasonryGhComponentTests.BuildStepPreviewComponent_Metadata_IsCorrect),
    ("masonry GH BuildStepPreview input/output count (Rhino)", Frahan.Tests.MasonryGhComponentTests.BuildStepPreviewComponent_HasExpectedInputAndOutputCount),
    // Build sequence JSON
    ("masonry GH BuildSequenceJson ComponentGuid (Rhino)", Frahan.Tests.MasonryGhComponentTests.BuildSequenceJsonComponent_ComponentGuid_IsExpectedValue),
    ("masonry GH BuildSequenceJson metadata (Rhino)", Frahan.Tests.MasonryGhComponentTests.BuildSequenceJsonComponent_Metadata_IsCorrect),
    ("masonry GH BuildSequenceJson input/output count (Rhino)", Frahan.Tests.MasonryGhComponentTests.BuildSequenceJsonComponent_HasExpectedInputAndOutputCount),
    // PLY parser (pure managed)
    ("PLY ascii unit cube parses verts and tris", PlyMeshReaderTests.Read_AsciiUnitCube_ParsesVertsAndTris),
    ("PLY ascii quad face fan-triangulates", PlyMeshReaderTests.Read_AsciiQuadFace_FanTriangulates),
    ("PLY ascii with vertex colors preserves rgb", PlyMeshReaderTests.Read_AsciiWithVertexColors_PreservesRgb),
    ("PLY binary LE unit cube parses verts and tris", PlyMeshReaderTests.Read_BinaryLEUnitCube_ParsesVertsAndTris),
    ("PLY missing magic throws", PlyMeshReaderTests.Read_MissingPlyMagic_Throws),
    ("PLY binary big endian parses", PlyMeshReaderTests.Read_BinaryBigEndian_ParsesVertsAndTris),
    ("PLY no vertex element throws", PlyMeshReaderTests.Read_NoVertexElement_Throws),
    ("PLY missing xyz throws", PlyMeshReaderTests.Read_MissingXyzProperties_Throws),
    // BlockCutOpt Phase 1 (pure managed)
    ("BlockCutOpt empty fractures: grid is full coverage", BlockCutOptSolverTests.Solve_EmptyFractures_GridIsFullCoverage),
    ("BlockCutOpt vertical plane across centre kills one row", BlockCutOptSolverTests.Solve_VerticalPlaneAcrossCenter_KillsOneRow),
    ("BlockCutOpt LimestoneStratumA defaults parse", BlockCutOptSolverTests.Solve_LimestoneStratumA_DefaultsParseCleanly),
    ("BlockCutOpt TraceVerticalExtruder two traces", BlockCutOptSolverTests.TraceVerticalExtruder_TwoTraces_EmitsEightVertsTwelveTriIndices),
    ("BlockCutOpt CuttingGrid psi=0 axis-aligned", BlockCutOptSolverTests.CuttingGrid_PsiZero_AxisAligned),
    ("BlockCutOpt CuttingGrid psi=90 deg rotation", BlockCutOptSolverTests.CuttingGrid_PsiQuarterTurn_NinetyDegreeRotation),
    ("BlockCutOpt Phase1D synthetic-DFN pipeline reduces count", BlockCutOptSolverTests.Phase1D_SyntheticDfn_PipelineRunsAndReducesCount),
    ("BlockCutOpt Phase1D seed determinism", BlockCutOptSolverTests.Phase1D_SyntheticDfn_DeterministicSeedGivesIdenticalResult),
    ("BlockCutOpt JointSetDfnPlyEmitter vertical plane through bench", BlockCutOptSolverTests.JointSetDfnPlyEmitter_VerticalPlaneThroughBench_EmitsRectangle),
    ("BlockCutOpt Phase2 BVH single triangle finds hit", BlockCutOptSolverTests.Bvh_BuildOnSingleTriangle_FindsHit),
    ("BlockCutOpt Phase2 BVH far triangle no hit", BlockCutOptSolverTests.Bvh_BuildOnFarTriangle_NoHit),
    ("BlockCutOpt Phase2 BVH deterministic same result", BlockCutOptSolverTests.Bvh_DeterministicSeed_SolverResultUnchanged),
    ("BlockCutOpt Phase2 BVH vs brute-force vertical plane", BlockCutOptSolverTests.Bvh_AgainstBruteForce_IdenticalCount_OneVerticalPlane),
    ("BlockCutOpt Phase3 sub-division 2x3 covers area", BlockCutOptSolverTests.Subdivision_2x3_Produces6Zones_CoverTestedArea),
    ("BlockCutOpt Phase3 SolveSubdivided 2x2 valid results", BlockCutOptSolverTests.SolveSubdivided_2x2_AllZonesReturnValidResults),
    ("BlockCutOpt Phase4 coarse-to-fine runs cleanly", BlockCutOptSolverTests.CoarseToFine_RunsCleanlyAndProducesValidResult),
    ("BlockCutOpt Phase4 coarse-to-fine fewer evals than uniform", BlockCutOptSolverTests.CoarseToFine_FewerEvaluationsThanUniformSweep),
    ("BlockCutOpt Phase6 Pareto front insert and prune", BlockCutOptSolverTests.ParetoFront_InsertAndPruneDominated),
    ("BlockCutOpt Phase6 Pareto solver full pipeline", BlockCutOptSolverTests.ParetoSolver_FullPipeline_ReturnsNonEmptyFront),
    ("BlockCutOpt Phase6 Pareto recovery matches scalar solver", BlockCutOptSolverTests.ParetoSolver_RecoveryMatchesScalarSolver),
    ("BlockCutOpt Phase8 Fisher-robust aggregate statistics", BlockCutOptSolverTests.FisherRobust_Solver_ProducesValidAggregateStatistics),
    ("BlockCutOpt Phase8 Fisher-robust deterministic seed", BlockCutOptSolverTests.FisherRobust_DeterministicForSameBaseSeed),
    ("BlockCutOpt Tolerances default kerf 50 mm", BlockCutOptSolverTests.Tolerances_KerfDefaultIs50mm),
    ("BlockCutOpt Tolerances Rhino unit converts to mm model", BlockCutOptSolverTests.Tolerances_RhinoUnitConvertsMmToMm),
    ("BlockCutOpt I12 SharedEdgeSlicer unit cube 5 z-slices", BlockCutOptSolverTests.SharedEdgeSlicer_UnitCube_FiveZSlices_ProducesContours),
    ("BlockCutOpt Phase9 AmrrPlanner cube->sphere removes volume", BlockCutOptSolverTests.AmrrPlanner_CubeToInscribedSphere_RemovesPositiveVolume),
    ("BlockCutOpt Phase9 AmrrPlanner defaults mm-converted", BlockCutOptSolverTests.AmrrPlanner_DefaultsUseMmConvertedSawblade),
    ("BlockCutOpt Phase9 ConvexPolyhedron half-space clip", BlockCutOptSolverTests.ConvexPolyhedron_ClipByHalfSpace_ReducesVolume),
    ("BlockCutOpt Phase7 density-watershed two clusters", BlockCutOptSolverTests.DensityWatershed_TwoClusters_ProducesAtLeastOneZone),
    ("BlockCutOpt I7 DLBF single size packs many copies", BlockCutOptSolverTests.Dlbf_SingleSize_PacksMultipleCopies),
    ("BlockCutOpt I7 DLBF mixed sizes prefer higher revenue per area", BlockCutOptSolverTests.Dlbf_MixedSizes_PrefersHigherRevenuePerArea),
    ("BlockCutOpt Phase11.5 ImageToWorldMap flip Y", BlockCutOptSolverTests.ImageToWorldMap_FlipYWorks),
    ("BlockCutOpt Phase11.5 CsvFractureTraceSource round-trip", BlockCutOptSolverTests.CsvFractureTraceSource_RoundTripParseCsv),
    ("BlockCutOpt ConvexPolyhedron.ToPlyMesh fan-triangulates", BlockCutOptSolverTests.ConvexPolyhedron_ToPlyMesh_FanTriangulatesFaces),
    ("BlockCutOpt AmrrPlanner with slicer still reduces volume", BlockCutOptSolverTests.AmrrPlanner_WithSlicer_StillReducesVolume),
    ("BlockCutOpt VtuWriter round-trips hex cells", BlockCutOptSolverTests.VtuWriter_RoundTripsHexahedrons),
    ("BlockCutOpt VtuWriter from grid + BVH splits correctly", BlockCutOptSolverTests.VtuWriter_FromGridAndBvh_SplitsCorrectly),
    ("BlockCutOpt OmniSolver uniform 2x2 end-to-end", BlockCutOptSolverTests.OmniSolver_Uniform_2x2_EndToEnd),
    ("BlockCutOpt OmniSolver density-watershed end-to-end", BlockCutOptSolverTests.OmniSolver_DensityWatershed_EndToEnd),
    ("BlockCutOpt VtuWriter AMRR sequence round-trips", BlockCutOptSolverTests.VtuWriter_AmrrSequence_RoundTrips),
    ("BlockCutOpt PythonDetector parse traces from CSV", BlockCutOptSolverTests.PythonSubprocessFractureDetector_ParseTracesFromCsv),
    ("BlockCutOpt Demo synthetic DFN end-to-end writes VTU", BlockCutOptSolverTests.Demo_RunSyntheticDfn_EndToEnd_WritesVtu),
    ("BlockCutOpt Demo couple to AMRR at best block", BlockCutOptSolverTests.Demo_CoupleToAmrrAtBestBlock_WritesPlanSequence),
    ("BlockCutOpt Phase1D regression synthetic reaches limestone order", BlockCutOptSolverTests.Phase1D_Regression_SyntheticReachesLimestoneOrder),
    ("BlockCutOpt I1 OBB Phase 1 ctor defaults vertical W", BlockCutOptSolverTests.I1_OrientedBlock_Phase1Constructor_DefaultsToVerticalW),
    ("BlockCutOpt I1 OBB full ctor accepts tilted axes", BlockCutOptSolverTests.I1_OrientedBlock_FullCtor_AcceptsTiltedAxes),
    ("BlockCutOpt I1 CuttingGrid theta=30 deg tilts UZ/VZ", BlockCutOptSolverTests.I1_CuttingGrid_GenerateTilted_NonZeroTheta_ChangesUZVZ),
    ("BlockCutOpt I1 solver theta=0 matches Phase 1", BlockCutOptSolverTests.I1_Solver_ThetaSweep_PreservesPsiOnlyResultWhenThetaZero),
    ("BlockCutOpt I1 solver tilted search runs cleanly", BlockCutOptSolverTests.I1_Solver_TiltedSearch_RunsCleanly),
    ("BlockCutOpt I4 edge-triangle agrees with SAT vertical plane", BlockCutOptSolverTests.I4_EdgeTriangleObb_AgreesWithSat_VerticalPlane),
    ("BlockCutOpt I4 edge-triangle agrees with SAT far triangle", BlockCutOptSolverTests.I4_EdgeTriangleObb_AgreesWithSat_FarTriangle),
    ("BlockCutOpt FractureInputReader CSV vertical extrude", BlockCutOptSolverTests.FractureInputReader_LoadCsv_VerticalExtrude),
    ("BlockCutOpt FractureInputReader lines space-separated", BlockCutOptSolverTests.FractureInputReader_LoadLines_SpaceSeparated),
    ("BlockCutOpt FractureInputReader PLY raw mesh", BlockCutOptSolverTests.FractureInputReader_LoadPly_ReadsRawMesh),
    ("BlockCutOpt SyntheticTnGraniteGenerator writes both formats", BlockCutOptSolverTests.SyntheticTnGraniteGenerator_WritesBothFormats),
    ("BlockCutOpt SyntheticTnGraniteGenerator deterministic seed", BlockCutOptSolverTests.SyntheticTnGraniteGenerator_DeterministicForSameSeed),
    ("BlockCutOpt SyntheticTnGraniteGenerator regenerate canonical samples", BlockCutOptSolverTests.SyntheticTnGraniteGenerator_RegenerateCanonicalSamples),
    ("BlockCutOpt I14 ToInequalities unit cube has 6 faces", BlockCutOptSolverTests.I14_ConvexPolyhedron_ToInequalities_UnitCube),
    ("BlockCutOpt I14 FromInequalities round-trips unit cube", BlockCutOptSolverTests.I14_ConvexPolyhedron_FromInequalities_RoundTripUnitCube),
    ("BlockCutOpt I14 ContainsPoint inside/outside", BlockCutOptSolverTests.I14_ContainsPoint_CenterInsideCornerOutside),
    ("BlockCutOpt I14 SignedGap negative inside positive outside", BlockCutOptSolverTests.I14_SignedGap_NegativeInsidePositiveOutside),
    ("BlockCutOpt I14 ClipBothSides unit cube two halves", BlockCutOptSolverTests.I14_ClipBothSides_UnitCubeAtX05_TwoHalves),
    ("BlockCutOpt I14 CompositeBlock two cubes total volume", BlockCutOptSolverTests.I14_CompositeBlock_TwoCubes_TotalVolumeAndAabb),
    // Multi-scale crack-aware RecoveryCascade (reject-coarse / recover-fine)
    ("Cascade reduces to BlockCutOpt at a single scale", RecoveryCascadeTests.ReducesToBaseline_SingleScale),
    ("Cascade finer scale recovers cracked-block value", RecoveryCascadeTests.CrackRouting_RecoversExtraValue),
    ("Cascade recovery is monotone in depth", RecoveryCascadeTests.MonotoneRecovery_InDepth),
    ("Cascade marketable-volume threshold gates recursion", RecoveryCascadeTests.StoppingRule_ThresholdGatesRecursion),
    ("Cascade is deterministic across runs", RecoveryCascadeTests.Deterministic_TwoRunsIdentical),
    ("Cascade zero-fracture: full recovery, no recursion", RecoveryCascadeTests.ZeroFracture_FullRecoveryNoRecursion),
    // PLY GH wrapper + library-match (Rhino-tagged)
    ("masonry GH ReadPlyMesh ComponentGuid (Rhino)", Frahan.Tests.MasonryGhComponentTests.ReadPlyMeshComponent_ComponentGuid_IsExpectedValue),
    ("masonry GH ReadPlyMesh metadata (Rhino)", Frahan.Tests.MasonryGhComponentTests.ReadPlyMeshComponent_Metadata_IsCorrect),
    ("masonry GH ReadPlyMesh input/output count (Rhino)", Frahan.Tests.MasonryGhComponentTests.ReadPlyMeshComponent_HasExpectedInputAndOutputCount),
    ("masonry GH MatchBlockTransform ComponentGuid (Rhino)", Frahan.Tests.MasonryGhComponentTests.MatchBlockTransformComponent_ComponentGuid_IsExpectedValue),
    ("masonry GH MatchBlockTransform metadata (Rhino)", Frahan.Tests.MasonryGhComponentTests.MatchBlockTransformComponent_Metadata_IsCorrect),
    ("masonry GH MatchBlockTransform input/output count (Rhino)", Frahan.Tests.MasonryGhComponentTests.MatchBlockTransformComponent_HasExpectedInputAndOutputCount),
    // Mesh quality + sanitizer (Phase 1 robustness)
    ("MeshSanitizer unit cube is clean solid", MeshSanitizerTests.Analyse_UnitCube_IsCleanSolid),
    ("MeshSanitizer open cube not closed", MeshSanitizerTests.Analyse_OpenCube_NotClosed),
    ("MeshSanitizer duplicate vertices counted", MeshSanitizerTests.Analyse_DuplicateVertices_Counted),
    ("MeshSanitizer degenerate triangle counted", MeshSanitizerTests.Analyse_DegenerateTriangle_Counted),
    ("MeshSanitizer flipped triangle inconsistent", MeshSanitizerTests.Analyse_FlippedTriangle_NormalInconsistent),
    ("MeshSanitizer dedup vertices remaps tris", MeshSanitizerTests.Sanitize_DedupVertices_MergesAndRemapsTriangles),
    ("MeshSanitizer drop degenerate removes zero area", MeshSanitizerTests.Sanitize_DropDegenerate_RemovesZeroAreaTris),
    ("MeshSanitizer unify normals flips to consistent", MeshSanitizerTests.Sanitize_UnifyNormals_FlipsToConsistent),
    ("GH MeshQualityReport metadata (Rhino)", MeshSanitizerTests.Gh_MeshQualityReportComponent_Metadata),
    // Assembly robustness (Phase 2)
    ("Adaptive tol fails statically but adaptive succeeds on large blocks", AssemblyRobustnessTests.Adaptive_TightStaticTolFailsButAdaptiveSucceeds_LargeBlocks),
    ("Adaptive tol backward compat with default 0 factor", AssemblyRobustnessTests.Adaptive_BackwardCompat_Default0FactorMatchesOldBehavior),
    ("Partial assembler single column grows step by step", AssemblyRobustnessTests.Partial_SingleColumn_GrowsOneBlockAtATime),
    ("Partial assembler keeps fixed blocks at step 0", AssemblyRobustnessTests.Partial_FixedBlocksAlwaysIncluded_EvenAtStepZero),
    ("Partial assembler unknown id throws", AssemblyRobustnessTests.Partial_OrderedIdNotInAssembly_Throws),
    ("Partial assembler duplicate id throws", AssemblyRobustnessTests.Partial_DuplicateIdInOrder_Throws),
    ("GH BuildOrderStabilityStream metadata (Rhino)", AssemblyRobustnessTests.Gh_BuildOrderStabilityStreamComponent_Metadata),
    // Packing robustness (Phase 3)
    ("Polygon signed area unit square is one", PackingRobustnessTests.Polygon_SignedArea_UnitSquare_IsOne),
    ("Polygon sanitize drops adjacent duplicates", PackingRobustnessTests.Polygon_Sanitize_DropsAdjacentDuplicates),
    ("Polygon sanitize drops collinear midpoint", PackingRobustnessTests.Polygon_Sanitize_DropsCollinearMidpoint),
    ("Clip full inside returns subject", PackingRobustnessTests.Clip_FullInside_ReturnsSubject),
    ("Clip partial overlap returns intersection", PackingRobustnessTests.Clip_PartialOverlap_ReturnsIntersection),
    ("Clip disjoint returns empty", PackingRobustnessTests.Clip_Disjoint_ReturnsEmpty),
    ("Extract simple quad finds outer loop", PackingRobustnessTests.Extract_SimpleQuad_FindsOuterLoop),
    ("Extract annulus mesh finds outer and one hole", PackingRobustnessTests.Extract_AnnulusMesh_FindsOuterAndOneHole),
    ("GH PolygonSanitize metadata (Rhino)", PackingRobustnessTests.Gh_PolygonSanitizeComponent_Metadata),
    ("GH MeshPlanarPolygonExtractor metadata (Rhino)", PackingRobustnessTests.Gh_MeshPlanarPolygonExtractorComponent_Metadata),
    // Cut validation (Phase 4)
    ("CutValidator perfect split conserves", CutValidationTests.Validate_PerfectSplit_ConservesVolume),
    ("CutValidator leaky cut not conserved", CutValidationTests.Validate_LeakyCut_FlagsAsNonConserved),
    ("CutValidator sliver detected", CutValidationTests.Validate_SliverDetected_BelowFraction),
    ("CutValidator enumerate slivers", CutValidationTests.EnumerateSlivers_ReturnsCorrectIndices),
    ("CutValidator drop slivers removes", CutValidationTests.DropSlivers_RemovesFlaggedPieces),
    ("CutValidator null inputs throw", CutValidationTests.Validate_NullInputs_Throw),
    ("GH CutValidation metadata (Rhino)", CutValidationTests.Gh_CutValidationComponent_Metadata),
    // Block cutting robustness (Phase 5)
    ("BlockSize uniform pieces low CV", BlockCuttingRobustnessTests.BlockSize_UniformPieces_LowCV),
    ("BlockSize huge outlier flagged", BlockCuttingRobustnessTests.BlockSize_OneHugeOutlier_FlaggedByTukey),
    ("BlockSize histogram covers data", BlockCuttingRobustnessTests.BlockSize_Histogram_BinsCoverData),
    ("BlockSize empty input safe", BlockCuttingRobustnessTests.BlockSize_EmptyInput_DegenerateButSafe),
    ("Merger small shard → large neighbour", BlockCuttingRobustnessTests.Merger_SmallShardMergesIntoLargeNeighbour),
    ("Merger isolated sliver keeps itself", BlockCuttingRobustnessTests.Merger_IsolatedSliver_KeepsItself),
    ("Merger chain accretes to largest", BlockCuttingRobustnessTests.Merger_ChainOfSlivers_AllAccreteToLargest),
    ("Merger null adjacency throws", BlockCuttingRobustnessTests.Merger_NullAdjacency_Throws),
    ("GH BlockSizeDistribution metadata (Rhino)", BlockCuttingRobustnessTests.Gh_BlockSizeDistributionComponent_Metadata),
    ("GH FragmentMerger metadata (Rhino)", BlockCuttingRobustnessTests.Gh_FragmentMergerComponent_Metadata),
    // Greiner-Hormann polygon clipper (Phase A — non-convex 2D booleans)
    ("GH-clip intersection of overlapping squares", GreinerHormannClipperTests.Intersection_TwoSquaresOverlap_ReturnsOverlap),
    ("GH-clip intersection disjoint empty", GreinerHormannClipperTests.Intersection_DisjointSquares_ReturnsEmpty),
    ("GH-clip intersection contained returns subject", GreinerHormannClipperTests.Intersection_SubjectFullyInside_ReturnsSubject),
    ("GH-clip union of overlapping squares", GreinerHormannClipperTests.Union_OverlappingSquares_ReturnsLShape),
    ("GH-clip union disjoint returns both", GreinerHormannClipperTests.Union_DisjointSquares_ReturnsBoth),
    ("GH-clip difference of overlapping squares", GreinerHormannClipperTests.Difference_OverlappingSquares_ReturnsSubjectMinusOverlap),
    ("GH-clip difference subject inside clip empty", GreinerHormannClipperTests.Difference_SubjectInsideClip_ReturnsEmpty),
    ("GH-clip non-convex L-shape vs square", GreinerHormannClipperTests.Intersection_LShapeAndSquare_HandlesNonConvex),
    ("GH-clip null subject throws", GreinerHormannClipperTests.Compute_NullSubject_Throws),
    ("GH-clip too few verts returns empty", GreinerHormannClipperTests.Compute_TooFewVertices_ReturnsEmpty),
    // 3D mesh CSG via BSP (Phase B)
    ("CSG union disjoint cubes preserves volumes", MeshCsgTests.Union_DisjointCubes_PreservesBothVolumes),
    ("CSG union overlapping inclusion-exclusion", MeshCsgTests.Union_OverlappingCubes_VolumeMatchesInclusionExclusion),
    ("CSG intersection overlapping returns overlap", MeshCsgTests.Intersection_OverlappingCubes_ReturnsOverlap),
    ("CSG intersection disjoint returns empty", MeshCsgTests.Intersection_DisjointCubes_ReturnsEmpty),
    ("CSG difference overlapping returns A minus overlap", MeshCsgTests.Difference_OverlappingCubes_ReturnsAMinusB),
    ("CSG difference B fully inside A returns A with cavity", MeshCsgTests.Difference_BFullyInsideA_ReturnsAWithCavity),
    ("CSG difference disjoint returns A unchanged", MeshCsgTests.Difference_OnlySubject_ReturnsAUnchanged),
    ("CSG null args throw", MeshCsgTests.Csg_NullArgs_Throw),
    // Coplanar-coincidence resolver (Phase C)
    ("Coplanar perfectly coincident squares find contact", CoplanarResolverTests.Coplanar_PerfectlyCoincidentSquares_FindsContact),
    ("Coplanar partial overlap finds intersection contact", CoplanarResolverTests.Coplanar_PartialOverlap_FindsContactAtIntersection),
    ("Coplanar side-by-side cubes still resolved", CoplanarResolverTests.Coplanar_NonCoincidentFaces_ResolverStillSafe),
    ("Coplanar disjoint returns zero", CoplanarResolverTests.Coplanar_NoContact_ReturnsZero),
    ("Coplanar default false unchanged behaviour", CoplanarResolverTests.Coplanar_BackwardCompat_DefaultFalseUnchanged),
    // Clipper2 production back-end (NuGet)
    ("Clipper2 intersect two squares returns overlap", Clipper2AdapterTests.Intersect_TwoSquares_ReturnsOverlap),
    ("Clipper2 union overlapping returns area 7", Clipper2AdapterTests.Union_TwoOverlappingSquares_ReturnsSevenAreaShape),
    ("Clipper2 difference overlapping returns area 3", Clipper2AdapterTests.Difference_OverlappingSquares_ReturnsThreeArea),
    ("Clipper2 xor overlapping returns area 6", Clipper2AdapterTests.Xor_OverlappingSquares_ReturnsSixAreaTwoLoops),
    ("Clipper2 fully coincident edges robust", Clipper2AdapterTests.Intersect_FullyCoincidentEdges_HandledRobustly),
    ("Clipper2 vertex on edge robust", Clipper2AdapterTests.Intersect_VertexOnEdgeCase_HandledRobustly),
    ("Clipper2 polygon with hole difference correct", Clipper2AdapterTests.Boolean_PolygonWithHole_DifferenceIsCorrect),
    ("Clipper2 null subject throws", Clipper2AdapterTests.NullSubject_Throws),
    // CGAL P/Invoke front-end (fallback path when shim absent)
    ("CGAL IsAvailable does not throw when DLL absent", CgalMeshBooleanTests.IsAvailable_DoesNotThrow_WhenDllAbsent),
    ("CGAL Version non-null regardless of availability", CgalMeshBooleanTests.Version_NonNull_RegardlessOfAvailability),
    ("CGAL union fallback matches BSP when DLL absent", CgalMeshBooleanTests.Union_FallbackMatchesBspWhenDllAbsent),
    ("CGAL intersection fallback correct volume", CgalMeshBooleanTests.Intersection_FallbackProducesCorrectVolume),
    ("CGAL difference fallback correct volume", CgalMeshBooleanTests.Difference_FallbackProducesCorrectVolume),
    ("CGAL null args throw", CgalMeshBooleanTests.NullArgs_Throw),
    ("masonry GH MasonryAssemblyComponent ComponentGuid is expected (Rhino)", Frahan.Tests.MasonryGhComponentTests.MasonryAssemblyComponent_ComponentGuid_IsExpectedValue),
    ("masonry GH MasonryAssemblyComponent metadata is correct (Rhino)", Frahan.Tests.MasonryGhComponentTests.MasonryAssemblyComponent_Metadata_IsCorrect),
    ("masonry GH MasonryAssemblyComponent has expected input/output count (Rhino)", Frahan.Tests.MasonryGhComponentTests.MasonryAssemblyComponent_HasExpectedInputAndOutputCount),
    ("masonry GH MasonryStabilityRbeComponent ComponentGuid is expected (Rhino)", Frahan.Tests.MasonryGhComponentTests.MasonryStabilityRbeComponent_ComponentGuid_IsExpectedValue),
    ("masonry GH MasonryStabilityRbeComponent metadata is correct (Rhino)", Frahan.Tests.MasonryGhComponentTests.MasonryStabilityRbeComponent_Metadata_IsCorrect),
    ("masonry GH MasonryStabilityRbeComponent has expected input/output count (Rhino)", Frahan.Tests.MasonryGhComponentTests.MasonryStabilityRbeComponent_HasExpectedInputAndOutputCount),
    ("masonry solver registry default is null by default", Frahan.Tests.MasonryGhComponentTests.MasonrySolverRegistry_DefaultIsNullByDefault),
    ("masonry solver registry EnsureDefaultSolver assigns managed when empty", Frahan.Tests.MasonryGhComponentTests.MasonrySolverRegistry_EnsureDefaultSolver_AssignsManagedSolverWhenEmpty),
    ("masonry solver registry EnsureDefaultSolver preserves existing", Frahan.Tests.MasonryGhComponentTests.MasonrySolverRegistry_EnsureDefaultSolver_PreservesExistingSolver),
    ("masonry solver registry EnsureDefaultSolver is idempotent", Frahan.Tests.MasonryGhComponentTests.MasonrySolverRegistry_EnsureDefaultSolver_IsIdempotent),
    // Slab cutting (Phase E.1): convex polyhedron splitting by oriented planes
    ("slab cutter plane misses slab returns single passthrough", SlabCutterTests.Cut_PlaneMissesSlab_ReturnsSingleSlabPassthrough),
    ("slab cutter axis-aligned bisecting plane produces two equal halves", SlabCutterTests.Cut_AxisAlignedBisectingPlane_ProducesTwoEqualHalves),
    ("slab cutter diagonal bisecting plane produces two equal volumes", SlabCutterTests.Cut_DiagonalBisectingPlane_ProducesTwoEqualVolumes),
    ("slab cutter plane on face does not produce degenerate sliver", SlabCutterTests.Cut_PlaneOnFace_DoesNotProduceDegenerateSliver),
    ("slab cutter three orthogonal bisectors produces eight octants", SlabCutterTests.Cut_ThreeOrthogonalBisectors_ProducesEightOctants),
    ("slab cutter null slab throws", SlabCutterTests.Cut_NullSlab_Throws),
    ("slab cutter null plane throws", SlabCutterTests.Cut_NullPlane_Throws),
    ("slab cutter null plane list throws", SlabCutterTests.Cut_NullPlaneList_Throws),
    ("slab cutter negative eps throws", SlabCutterTests.Cut_NegativeEps_Throws),
    ("slab cutter multi-slab single-plane parent indices recorded", SlabCutterTests.Cut_MultiSlabSinglePlane_ParentIndicesRecorded),
    ("slab cutter output slab vertex pool integrity", SlabCutterTests.Cut_OutputSlab_VertexPoolIntegrity),
    ("slab cutter output slab ToMasonryBlock round-trips", SlabCutterTests.Cut_OutputSlab_ToMasonryBlock_RoundTrips),
    // Slab cutting GH components
    ("slab cut GH SlabFromMeshComponent ComponentGuid is expected (Rhino)", SlabCutGhComponentTests.SlabFromMeshComponent_ComponentGuid_IsExpectedValue),
    ("slab cut GH SlabFromMeshComponent metadata is correct (Rhino)", SlabCutGhComponentTests.SlabFromMeshComponent_Metadata_IsCorrect),
    ("slab cut GH SlabFromMeshComponent has expected input/output count (Rhino)", SlabCutGhComponentTests.SlabFromMeshComponent_HasExpectedInputAndOutputCount),
    ("slab cut GH SlabCutByFracturesComponent ComponentGuid is expected (Rhino)", SlabCutGhComponentTests.SlabCutByFracturesComponent_ComponentGuid_IsExpectedValue),
    ("slab cut GH SlabCutByFracturesComponent metadata is correct (Rhino)", SlabCutGhComponentTests.SlabCutByFracturesComponent_Metadata_IsCorrect),
    ("slab cut GH SlabCutByFracturesComponent has expected input/output count (Rhino)", SlabCutGhComponentTests.SlabCutByFracturesComponent_HasExpectedInputAndOutputCount),
    // Phase E.2 — finite fracture polygons (FracturePolygon validation, SlabCrossSection, FractureCutter)
    ("fracture polygon null boundary throws", FractureCutterTests.FracturePolygon_NullBoundary_Throws),
    ("fracture polygon too few vertices throws", FractureCutterTests.FracturePolygon_TooFewVertices_Throws),
    ("fracture polygon length not multiple of 3 throws", FractureCutterTests.FracturePolygon_LengthNotMultipleOf3_Throws),
    ("fracture polygon non-coplanar throws", FractureCutterTests.FracturePolygon_NonCoplanar_Throws),
    ("fracture polygon non-convex throws", FractureCutterTests.FracturePolygon_NonConvex_Throws),
    ("fracture polygon valid rectangle stores plane and verts", FractureCutterTests.FracturePolygon_ValidRectangle_StoresPlaneAndVerts),
    ("slab cross-section plane misses returns empty", FractureCutterTests.SlabCrossSection_PlaneMisses_ReturnsEmpty),
    ("slab cross-section axis-aligned bisector returns unit square", FractureCutterTests.SlabCrossSection_AxisAlignedBisector_ReturnsUnitSquare),
    ("slab cross-section diagonal bisector returns valid polygon", FractureCutterTests.SlabCrossSection_DiagonalBisector_ReturnsValidPolygon),
    ("slab cross-section negative eps throws", FractureCutterTests.SlabCrossSection_NegativeEps_Throws),
    ("fracture cut rectangle larger than slab outcome is Spans and produces two halves", FractureCutterTests.FractureCut_RectangleLargerThanSlab_OutcomeIsSpansAndProducesTwoHalves),
    ("fracture cut rectangle exactly matching cross-section outcome is Spans", FractureCutterTests.FractureCut_RectangleExactlyMatchingCrossSection_OutcomeIsSpans),
    ("fracture cut diagonal large polygon outcome is Spans", FractureCutterTests.FractureCut_DiagonalLargePolygon_OutcomeIsSpans),
    ("fracture cut Spans result volume conserved", FractureCutterTests.FractureCut_SpansResult_VolumeConserved),
    ("fracture cut plane above slab outcome is Miss and passthrough", FractureCutterTests.FractureCut_PlaneAboveSlab_OutcomeIsMissAndPassthrough),
    ("fracture cut Miss result preserves slab", FractureCutterTests.FractureCut_MissResult_PreservesSlab),
    ("fracture cut plane below slab outcome is Miss", FractureCutterTests.FractureCut_PlaneBelowSlab_OutcomeIsMiss),
    ("fracture cut small rectangle outcome is Partial and passthrough", FractureCutterTests.FractureCut_SmallRectangle_OutcomeIsPartialAndPassthrough),
    ("fracture cut Partial result preserves slab volume", FractureCutterTests.FractureCut_PartialResult_PreservesSlabVolume),
    ("fracture cut offset rectangle outcome is Partial", FractureCutterTests.FractureCut_OffsetRectangle_OutcomeIsPartial),
    ("fracture cut PartialExtended outcome and produces two halves", FractureCutterTests.FractureCut_PartialExtended_OutcomeIsPartialExtendedAndProducesTwoHalves),
    ("fracture cut null slab throws", FractureCutterTests.FractureCut_NullSlab_Throws),
    ("fracture cut null polygon throws", FractureCutterTests.FractureCut_NullPolygon_Throws),
    ("fracture cut null options uses default", FractureCutterTests.FractureCut_NullOptions_UsesDefault),
    ("fracture cut many two orthogonal rectangles produces four pieces", FractureCutterTests.FractureCutMany_TwoOrthogonalRectangles_ProducesFourPieces),
    ("fracture cut many null slabs list throws", FractureCutterTests.FractureCutMany_NullSlabsList_Throws),
    // Phase E.2 GH components
    ("fracture cut GH FracturePolygonFromCurveComponent ComponentGuid is expected (Rhino)", FractureCutGhComponentTests.FracturePolygonFromCurveComponent_ComponentGuid_IsExpectedValue),
    ("fracture cut GH FracturePolygonFromCurveComponent metadata is correct (Rhino)", FractureCutGhComponentTests.FracturePolygonFromCurveComponent_Metadata_IsCorrect),
    ("fracture cut GH FracturePolygonFromCurveComponent has expected input/output count (Rhino)", FractureCutGhComponentTests.FracturePolygonFromCurveComponent_HasExpectedInputAndOutputCount),
    ("fracture cut GH SlabCutByFracturePolygonsComponent ComponentGuid is expected (Rhino)", FractureCutGhComponentTests.SlabCutByFracturePolygonsComponent_ComponentGuid_IsExpectedValue),
    ("fracture cut GH SlabCutByFracturePolygonsComponent metadata is correct (Rhino)", FractureCutGhComponentTests.SlabCutByFracturePolygonsComponent_Metadata_IsCorrect),
    ("fracture cut GH SlabCutByFracturePolygonsComponent has expected input/output count (Rhino)", FractureCutGhComponentTests.SlabCutByFracturePolygonsComponent_HasExpectedInputAndOutputCount),
    // BoundaryRailIndex unit tests (pure managed; B1 fix verification)
    ("rail index EdgeKey equal 4 buckets", BoundaryRailIndexTests.EdgeKey_Equality_Same4Buckets_AreEqual),
    ("rail index EdgeKey unequal different buckets", BoundaryRailIndexTests.EdgeKey_Equality_DifferentBuckets_AreUnequal),
    ("rail index EdgeKey hash stable across instances", BoundaryRailIndexTests.EdgeKey_HashIsStableAcrossInstances),
    ("rail index Add stores interval Query returns it", BoundaryRailIndexTests.Add_StoresInterval_QueryReturnsIt),
    ("rail index Query missing key returns empty", BoundaryRailIndexTests.Query_MissingKey_ReturnsEmpty),
    ("rail index Add null interval throws", BoundaryRailIndexTests.Add_NullInterval_Throws),
    ("rail index Add two intervals same key both returned", BoundaryRailIndexTests.Add_TwoIntervalsSameKey_AreBothReturned),
    ("rail index KnownZones tracks distinct zone buckets", BoundaryRailIndexTests.KnownZones_TracksDistinctZoneBuckets),
    ("rail index QueryNeighbors length radius finds adjacent", BoundaryRailIndexTests.QueryNeighbors_LengthRadius_FindsAdjacentBuckets),
    ("rail index QueryNeighbors angle radius finds adjacent", BoundaryRailIndexTests.QueryNeighbors_AngleRadius_FindsAdjacentBuckets),
    ("rail index QueryNeighbors preserveZone=true narrows", BoundaryRailIndexTests.QueryNeighbors_PreserveZoneTrue_NarrowsToSingleZone),
    ("rail index QueryNeighbors preserveZone=false widens (B1 fix)", BoundaryRailIndexTests.QueryNeighbors_PreserveZoneFalse_WidensAcrossAllZones_FixesB1),
    ("rail index QueryNeighbors curvature bucket must match", BoundaryRailIndexTests.QueryNeighbors_CurvatureBucketMustMatch),
    ("rail index QueryNeighbors negative radius throws", BoundaryRailIndexTests.QueryNeighbors_NegativeRadius_Throws),
    ("rail index QueryNeighbors radius 0 returns exact only", BoundaryRailIndexTests.QueryNeighbors_RadiusZero_ReturnsExactMatchOnly),
    // Surface packing unit tests (no Rhino runtime required)
    ("surface UV table stores and retrieves", SurfacePackingTests.FaceCornerUvTable_StoresAndRetrieves),
    ("surface UV table overwrites duplicate key", SurfacePackingTests.FaceCornerUvTable_OverwritesDuplicateKey),
    ("surface UV table key equality and hash", SurfacePackingTests.FaceCornerUvTable_FaceCornerKeyEquality),
    ("surface OBJ parses valid BFF output", SurfacePackingTests.MeshObjIO_ParsesValidBffOutput),
    ("surface OBJ rejects face without UV indices", SurfacePackingTests.MeshObjIO_RejectsMissingUVIndex),
    ("surface OBJ rejects missing file", SurfacePackingTests.MeshObjIO_RejectsMissingFile),
    ("surface OBJ rejects face count mismatch", SurfacePackingTests.MeshObjIO_RejectsFaceCountMismatch),
    ("surface OBJ ignores comments and blank lines", SurfacePackingTests.MeshObjIO_IgnoresCommentAndBlankLines),
    // Barycentric mapper tests
    ("barycentric mapper identity triangle maps to self", SurfacePackingTests.BarycentricMapper_IdentityTriangle_MapsToSelf),
    ("barycentric mapper scaled triangle maps correctly", SurfacePackingTests.BarycentricMapper_ScaledTriangle_MapsCorrectly),
    ("barycentric mapper point outside mesh returns null", SurfacePackingTests.BarycentricMapper_PointOutsideMesh_ReturnsNull),
    ("barycentric mapper null curve returns null", SurfacePackingTests.BarycentricMapper_NullCurve_ReturnsNull),
    // F-2D-002 Trencadís solver tests (2026-05-07)
    ("trencadís component ComponentGuid is expected (Rhino)", TrencadisFillTests.TrencadisComponent_ComponentGuid_IsExpectedValue),
    ("trencadís component metadata is correct (Rhino)", TrencadisFillTests.TrencadisComponent_Metadata_IsCorrect),
    ("trencadís component has 17 inputs and 10 outputs (Rhino)", TrencadisFillTests.TrencadisComponent_HasExpectedInputAndOutputCount),
    ("trencadís component new inputs 13–16 named correctly (Rhino)", TrencadisFillTests.TrencadisComponent_NewInputs_HaveExpectedNames),
    ("CvdLloyd GenerateSeeds square domain all inside", TrencadisFillTests.CvdLloyd_GenerateSeeds_SquareDomain_AllInside),
    ("CvdLloyd GenerateSeeds deterministic for same seed", TrencadisFillTests.CvdLloyd_GenerateSeeds_DeterministicForSeed),
    ("CvdLloyd GenerateSeeds Lloyd moves to uniform quadrant centres", TrencadisFillTests.CvdLloyd_GenerateSeeds_LloydMovesToUniform),
    ("CvdLloyd GenerateSeeds excludes points inside hole", TrencadisFillTests.CvdLloyd_GenerateSeeds_HoleExcluded),
    ("Gvf Compute degenerate input returns empty field", TrencadisFillTests.Gvf_Compute_DegenerateInput_ReturnsEmptyField),
    ("Gvf Sample outside bbox returns zero", TrencadisFillTests.Gvf_Sample_OutsideBbox_ReturnsZero),
    ("Gvf Sample inside domain has non-zero magnitude somewhere", TrencadisFillTests.Gvf_Sample_InsideDomain_NonZero),
    ("Gvf OrientationDeg in [0, 180) inside, null outside", TrencadisFillTests.Gvf_OrientationDeg_InRange0to180),
    // Trencadís boundary modes (F-2D-002.E, 2026-05-07)
    ("trencadís boundary mode 1 ctor does not throw (Rhino)", TrencadisFillTests.TrencadisFill_BoundaryMode1_Construction_DoesNotThrow),
    ("trencadís boundary mode 2 ctor does not throw (Rhino)", TrencadisFillTests.TrencadisFill_BoundaryMode2_Construction_DoesNotThrow),
    ("trencadís boundary mode 3 ctor does not throw (Rhino)", TrencadisFillTests.TrencadisFill_BoundaryMode3_Construction_DoesNotThrow),
    ("trencadís boundary mode 2 packs without crash (Rhino)", TrencadisFillTests.TrencadisFill_BoundaryMode2_PacksWithoutCrash),
    ("trencadís boundary mode 3 packs on ring (Rhino)", TrencadisFillTests.TrencadisFill_BoundaryMode3_PacksOnRing),
    ("hungarian 2x2 optimal assignment", TrencadisFillTests.Hungarian_2x2_OptimalAssignment),
    ("hungarian 3x3 known optimum", TrencadisFillTests.Hungarian_3x3_KnownOptimum),
    ("hungarian 4x4 identity permutation", TrencadisFillTests.Hungarian_4x4_Permutation),
    // Existing packing tests
    ("packs simple blocks", PacksSimpleBlocks),
    ("reports failures", ReportsFailures),
    ("tries yaw rotation", TriesYawRotation),
    ("mesh heightmap fits into irregular footprint void", MeshHeightmapFitsIntoIrregularFootprintVoid),
    ("mesh heightmap reports vertical collision failure", MeshHeightmapReportsVerticalCollisionFailure),
    ("irregular mesh container rejects missing footprint cells", IrregularMeshContainerRejectsMissingFootprintCells),
    ("irregular mesh container uses triangular footprint", IrregularMeshContainerUsesTriangularFootprint),
    ("irregular mesh container supports separated vertical cavities", IrregularMeshContainerSupportsSeparatedVerticalCavities),
    ("mesh pack seed can explore alternatives", MeshPackSeedCanExploreAlternatives),
    // Ashlar packer Stage 1
    ("ashlar Stage1 coursed ashlar all uniform boxes full coverage", AshlarLayoutEngineTests.CoursedAshlar_AllUniformBoxes_FullCoverage),
    ("ashlar Stage1 boundary conditions bottom course fixed", AshlarLayoutEngineTests.BoundaryConditions_BottomCourseFixed),
    ("ashlar Stage1 leftover when no fit records gap", AshlarLayoutEngineTests.LeftoverWhenNoFit_RecordsGap),
    ("ashlar Stage1 running bond stagger offsets odd courses", AshlarLayoutEngineTests.RunningBondStagger_OffsetsOddCourses),
    ("ashlar Stage1 loop guard trips on pathological input", AshlarLayoutEngineTests.LoopGuard_TripsOnPathologicalInput),
    // Ashlar packer Stage 2
    ("ashlar Stage2 options constructed from WallFrame round-trips", AshlarLayoutEngineTests.Options_ConstructedFromWallFrame_RoundTrips),
    ("ashlar Stage2 coursed rubble variable heights bins by tolerance", AshlarLayoutEngineTests.CoursedRubble_VariableHeights_BinsByTolerance),
    ("ashlar Stage2 coursed rubble across bins never mixes in one course", AshlarLayoutEngineTests.CoursedRubble_AcrossBins_NeverMixesInOneCourse),
    ("ashlar Stage2 options invalid negative stagger throws", AshlarLayoutEngineTests.Options_InvalidNegativeStagger_Throws),
    // Ashlar packer Stage 3
    ("ashlar Stage3 diagnostics round trips result", AshlarLayoutEngineTests.Diagnostics_RoundTripsResult),
    ("ashlar Stage3 diagnostics handles empty leftovers", AshlarLayoutEngineTests.Diagnostics_HandlesEmptyLeftovers),
    ("ashlar Stage3 diagnostics placed blocks match assembly", AshlarLayoutEngineTests.Diagnostics_PlacedBlocksMatchAssembly),
    // Ashlar Stage A: rotation + trim-to-fit
    ("ashlar StageA rotation disabled stays translation only", AshlarLayoutEngineTests.Rotation_Disabled_StaysTranslationOnly),
    ("ashlar StageA rotation enabled packs oversized slabs by yawing", AshlarLayoutEngineTests.Rotation_Enabled_PacksOversizedSlabsByYawing),
    ("ashlar StageA trim disabled still records gap", AshlarLayoutEngineTests.Trim_Disabled_StillRecordsGap),
    ("ashlar StageA trim enabled fills gap with trimmed piece", AshlarLayoutEngineTests.Trim_Enabled_FillsGapWithTrimmedPiece),
    // Stage B: paper-faithful Hessian + QP closed-form fast path
    ("StageB RBE tangential scale applies to tangents only", StageBSolverTests.RbeFormulation_TangentialScale_AppliesToTangentDiagonalsOnly),
    ("StageB RBE tangential scale non-positive throws", StageBSolverTests.RbeFormulation_TangentialScale_NonPositive_Throws),
    ("StageB ManagedQp closed-form two vars finds midpoint", StageBSolverTests.ManagedQp_ClosedForm_TwoVars_FindsMidpoint),
    ("StageB ManagedQp closed-form non-uniform diagonal still solves", StageBSolverTests.ManagedQp_ClosedForm_NonUniformDiagonal_StillSolves),
    ("StageB ManagedQp closed-form bounds violated falls through", StageBSolverTests.ManagedQp_ClosedForm_BoundsViolated_FallsThrough),
    ("StageB E2E one free on ground RBE solver returns Optimal", StageBSolverTests.EndToEnd_OneFreeOnGround_FeedsRbeSolver_ReturnsOptimal),
    ("StageB E2E packed stack feeds RBE solver returns Optimal", StageBSolverTests.EndToEnd_PackedStack_FeedsRbeSolver_ReturnsOptimal),
    // Stage C: mesh shell splitter + convex hull
    ("StageC shell one closed tetrahedron returns one shell", StageCQuarryTests.Shell_OneClosedTetrahedron_ReturnsOneShell),
    ("StageC shell two separated tetrahedra returns two shells", StageCQuarryTests.Shell_TwoSeparatedTetrahedra_ReturnsTwoShells),
    ("StageC shell empty triangles returns empty", StageCQuarryTests.Shell_EmptyTriangles_ReturnsEmpty),
    ("StageC shell bad index throws", StageCQuarryTests.Shell_BadIndex_Throws),
    ("StageC hull unit cube 8 vertices returns closed hull", StageCQuarryTests.Hull_UnitCube8Vertices_ReturnsClosedHull),
    ("StageC hull non-convex input bounds convex shape", StageCQuarryTests.Hull_NonConvexInput_BoundsConvexShape),
    ("StageC hull coplanar throws", StageCQuarryTests.Hull_Coplanar_Throws),
    ("StageC hull collinear throws", StageCQuarryTests.Hull_Collinear_Throws),
    ("StageC hull too few points throws", StageCQuarryTests.Hull_TooFewPoints_Throws),
    ("StageC hull null points throws", StageCQuarryTests.Hull_NullPoints_Throws),
    // Stage D: IPOPT shim
    ("StageD ipopt stub IsAvailable is false", StageDIpoptTests.IpoptStub_IsAvailable_IsFalse),
    ("StageD ipopt stub Name is expected", StageDIpoptTests.IpoptStub_Name_IsExpected),
    ("StageD ipopt stub Solve returns NotImplemented", StageDIpoptTests.IpoptStub_Solve_ReturnsNotImplemented),
    ("StageD ipopt stub null problem throws", StageDIpoptTests.IpoptStub_NullProblem_Throws),
    ("StageD registry UseIpoptIfAvailable falls back to managed", StageDIpoptTests.Registry_UseIpoptIfAvailable_FallsBackToManaged),
    ("StageD registry UseIpoptIfAvailable preserves existing", StageDIpoptTests.Registry_UseIpoptIfAvailable_PreservesExisting),
    // Stage C GH smoke tests
    ("StageC GH MeshShellSplit ComponentGuid (Rhino)", QuarryFracturesGhComponentTests.MeshShellSplit_ComponentGuid_IsExpectedValue),
    ("StageC GH MeshShellSplit metadata (Rhino)", QuarryFracturesGhComponentTests.MeshShellSplit_Metadata_IsCorrect),
    ("StageC GH ConvexHullSlab ComponentGuid (Rhino)", QuarryFracturesGhComponentTests.ConvexHullSlab_ComponentGuid_IsExpectedValue),
    ("StageC GH ConvexHullSlab metadata (Rhino)", QuarryFracturesGhComponentTests.ConvexHullSlab_Metadata_IsCorrect),
    // Stage E: more fracture patterns
    ("StageE Layered axisX returns expected count", StageEFracturePatternsTests.Layered_AxisX_ReturnsExpectedCount),
    ("StageE Layered axis out of range throws", StageEFracturePatternsTests.Layered_AxisOutOfRange_Throws),
    ("StageE Radial four planes are orthogonal to axis", StageEFracturePatternsTests.Radial_FourPlanes_AreOrthogonalToAxis),
    ("StageE Radial zero axis throws", StageEFracturePatternsTests.Radial_ZeroAxis_Throws),
    ("StageE Radial count zero returns empty", StageEFracturePatternsTests.Radial_CountZero_ReturnsEmpty),
    ("StageE BrickPattern only horizontals returns Z parallel only", StageEFracturePatternsTests.BrickPattern_OnlyHorizontals_ReturnsZParallelOnly),
    ("StageE BrickPattern has both orientations", StageEFracturePatternsTests.BrickPattern_HasBothOrientations),
    ("StageE BrickPattern negative count throws", StageEFracturePatternsTests.BrickPattern_NegativeCount_Throws),
    ("StageE JitteredGrid deterministic for seed", StageEFracturePatternsTests.JitteredGrid_DeterministicForSeed),
    ("StageE JitteredGrid jitter out of range throws", StageEFracturePatternsTests.JitteredGrid_JitterOutOfRange_Throws),
    ("StageE Filter keeps intersecting drops outside", StageEFracturePatternsTests.Filter_KeepsIntersectingPlanes_DropsOutsidePlanes),
    ("StageE Filter on face keeps plane", StageEFracturePatternsTests.Filter_OnFace_KeepsPlane),
    ("StageE Filter null planes throws", StageEFracturePatternsTests.Filter_NullPlanes_Throws),
    ("StageE GH Layered metadata (Rhino)", StageEFracturePatternsTests.Gh_LayeredFracturePlanes_Metadata),
    ("StageE GH Radial metadata (Rhino)", StageEFracturePatternsTests.Gh_RadialFracturePlanes_Metadata),
    ("StageE GH BrickPattern metadata (Rhino)", StageEFracturePatternsTests.Gh_BrickPatternFracturePlanes_Metadata),
    ("StageE GH JitteredGrid metadata (Rhino)", StageEFracturePatternsTests.Gh_JitteredGridFracturePlanes_Metadata),
    ("StageE GH FracturePlaneFilter metadata (Rhino)", StageEFracturePatternsTests.Gh_FracturePlaneFilter_Metadata),
    ("StageE GH all GUIDs unique", StageEFracturePatternsTests.Gh_StageE_AllGuidsUnique),
    // Interop pass (Phase 1-5)
    ("Phase1 SlabCutByFractures Plane input is generic (not Param_Plane)", InteropPhasesTests.Phase1_SlabCutByFracturesComponent_PlaneInput_IsGeneric),
    ("Phase1 GhInterop UnwrapPlane accepts FracturePlane DTO", InteropPhasesTests.Phase1_GhInterop_UnwrapPlane_AcceptsFracturePlaneDto),
    ("Phase1 GhInterop UnwrapPlane accepts Rhino Plane (Rhino)", InteropPhasesTests.Phase1_GhInterop_UnwrapPlane_AcceptsRhinoPlane),
    ("Phase1 GhInterop UnwrapPlane rejects nonsense", InteropPhasesTests.Phase1_GhInterop_UnwrapPlane_RejectsNonsense),
    ("Phase2 SlabFromMesh has Mesh output", InteropPhasesTests.Phase2_SlabFromMesh_HasMeshOutput),
    ("Phase2 SlabCutByFractures has Mesh output", InteropPhasesTests.Phase2_SlabCutByFractures_HasMeshOutput),
    ("Phase2 SlabCutByFracturePolygons has Mesh output", InteropPhasesTests.Phase2_SlabCutByFracturePolygons_HasMeshOutput),
    ("Phase2 QuarryDecompose has Mesh output", InteropPhasesTests.Phase2_QuarryDecompose_HasMeshOutput),
    ("Phase2 MeshShellSplit has Mesh output", InteropPhasesTests.Phase2_MeshShellSplit_HasMeshOutput),
    ("Phase2 ConvexHullSlab has Mesh output", InteropPhasesTests.Phase2_ConvexHullSlab_HasMeshOutput),
    ("Phase2 GhInterop SlabToMesh round trips (Rhino)", InteropPhasesTests.Phase2_GhInterop_SlabToMesh_RoundTrips),
    ("Phase3 GhInterop UnwrapSlab accepts Rhino Mesh (Rhino)", InteropPhasesTests.Phase3_GhInterop_UnwrapSlab_AcceptsRhinoMesh),
    ("Phase3 GhInterop UnwrapSlab accepts Slab DTO", InteropPhasesTests.Phase3_GhInterop_UnwrapSlab_AcceptsSlabDto),
    ("Phase4 FracturePolygonFromCurve has ForceProject input", InteropPhasesTests.Phase4_FracturePolygonFromCurve_HasForceProjectInput),
    ("Phase5 all 24 components share Frahan category", InteropPhasesTests.Phase5_AllMasonryComponents_ShareFrahanCategory),
    // Phase 6: JointSet + Quarry DFN
    ("JointSet vertical north dip normal is north", JointSetDfnTests.JointSet_VerticalNorthDip_NormalIsNorth),
    ("JointSet horizontal dip normal is up", JointSetDfnTests.JointSet_HorizontalDip_NormalIsUp),
    ("JointSet vertical east dip normal is east", JointSetDfnTests.JointSet_VerticalEastDip_NormalIsEast),
    ("JointSet negative spacing throws", JointSetDfnTests.JointSet_NegativeSpacing_Throws),
    ("JointSet out of range dip direction throws", JointSetDfnTests.JointSet_OutOfRangeDipDirection_Throws),
    ("JointSet out of range dip throws", JointSetDfnTests.JointSet_OutOfRangeDip_Throws),
    ("Dfn one vertical set spacing matches grid count", JointSetDfnTests.Dfn_OneVerticalSet_SpacingMatchesGridCount),
    ("Dfn two orthogonal sets accumulates planes", JointSetDfnTests.Dfn_TwoOrthogonalSets_AccumulatesPlanes),
    ("Dfn deterministic for seed", JointSetDfnTests.Dfn_DeterministicForSeed),
    ("Dfn null jointSets throws", JointSetDfnTests.Dfn_NullJointSets_Throws),
    ("Dfn decompose by joint sets cuts the quarry", JointSetDfnTests.Dfn_DecomposeByJointSets_CutsTheQuarry),
    ("GH JointSet metadata (Rhino)", JointSetDfnTests.Gh_JointSetComponent_Metadata),
    ("GH QuarryDfn metadata (Rhino)", JointSetDfnTests.Gh_QuarryDfnComponent_Metadata),
    // Mesh diagnostics + 4-colour block graph
    ("GH MeshAabb metadata (Rhino)", MeshSolverTests.Gh_MeshAabbComponent_Metadata),
    ("GH MeshPca metadata (Rhino)", MeshSolverTests.Gh_MeshPcaComponent_Metadata),
    ("GH MeshDiagnostics metadata (Rhino)", MeshSolverTests.Gh_MeshDiagnosticsComponent_Metadata),
    ("BlockGraphColorer two stacked blocks get two colours", MeshSolverTests.BlockGraphColorer_TwoStackedBlocks_GetTwoColours),
    ("BlockGraphColorer no interfaces all zero", MeshSolverTests.BlockGraphColorer_NoInterfaces_AllZero),
    ("BlockGraphColorer triangle graph requires three", MeshSolverTests.BlockGraphColorer_TriangleGraph_RequiresThree),
    ("BlockGraphColorer null assembly throws", MeshSolverTests.BlockGraphColorer_NullAssembly_Throws),
    ("GH BlockGraphColoring metadata (Rhino)", MeshSolverTests.Gh_BlockGraphColoringComponent_Metadata),
    // Mesh proximity-based contact detection (Cockroach-style)
    ("MeshContact two stacked cubes finds one contact", MeshContactDetectorTests.Detect_TwoStackedCubes_FindsOneContact),
    ("MeshContact two cubes side by side finds one contact", MeshContactDetectorTests.Detect_TwoCubesSideBySide_FindsOneContact),
    ("MeshContact gap within tolerance still finds contact", MeshContactDetectorTests.Detect_GapWithinTolerance_StillFindsContact),
    ("MeshContact gap beyond tolerance finds nothing", MeshContactDetectorTests.Detect_GapBeyondTolerance_FindsNothing),
    ("MeshContact no overlap finds nothing", MeshContactDetectorTests.Detect_NoOverlap_FindsNothing),
    ("MeshContact partial overlap detects correct area", MeshContactDetectorTests.Detect_PartialOverlap_DetectsCorrectArea),
    ("MeshContact null meshes throws", MeshContactDetectorTests.Detect_NullMeshes_Throws),
    ("MeshContact mismatched ids count throws", MeshContactDetectorTests.Detect_MismatchedIdsCount_Throws),
    ("MeshContact negative tolerance throws", MeshContactDetectorTests.Detect_NegativeTolerance_Throws),
    ("MeshContact many stacked cubes chains contacts (broad-phase)", MeshContactDetectorTests.Detect_ManyStackedCubes_ChainsContactsCorrectly),
    ("MeshContact distant clusters broad-phase skips cleanly", MeshContactDetectorTests.Detect_DistantClusters_BroadPhaseSkipsCleanly),
    // Spatial grid (third indexing layer)
    ("Grid two overlapping cubes yields one pair", MeshContactDetectorTests.Grid_TwoOverlappingCubes_YieldsOnePair),
    ("Grid distant cubes yield no pair", MeshContactDetectorTests.Grid_DistantCubes_YieldNoPair),
    ("Grid chain of cubes yields consecutive pairs once", MeshContactDetectorTests.Grid_ChainOfCubes_YieldsConsecutivePairsOnce),
    ("Grid bad cell size throws", MeshContactDetectorTests.Grid_BadCellSize_Throws),
    ("Detect over threshold returns correct contacts via grid", MeshContactDetectorTests.Detect_OverThreshold_StillReturnsCorrectContacts),
    ("GH RobustAutoInterfaces metadata (Rhino)", MeshContactDetectorTests.Gh_RobustAutoInterfacesComponent_Metadata),
    // BVH + best-fit packer + mesh repair
    ("BVH unit cube query inside returns zero", BvhAndBestFitTests.Bvh_UnitCube_QueryInsideReturnsZero),
    ("BVH unit cube query outside returns nearest face", BvhAndBestFitTests.Bvh_UnitCube_QueryOutsideReturnsNearestFace),
    ("BVH respects max distance", BvhAndBestFitTests.Bvh_RespectsMaxDistance),
    ("BVH matches brute force on random points", BvhAndBestFitTests.Bvh_MatchesBruteForce_OnRandomPoints),
    ("BVH contact detector still finds stacked contact", BvhAndBestFitTests.Bvh_ContactDetector_StillFindsStackedContact),
    ("BestFit inventory prefers exact match", BvhAndBestFitTests.BestFit_Inventory_PrefersExactMatch),
    ("BestFit pack full coverage on uniform inventory", BvhAndBestFitTests.BestFit_Pack_FullCoverage_OnUniformInventory),
    ("BestFit null slabs throws", BvhAndBestFitTests.BestFit_NullSlabs_Throws),
    ("GH BestFitPack metadata (Rhino)", BvhAndBestFitTests.Gh_BestFitPackComponent_Metadata),
    ("GH MeshRepair metadata (Rhino)", BvhAndBestFitTests.Gh_MeshRepairComponent_Metadata),
    // Ashlar-pack GH component smoke tests (Rhino-tagged: SKIP without Grasshopper, PASS with)
    ("ashlar GH AshlarPack ComponentGuid (Rhino)", AshlarPackGhComponentTests.AshlarPackComponent_ComponentGuid_IsExpectedValue),
    ("ashlar GH AshlarPack metadata (Rhino)", AshlarPackGhComponentTests.AshlarPackComponent_Metadata_IsCorrect),
    ("ashlar GH AshlarPack input/output counts (Rhino)", AshlarPackGhComponentTests.AshlarPackComponent_HasExpectedInputAndOutputCount),
    ("ashlar GH AshlarPack optional inputs (Rhino)", AshlarPackGhComponentTests.AshlarPackComponent_OptionalInputs_AreOptional),
    ("ashlar GH WallFrame ComponentGuid (Rhino)", AshlarPackGhComponentTests.WallFrameComponent_ComponentGuid_IsExpectedValue),
    ("ashlar GH WallFrame metadata (Rhino)", AshlarPackGhComponentTests.WallFrameComponent_Metadata_IsCorrect),
    ("ashlar GH WallFrame input/output counts (Rhino)", AshlarPackGhComponentTests.WallFrameComponent_HasExpectedInputAndOutputCount),
    ("ashlar GH AshlarPackOptions ComponentGuid (Rhino)", AshlarPackGhComponentTests.AshlarPackOptionsComponent_ComponentGuid_IsExpectedValue),
    ("ashlar GH AshlarPackOptions input/output counts (Rhino)", AshlarPackGhComponentTests.AshlarPackOptionsComponent_HasExpectedInputAndOutputCount),
    ("ashlar GH PackDiagnostics ComponentGuid (Rhino)", AshlarPackGhComponentTests.PackDiagnosticsComponent_ComponentGuid_IsExpectedValue),
    ("ashlar GH PackDiagnostics input/output counts (Rhino)", AshlarPackGhComponentTests.PackDiagnosticsComponent_HasExpectedInputAndOutputCount),
    ("ashlar GH PackPreview ComponentGuid (Rhino)", AshlarPackGhComponentTests.PackPreviewComponent_ComponentGuid_IsExpectedValue),
    ("ashlar GH PackPreview input/output counts (Rhino)", AshlarPackGhComponentTests.PackPreviewComponent_HasExpectedInputAndOutputCount),
    ("ashlar GH new GUIDs are unique", AshlarPackGhComponentTests.NewComponentGuids_AreUnique),
    // End-to-end integration with the Masonry RBE QP solver
    ("ashlar E2E packed wall feeds RBE solver produces well-formed QP", AshlarLayoutEngineTests.EndToEnd_PackedWall_FeedsRbeSolver_ProducesWellFormedQp),
    ("ashlar E2E packed wall one course has zero bed joints", AshlarLayoutEngineTests.EndToEnd_PackedWall_OneCourse_HasZeroBedJoints),
    // Quarry / fractures / auto-interfaces (Phase E.3 / E.4 / P3)
    ("bbox from slab unit cube has unit extents", QuarryFracturesInterfacesTests.BoundingBox_FromSlab_UnitCube_HasUnitExtents),
    ("bbox degenerate axis throws", QuarryFracturesInterfacesTests.BoundingBox_DegenerateAxis_Throws),
    ("grid generator nX nY nZ returns expected count", QuarryFracturesInterfacesTests.Grid_NxNyNz_ReturnsExpectedCount),
    ("grid generator planes are evenly spaced", QuarryFracturesInterfacesTests.Grid_PlanesAreEvenlySpaced),
    ("random generator deterministic for seed", QuarryFracturesInterfacesTests.Random_DeterministicForSeed),
    ("random generator different seeds give different planes", QuarryFracturesInterfacesTests.Random_DifferentSeeds_GiveDifferentPlanes),
    ("voronoi bisectors two seeds returns one plane", QuarryFracturesInterfacesTests.VoronoiBisectors_TwoSeeds_ReturnsOnePlane),
    ("voronoi bisectors four seeds returns six planes", QuarryFracturesInterfacesTests.VoronoiBisectors_FourSeeds_ReturnsSixPlanes),
    ("voronoi bisectors single seed throws", QuarryFracturesInterfacesTests.VoronoiBisectors_SingleSeed_Throws),
    ("quarry decompose by grid unit cube produces 8 octants", QuarryFracturesInterfacesTests.QuarryDecompose_ByGrid_UnitCube_Produces8Octants),
    ("quarry decompose by grid splits conserves volume", QuarryFracturesInterfacesTests.QuarryDecompose_ByGrid_Splits_Conserves_Volume),
    ("quarry decompose by voronoi two seeds produces two cells", QuarryFracturesInterfacesTests.QuarryDecompose_ByVoronoi_TwoSeeds_ProducesTwoCells),
    ("quarry decompose zero fractures passes through original slab", QuarryFracturesInterfacesTests.QuarryDecompose_ZeroFractures_PassesThroughOriginalSlab),
    ("auto interfaces two stacked cubes finds one bed joint", QuarryFracturesInterfacesTests.Detect_TwoStackedCubes_FindsOneBedJoint),
    ("auto interfaces two cubes side by side finds one head joint", QuarryFracturesInterfacesTests.Detect_TwoCubesSideBySide_FindsOneHeadJoint),
    ("auto interfaces non-contacting finds zero", QuarryFracturesInterfacesTests.Detect_NonContacting_FindsZero),
    ("auto interfaces partial overlap respects clipping", QuarryFracturesInterfacesTests.Detect_PartialOverlap_PolygonRespectsClipping),
    ("auto interfaces null slabs throws", QuarryFracturesInterfacesTests.Detect_NullSlabs_Throws),
    ("auto interfaces mismatched ids count throws", QuarryFracturesInterfacesTests.Detect_MismatchedIdsCount_Throws),
    ("auto interfaces packed wall matches ashlar engine count", QuarryFracturesInterfacesTests.Detect_PackedWall_MatchesAshlarLayoutEngineCount),
    // Quarry / fractures / interfaces GH smoke tests
    ("quarry GH GridFracturePlanes ComponentGuid (Rhino)", QuarryFracturesGhComponentTests.GridFracturePlanes_ComponentGuid_IsExpectedValue),
    ("quarry GH GridFracturePlanes metadata (Rhino)", QuarryFracturesGhComponentTests.GridFracturePlanes_Metadata_IsCorrect),
    ("quarry GH RandomFracturePlanes ComponentGuid (Rhino)", QuarryFracturesGhComponentTests.RandomFracturePlanes_ComponentGuid_IsExpectedValue),
    ("quarry GH RandomFracturePlanes metadata (Rhino)", QuarryFracturesGhComponentTests.RandomFracturePlanes_Metadata_IsCorrect),
    ("quarry GH VoronoiFracturePlanes ComponentGuid (Rhino)", QuarryFracturesGhComponentTests.VoronoiFracturePlanes_ComponentGuid_IsExpectedValue),
    ("quarry GH VoronoiFracturePlanes metadata (Rhino)", QuarryFracturesGhComponentTests.VoronoiFracturePlanes_Metadata_IsCorrect),
    ("quarry GH QuarryDecompose ComponentGuid (Rhino)", QuarryFracturesGhComponentTests.QuarryDecompose_ComponentGuid_IsExpectedValue),
    ("quarry GH QuarryDecompose metadata (Rhino)", QuarryFracturesGhComponentTests.QuarryDecompose_Metadata_IsCorrect),
    ("quarry GH AutoInterfaces ComponentGuid (Rhino)", QuarryFracturesGhComponentTests.AutoInterfaces_ComponentGuid_IsExpectedValue),
    ("quarry GH AutoInterfaces metadata (Rhino)", QuarryFracturesGhComponentTests.AutoInterfaces_Metadata_IsCorrect),
    ("quarry GH new GUIDs are unique", QuarryFracturesGhComponentTests.NewQuarryFractureGuids_AreUnique),
    // EdgeMatching (2026-05-11): Trencadís/wood-plank edge-matching solver.
    // Pure-managed tests run anywhere; Panel/Segmenter/ICP tests need Rhino
    // and take the existing native-SKIP path otherwise.
    ("edgematch PhaseCorrelator perfect complement scores 1.0", EdgeMatchingPhaseCorrelatorTests.PerfectComplement_ScoresOne),
    ("edgematch PhaseCorrelator unrelated signals score below 0.95", EdgeMatchingPhaseCorrelatorTests.UnrelatedSignatures_ScoreBelowHalf),
    ("edgematch PhaseCorrelator empty signatures return (0,0)", EdgeMatchingPhaseCorrelatorTests.EmptySignatures_ReturnZero),
    ("edgematch PhaseCorrelator mismatched length throws", EdgeMatchingPhaseCorrelatorTests.MismatchedLength_Throws),
    ("edgematch SegmentHashIndex complement finds mirror", EdgeMatchingSegmentHashIndexTests.QueryComplement_FindsMirrorSegment),
    ("edgematch SegmentHashIndex deterministic order", EdgeMatchingSegmentHashIndexTests.QueryComplement_DeterministicOrder),
    ("edgematch SegmentHashKey equality + hash round-trip", EdgeMatchingSegmentHashIndexTests.HashKey_RoundTrips),
    ("edgematch SegmentHashIndex 3D complement round-trip", EdgeMatchingSegmentHashIndexTests.QueryComplement3D_FindsMirrorSegment),
    ("edgematch SegmentHashIndex 3D query ignores 2D buckets", EdgeMatchingSegmentHashIndexTests.Query3D_IgnoresPlanar2DBuckets),
    ("edgematch SegmentHashIndex 2D query ignores 3D buckets", EdgeMatchingSegmentHashIndexTests.Query2D_IgnoresSpatial3DBuckets),
    ("edgematch SegmentHashIndex Count2D/Count3D reflect adds", EdgeMatchingSegmentHashIndexTests.Count2D_Count3D_ReflectAdds),
    ("edgematch PlanarityTester planar square has zero RMS (Rhino)", EdgeMatchingPlanarityTesterTests.PerfectlyPlanarSquare_HasZeroRms),
    ("edgematch PlanarityTester helix has non-zero RMS (Rhino)", EdgeMatchingPlanarityTesterTests.HelixCurve_HasNonZeroRms),
    ("edgematch Panel planar contour classifies Planar2D (Rhino)", EdgeMatchingPlanarityTesterTests.PlanarContour_ClassifiesAsPlanar2D),
    ("edgematch Panel warped contour classifies Spatial3D (Rhino)", EdgeMatchingPlanarityTesterTests.WarpedContour_ClassifiesAsSpatial3D),
    ("edgematch Panel non-frame open contour throws (Rhino)", EdgeMatchingPlanarityTesterTests.NonFrame_OpenContour_Throws),
    ("edgematch BoundarySegmenter irregular polygon produces segments (Rhino)", EdgeMatchingBoundarySegmenterTests.IrregularPolygon_ProducesSegments),
    ("edgematch BoundarySegmenter smooth circle has no breaks (Rhino)", EdgeMatchingBoundarySegmenterTests.FullCircle_StraightLine_ProducesNoBreaks),
    // R1 partial sub-segment emission (opt-in). PartialWindows test is pure
    // managed; the segmenter tests need the Rhino runtime.
    ("edgematch Partial off identical to base (Rhino)", EdgeMatchingPartialSegmentTests.PartialsOff_IdenticalToBase),
    ("edgematch Partial on adds shorter sub-segments (Rhino)", EdgeMatchingPartialSegmentTests.PartialsOn_AddsSegments),
    ("edgematch Partial on is deterministic (Rhino)", EdgeMatchingPartialSegmentTests.PartialsOn_IsDeterministic),
    ("edgematch Partial empty fractions adds nothing (Rhino)", EdgeMatchingPartialSegmentTests.EmptyFractions_NoPartialsEvenWhenOn),
    ("edgematch PartialWindows deterministic ranges + order", EdgeMatchingPartialSegmentTests.PartialWindows_DeterministicRangesAndOrder),
    ("edgematch ICP2D identity on same polyline → zero residual (Rhino)", EdgeMatchingIcp2DTests.Refine_IdentityOnSamePolyline_ReturnsZeroResidual),
    ("edgematch ICP2D perturbed transform recovers truth (Rhino)", EdgeMatchingIcp2DTests.Refine_PerturbedTransform_RecoversTruth),
    ("edgematch ICP3D identity on same polyline → zero residual (Rhino)", EdgeMatchingIcp3DTests.Refine_IdentityOnSamePolyline_ReturnsZeroResidual),
    ("edgematch ICP3D known 6-DoF transform recovered (Rhino)", EdgeMatchingIcp3DTests.Refine_KnownRigidTransform_RecoversTruth),
    ("edgematch Dispatch planar panel → null torsion (Rhino)", EdgeMatchingDispatchTests.PlanarPanel_Segmenter_StampsNullTorsion),
    ("edgematch Dispatch spatial panel → populated torsion (Rhino)", EdgeMatchingDispatchTests.SpatialPanel_Segmenter3D_PopulatesTorsion),
    ("edgematch Dispatch mixed assembly splits 2D/3D buckets (Rhino)", EdgeMatchingDispatchTests.MixedAssembly_IndexBuckets_SplitByMode),
    ("edgematch Dispatch mixed solver runs without crash (Rhino)", EdgeMatchingDispatchTests.AssemblySolver_MixedPanels_RunsWithoutCrash),
    ("edgematch Determinism two runs same input → same output (Rhino)", EdgeMatchingDeterminismTests.TwoRuns_SameInput_SameOutput),
    ("edgematch Determinism hash identical across runs (Rhino)", EdgeMatchingDeterminismTests.TwoRuns_HashIdentical),
    ("edgematch component EdgeMatchSolve GUID parses", EdgeMatchingComponentGuidTests.EdgeMatchSolveComponent_GuidParses),
    ("edgematch component EdgeMatchSegments GUID parses", EdgeMatchingComponentGuidTests.EdgeMatchSegmentsComponent_GuidParses),
    ("edgematch component TrencadisEdgeMatch GUID parses", EdgeMatchingComponentGuidTests.TrencadisEdgeMatchComponent_GuidParses),
    ("edgematch component EdgeMatchOptions GUID parses", EdgeMatchingComponentGuidTests.EdgeMatchOptionsComponent_GuidParses),
    ("edgematch component GUIDs are unique", EdgeMatchingComponentGuidTests.AllFourComponents_HaveUniqueGuids),
    // EdgeMatch Options DTO surfacing (2026-05-25): the EdgeMatch Options
    // component bundles the advanced AssemblyOptions flags; EdgeMatch Solve
    // consumes them on its optional Opt input. Pure-managed value semantics:
    // empty == Core defaults, advanced merge round-trips, basic fields preserved.
    ("edgematch Options empty equals Core defaults", EdgeMatchOptionsTests.EmptyOptions_EqualCoreDefaults),
    ("edgematch Options advanced merge round-trips, basics preserved", EdgeMatchOptionsTests.Merge_AdvancedFields_RoundTrips_BasicFieldsPreserved),
    ("edgematch Options no-merge leaves simple options untouched", EdgeMatchOptionsTests.NoMerge_LeavesSimpleOptionsUntouched),
    // OrderedBoundaryMatcher (2026-05-24): non-crossing rim correspondence.
    // Pure-managed (Point3d value struct only) so runs without Rhino runtime.
    ("edgematch OrderedMatcher MatchOpen is monotone (non-crossing)", EdgeMatchingOrderedMatcherTests.MatchOpen_ProducesMonotoneCorrespondence),
    ("edgematch OrderedMatcher beats nearest-neighbour on wiggly rim", EdgeMatchingOrderedMatcherTests.MatchOpen_BeatsNearestNeighbour_OnWigglyRim),
    ("edgematch OrderedMatcher MatchClosed picks reversed orientation on complementary rim", EdgeMatchingOrderedMatcherTests.MatchClosed_PicksReversedOrientation_OnComplementaryRim),
    ("edgematch OrderedMatcher empty/single-point no throw", EdgeMatchingOrderedMatcherTests.MatchOpen_EmptyAndSinglePoint_NoThrow),
    ("edgematch OrderedMatcher MatchClosed is deterministic", EdgeMatchingOrderedMatcherTests.MatchClosed_IsDeterministic),
    // R0 Agglomerative assembly (2026-05-25): pairwise graph + spanning-tree
    // composition. ComposeRelatives_* + DefaultMode_* are pure-managed (Transform
    // value-struct math only); Solve_Agglomerative_* need the Rhino runtime (SKIP).
    ("edgematch Agglomerative compose-relatives along chain matches product", EdgeMatchingAgglomerativeTests.ComposeRelatives_AlongChain_MatchesProduct),
    ("edgematch Agglomerative reversed edge uses inverse", EdgeMatchingAgglomerativeTests.ComposeRelatives_ReversedEdge_UsesInverse),
    ("edgematch Agglomerative default mode is FrameAnchored", EdgeMatchingAgglomerativeTests.DefaultMode_IsFrameAnchored),
    ("edgematch Agglomerative two halves place both (Rhino)", EdgeMatchingAgglomerativeTests.Solve_Agglomerative_TwoHalves_PlacesBoth),
    ("edgematch Agglomerative is deterministic (Rhino)", EdgeMatchingAgglomerativeTests.Solve_Agglomerative_IsDeterministic),
    // R2 global non-overlap resolve (2026-05-25): overlap penalty + edge-
    // exclusivity + post-solve 2D depenetration polish. Defaults_* are pure-
    // managed (option defaults only); Resolve_* need the Rhino runtime (curve
    // boolean) and SKIP without it.
    ("edgematch R2 default knobs are off", EdgeMatchingOverlapResolveTests.Defaults_R2KnobsAreOff),
    ("edgematch R2 resolve tuning has sane values", EdgeMatchingOverlapResolveTests.Defaults_ResolveTuningHasSaneValues),
    ("edgematch R2 depenetration reduces synthetic overlap (Rhino)", EdgeMatchingOverlapResolveTests.Resolve_ReducesSyntheticOverlap),
    ("edgematch R2 depenetration is deterministic (Rhino)", EdgeMatchingOverlapResolveTests.Resolve_IsDeterministic),
    ("edgematch R2 depenetration no-overlap no-movement (Rhino)", EdgeMatchingOverlapResolveTests.Resolve_NoOverlap_NoMovement),
    // Pillar A Soft-ICP (2026-05-25): EM weighted-Kabsch rim-contact +
    // non-penetration refiner. All PURE MANAGED (Lie Exp + Transform value math +
    // MathNet SVD; 2D/3D refine driven with null solids/contours so no rhcommon_c).
    // Default-OFF guard + Lie-Exp correctness + EM alignment recovery + determinism.
    ("edgematch SoftIcp default refine is off", EdgeMatchingSoftIcpTests.Defaults_SoftIcpRefineIsOff),
    ("edgematch SoftIcp SE2 Exp translation+rotation", EdgeMatchingSoftIcpTests.LieSe2_Exp_PureTranslationAndRotation),
    ("edgematch SoftIcp SE2 Exp is proper rotation", EdgeMatchingSoftIcpTests.LieSe2_Exp_IsProperRotation),
    ("edgematch SoftIcp so3 Exp matches Rhino rotation", EdgeMatchingSoftIcpTests.LieSe3_ExpSo3_MatchesRhinoRotation),
    ("edgematch SoftIcp SE3 Exp small twist valid", EdgeMatchingSoftIcpTests.LieSe3_Exp_SmallTwistIsValid),
    ("edgematch SoftIcp EM recovers known 2D alignment", EdgeMatchingSoftIcpTests.Em_RecoversKnown2DAlignment),
    ("edgematch SoftIcp EM recovers known 3D alignment", EdgeMatchingSoftIcpTests.Em_RecoversKnown3DAlignment),
    ("edgematch SoftIcp EM is deterministic", EdgeMatchingSoftIcpTests.Em_IsDeterministic),
    // 2.5D per-facet PROJECTION BOOTSTRAP (2026-05-25): PCA facet-plane fit +
    // 2D->3D lift composition. PURE MANAGED (MathNet 3x3 EVD + Transform value
    // math); default-OFF guard + plane-fit correctness + lift recovery on a known pair.
    ("edgematch ProjectionBootstrap default is off", EdgeMatchingProjectionBootstrapTests.Defaults_ProjectionBootstrapIsOff),
    ("edgematch ProjectionBootstrap PCA fits known planar loop", EdgeMatchingProjectionBootstrapTests.FitFacetPlane_KnownPlanarLoop_RecoversNormalAndZeroResidual),
    ("edgematch ProjectionBootstrap PCA non-planar has residual", EdgeMatchingProjectionBootstrapTests.FitFacetPlane_NonPlanar_HasResidual),
    ("edgematch ProjectionBootstrap lift composes antiparallel contact", EdgeMatchingProjectionBootstrapTests.Lift_KnownPair_ComposesAntiparallelContact),
    ("edgematch ProjectionBootstrap lift respects in-plane match", EdgeMatchingProjectionBootstrapTests.Lift_InPlaneMatch_ShiftsAlongFacetAxes),
    ("QuarryCutOpt Inventory builds and aggregates", QuarryCutOptTests.QuarryInventory_BuildsAndAggregates),
    ("QuarryCutOpt Inventory rejects duplicate ids", QuarryCutOptTests.QuarryInventory_DuplicateIds_Throws),
    ("QuarryCutOpt Yield estimator empty fractures", QuarryCutOptTests.BlockYieldEstimator_EmptyFractures_AllAccepted),
    ("QuarryCutOpt Extraction order greedy sort", QuarryCutOptTests.ExtractionOrder_GreedySortsByScore),
    ("QuarryCutOpt Extraction skips low yield", QuarryCutOptTests.ExtractionOrder_SkipsLowYield),
    ("QuarryCutOpt SawBedScheduler LPT balances two beds", QuarryCutOptTests.SawBedScheduler_LptBalancesTwoBeds),
    ("QuarryCutOpt Report Markdown contains aggregates", QuarryCutOptTests.QuarryReport_MarkdownContainsAggregates),
    ("QuarryCutOpt Gpr radargram CSV round-trip", QuarryCutOptTests.GprRadargramReader_RoundTripCsv),
    ("QuarryCutOpt GeoFractNet mask reader parses predictions", QuarryCutOptTests.GeoFractNetMaskReader_ParsesPredictions),
    ("Ingest Shapefile fracture reader loads Loviisa if present", VectorAndSegYIngestTests.ShapefileFractureReader_LoadsLoviisaIfPresent),
    ("Ingest GeoJSON parses LineString", VectorAndSegYIngestTests.GeoJsonFractureReader_ParsesLineString),
    ("Ingest GeoJSON handles MultiLineString", VectorAndSegYIngestTests.GeoJsonFractureReader_HandlesMultiLineString),
    ("Ingest VectorFractureReader dispatches by extension", VectorAndSegYIngestTests.VectorFractureReader_DispatchesByExtension),
    ("Ingest VectorFractureReader rejects unknown extension", VectorAndSegYIngestTests.VectorFractureReader_RejectsUnknownExtension),
    ("Ingest SEG-Y IEEE float32 round-trip", VectorAndSegYIngestTests.GprSegYReader_RoundTripIeeeFloat32),
    ("Ingest SEG-Y IBM float32 decode", VectorAndSegYIngestTests.GprSegYReader_DecodesIbmFloat32),
    ("Ingest MALA RD3 round-trip", VectorAndSegYIngestTests.GprMalaRd3Reader_RoundTrip),
    ("Ingest MALA RD3 rejects missing .rad", VectorAndSegYIngestTests.GprMalaRd3Reader_RejectsMissingRad),
    ("Ingest DT1 pulseEKKO round-trip", VectorAndSegYIngestTests.GprDt1Reader_RoundTrip),
    ("Ingest GprFileReader dispatches by extension", VectorAndSegYIngestTests.GprFileReader_DispatchesByExtension),
    ("Kintsugi-port Fps picks corners of a square", KintsugiPortPrimitiveTests.Fps_PicksCornersOfASquare),
    ("Kintsugi-port Fps second pick is opposite", KintsugiPortPrimitiveTests.Fps_SecondPickIsOpposite),
    ("Kintsugi-port Knn returns closest points", KintsugiPortPrimitiveTests.Knn_ReturnsClosestPoints),
    ("Kintsugi-port Matmul identity vs known product", KintsugiPortPrimitiveTests.Matmul_IdentityVsKnownProduct),
    ("Kintsugi-port Matmul large shapes vs naive loop", KintsugiPortPrimitiveTests.Matmul_LargeShapesProduceSameResultAsNaiveLoop),
    ("Kintsugi-port MatVec matches MatMul N=1", KintsugiPortPrimitiveTests.MatVec_MatchesMatMulN1),
    ("Kintsugi-port Gelu at zero is zero", KintsugiPortPrimitiveTests.Activations_GeluAtZeroIsZero),
    ("Kintsugi-port Gelu at positive tends to input", KintsugiPortPrimitiveTests.Activations_GeluAtPositiveTendsToInput),
    ("Kintsugi-port Silu at zero is zero", KintsugiPortPrimitiveTests.Activations_SiluAtZeroIsZero),
    ("Kintsugi-port Relu clamps negatives", KintsugiPortPrimitiveTests.Activations_ReluClampsNegatives),
    ("Kintsugi-port Softmax row sums to one", KintsugiPortPrimitiveTests.Activations_SoftmaxRowSumsToOne),
    ("Kintsugi-port LayerNorm zero mean unit var", KintsugiPortPrimitiveTests.LayerNorm_ZeroMeanUnitVar),
    ("Kintsugi-port LayerNorm gamma+beta scale and shift", KintsugiPortPrimitiveTests.LayerNorm_GammaBetaScaleAndShift),
    ("Kintsugi-port TimeEmbedding length and t=0 symmetry", KintsugiPortPrimitiveTests.TimeEmbedding_LengthAndSymmetry),
    ("Kintsugi-port MultiHeadAttention identity weights pass through", KintsugiPortPrimitiveTests.MultiHeadAttention_IdentityWeightsPassThrough),
    ("Kintsugi-port VqVae replaces with nearest codebook entry", KintsugiPortPrimitiveTests.VqVae_ReplacesWithNearestCodebookEntry),
    ("Kintsugi-port WeightReader loads kintsugi.bin if present", KintsugiPortWeightLoadTest.WeightReader_LoadsKintsugiBinIfPresent),
    ("Kintsugi-port BatchNorm1d identity with unit gamma zero beta", KintsugiPortAdvancedPrimitiveTests.BatchNorm1d_IdentityWithUnitGammaZeroBeta),
    ("Kintsugi-port BatchNorm1d applies gamma shift beta", KintsugiPortAdvancedPrimitiveTests.BatchNorm1d_AppliesGammaShiftBeta),
    ("Kintsugi-port BatchNorm1d subtracts running mean", KintsugiPortAdvancedPrimitiveTests.BatchNorm1d_SubtractsRunningMean),
    ("Kintsugi-port AdaLN identity when emb projects to zero", KintsugiPortAdvancedPrimitiveTests.AdaLN_IdentityWhenEmbProjectsToZero),
    ("Kintsugi-port AdaLN gate is broadcasted across tokens", KintsugiPortAdvancedPrimitiveTests.AdaLN_GateIsBroadcastedAcrossTokens),
    ("Kintsugi-port MultiHeadAttention split-QKV via separate weights", KintsugiPortAdvancedPrimitiveTests.MultiHeadAttention_SplitQkvViaSeparateWeights),
    ("Kintsugi-port BallQuery picks all in radius", KintsugiPortAdvancedPrimitiveTests.BallQuery_PicksAllInRadius),
    ("Kintsugi-port BallQuery caps at nsample", KintsugiPortAdvancedPrimitiveTests.BallQuery_CapsAtNsample),
    ("Kintsugi-port BallQuery no-neighbours padding safe", KintsugiPortAdvancedPrimitiveTests.BallQuery_NoNeighboursFillsPaddingSafely),
    ("Kintsugi-port encoder loads weights for all three SA layers", KintsugiPortEncoderParityTests.Encoder_LoadsWeightsForAllThreeSALayers),
    ("Kintsugi-port encoder SA1 output shape matches reference", KintsugiPortEncoderParityTests.Encoder_SA1_OutputShapeMatchesReference),
    ("Kintsugi-port encoder SA2 output shape matches reference", KintsugiPortEncoderParityTests.Encoder_SA2_OutputShapeMatchesReference),
    ("Kintsugi-port encoder SA3 output shape matches reference", KintsugiPortEncoderParityTests.Encoder_SA3_OutputShapeMatchesReference),
    ("Kintsugi-port encoder SA1 no NaN or Inf", KintsugiPortEncoderParityTests.Encoder_SA1_NoNanOrInf),
    ("Kintsugi-port encoder final features no NaN or Inf", KintsugiPortEncoderParityTests.Encoder_FinalFeatures_NoNanOrInf),
    ("Kintsugi-port encoder SA1 L-inf deviation report", KintsugiPortEncoderParityTests.Encoder_SA1_LInfDeviationReport),
    ("Kintsugi-port encoder SA2 L-inf deviation report", KintsugiPortEncoderParityTests.Encoder_SA2_LInfDeviationReport),
    ("Kintsugi-port encoder SA3 L-inf deviation report", KintsugiPortEncoderParityTests.Encoder_SA3_LInfDeviationReport),
    ("Kintsugi-port encoder final_features L-inf deviation report", KintsugiPortEncoderParityTests.Encoder_FinalFeatures_LInfDeviationReport),
    ("Kintsugi-port encoder full chain under 10s", KintsugiPortEncoderParityTests.Encoder_FullChain_Under2Seconds),
    ("Kintsugi-port denoiser audit weight coverage", KintsugiPortDenoiserParityTests.Denoiser_AuditWeightCoverage),
    ("Kintsugi-port denoiser loads all weights", KintsugiPortDenoiserParityTests.Denoiser_LoadsAllWeights),
    ("Kintsugi-port denoiser forward smoke runs without error", KintsugiPortDenoiserParityTests.Denoiser_ForwardSmokeRunsWithoutError),
    ("Kintsugi-port denoiser layer0 L-inf deviation report", KintsugiPortDenoiserParityTests.Denoiser_Layer0_LInfDeviationReport),
    ("Kintsugi-port denoiser single forward time", KintsugiPortDenoiserPerfTests.Perf_Denoiser_SingleForward_Time),
    ("Kintsugi-port GPU availability report", KintsugiPortGpuTests.Gpu_Availability_Report),
    ("Kintsugi-port GPU matmul matches CPU to tolerance", KintsugiPortGpuTests.Gpu_Matmul_MatchesCpuToTolerance),
    ("Kintsugi-port GPU matmul speed report 512^3", KintsugiPortGpuTests.Gpu_Matmul_SpeedReport_512Cubed),
    ("Kintsugi-port TorchSharp libtorch available report", KintsugiPortTorchSharpTests.TorchSharp_LibtorchAvailable_Report),
    ("Kintsugi-port TorchSharp encoder path loads weights", KintsugiPortTorchSharpTests.TorchSharp_EncoderPathLoadsWeights),
    ("Kintsugi-port verifier audit weight coverage", KintsugiPortVerifierTests.Verifier_AuditWeightCoverage),
    ("Kintsugi-port verifier loads all weights", KintsugiPortVerifierTests.Verifier_LoadsAllWeights),
    ("Kintsugi-port verifier forward smoke", KintsugiPortVerifierTests.Verifier_ForwardSmoke),
    ("Kintsugi-port scheduler alpha_bar monotone decreasing", KintsugiPortVerifierTests.DiffusionScheduler_AlphaBarMonotonicallyDecreasing),
    ("Kintsugi-port scheduler set_timesteps 20 is descending", KintsugiPortVerifierTests.DiffusionScheduler_SetTimesteps20IsDescending),
    ("Kintsugi-port scheduler step produces finite output", KintsugiPortVerifierTests.DiffusionScheduler_StepProducesFiniteOutput),
    ("Kintsugi-port inference end-to-end runs without error", KintsugiPortVerifierTests.Inference_EndToEndRunsWithoutError),
    ("Kintsugi-port VQ find codebook tensor", KintsugiPortVqTests.Vq_FindCodebookTensor),
    ("Kintsugi-port Breaking Bad loads sample", KintsugiPortBreakingBadTest.BreakingBad_LoadsSample),
    // SKIP ("Kintsugi-port Breaking Bad run inference and report scores", KintsugiPortBreakingBadTest.BreakingBad_RunInferenceAndReportScores),
    ("Kintsugi-port denoiser_v2 all layers L-inf deviation report", KintsugiPortDenoiserLayerParityTests.DenoiserV2_AllLayersLInfDeviationReport),
    ("Kintsugi-port denoiser layer 0 sub-block L-inf", KintsugiPortDenoiserSubBlockParityTests.DenoiserLayer0_SubBlockLInf),
    ("Kintsugi-port verifier_v2 all layers L-inf", KintsugiPortVerifierLayerParityTests.VerifierV2_AllLayersLInf),
    ("Kintsugi-port parity input point cloud roundtrips through fixture", KintsugiPortParityTests.Parity_InputPointCloud_RoundtripsThroughFixture),
    ("Kintsugi-port parity noisy poses have unit quaternions", KintsugiPortParityTests.Parity_NoisyPoses_HasUnitQuaternions),
    ("Kintsugi-port parity encoder SA1 output present and shaped", KintsugiPortParityTests.Parity_Encoder_SA1Output_PresentAndShaped),
    ("Kintsugi-port parity encoder SA2 output present and shaped", KintsugiPortParityTests.Parity_Encoder_SA2Output_PresentAndShaped),
    ("Kintsugi-port parity encoder SA3 output present and shaped", KintsugiPortParityTests.Parity_Encoder_SA3Output_PresentAndShaped),
    ("Kintsugi-port parity denoiser layer 0 present and shaped", KintsugiPortParityTests.Parity_Denoiser_Layer0_PresentAndShaped),
    ("Kintsugi-port parity denoiser layer 5 present and shaped", KintsugiPortParityTests.Parity_Denoiser_Layer5_PresentAndShaped),
    ("Kintsugi-port parity denoiser final residual present and shaped", KintsugiPortParityTests.Parity_Denoiser_FinalResidual_PresentAndShaped),
    ("Kintsugi-port parity verifier logit and score consistent", KintsugiPortParityTests.Parity_Verifier_LogitAndScore_Consistent),
    ("Kintsugi-port parity encoder SA1 shape matches upstream", KintsugiPortParityTests.Parity_Encoder_SA1OutputShape_MatchesUpstream),
    ("Kintsugi-port parity encoder SA2 shape matches upstream", KintsugiPortParityTests.Parity_Encoder_SA2OutputShape_MatchesUpstream),
    ("Kintsugi-port parity encoder SA3 shape matches upstream", KintsugiPortParityTests.Parity_Encoder_SA3OutputShape_MatchesUpstream),
    ("Kintsugi-port parity encoder SA1 magnitude regression", KintsugiPortParityTests.Parity_Encoder_SA1MagnitudeRegression),
    ("Kintsugi-port parity encoder final features shape", KintsugiPortParityTests.Parity_Encoder_FinalFeaturesShape_MatchesUpstream),
    ("Kintsugi-port parity denoiser all layers consistent shape", KintsugiPortParityTests.Parity_Denoiser_AllLayersConsistentShape),
    ("Kintsugi-port parity denoiser output dim is embed_dim", KintsugiPortParityTests.Parity_Denoiser_OutputDimensionalityIsEmbedDim),
    ("Kintsugi-port parity denoiser token count matches N*L", KintsugiPortParityTests.Parity_Denoiser_TokenCountMatchesNxL),
    ("Kintsugi-port perf FPS N=1000 K=256 under 200ms", KintsugiPortPerformanceTests.Perf_Fps_N1000K256_Under50ms),
    ("Kintsugi-port perf Matmul 512^3 under 500ms", KintsugiPortPerformanceTests.Perf_Matmul_512x512x512_Under100ms),
    ("Kintsugi-port perf LayerNorm 500x512 under 100ms", KintsugiPortPerformanceTests.Perf_LayerNorm_500x512_Under20ms),
    ("Kintsugi-port stability Matmul deterministic across runs", KintsugiPortPerformanceTests.Stability_Matmul_DeterministicAcrossRuns),
    ("Kintsugi-port stability FPS deterministic with fixed seed", KintsugiPortPerformanceTests.Stability_Fps_DeterministicWithFixedSeed),
    ("Kintsugi-port stability LayerNorm deterministic across runs", KintsugiPortPerformanceTests.Stability_LayerNorm_DeterministicAcrossRuns),
    ("Kintsugi-port stability LayerNorm no NaN on typical input", KintsugiPortPerformanceTests.Stability_LayerNorm_NoNanOnTypicalInput),
    ("Kintsugi-port stability LayerNorm handles zero variance", KintsugiPortPerformanceTests.Stability_LayerNorm_HandlesZeroVarianceInput),
    ("Kintsugi-port stability Gelu no NaN on large magnitudes", KintsugiPortPerformanceTests.Stability_Gelu_NoNanOnLargeMagnitudes),
    ("Kintsugi-port stability FFN chain deterministic and bounded", KintsugiPortPerformanceTests.Stability_TransformerFfnLikeChain_DeterministicAndBounded),
    ("Bridge BlockCutOpt SolveAndExtract returns all blocks", QuarryBridgeTests.SolveAndExtract_EmptyFractures_ReturnsAllBlocks),
    ("Bridge BenchBlockSlabBuilder one block produces slabs", QuarryBridgeTests.BenchBlockSlabBuilder_OneBlock_ProducesSlabs),
    ("Bridge SlabYieldOptimizer picks longest axis", QuarryBridgeTests.SlabYieldOptimizer_PicksLongestAxis),
    ("Bridge BilletCutter 10cm splits one meter into ten", QuarryBridgeTests.BilletCutter_TenCm_SplitsOneMeterIntoTen),
    ("Bridge GeoPack crack graph from fracture planes", QuarryBridgeTests.GeoPack_CrackGraph_FromFracturePlanes),
    ("Bridge GeoPack block graph splits bench in two", QuarryBridgeTests.GeoPack_BlockGraph_OnePlaneSplitsBenchIntoTwo),
    ("Bridge GeoPack candidates to inventory roundtrip", QuarryBridgeTests.GeoPack_Candidates_ToInventoryRoundtrip),
    ("Bridge BestFit rubble places varied-height slabs", QuarryBridgeTests.BestFit_RubbleMode_PlacesVariedHeightSlabs),
    ("Integration Quarry → Masonry full pipeline ends in packed assembly", QuarryToMasonryIntegrationTests.Quarry_To_Masonry_FullPipeline_EndsInPackedAssembly),
    ("Monument sampler has 24 rotations", MonumentPackingTests.Sampler_Has24Rotations),
    ("Monument sampler rotations are proper (det=+1)", MonumentPackingTests.Sampler_AllRotationsAreProper),
    ("Monument sampler rotated AABB of cube is cube", MonumentPackingTests.Sampler_RotatedAabbOfCubeIsCube),
    ("Monument sampler rotated AABB of 1x2x3 box has 6 permutations", MonumentPackingTests.Sampler_RotatedAabbOfElongatedBoxSwapsAxes),
    ("Monument packer one-cell one-monument places it", MonumentPackingTests.BenchMonumentPacker_OneCellOneFittingMonument_Placed),
    ("Monument packer monument too large stays unplaced", MonumentPackingTests.BenchMonumentPacker_MonumentTooLarge_Unplaced),
    ("Monument packer fracture splits bench monument respects it", MonumentPackingTests.BenchMonumentPacker_FractureSplitsBench_MonumentInOneCellOnly),
    ("DLBF 3D single-size floor-only fills floor", Dlbf3dMixedSizePackerTests.Dlbf3D_SingleSize_FloorOnly_FillsFloor),
    ("DLBF 3D single-size stacked fills volume", Dlbf3dMixedSizePackerTests.Dlbf3D_SingleSize_Stacked_FillsVolume),
    ("DLBF 3D heterogeneous heights all placed", Dlbf3dMixedSizePackerTests.Dlbf3D_HeterogeneousHeights_AllPlaced),
    ("DLBF 3D prefers higher revenue per volume", Dlbf3dMixedSizePackerTests.Dlbf3D_PrefersHigherRevenuePerVolume),
    ("DLBF 3D forbidden column leaves hole", Dlbf3dMixedSizePackerTests.Dlbf3D_ForbiddenColumn_LeavesHole),
    ("DLBF 3D oversized piece stays unplaced", Dlbf3dMixedSizePackerTests.Dlbf3D_OversizedPiece_StaysUnplaced),
    // Sequencing — Kim 2024 polygonal-masonry installation-order DAG (pure managed)
    ("sequencing Geom Orient basic cases", SequencingTests.Geom_Orient_BasicCases),
    ("sequencing Geom SignedArea CCW positive", SequencingTests.Geom_SignedArea_CcwIsPositive),
    ("sequencing Geom PointInRing inside and outside", SequencingTests.Geom_PointInRing_InsideAndOutside),
    ("sequencing Geom ChainIsMonotone accepts x and vertical y", SequencingTests.Geom_ChainIsMonotone_AcceptsXAndVerticalY),
    ("sequencing Geom RingCentroid of unit square is centre", SequencingTests.Geom_RingCentroid_OfUnitSquareIsCentre),
    ("sequencing PSLG unit square has one bounded face", SequencingTests.Pslg_UnitSquare_HasOneBoundedFace),
    ("sequencing PSLG two squares sharing edge T-junction split", SequencingTests.Pslg_TwoSquaresSharingEdge_TJunctionSplit),
    ("sequencing Kahn linear chain assigns descending depth", SequencingTests.Kahn_LinearChain_AssignsDescendingDepth),
    ("sequencing Kahn transitive edge invariance", SequencingTests.Kahn_TransitiveEdgeInvariance),
    ("sequencing Kahn diamond graph max depth at source", SequencingTests.Kahn_DiamondGraph_MaxDepthAtSource),
    ("sequencing Wall two chains three band has one stone", SequencingTests.Wall_TwoChainThreeBandWall_HasOneStone),
    ("sequencing Wall chains with vertical connectors is acyclic", SequencingTests.Wall_ChainsWithVerticalConnectors_IsAcyclic),
    ("sequencing Wall RemoveRegions excludes from plan", SequencingTests.Wall_RemoveRegions_ExcludesFromPlan),
    ("sequencing Wall rule (8) basic horizontal does not throw", SequencingTests.Wall_RuleEight_CycleDetection),
    // GH PolygonalMasonrySequenceComponent metadata (Rhino runtime SKIP without Grasshopper)
    ("GH PolygonalMasonrySequence metadata (Rhino)", PolygonalMasonrySequenceComponentTests.Metadata_IsExpectedValues),
    // Wall3D — 3D extension of the sequencing algorithm (structural, no Voronoi kernel)
    ("Wall3D tower four stacked orders bottom to top", Wall3DTests.Tower_FourStacked_OrdersBottomToTop),
    ("Wall3D side by side same z produces no edge", Wall3DTests.SideBySide_SameZ_NoEdge),
    ("Wall3D pyramid one on four installs top last", Wall3DTests.Pyramid_OneOnFour_TopInstallsLast),
    ("Wall3D RemoveCells skips removed from plan", Wall3DTests.RemoveCells_SkipsRemovedFromPlan),
    ("Wall3D NormaliseAdjacency dedups and drops self-loops", Wall3DTests.NormaliseAdjacency_DedupAndSelfLoops),
    ("Wall3D unbounded cell not installable", Wall3DTests.UnboundedCell_NotInstallable),
    ("Wall3D duplicate cell id throws", Wall3DTests.DuplicateCellId_Throws),
    // GH PolygonalMasonrySequence3DComponent metadata (Rhino runtime SKIP without Grasshopper)
    ("GH PolygonalMasonrySequence3D metadata (Rhino)", PolygonalMasonrySequence3DComponentTests.Metadata_IsExpectedValues),
    ("GH BoxToMesh metadata (Rhino)", BoxToMeshComponentTests.Metadata_IsExpectedValues),
    ("GH CSV Parts Reader metadata (Rhino)", CsvPartsReaderComponentTests.Metadata_IsExpectedValues),
    // Rubble Wall Settle — Z-up concave-aware rubble settle Core port (Rhino runtime SKIP without rhcommon_c)
    ("RubbleWallSettle all stones placed order preserved (Rhino)", RubbleWallSettleTests.Settle_AllStonesPlaced_OrderPreserved),
    ("RubbleWallSettle no two placed stones interpenetrate (Rhino)", RubbleWallSettleTests.Settle_NoTwoPlacedStonesInterpenetrate),
    ("RubbleWallSettle stability flags match clearance (Rhino)", RubbleWallSettleTests.Settle_StabilityFlagsComputed_MatchClearance),
    ("RubbleWallSettle deterministic two runs identical (Rhino)", RubbleWallSettleTests.Settle_Deterministic_TwoRunsIdentical),
    ("RubbleWallSettle stability-aware places all and is safer (Rhino)", RubbleWallSettleTests.Settle_StabilityAware_PlacesAllAndIsSafer),
    ("RubbleWallSettle empty input returns empty", RubbleWallSettleTests.Settle_EmptyInput_ReturnsEmpty),
    // P0 hygiene 2026-05-29 — repo-wide GUID uniqueness guard (catches the LoadE57/ReadLas-type collision)
    ("all GH component GUIDs are globally unique (Rhino)", ComponentGuidUniquenessTests.AllGhComponents_HaveUniqueGuids),
    ("new output-branch component GUIDs parse + unique (Rhino)", ComponentGuidUniquenessTests.NewOutputBranchComponents_GuidsParseAndAreUnique),
    // Lab-gating preservation contract 2026-05-30 (v1_consolidated_plan §0.1 + §4.3 item 4)
    ("Lab-gating preserves source/icon/GUID round-trip (Rhino)", LabGatingRoundtripTests.EveryLabGatedGuid_PreservesSourceIconAndGuid),
    // Sculpt branch — digital pointing-machine math (pure managed)
    ("SculptureFitter factor mode is uniform", SculptureFitterTests.EnlargeFactors_Factor_AllEqual),
    ("SculptureFitter target-longest scales by longest axis", SculptureFitterTests.EnlargeFactors_TargetLongest_ScalesByLongestAxis),
    ("SculptureFitter target-height scales by Z", SculptureFitterTests.EnlargeFactors_TargetHeight_ScalesByZ),
    ("SculptureFitter non-uniform per-axis factors", SculptureFitterTests.EnlargeFactors_NonUniform_PerAxis),
    ("SculptureFitter small piece fits with clearance", SculptureFitterTests.FitsInBlock_Fits_PositiveClearance),
    ("SculptureFitter oversized piece does not fit", SculptureFitterTests.FitsInBlock_TooBig_DoesNotFit),
    ("SculptureFitter margin can push out of fit", SculptureFitterTests.FitsInBlock_Margin_PushesOutOfFit),
    ("SculptureFitter matches best axis orientation", SculptureFitterTests.FitsInBlock_MatchesBestAxisOrientation),
    // Fabricate branch — stone-cut metadata (pure managed)
    ("StoneCutMetadata always emits schema", StoneCutMetadataTests.ToUserStrings_AlwaysEmitsSchema),
    ("StoneCutMetadata skips unset fields", StoneCutMetadataTests.ToUserStrings_SkipsUnsetFields),
    ("StoneCutMetadata emits populated fields", StoneCutMetadataTests.ToUserStrings_EmitsPopulatedFields),
    // Fabricate flagship — staggered running-bond layout (pure managed)
    ("StaggeredBlockLayout produces two courses", StaggeredBlockLayoutTests.Build_ProducesTwoCourses),
    ("StaggeredBlockLayout odd course is staggered", StaggeredBlockLayoutTests.Build_OddCourseIsStaggered),
    ("StaggeredBlockLayout cells stay within box", StaggeredBlockLayoutTests.Build_CellsStayWithinBox),
    ("StaggeredBlockLayout depth axis spans full", StaggeredBlockLayoutTests.Build_DepthAxisSpansFull),
    ("StaggeredBlockLayout invalid course height throws", StaggeredBlockLayoutTests.Build_InvalidCourseHeight_Throws),
    // Photogrammetry — marker / GCP CSV parsing (pure managed)
    ("MarkerFileReader world-only 4 columns", MarkerFileReaderTests.Parse_WorldOnly_FourColumns),
    ("MarkerFileReader world+model 7 columns", MarkerFileReaderTests.Parse_WorldAndModel_SevenColumns),
    ("MarkerFileReader numeric-first auto labels", MarkerFileReaderTests.Parse_NumericFirst_AutoLabels),
    ("MarkerFileReader skips comments and blanks", MarkerFileReaderTests.Parse_SkipsCommentsAndBlanks),
    ("MarkerFileReader too-few-columns skipped", MarkerFileReaderTests.Parse_TooFewColumns_Skipped),
    ("MarkerFileReader semicolon and tab separators", MarkerFileReaderTests.Parse_SemicolonAndTabSeparators),
    // Sculpt — carving-pass offset schedule (pure managed)
    ("CarvingStages schedule descends max->finish", CarvingStagesTests.OffsetSchedule_DescendsMaxToFinish),
    ("CarvingStages single stage is finish", CarvingStagesTests.OffsetSchedule_SingleStage_IsFinish),
    ("CarvingStages respects finish allowance", CarvingStagesTests.OffsetSchedule_RespectsFinishAllowance),
    ("CarvingStages invalid stages throws", CarvingStagesTests.OffsetSchedule_InvalidStages_Throws),
    ("CarvingStages max below finish throws", CarvingStagesTests.OffsetSchedule_MaxBelowFinish_Throws),
    // Fabricate — weight + lift class (pure managed)
    ("FabricationReport weight = volume x density", FabricationReportTests.WeightKg_VolumeTimesDensity),
    ("FabricationReport lift-class thresholds", FabricationReportTests.Classify_Thresholds),
    ("FabricationReport lift-class boundaries", FabricationReportTests.Classify_Boundaries),
    ("FabricationReport default granite density", FabricationReportTests.Classify_DefaultDensityConstant),
    // MatcherRegistry substrate + HungarianAssigner (2026-05-31 nightshift)
    ("Hungarian empty matrix → empty", HungarianAssignerTests.Solve_Empty_ReturnsEmpty),
    ("Hungarian 1×1 → row 0 → col 0", HungarianAssignerTests.Solve_OneByOne_ReturnsZero),
    ("Hungarian identity 3×3 → diagonal", HungarianAssignerTests.Solve_Identity3x3_PicksDiagonal),
    ("Hungarian anti-diagonal 3×3", HungarianAssignerTests.Solve_AntiDiagonal3x3_PicksAntiDiagonal),
    ("Hungarian 2×4 (more supply)", HungarianAssignerTests.Solve_2x4_PicksTwoBestCols),
    ("Hungarian 4×2 leaves 2 unassigned", HungarianAssignerTests.Solve_4x2_LeavesTwoUnassigned),
    ("Hungarian all-infeasible → all unassigned", HungarianAssignerTests.Solve_AllInfeasible_AllUnassigned),
    ("Hungarian partial feasibility routing", HungarianAssignerTests.Solve_PartialFeasibility_RoutesAroundInfeasible),
    ("Hungarian null cost throws", HungarianAssignerTests.Solve_NullCost_Throws),
    ("Hungarian bad dimensions throws", HungarianAssignerTests.Solve_BadDimensions_Throws),
    ("Hungarian deterministic", HungarianAssignerTests.Solve_Deterministic_SameInputSameOutput),
    ("ConstraintDictionary numeric >= ops", MatcherRegistryTests.ConstraintDictionary_NumericGe_PassesAndFails),
    ("ConstraintDictionary categorical == ops", MatcherRegistryTests.ConstraintDictionary_CategoricalEq_PassesAndFails),
    ("ConstraintDictionary unknown op throws", MatcherRegistryTests.ConstraintDictionary_UnknownOperator_Throws),
    ("IncidenceMatrix tabulates feasibility", MatcherRegistryTests.IncidenceMatrix_Build_TabulatesFeasibility),
    ("WeightMatrix NaN where infeasible", MatcherRegistryTests.WeightMatrix_Build_NaNWhereInfeasible),
    ("MatcherRegistry register + run", MatcherRegistryTests.MatcherRegistry_RegisterAndRun),
    ("MatcherRegistry unknown solver throws", MatcherRegistryTests.MatcherRegistry_UnknownSolver_Throws),
    ("End-to-end voussoir-shape Hungarian", MatcherRegistryTests.EndToEnd_TwoVoussoirsTwoStones_OptimalAssignment),
    // 2026-06-03 - exact NFP Bottom-Left-Fill solver (zero-overlap guarantee, Rhino)
    ("NFP-BLF places parts with zero overlap (Rhino)", NfpBlfPackingTests.Pack_PlacesParts_NoOverlap),
    ("NFP-BLF respects holes with zero overlap (Rhino)", NfpBlfPackingTests.Pack_RespectsHole_NoOverlap),
    // 2026-06-03 - NFP-BLF foundation, pure managed (runs headless, no Rhino)
    ("NFP-BLF Minkowski NFP of two unit squares is [-1,1]^2 area 4", Clipper2AdapterTests.MinkowskiSum_TwoUnitSquares_NfpIsExpected),
    ("NFP-BLF place-at-BL-vertex is overlap-free by construction", Clipper2AdapterTests.NfpBlf_PlaceAtBottomLeftVertex_NoOverlapByConstruction),
    ("NFP-BLF InflateLoops grows and shrinks area", Clipper2AdapterTests.InflateLoops_GrowsAndShrinks),
    // 2026-06-04 - V3 evolution batch 1: numeric hygiene (roadmap #1/#2) + Hungarian big-M fix
    ("GeometryNumerics recenter far-from-origin -> centroid ~0", NumericsAndHungarianTests.Recenter_FarFromOrigin_CentroidNearZero),
    ("GeometryNumerics scale-relative epsilon scales", NumericsAndHungarianTests.ScaleRelativeEpsilon_Scales),
    ("GeometryNumerics safe integer scale avoids int64 overflow", NumericsAndHungarianTests.SafeIntegerScale_NoOverflow),
    ("GeometryNumerics tolerance budget from one source", NumericsAndHungarianTests.ToleranceBudget_OneSource),
    ("Hungarian rectangular optimal with big-M padding", NumericsAndHungarianTests.Hungarian_Rectangular_OptimalWithPadding),
    ("Hungarian dense-infeasible large-cost finds unique feasible", NumericsAndHungarianTests.Hungarian_DenseInfeasible_LargeCosts_FindsUniqueFeasible),
    ("Hungarian all-infeasible row is Unassigned", NumericsAndHungarianTests.Hungarian_InfeasibleRow_Unassigned),
    ("SpatialHash3D radius query matches brute force (sorted)", NumericsAndHungarianTests.SpatialHash_RadiusQuery_MatchesBruteForce),
    // 2026-06-04 - V3 evolution batch 3: BlockCutOpt pose-grid parallelisation (deterministic)
    ("BlockCutOpt parallel matches serial and is faster (DFN)", BlockCutOptParallelTests.Parallel_MatchesSerial_AndIsFaster),

    // 2026-06-04 - GPR processing chain port (RadargramProcessor + FractureExtractor + Fft)
    ("GPR Fft forward/inverse round-trips", RadargramProcessingTests.Fft_ForwardInverse_RoundTrips),
    ("GPR Hilbert energy of cosine is flat", RadargramProcessingTests.HilbertEnergy_Cosine_FlatEnvelope),
    ("GPR chain extracts planted reflector at depth", RadargramProcessingTests.Chain_PlantedReflector_ExtractedAtCorrectDepth),
    ("GPR Stolt migration concentrates diffraction", RadargramProcessingTests.StoltMigration_Diffraction_ConcentratesEnergy),
    ("GPR real marble end-to-end (if present)", RadargramProcessingTests.RealMarble_EndToEnd_IfPresent),

    // 2026-06-04 - deferred earthworks roadmap: TinPeelFilter (A2), TinMerge (A3), BedrockSurface (A9)
    ("TinPeel removes cap spike", EarthworksTinTests.TinPeel_RemovesCapSpike),
    ("TinPeel drops tiny component", EarthworksTinTests.TinPeel_DropsTinyComponent),
    ("TinMerge IDW recovers a plane", EarthworksTinTests.TinMerge_IdwRecoversPlane),
    ("TinMerge flags out-of-range targets", EarthworksTinTests.TinMerge_FlagsOutOfRange),
    ("Bedrock deepest-per-column + datum shift", EarthworksTinTests.Bedrock_DeepestPerColumn_DatumShift),
    ("Bedrock to common TIN end-to-end", EarthworksTinTests.Bedrock_ToCommonTin_EndToEnd),

    // 2026-06-04 - FractureTracer: picks -> continuous fracture lines (both modes)
    ("Tracer single planted line", FractureTracerTests.Trace_SinglePlantedLine_OneLine),
    ("Tracer short reflector dropped", FractureTracerTests.Trace_ShortReflector_Dropped),
    ("Tracer two separated lines (both modes)", FractureTracerTests.Trace_TwoSeparatedLines_Two),
    ("Tracer dip recovered", FractureTracerTests.Trace_DipRecovered),
    ("Tracer crossing connected-vs-orientation", FractureTracerTests.Trace_Crossing_ConnectedMergesOrientationSeparates),
    ("Surface loft horizontal -> flat", FractureTracerTests.Loft_HorizontalLine_FlatSurface),
    ("Surface loft across grid", FractureTracerTests.LoftAcrossLines_Grid_Surface),

    // 2026-06-05 - W1 keep-or-cut: audience report composer (engineer/artist/geologist)
    ("AudienceReport engineer refuses without CRS", AudienceReportTests.Engineer_NoCrs_Refused),
    ("AudienceReport engineer with CRS releases", AudienceReportTests.Engineer_WithCrs_NotRefused),
    ("AudienceReport artist flags grain/vein unknown", AudienceReportTests.Artist_AddsGrainVeinCaveat),
    ("AudienceReport geologist flags rock-mass worksheet", AudienceReportTests.Geologist_FlagsRockMassWorksheet),
    ("AudienceReport sections route by audience", AudienceReportTests.Sections_RoutedByAudience),
    ("AudienceReport FrahanReport numerics become rows", AudienceReportTests.FrahanReport_NumericsBecomeRows),
    ("AudienceReport CSV is well-formed", AudienceReportTests.Csv_IsWellFormed),

    // 2026-06-06 - NFP-BLF SLM evolution (multi-start + compaction + reinsertion + concave overlap-verify)
    ("NFP-BLF evolved concave pack is 0-overlap", NfpBlfEvolutionTests.Evolved_ConcaveParts_ZeroOverlap),
    ("NFP-BLF evolved is deterministic", NfpBlfEvolutionTests.Evolved_IsDeterministic),
    ("NFP-BLF legacy flags-off is deterministic", NfpBlfEvolutionTests.Legacy_FlagsOff_IsDeterministic),
    ("NFP-BLF multi-start ran", NfpBlfEvolutionTests.Evolved_MultiStart_Ran),

    // 2026-06-07 - Voussoir cell generators (arch + pendentive vault geometry front end)
    ("VoussoirCellFactory arch semicircular 11 closed cells", VoussoirCellFactoryTests.Arch_Semicircular_Produces11ClosedCells),
    ("VoussoirCellFactory arch semicircular total volume in band", VoussoirCellFactoryTests.Arch_Semicircular_TotalVolume_InBand),
    ("VoussoirCellFactory arch springers on ground + X span", VoussoirCellFactoryTests.Arch_Semicircular_SpringersOnGround),
    ("VoussoirCellFactory arch keystone index is central", VoussoirCellFactoryTests.Arch_KeystoneIndex_IsCentral),
    ("VoussoirCellFactory arch all profiles build closed cells", VoussoirCellFactoryTests.Arch_AllProfiles_BuildClosedCells),
    ("VoussoirCellFactory arch base point translates", VoussoirCellFactoryTests.Arch_BasePoint_Translates),
    ("VoussoirCellFactory pendentive 36 closed cells", VoussoirCellFactoryTests.Pendentive_Produces36ClosedCells),
    ("VoussoirCellFactory pendentive drop-to-ground min Z ~0", VoussoirCellFactoryTests.Pendentive_DropToGround_MinZ_NearZero),
    ("VoussoirCellFactory pendentive rejects corners off sphere", VoussoirCellFactoryTests.Pendentive_RejectsCornersOffSphere),
    ("VoussoirCellFactory assembly records ready for matcher", VoussoirCellFactoryTests.Assembly_RecordsReadyForMatcher),

    // TEMPORARY (revert before commit) — KB-10 local registration; the
    // orchestrator integrates the final tuples.
    ("kb10 6x4 exact-joint wall certifies (was SolverError)", Kb10ExactJointConditioningTests.Wall6x4_ExactJoints_CertifiesWithoutSolverError),
    ("kb10 exact-joint wall sweep 6x4/8x5/10x6 reports verdict+ms", Kb10ExactJointConditioningTests.WallSweep_ExactJoints_NoSolverError)
};

// TEMPORARY (revert before commit) — KB-10 dev-only name filter so the fix can
// be iterated without running the full battery: set FRAHAN_TEST_FILTER=kb10.
var tempFilter = Environment.GetEnvironmentVariable("FRAHAN_TEST_FILTER");
if (!string.IsNullOrEmpty(tempFilter))
    tests = tests.FindAll(t => t.Name.IndexOf(tempFilter, StringComparison.OrdinalIgnoreCase) >= 0);

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex) when (IsNativeRhinoException(ex))
    {
        // Mesh construction requires rhcommon_c.dll, and GH_Component instantiation
        // requires Grasshopper.dll - both only available inside a live Rhino process.
        Console.WriteLine($"SKIP {test.Name} (requires Rhino/Grasshopper runtime)");
        // 2026-05-04 - log the actual exception so the SKIP is debuggable. The
        // count-grep parsers ("grep -c ^SKIP") still see the SKIP line above and
        // are unaffected; the indented diagnostic lines below do not start with
        // SKIP / PASS / FAIL, so they do not perturb the gate counters.
        Console.WriteLine($"        type:    {ex.GetType().FullName}");
        Console.WriteLine($"        message: {ex.Message}");
        if (ex is System.IO.FileNotFoundException fnf && !string.IsNullOrEmpty(fnf.FileName))
            Console.WriteLine($"        file:    {fnf.FileName}");
        if (ex is System.TypeLoadException tle && !string.IsNullOrEmpty(tle.TypeName))
            Console.WriteLine($"        type:    {tle.TypeName}");
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
            Console.WriteLine($"        inner:   {inner.GetType().FullName}: {inner.Message}");
    }
    catch (Frahan.Tests.SkipTest skip)
    {
        Console.WriteLine($"SKIP {test.Name} ({skip.Message})");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

static bool IsNativeRhinoException(Exception ex)
{
    for (var e = ex; e != null; e = e.InnerException)
    {
        if (e is System.DllNotFoundException || e is System.BadImageFormatException)
            return true;
        // Grasshopper.dll / GH_IO.dll / RhinoCommon.dll are referenced with
        // Private=false in the test csproj (avoid copying ~50MB into test output).
        // When a test instantiates a GH_Component, the JIT tries to load Grasshopper
        // and throws FileNotFoundException at runtime - treat as SKIP, same as
        // rhcommon_c.dll missing.
        if (e is System.IO.FileNotFoundException fnf)
        {
            var name = fnf.FileName ?? "";
            var msg = fnf.Message ?? "";
            if (name.Contains("Grasshopper") || name.Contains("GH_IO") || name.Contains("RhinoCommon")
                || msg.Contains("Grasshopper") || msg.Contains("GH_IO") || msg.Contains("RhinoCommon"))
                return true;
        }
    }
    return false;
}

if (failed > 0)
{
    Environment.Exit(1);
}

static void PacksSimpleBlocks()
{
    var items = new[]
    {
        new PackItem("a", new Size3(10, 10, 10)),
        new PackItem("b", new Size3(10, 10, 10)),
        new PackItem("c", new Size3(10, 10, 10))
    };
    var result = new GreedyHeightmapPacker().Pack(items, new PackContainer(20, 20, 20), new PackSettings { CellSize = 10 });
    Assert(result.Placements.Count == 3, "expected all blocks to be placed");
    Assert(result.Failures.Count == 0, "expected no failures");
}

static void ReportsFailures()
{
    var items = new[] { new PackItem("too_big", new Size3(30, 30, 30)) };
    var result = new GreedyHeightmapPacker().Pack(items, new PackContainer(20, 20, 20), new PackSettings { CellSize = 10 });
    Assert(result.Placements.Count == 0, "expected no placements");
    Assert(result.Failures.Count == 1, "expected one failure");
}

static void TriesYawRotation()
{
    var items = new[] { new PackItem("rotates", new Size3(30, 10, 10)) };
    var result = new GreedyHeightmapPacker().Pack(items, new PackContainer(10, 30, 20), new PackSettings { CellSize = 10, AllowYaw90 = true });
    Assert(result.Placements.Count == 1, "expected rotated item to fit");
    Assert(Math.Abs(result.Placements[0].YawDegrees - 90) < 1e-9, "expected yaw 90 placement");
}

static void MeshHeightmapFitsIntoIrregularFootprintVoid()
{
    var items = new[]
    {
        CreateLPrism("l", 10, 10),
        CreateBoxMesh("cube", new Vec3(0, 0, 0), new Size3(10, 10, 10))
    };
    var settings = new MeshPackSettings { CellSize = 10, AllowYaw90 = false, SortByVolumeDescending = true };
    var result = new GreedyMeshHeightmapPacker().Pack(items, new PackContainer(20, 20, 10), settings);

    Assert(result.Placements.Count == 2, "expected L prism and cube to fit in the same layer");
    Assert(result.Failures.Count == 0, "expected no mesh-heightmap failures");

    var cubePlacement = result.Placements.Find(p => p.Item.Id == "cube");
    Assert(Math.Abs(cubePlacement.GeometryOrigin.Z) < 1e-9, "expected cube to use the L-shape footprint void at z=0");
}

static void MeshHeightmapReportsVerticalCollisionFailure()
{
    var items = new[]
    {
        CreateBoxMesh("a", new Vec3(0, 0, 0), new Size3(10, 10, 10)),
        CreateBoxMesh("b", new Vec3(0, 0, 0), new Size3(10, 10, 10))
    };
    var settings = new MeshPackSettings { CellSize = 10, AllowYaw90 = false, SortByVolumeDescending = false };
    var result = new GreedyMeshHeightmapPacker().Pack(items, new PackContainer(10, 10, 10), settings);

    Assert(result.Placements.Count == 1, "expected only one cube to fit in one height layer");
    Assert(result.Failures.Count == 1, "expected the second cube to fail containment after collision-safe z solve");
}

static void IrregularMeshContainerRejectsMissingFootprintCells()
{
    var containerMesh = CreateLPrism("container", 10, 10);
    var container = IrregularMeshContainer.FromMesh(containerMesh, 10);
    var items = new[]
    {
        CreateBoxMesh("a", new Vec3(0, 0, 0), new Size3(10, 10, 10)),
        CreateBoxMesh("b", new Vec3(0, 0, 0), new Size3(10, 10, 10)),
        CreateBoxMesh("c", new Vec3(0, 0, 0), new Size3(10, 10, 10)),
        CreateBoxMesh("d", new Vec3(0, 0, 0), new Size3(10, 10, 10))
    };
    var settings = new MeshPackSettings { CellSize = 10, AllowYaw90 = false, SortByVolumeDescending = false };
    var result = new GreedyMeshHeightmapPacker().Pack(items, container, settings);

    Assert(container.IsAllowed(0, 0), "expected L container cell 0,0 to be allowed");
    Assert(container.IsAllowed(1, 0), "expected L container cell 1,0 to be allowed");
    Assert(container.IsAllowed(0, 1), "expected L container cell 0,1 to be allowed");
    Assert(!container.IsAllowed(1, 1), "expected L container missing corner to be unavailable");
    Assert(result.Placements.Count == 3, "expected only three cubes to fit in the L-shaped one-layer container");
    Assert(result.Failures.Count == 1, "expected one cube to fail because the missing container corner is unavailable");
}

static void IrregularMeshContainerUsesTriangularFootprint()
{
    var containerMesh = CreateTriangularPrism("container", 20, 10);
    var container = IrregularMeshContainer.FromMesh(containerMesh, 10);
    var items = new[]
    {
        CreateBoxMesh("a", new Vec3(0, 0, 0), new Size3(10, 10, 10)),
        CreateBoxMesh("b", new Vec3(0, 0, 0), new Size3(10, 10, 10)),
        CreateBoxMesh("c", new Vec3(0, 0, 0), new Size3(10, 10, 10)),
        CreateBoxMesh("d", new Vec3(0, 0, 0), new Size3(10, 10, 10))
    };
    var settings = new MeshPackSettings { CellSize = 10, AllowYaw90 = false, SortByVolumeDescending = false };
    var result = new GreedyMeshHeightmapPacker().Pack(items, container, settings);

    Assert(container.IsAllowed(0, 0), "expected triangular container base cell to be allowed");
    Assert(!container.IsAllowed(1, 1), "expected triangular container upper corner to be unavailable");
    Assert(result.Placements.Count < 4, "expected triangular container to reject at least one cube instead of acting like a rectangular bounding box");
}

static void IrregularMeshContainerSupportsSeparatedVerticalCavities()
{
    var containerMesh = CreateSeparatedCavityContainer("container");
    var container = IrregularMeshContainer.FromMesh(containerMesh, 10);
    var items = new[]
    {
        CreateBoxMesh("a", new Vec3(0, 0, 0), new Size3(10, 10, 10)),
        CreateBoxMesh("b", new Vec3(0, 0, 0), new Size3(10, 10, 10))
    };
    var settings = new MeshPackSettings { CellSize = 10, AllowYaw90 = false, SortByVolumeDescending = false };
    var result = new GreedyMeshHeightmapPacker().Pack(items, container, settings);

    Assert(container.IntervalsAt(0, 0).Count == 2, "expected two separated vertical container intervals");
    Assert(result.Placements.Count == 2, "expected one cube in each separated vertical cavity");
    Assert(Math.Abs(result.Placements[0].GeometryOrigin.Z) < 1e-9, "expected first cube in lower cavity");
    Assert(Math.Abs(result.Placements[1].GeometryOrigin.Z - 20) < 1e-9, "expected second cube raised into upper cavity");
}

static void MeshPackSeedCanExploreAlternatives()
{
    var items = new[]
    {
        CreateBoxMesh("a", new Vec3(0, 0, 0), new Size3(10, 10, 10)),
        CreateBoxMesh("b", new Vec3(0, 0, 0), new Size3(10, 10, 10)),
        CreateBoxMesh("c", new Vec3(0, 0, 0), new Size3(10, 10, 10))
    };
    var settingsA = new MeshPackSettings
    {
        CellSize = 10,
        AllowYaw90 = false,
        SortByVolumeDescending = true,
        Seed = 1,
        RandomTieBreakWeight = 1000
    };
    var settingsB = new MeshPackSettings
    {
        CellSize = 10,
        AllowYaw90 = false,
        SortByVolumeDescending = true,
        Seed = 2,
        RandomTieBreakWeight = 1000
    };

    var resultA = new GreedyMeshHeightmapPacker().Pack(items, new PackContainer(30, 10, 10), settingsA);
    var resultB = new GreedyMeshHeightmapPacker().Pack(items, new PackContainer(30, 10, 10), settingsB);

    Assert(resultA.Placements.Count == 3, "expected seed A to place all cubes");
    Assert(resultB.Placements.Count == 3, "expected seed B to place all cubes");

    var signatureA = PlacementSignature(resultA);
    var signatureB = PlacementSignature(resultB);
    Assert(signatureA != signatureB, "expected different nonzero seeds to produce different placement options");
}

static string PlacementSignature(MeshPackResult result)
{
    var parts = new List<string>();
    foreach (var placement in result.Placements)
    {
        parts.Add($"{placement.Item.Id}:{placement.GeometryOrigin.X:0.###},{placement.GeometryOrigin.Y:0.###},{placement.GeometryOrigin.Z:0.###}");
    }

    return string.Join("|", parts);
}

static MeshPackItem CreateLPrism(string id, double cellSize, double height)
{
    var vertices = new List<Vec3>();
    var triangles = new List<MeshTriangle>();
    AddBox(vertices, triangles, new Vec3(0, 0, 0), new Size3(cellSize, cellSize, height));
    AddBox(vertices, triangles, new Vec3(cellSize, 0, 0), new Size3(cellSize, cellSize, height));
    AddBox(vertices, triangles, new Vec3(0, cellSize, 0), new Size3(cellSize, cellSize, height));
    return new MeshPackItem(id, vertices, triangles);
}

static MeshPackItem CreateBoxMesh(string id, Vec3 min, Size3 size)
{
    var vertices = new List<Vec3>();
    var triangles = new List<MeshTriangle>();
    AddBox(vertices, triangles, min, size);
    return new MeshPackItem(id, vertices, triangles);
}

static MeshPackItem CreateTriangularPrism(string id, double size, double height)
{
    var vertices = new List<Vec3>
    {
        new Vec3(0, 0, 0),
        new Vec3(size, 0, 0),
        new Vec3(0, size, 0),
        new Vec3(0, 0, height),
        new Vec3(size, 0, height),
        new Vec3(0, size, height)
    };
    var triangles = new List<MeshTriangle>
    {
        new MeshTriangle(0, 2, 1),
        new MeshTriangle(3, 4, 5),
        new MeshTriangle(0, 1, 4),
        new MeshTriangle(0, 4, 3),
        new MeshTriangle(1, 2, 5),
        new MeshTriangle(1, 5, 4),
        new MeshTriangle(2, 0, 3),
        new MeshTriangle(2, 3, 5)
    };

    return new MeshPackItem(id, vertices, triangles);
}

static MeshPackItem CreateSeparatedCavityContainer(string id)
{
    var vertices = new List<Vec3>();
    var triangles = new List<MeshTriangle>();
    AddBox(vertices, triangles, new Vec3(0, 0, 0), new Size3(10, 10, 10));
    AddBox(vertices, triangles, new Vec3(0, 0, 20), new Size3(10, 10, 10));
    return new MeshPackItem(id, vertices, triangles);
}

static void AddBox(List<Vec3> vertices, List<MeshTriangle> triangles, Vec3 min, Size3 size)
{
    var start = vertices.Count;
    var max = min + new Vec3(size.Width, size.Depth, size.Height);

    vertices.Add(new Vec3(min.X, min.Y, min.Z));
    vertices.Add(new Vec3(max.X, min.Y, min.Z));
    vertices.Add(new Vec3(max.X, max.Y, min.Z));
    vertices.Add(new Vec3(min.X, max.Y, min.Z));
    vertices.Add(new Vec3(min.X, min.Y, max.Z));
    vertices.Add(new Vec3(max.X, min.Y, max.Z));
    vertices.Add(new Vec3(max.X, max.Y, max.Z));
    vertices.Add(new Vec3(min.X, max.Y, max.Z));

    AddQuad(triangles, start + 0, start + 1, start + 2, start + 3);
    AddQuad(triangles, start + 4, start + 7, start + 6, start + 5);
    AddQuad(triangles, start + 0, start + 4, start + 5, start + 1);
    AddQuad(triangles, start + 1, start + 5, start + 6, start + 2);
    AddQuad(triangles, start + 2, start + 6, start + 7, start + 3);
    AddQuad(triangles, start + 3, start + 7, start + 4, start + 0);
}

static void AddQuad(List<MeshTriangle> triangles, int a, int b, int c, int d)
{
    triangles.Add(new MeshTriangle(a, b, c));
    triangles.Add(new MeshTriangle(a, c, d));
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static class MeshPackPlacementExtensions
{
    public static MeshPackPlacement Find(this IReadOnlyList<MeshPackPlacement> placements, Predicate<MeshPackPlacement> predicate)
    {
        foreach (var placement in placements)
        {
            if (predicate(placement))
            {
                return placement;
            }
        }

        throw new InvalidOperationException("Expected placement was not found.");
    }
}
