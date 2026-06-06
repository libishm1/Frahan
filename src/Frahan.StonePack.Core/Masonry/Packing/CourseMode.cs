#nullable disable

namespace Frahan.Masonry.Packing;

/// <summary>
/// Layout strategy for the ashlar packer. Both values are declared so the
/// engine can fail loud (NotSupportedException) on the unimplemented branch
/// in Stage 1; CoursedRubble is wired in Stage 2.
/// </summary>
public enum CourseMode
{
    /// <summary>Single height bin per wall: every slab in the bin matches TargetCourseHeight within HeightTolerance.</summary>
    CoursedAshlar = 0,

    /// <summary>Multiple height bins; each course picks one bin and lays its slabs in running bond. Stage 2.</summary>
    CoursedRubble = 1,
}
