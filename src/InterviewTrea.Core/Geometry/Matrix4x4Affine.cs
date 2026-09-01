using System;

namespace InterviewTrea.Core.Geometry;

/// <summary>
/// An affine transform between two 3D spaces, stored as the three basis vectors and
/// the translation rather than as sixteen numbers - the bottom row of a 4x4 affine is
/// always [0 0 0 1], so carrying it would be memory and confusion for no benefit.
/// </summary>
/// <remarks>
/// <para>
/// Read the columns physically. For the volume's voxel-to-patient transform,
/// <see cref="AxisI"/> is "how far in patient millimetres do I move per +1 column
/// index", <see cref="AxisJ"/> the same per +1 row index, <see cref="AxisK"/> per +1
/// slice index, and <see cref="Origin"/> is the patient location of voxel (0,0,0).
/// </para>
/// <para>
/// The type is direction-agnostic, so <see cref="Inverse"/> also returns one of these.
/// On an inverted transform the columns instead read as "how much does the voxel index
/// change per millimetre along patient X / Y / Z". Same maths, mirrored meaning.
/// </para>
/// </remarks>
public readonly record struct Matrix4x4Affine(
    Vector3D AxisI,
    Vector3D AxisJ,
    Vector3D AxisK,
    Point3D Origin)
{
    // A CT volume's determinant is the product of its voxel spacings - on the order of
    // 0.1 to 10 mm^3. Anything near zero means a collapsed axis (zero spacing, parallel
    // direction cosines) and the transform has no inverse.
    private const double MinimumInvertibleDeterminant = 1e-12;

    /// <summary>
    /// Builds the voxel-to-patient transform directly from the DICOM image plane tags.
    /// </summary>
    /// <param name="rowCosine">
    /// ImageOrientationPatient (0020,0037) values [0..2]. Despite the name this is the
    /// direction travelled <em>along</em> a row - that is, as the column index rises.
    /// </param>
    /// <param name="columnCosine">
    /// ImageOrientationPatient values [3..5]: the direction travelled down a column,
    /// as the row index rises.
    /// </param>
    /// <param name="adjacentRowSpacing">PixelSpacing (0028,0030) value [0].</param>
    /// <param name="adjacentColumnSpacing">PixelSpacing value [1].</param>
    /// <param name="sliceStep">
    /// The <em>measured</em> displacement between successive slices, i.e.
    /// ImagePositionPatient of slice 1 minus that of slice 0. Deliberately not derived
    /// as (spacing * sliceNormal): that form assumes the slices stack perpendicular to
    /// their own plane, which is exactly the assumption gantry tilt breaks. Passing the
    /// measured vector means a tilted series produces a visibly sheared transform
    /// rather than a clean-looking wrong one, and lets FR-107b test the two against
    /// each other instead of trusting one of them.
    /// </param>
    /// <param name="origin">ImagePositionPatient (0020,0032) of the first slice.</param>
    public static Matrix4x4Affine FromImagePlane(
        Vector3D rowCosine,
        Vector3D columnCosine,
        double adjacentRowSpacing,
        double adjacentColumnSpacing,
        Vector3D sliceStep,
        Point3D origin)
    {
        // The crossover that catches everyone. DICOM PS3.3 C.7.6.2.1.1 defines
        // PixelSpacing as "adjacent row spacing \ adjacent column spacing":
        //   [0] = the gap between adjacent ROWS    = the vertical step   -> column cosine
        //   [1] = the gap between adjacent COLUMNS = the horizontal step -> row cosine
        // So the indices swap relative to the cosine they scale. On square pixels -
        // which is most CT - getting this backwards is completely invisible.
        return new Matrix4x4Affine(
            rowCosine.Scale(adjacentColumnSpacing),
            columnCosine.Scale(adjacentRowSpacing),
            sliceStep,
            origin);
    }

    /// <summary>
    /// The signed volume of the parallelepiped spanned by the three axes - for a
    /// voxel-to-patient transform, the volume of one voxel in mm^3. Positive means the
    /// basis is right-handed.
    /// </summary>
    public double Determinant => AxisI.Dot(AxisJ.Cross(AxisK));

    public Point3D Transform(double x, double y, double z) =>
        Origin + AxisI.Scale(x) + AxisJ.Scale(y) + AxisK.Scale(z);

    public Point3D Transform(Point3D source) => Transform(source.X, source.Y, source.Z);

    /// <summary>Returns the transform that undoes this one.</summary>
    /// <exception cref="InvalidOperationException">The transform is singular.</exception>
    public Matrix4x4Affine Inverse()
    {
        double determinant = Determinant;
        if (Math.Abs(determinant) < MinimumInvertibleDeterminant || !double.IsFinite(determinant))
        {
            throw new InvalidOperationException(
                $"Transform is singular (determinant {determinant}); at least one axis has collapsed.");
        }

        // Cramer's rule. Each output coordinate is a scalar triple product against the
        // other two axes, which falls straight out of Vector3D.Cross rather than
        // needing a general matrix routine:
        //     i = (AxisJ x AxisK) . (p - Origin) / det
        // These three vectors are the ROWS of the inverse linear map.
        double inverseDeterminant = 1.0 / determinant;
        Vector3D rowForI = AxisJ.Cross(AxisK).Scale(inverseDeterminant);
        Vector3D rowForJ = AxisK.Cross(AxisI).Scale(inverseDeterminant);
        Vector3D rowForK = AxisI.Cross(AxisJ).Scale(inverseDeterminant);

        // This type stores columns, so transpose the rows into place.
        Vector3D origin = Origin.AsVector();
        return new Matrix4x4Affine(
            new Vector3D(rowForI.X, rowForJ.X, rowForK.X),
            new Vector3D(rowForI.Y, rowForJ.Y, rowForK.Y),
            new Vector3D(rowForI.Z, rowForJ.Z, rowForK.Z),
            new Point3D(-rowForI.Dot(origin), -rowForJ.Dot(origin), -rowForK.Dot(origin)));
    }
}
