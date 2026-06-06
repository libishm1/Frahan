using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Surface
{
    public sealed class ChartDistortionReport
    {
        public double MaxEdgeStretch { get; set; } = 1.0;
        public double MinEdgeStretch { get; set; } = 1.0;
        public List<string> Warnings { get; } = new List<string>();

        public bool HasWarnings => Warnings.Count > 0;
    }

    /// <summary>
    /// Compares edge lengths between the 3D surface mesh and the 2D flat mesh
    /// to detect fabrication-relevant stretch and compression.
    /// Edge-scale distortion (not area) is the correct metric for cut path accuracy.
    /// Both meshes must have the same face topology and count.
    /// </summary>
    public static class ChartDistortionAnalyzer
    {
        private const double WarnStretchHigh = 1.15;
        private const double WarnStretchLow = 0.85;

        public static ChartDistortionReport Analyze(Mesh surfaceMesh, Mesh flatMesh, double chartScale)
        {
            var report = new ChartDistortionReport
            {
                MaxEdgeStretch = 1.0,
                MinEdgeStretch = double.MaxValue
            };

            if (surfaceMesh == null || flatMesh == null)
            {
                report.Warnings.Add("Cannot analyze distortion: null mesh.");
                return report;
            }

            if (surfaceMesh.Faces.Count != flatMesh.Faces.Count)
            {
                report.Warnings.Add(
                    $"Cannot analyze distortion: face count mismatch " +
                    $"(surface={surfaceMesh.Faces.Count}, flat={flatMesh.Faces.Count}).");
                return report;
            }

            if (chartScale <= 1e-12)
            {
                report.Warnings.Add("Cannot analyze distortion: chartScale is zero or negative.");
                return report;
            }

            int edgesChecked = 0;

            for (int i = 0; i < surfaceMesh.Faces.Count; i++)
            {
                if (!surfaceMesh.Faces[i].IsTriangle || !flatMesh.Faces[i].IsTriangle) continue;

                Point3d s0 = surfaceMesh.Vertices[surfaceMesh.Faces[i].A];
                Point3d s1 = surfaceMesh.Vertices[surfaceMesh.Faces[i].B];
                Point3d s2 = surfaceMesh.Vertices[surfaceMesh.Faces[i].C];

                // Scale flat UV vertices to real dimensions before comparing
                Point3d f0 = (Point3d)flatMesh.Vertices[flatMesh.Faces[i].A] * chartScale;
                Point3d f1 = (Point3d)flatMesh.Vertices[flatMesh.Faces[i].B] * chartScale;
                Point3d f2 = (Point3d)flatMesh.Vertices[flatMesh.Faces[i].C] * chartScale;

                UpdateStretch(s0.DistanceTo(s1), f0.DistanceTo(f1), report, ref edgesChecked);
                UpdateStretch(s1.DistanceTo(s2), f1.DistanceTo(f2), report, ref edgesChecked);
                UpdateStretch(s2.DistanceTo(s0), f2.DistanceTo(f0), report, ref edgesChecked);
            }

            if (edgesChecked == 0)
            {
                report.MinEdgeStretch = 1.0;
                report.Warnings.Add("No valid triangle edges found for distortion analysis.");
                return report;
            }

            if (report.MaxEdgeStretch > WarnStretchHigh)
                report.Warnings.Add(
                    $"High edge stretch: {report.MaxEdgeStretch:F3}x. Increase clearance to compensate.");

            if (report.MinEdgeStretch < WarnStretchLow)
                report.Warnings.Add(
                    $"High edge compression: {report.MinEdgeStretch:F3}x. Parts may be under-scaled after mapping.");

            return report;
        }

        private static void UpdateStretch(
            double len3D, double len2D, ChartDistortionReport report, ref int edgesChecked)
        {
            if (len3D < 1e-8 || len2D < 1e-8) return;

            double stretch = len3D / len2D;
            if (stretch > report.MaxEdgeStretch) report.MaxEdgeStretch = stretch;
            if (stretch < report.MinEdgeStretch) report.MinEdgeStretch = stretch;
            edgesChecked++;
        }
    }
}
