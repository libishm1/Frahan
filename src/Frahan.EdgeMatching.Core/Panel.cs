using System;
using Rhino.Geometry;

namespace Frahan.EdgeMatching
{
    /// <summary>
    /// A panel (shard, plank, or frame). SourceContour is duplicated on
    /// construction and is never mutated thereafter. Matching results
    /// compose left-multiplicatively into AppliedTransform; downstream
    /// callers materialise the placed contour via CurrentContour().
    /// </summary>
    public sealed class Panel
    {
        public const double DefaultPlanarityTolerance = 0.5;

        public string Id { get; }
        public PolylineCurve SourceContour { get; }
        public Transform AppliedTransform { get; private set; }
        public bool IsAnchored { get; set; }
        public PanelKind Kind { get; }

        public PanelMode Mode { get; }
        public Plane LocalFrame { get; }
        public double PlanarityRms { get; }

        public Vector3d? GrainDirection { get; set; }

        public Panel(
            string id,
            PolylineCurve source,
            PanelKind kind,
            double planarityTolerance = DefaultPlanarityTolerance)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!source.IsClosed && kind != PanelKind.Frame)
                throw new ArgumentException("Non-frame panel contour must be closed.", nameof(source));

            Id = id;
            SourceContour = (PolylineCurve)source.DuplicateCurve();
            AppliedTransform = Transform.Identity;
            Kind = kind;

            var (plane, rms) = PlanarityTester.BestFitPlane(SourceContour);
            LocalFrame = plane;
            PlanarityRms = rms;
            Mode = rms <= planarityTolerance ? PanelMode.Planar2D : PanelMode.Spatial3D;
        }

        public void Apply(Transform t)
        {
            if (IsAnchored)
                throw new InvalidOperationException($"Panel {Id} is anchored.");
            AppliedTransform = Transform.Multiply(t, AppliedTransform);
        }

        public PolylineCurve CurrentContour()
        {
            var c = (PolylineCurve)SourceContour.DuplicateCurve();
            c.Transform(AppliedTransform);
            return c;
        }
    }
}
