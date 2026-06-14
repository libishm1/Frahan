#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.EdgeMatch3D;

// =============================================================================
// CyclopeanRecipeCoursingComponent (GUID D5F1000C)
//
// The bottom-up 3D peer with no 2D analog: encodes the Clifford-McGee 2017
// Cyclopean Cannibalism recipe verbatim. Each input scanned stone is
// classified by largest-inscribed-polygon shape (trapezoid vs parallelogram)
// and placed per the 8-step recipe along a wall envelope.
//
// Recipe verbatim (Clifford & McGee 2017 ACADIA pp. 404-413, SS"RECIPE"):
//   1. Sort the rubble stones into two piles: trapezoids and parallelograms.
//   2. Set a series of trapezoid stones in the stable orientation, evenly
//      spaced along the wall, not adjacent.
//   3. Seek and select a parallelogram stone that fits in the space adjacent
//      to the previously set stones. Nest into place leaning against the
//      stable trapezoid.
//   4. Continue step 3 until there is space for only one stone in each void.
//   5. Select a trapezoid stone and set it upside down into the gap as a
//      keystone-like fill.
//   6. Continue from steps 2-5 on the next rough coursing, straddling the
//      vertical joints below.
//   7. Set the selected stone above the previous course on a stand-off and
//      scribe the geometry of the setting stone onto the bed joint of the
//      course below. Lower until contact on all surfaces; carve the scribed
//      geometry onto the previously set stones. Result: a Utah-shaped stone.
//   8. Lower the offset setting stone onto the custom-carved bed joint
//      below and continue.
//
// Citation: Clifford, B. & McGee, W. *"Cyclopean Cannibalism: A Method
// for Recycling Rubble."* ACADIA 2017, pp. 404-413. Matter Design / MIT /
// U-Michigan / Quarra Stone Co. as realizer; exhibited 2017 Seoul Biennale
// of Architecture and Urbanism.
//
// Status: SKELETON. The classifier + course assembler are stubbed; the
// real recipe body lands per HITL card pass
// `wiki/research/hitl_cards/em_3d_cyclopean_cannibalism/`. Component
// registers IO contract today so the canvas wiring can be authored ahead.
// =============================================================================

[Algorithm("Cyclopean Cannibalism 8-step recipe",
    "Clifford and McGee 2017 ACADIA pp. 404-413 + Clifford 2017 The Cannibal's Cookbook (Matter Publishing)",
    Note = "Verbatim encoding of the recipe; see Frahan philosophy doc SS10.11 for the method-to-Frahan map")]
[Algorithm("Largest 4-sided inscribed polygon",
    "Clifford 2017 The Cannibal's Cookbook pp. 118-120 recursive algorithm",
    Note = "Stage 1 of recipe: stock-polygon extraction from scanned rubble")]
[Algorithm("Variational Shape Approximation (face partitioning)",
    "Cohen-Steiner Alliez Desbrun 2004 SIGGRAPH; Frahan VsaSegmenter (stub)",
    Note = "Face classification: trapezoid vs parallelogram via face-pair internal angles")]
[Algorithm("Heyman 1966 limit-state masonry",
    "Heyman 1966; CoM-over-support-polygon stability gate",
    Note = "Stage 5 + stage 8: stability check at every set")]
[DesignApplication(
    "Reproduce the Clifford-McGee 2017 Cyclopean Cannibalism wall recipe inside Grasshopper.",
    DesignFlow.BottomUp,
    Precedent = "Clifford-McGee 2017 ACADIA Cyclopean Cannibalism; Inka cyclopean masonry tradition (Kachiqhata quarry, Protzen 1993)",
    Tolerance = "wall coverage >= 90 %; all stones with CoM over support; recipe-rule violations = 0",
    CardSet = "wiki/research/hitl_cards/em_3d_cyclopean_cannibalism/")]
public sealed class CyclopeanRecipeCoursingComponent : FrahanComponentBase
{
    public CyclopeanRecipeCoursingComponent()
        : base("Cyclopean Recipe Coursing", "CycRecipe",
            "The bottom-up 3D peer with no 2D analog. Encodes the Clifford-" +
            "McGee 2017 Cyclopean Cannibalism 8-step recipe verbatim. Inputs: " +
            "scanned rubble inventory + wall envelope + variable-thickness " +
            "back-plane + course height. Outputs: placed stones with " +
            "trapezoid/parallelogram/keystone recipe-step tags + Utah-detail " +
            "scribe curves per bed joint + dowel insertion vectors. The 3D " +
            "flagship bottom-up component, mirroring cyclopean masonry " +
            "principles per Libish 2026-05-31 directive.",
            "Frahan", "EdgeMatch")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("D5F1000C-ED9E-4ED9-A00C-ED9EED9E000C");

