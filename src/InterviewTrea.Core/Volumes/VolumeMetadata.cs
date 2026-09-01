namespace InterviewTrea.Core.Volumes;

/// <summary>
/// Identifying and display information carried alongside a reconstructed volume.
/// </summary>
/// <remarks>
/// The nullability here is the requirement, not an oversight. DI-3: public research
/// data is de-identified, so SeriesDescription, PatientName, PatientId and StudyDate
/// are routinely absent or empty and must never fail a load. The UIDs and modality are
/// non-null because a series that lacks them cannot be validated or grouped at all.
/// <para>
/// DI-1 forbids PatientName and PatientBirthDate from the viewport overlay. They live
/// here only to feed the collapsed study-information panel of DI-2, which labels them
/// as de-identified research codes.
/// </para>
/// </remarks>
public sealed record VolumeMetadata
{
    public required string StudyInstanceUid { get; init; }

    public required string SeriesInstanceUid { get; init; }

    /// <summary>All slices in a volume share this (FR-105).</summary>
    public required string FrameOfReferenceUid { get; init; }

    /// <summary>Expected to be "CT"; Phase 1 supports no other modality by design.</summary>
    public required string Modality { get; init; }

    public string? SeriesDescription { get; init; }

    /// <summary>DI-2 only. Never render this in a viewport overlay.</summary>
    public string? PatientId { get; init; }

    /// <summary>DI-2 only. In TCIA data this reads like "LIDC-IDRI-0142".</summary>
    public string? PatientName { get; init; }

    /// <summary>
    /// Kept as the raw DICOM string rather than a parsed date. DI-2 requires the
    /// identifier fields be shown verbatim, and TCIA shifts study dates anyway, so
    /// parsing would add a failure mode and discard the only honest representation.
    /// </summary>
    public string? StudyDate { get; init; }

    /// <summary>WindowCenter (0028,1050), used as the initial preset when present (FR-306).</summary>
    public double? WindowCenter { get; init; }

    /// <summary>WindowWidth (0028,1051), used as the initial preset when present (FR-306).</summary>
    public double? WindowWidth { get; init; }
}
