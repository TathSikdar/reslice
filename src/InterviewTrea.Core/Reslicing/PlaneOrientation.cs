namespace InterviewTrea.Core.Reslicing;

/// <summary>
/// The three standard anatomical planes, named for the plane itself rather than for the
/// axis it is perpendicular to.
/// </summary>
/// <remarks>
/// These are defined in <em>patient</em> space, not in voxel space. A series acquired at
/// an angle still has an anatomically correct coronal view, because the plane is built
/// from the DICOM patient coordinate axes and the volume's own orientation is carried by
/// its affine transform.
/// </remarks>
public enum PlaneOrientation
{
    Axial,
    Coronal,
    Sagittal,
}
