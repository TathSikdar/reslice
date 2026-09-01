namespace InterviewTrea.Rendering.Reslicing;

/// <summary>How a slab of samples along the view normal collapses to one pixel (FR-207).</summary>
public enum SlabMode
{
    /// <summary>
    /// Maximum intensity projection. The brightest sample wins, so contrast-filled
    /// vessels and calcium stand out of the surrounding tissue - the reason MIP is the
    /// default for vascular and calcium work.
    /// </summary>
    Maximum,

    /// <summary>
    /// Minimum intensity projection. The darkest sample wins, which is what makes airways
    /// and emphysema visible against the lung parenchyma around them.
    /// </summary>
    Minimum,

    /// <summary>
    /// Mean of the samples. Behaves like a thicker acquired slice: less noise, less
    /// detail, and unlike the other two it is not dominated by a single outlying voxel.
    /// </summary>
    Average,
}
