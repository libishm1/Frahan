#nullable disable
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Core.Voussoir;

// =============================================================================
// VoussoirRecord — typed record per voussoir flowing through the Frahan
// stereotomy pipeline. Mirror of QuarryBlock's pattern (Frahan.Core.Quarry):
// simple public fields, no behaviour. Reference type so cheap on GH wires.
//
// Produced by VoussoirIngestComponent (Frahan > Voussoir > Voussoir Ingest)
// from a list of voussoir meshes (typically from the Voussoir GH plugin
// at food4rhino.com/en/app/voussoir or from Frahan's Stereotomic Vault Mode).
// Consumed by VoussoirStoneMatcherComponent (Hungarian bipartite assignment
// to quarry stones) and VoussoirPackIntoBlockComponent (3D bin-pack inside
// one quarried block).
//
// Spec: wiki/research/voussoir_stereotomy_integration.md Phase 1; the
// architectural decisions doc 2026-05-31 §9.1 (stone fabrication discipline);
// philosophy doc §10.6 (voussoir / raw-stone shared Hungarian solver).
// =============================================================================

public sealed class VoussoirRecord
{
    /// <summary>Stable identifier (typically "V" + index, e.g. "V001").</summary>
    public string Id;

    /// <summary>The voussoir's closed-solid mesh in world coordinates.</summary>
    public Mesh Geometry;

    /// <summary>Oriented bounding box derived from the mesh's principal axes
    /// (Mesh PCA). Used for OBB containment + yield-ratio scoring against
    /// QuarryBlock candidates.</summary>
    public Box OrientedBoundingBox;

    /// <summary>Mesh volume (cubed model units). Frahan-original via
    /// Mesh.Volume (signed; we store the absolute value).</summary>
    public double Volume;

    /// <summary>Mesh centroid (geometric centre).</summary>
    public Point3d Centroid;

    /// <summary>Bed-joint plane: the bottom face that contacts the course
    /// below. Detected as the largest-area face whose normal points "down"
    /// relative to the LoadAxis; null if not auto-detectable. Architects
    /// may override via the JointPlanes upstream input.</summary>
    public Plane BedPlane;

    /// <summary>Head-joint plane: the top face that the next course rests on.
    /// Counterpart to BedPlane. Null if not auto-detectable.</summary>
    public Plane HeadPlane;

    /// <summary>Direction of designed compressive load. For Gothic ribs this
    /// is along the thrust-line tangent at the voussoir's position. Used for
    /// grain-alignment scoring against stone bed-plane.</summary>
    public Vector3d LoadAxis;

    /// <summary>Categorical position role within the assembly:
    /// "bed" = bottom course (rests on ground / sill).
    /// "head" = top course (no voussoir above).
    /// "key" = keystone (closes the arch).
    /// "ground" = abutment / springer.
    /// "void" = ordinary intermediate voussoir.
    /// Default "void". Drives sequencing + matching priorities.</summary>
    public string JointClass;

    /// <summary>Optional lithology hint specified by the architect (e.g.
    /// "Vermont Marble", "Virginia Mist Granite"). Used as a categorical
    /// constraint when matching to QuarryBlock inventory. Empty / null means
    /// "no constraint."</summary>
    public string LithologyHint;

    /// <summary>Position index along the assembly's primary sequence. Used
    /// by Build Order Sequencer (Kim 2024) to derive the install DAG.
    /// 0-based; -1 means unsequenced.</summary>
    public int SequenceIndex;

    /// <summary>Tag the architect may attach for provenance / display.</summary>
    public string Label;

    public VoussoirRecord()
    {
        JointClass = "void";
        LithologyHint = "";
        Label = "";
        SequenceIndex = -1;
    }
}
