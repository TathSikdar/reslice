using InterviewTrea.Core.Geometry;

namespace InterviewTrea.Dicom;

/// <summary>
/// One slice's geometry, parsed from its header without touching pixel data.
/// </summary>
/// <remarks>
/// <para>
/// Everything downstream - validation, sorting, building the affine - works on these
/// rather than on <c>DicomDataset</c>. That keeps the geometry logic readable and means
/// the only code that knows about DICOM tags is the parser in <see cref="SeriesLoader"/>.
/// </para>
/// <para>
/// <see cref="AdjacentRowSpacing"/> and <see cref="AdjacentColumnSpacing"/> are named for
/// what they measure rather than for their tag positions. PixelSpacing (0028,0030) is
/// [between rows, between columns], so element [0] is the y step and element [1] is the x
/// step. On the square pixels of most CT the two are equal and a swap is invisible, which
/// is exactly why it survives into production elsewhere.
/// </para>
/// </remarks>
public sealed record SliceHeader(
    string FilePath,
    string SeriesInstanceUid,
    string FrameOfReferenceUid,
    Point3D Position,
    Vector3D RowCosine,
    Vector3D ColumnCosine,
    double AdjacentRowSpacing,
    double AdjacentColumnSpacing,
    int Rows,
    int Columns)
{
    /// <summary>
    /// Unit normal of the image plane: row cosine crossed with column cosine, in that
    /// order. The cross product is anti-commutative, so reversing the operands flips the
    /// normal and reverses the entire slice ordering.
    /// </summary>
    public Vector3D Normal => RowCosine.Cross(ColumnCosine).Normalized();

    /// <summary>
    /// Signed distance of this slice along a stacking direction. Sorting by this is
    /// FR-103; sorting by InstanceNumber is the trap it exists to avoid.
    /// </summary>
    public double DistanceAlong(Vector3D direction) => Position.AsVector().Dot(direction);
}