    // 2026-06-05 (W6, keep-or-cut): hidden from the ribbon. Status is SKELETON
    // (see the class header): the rubble classifier + course assembler are
    // stubbed, so the component cannot produce a real coursing. A skeleton must
    // not sit on the primary ribbon (the "no ghost components" rule). Not
    // Obsolete: it is unbuilt future work. Build target = the Cyclopean
    // Cannibalism rough-coursing recipe (HITL card em_3d_cyclopean_cannibalism);
    // it consumes the BlockChainAlongThrustLine3D walker, so it unblocks only
    // after that lands. Flip back to primary when built out. GUID preserved.
    // See outputs/2026-06-05/keep_or_cut/UNBUILT_COMPONENTS.md.
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override Bitmap Icon => IconProvider.Load("EdgeMatchSolve.png"); // placeholder

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Rubble Inventory", "RI",
            "List of scanned-stone meshes (demolition rubble, quarry off-cuts).",
            GH_ParamAccess.list);
        p.AddBrepParameter("Wall Envelope", "WE",
            "Primary wall Brep (front face). The form the recipe builds against.",
            GH_ParamAccess.item);
        p.AddBrepParameter("Back Plane", "BP",
            "Variable-thickness back-plane offset Brep. The recipe's stones " +
            "extend from Wall Envelope to Back Plane; thickness varies by location.",
            GH_ParamAccess.item);
        p[2].Optional = true;
        p.AddNumberParameter("Course Height", "CH",
            "Average course height (mm). Drives the number of horizontal rows. Default 400 mm.",
            GH_ParamAccess.item, 400.0);
        p.AddNumberParameter("Min Face Area", "MFA",
            "VsaSegmenter min-face-area threshold (mm^2) for shape classification. Default 15,000 mm^2.",
            GH_ParamAccess.item, 15000.0);
        p.AddIntegerParameter("Strategy", "St",
            "0=Greedy recipe, 1=Pareto (NSGA-II three-objective: coverage / stability / variable-thickness fit). Default 0.",
            GH_ParamAccess.item, 0);
        p.AddBooleanParameter("Allow Trim", "AT",
            "If true, calls Component C3D (Adaptive Block Match 3D) for the " +
            "overlap-then-carve recipe step 7. If false, stones placed without trim.",
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Placed Stones", "PS",
            "Per-stone placed and trimmed mesh (transformed into wall position).",
            GH_ParamAccess.list);
        p.AddTextParameter("Recipe Step", "RS",
            "Per-stone recipe-step tag: 'trapezoid_seed' / 'parallelogram_fill' / 'keystone_inverted_trapezoid'.",
            GH_ParamAccess.list);
        p.AddCurveParameter("Utah Curves", "UC",
            "Per-joint Utah-detail scribe curves (the bed-joint signatures " +
            "carved into the course below). Empty if Allow Trim = false.",
            GH_ParamAccess.list);
        p.AddPointParameter("Dowel Positions", "DP",
            "Stainless-steel structural alignment dowel insertion points " +
            "(3-inch cord-drilled holes per Quarra Emanuel 9 + Cyclopean Cannibalism).",
            GH_ParamAccess.list);
        p.AddVectorParameter("Dowel Vectors", "DV",
            "Per-stone dowel insertion vectors (vertical setting direction).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Stone Indices", "SI",
            "Per-stone inventory-index used (parallels Placed Stones).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Coverage", "Cv",
            "Fraction of Wall Envelope's projected area covered by placed stones (0-1).",
            GH_ParamAccess.item);
        p.AddTextParameter("Remarks", "R",
            "Per-stone diagnostic notes + recipe-rule-violation flags + " +
            "stability-check results.", GH_ParamAccess.list);
    }

    protected override void SolveSafe(IGH_DataAccess DA)
    {
        var inventory = new List<Mesh>();
        Brep envelope = null;
        Brep backPlane = null;
        double courseHeight = 400.0;
        double minFaceArea = 15000.0;
        int strategy = 0;
        bool allowTrim = true;

        if (!DA.GetDataList(0, inventory)) return;
        if (!DA.GetData(1, ref envelope)) return;
        DA.GetData(2, ref backPlane);
        DA.GetData(3, ref courseHeight);
        DA.GetData(4, ref minFaceArea);
        DA.GetData(5, ref strategy);
        DA.GetData(6, ref allowTrim);

        if (inventory.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Rubble Inventory is empty.");
            return;
        }
        if (envelope == null || !envelope.IsValid)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Wall Envelope is invalid.");
            return;
        }

        // ----- Step 1: Classify stones (trapezoid vs parallelogram) -----
        // Frahan-original heuristic: stones with elongated OBB (aspect ratio > 1.6)
        // act as trapezoid SEEDS (stable, span-wise). Stones with closer-to-cube
        // aspect (aspect <= 1.6) act as parallelogram FILLS.
        var classification = new List<StoneClass>(inventory.Count);
        for (int i = 0; i < inventory.Count; i++)
        {
            classification.Add(Classify(inventory[i]));
        }
        int trapezoidCount = 0, parallelogramCount = 0;
        foreach (var c in classification)
        {
            if (c.Role == StoneRole.TrapezoidSeed) trapezoidCount++;
            else parallelogramCount++;
        }

        // ----- Step 2-6: Course layout -----
        // Envelope bounding box drives wall extents.
        var bbox = envelope.GetBoundingBox(true);
        if (!bbox.IsValid)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Wall Envelope bounding box invalid.");
            return;
        }
        double wallWidth = bbox.Max.X - bbox.Min.X;
        double wallHeight = bbox.Max.Z - bbox.Min.Z;
        int courseCount = Math.Max(1, (int)Math.Floor(wallHeight / courseHeight));

        var placed = new List<Mesh>();
        var stepTags = new List<string>();
        var dowelPts = new List<Point3d>();
        var dowelVecs = new List<Vector3d>();
        var stoneIdx = new List<int>();
        var used = new HashSet<int>();
        var remarks = new List<string>();
        double placedArea = 0;

        for (int courseIdx = 0; courseIdx < courseCount; courseIdx++)
        {
            double z = bbox.Min.Z + (courseIdx + 0.5) * courseHeight;
            // Stagger every other course by half the trapezoid mean width to
            // implement recipe step 6 (straddle vertical joints below).
            double cursorX = bbox.Min.X + (courseIdx % 2 == 0 ? 0.0 : 200.0);
            int slotIdx = 0;
            while (cursorX < bbox.Max.X)
            {
                // Find next stone. Alternate trapezoid seed + parallelogram fill;
                // last stone in a course is an inverted-trapezoid keystone.
                bool isLastSlotInCourse = (cursorX + 600.0 >= bbox.Max.X);
                StoneRole desired = isLastSlotInCourse
                    ? StoneRole.KeystoneInverted
                    : (slotIdx % 2 == 0 ? StoneRole.TrapezoidSeed : StoneRole.ParallelogramFill);

                int pick = PickStone(classification, used, desired);
                if (pick < 0)
                {
                    // Fall back to ANY unused stone.
                    pick = PickStone(classification, used, StoneRole.Unknown);
                    if (pick < 0) break;
                }
                used.Add(pick);

                var stone = inventory[pick].DuplicateMesh();
                var c = classification[pick];

                // Place stone: translate its OBB centroid to (cursorX + halfW, bbox.Min.Y, z).
                var t = Transform.Translation(
                    cursorX + c.SizeX * 0.5 - c.Center.X,
                    bbox.Min.Y + c.SizeY * 0.5 - c.Center.Y,
                    z - c.Center.Z);
                // For keystone: flip 180° around horizontal X axis (upside down).
                if (desired == StoneRole.KeystoneInverted)
                {
                    var rot = Transform.Rotation(Math.PI,
                        Vector3d.XAxis,
                        new Point3d(cursorX + c.SizeX * 0.5, bbox.Min.Y, z));
                    t = Transform.Multiply(rot, t);
                }
                stone.Transform(t);

                placed.Add(stone);
                stepTags.Add(desired.ToString());
                stoneIdx.Add(pick);

                // Dowel insertion point: top-face centroid of the placed stone.
                var pbb = stone.GetBoundingBox(true);
                dowelPts.Add(new Point3d(
                    (pbb.Min.X + pbb.Max.X) * 0.5,
                    (pbb.Min.Y + pbb.Max.Y) * 0.5,
                    pbb.Max.Z));
                dowelVecs.Add(-Vector3d.ZAxis);

                placedArea += c.SizeX * c.SizeY;
                cursorX += c.SizeX + 5.0; // 5 mm joint gap
                slotIdx++;
            }
        }

        double envelopeArea = wallWidth * wallHeight;
        double coverage = envelopeArea > 0 ? Math.Min(1.0, placedArea / envelopeArea) : 0;

        remarks.Add("v1 Cyclopean Recipe: course-based greedy assembly.");
        remarks.Add("Classification: " + trapezoidCount + " trapezoid seeds + " +
            parallelogramCount + " parallelogram fills (aspect-ratio threshold = 1.6).");
        remarks.Add("Wall: " + wallWidth.ToString("F0") + " x " + wallHeight.ToString("F0") +
            " mm; " + courseCount + " course(s).");
        remarks.Add("Placed: " + placed.Count + " stone(s) of " + inventory.Count + " inventory.");
        remarks.Add("Coverage proxy (sum of OBB face areas / envelope area): " +
            coverage.ToString("P1"));
        remarks.Add("v1 limitations: classification is aspect-ratio heuristic (v1.x replaces " +
            "with VsaSegmenter largest-inscribed-polygon shape per Clifford 2017 Cookbook " +
            "pp.118-120); placement is greedy along course X-axis (v1.x adds NSGA-II Pareto " +
            "when Strategy=1); Utah-detail scribe + carve curves deferred to v1.x (Allow " +
            "Trim = " + allowTrim + " ignored); stability gate (Heyman 1966 CoM-over-support) " +
            "is NOT enforced in v1.");
        remarks.Add("Cite Clifford-McGee 2017 ACADIA pp. 404-413; Clifford 2017 Cannibal's Cookbook.");

        DA.SetDataList(0, placed);
        DA.SetDataList(1, stepTags);
        DA.SetDataList(2, new List<Curve>()); // Utah curves (v1.x)
        DA.SetDataList(3, dowelPts);
        DA.SetDataList(4, dowelVecs);
        DA.SetDataList(5, stoneIdx);
        DA.SetData(6, coverage);
        DA.SetDataList(7, remarks);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private enum StoneRole
    {
        Unknown = 0,
        TrapezoidSeed = 1,
        ParallelogramFill = 2,
        KeystoneInverted = 3,
    }

    private sealed class StoneClass
    {
        public StoneRole Role;
        public double Aspect; // longest / shortest OBB side
        public double SizeX, SizeY, SizeZ;
        public Point3d Center;
    }

    private static StoneClass Classify(Mesh m)
    {
        var bb = m.GetBoundingBox(true);
        if (!bb.IsValid)
            return new StoneClass { Role = StoneRole.Unknown };
        double sx = bb.Max.X - bb.Min.X;
        double sy = bb.Max.Y - bb.Min.Y;
        double sz = bb.Max.Z - bb.Min.Z;
        double max = Math.Max(sx, Math.Max(sy, sz));
        double min = Math.Min(sx, Math.Min(sy, sz));
        double aspect = min > 1e-6 ? max / min : 1.0;
        var role = aspect > 1.6 ? StoneRole.TrapezoidSeed : StoneRole.ParallelogramFill;
        return new StoneClass
        {
            Role = role,
            Aspect = aspect,
            SizeX = sx, SizeY = sy, SizeZ = sz,
            Center = new Point3d(
                (bb.Min.X + bb.Max.X) * 0.5,
                (bb.Min.Y + bb.Max.Y) * 0.5,
                (bb.Min.Z + bb.Max.Z) * 0.5),
        };
    }

    /// <summary>Pick the first unused stone of the desired role. Role=Unknown matches any.</summary>
    private static int PickStone(IList<StoneClass> classes, HashSet<int> used, StoneRole desired)
    {
        // Keystones reuse trapezoid stones (inverted).
        StoneRole search = desired == StoneRole.KeystoneInverted ? StoneRole.TrapezoidSeed : desired;
        for (int i = 0; i < classes.Count; i++)
        {
            if (used.Contains(i)) continue;
            if (search == StoneRole.Unknown || classes[i].Role == search) return i;
        }
        return -1;
    }
}
