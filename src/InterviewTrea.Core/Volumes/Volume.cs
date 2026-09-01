using System;
using InterviewTrea.Core.Geometry;

namespace InterviewTrea.Core.Volumes;

/// <summary>
/// A reconstructed CT volume: Hounsfield units in a flat array, plus the transform that
/// gives every voxel a location in patient space.
/// </summary>
/// <remarks>
/// <para>
/// Storage is a single flat <see cref="short"/> array, x fastest, then y, then z.
/// <c>short</c> because the CT range is roughly -1024..3071 and it halves memory against
/// <c>float</c> - a 512 x 512 x 400 volume is ~210 MB rather than ~420 MB (NFR-101).
/// Flat rather than <c>short[,,]</c> because .NET multidimensional arrays index more
/// slowly and defeat some bounds-check elimination.
/// </para>
/// <para>
/// X-fastest means an axial slice is one contiguous run of memory while coronal and
/// sagittal reads are strided. That asymmetry is deliberate and shows up in the
/// benchmarks: the axial fast path is close to a memcpy, the other two are not.
/// </para>
/// <para>
/// <see cref="VoxelToPatient"/> is the single source of truth for geometry.
/// <see cref="Spacing"/> and <see cref="Origin"/> are derived from it and cannot be set
/// independently, so there is no way for the two to disagree.
/// </para>
/// </remarks>
public sealed class Volume
{
    private readonly int sliceStride;

    public Volume(
        short[] voxels,
        int dimX,
        int dimY,
        int dimZ,
        Matrix4x4Affine voxelToPatient,
        VolumeMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(voxels);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimX, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimY, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimZ, 1);

        long expected = (long)dimX * dimY * dimZ;
        if (voxels.LongLength != expected)
        {
            throw new ArgumentException(
                $"Expected {expected} voxels for a {dimX}x{dimY}x{dimZ} volume, got {voxels.LongLength}.",
                nameof(voxels));
        }

        Voxels = voxels;
        DimX = dimX;
        DimY = dimY;
        DimZ = dimZ;
        Metadata = metadata;
        sliceStride = dimX * dimY;

        VoxelToPatient = voxelToPatient;

        // Inverting here means a degenerate transform cannot survive construction, and
        // the render loop never pays for the inverse. Throws if an axis has collapsed.
        PatientToVoxel = voxelToPatient.Inverse();

        // Derived, computed once. The magnitude of each axis is the millimetre step per
        // one index along it, which is what "spacing" means.
        Spacing = new Vector3D(
            voxelToPatient.AxisI.Length,
            voxelToPatient.AxisJ.Length,
            voxelToPatient.AxisK.Length);
    }

    /// <summary>
    /// Hounsfield units, x fastest. Exposed as the raw array rather than a span because
    /// the renderer indexes it directly in its inner loop; treat it as read-only.
    /// </summary>
#pragma warning disable CA1819 // Properties should not return arrays - deliberate, see above.
    public short[] Voxels { get; }
#pragma warning restore CA1819

    public int DimX { get; }

    public int DimY { get; }

    public int DimZ { get; }

    /// <summary>Millimetres per one index step along each voxel axis. Derived from <see cref="VoxelToPatient"/>.</summary>
    public Vector3D Spacing { get; }

    /// <summary>Patient-space location of voxel (0,0,0). Derived from <see cref="VoxelToPatient"/>.</summary>
    public Point3D Origin => VoxelToPatient.Origin;

    public Matrix4x4Affine VoxelToPatient { get; }

    public Matrix4x4Affine PatientToVoxel { get; }

    public VolumeMetadata Metadata { get; }

    public long VoxelCount => Voxels.LongLength;

    /// <summary>Bytes of managed heap occupied by the voxel data (NFR-101).</summary>
    public long ByteCount => Voxels.LongLength * sizeof(short);

    public bool Contains(int i, int j, int k) =>
        (uint)i < (uint)DimX && (uint)j < (uint)DimY && (uint)k < (uint)DimZ;

    /// <summary>Flat array offset of voxel (i, j, k). No bounds checking.</summary>
    public int IndexOf(int i, int j, int k) => (k * sliceStride) + (j * DimX) + i;

    /// <summary>Hounsfield value at an integer voxel coordinate.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Any index is outside the volume.</exception>
    public short this[int i, int j, int k]
    {
        get
        {
            // Each axis is checked separately rather than leaning on the array's own
            // bounds check. An out-of-range i wraps into the next row and produces a
            // perfectly valid flat index - a silent neighbour-row read that would look
            // like a subtle rendering artefact rather than an error.
            if (!Contains(i, j, k))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(i),
                    $"Voxel ({i}, {j}, {k}) is outside a {DimX}x{DimY}x{DimZ} volume.");
            }

            return Voxels[IndexOf(i, j, k)];
        }
    }

    /// <summary>
    /// Value returned for a sample that falls outside the volume.
    /// </summary>
    /// <remarks>
    /// Deliberately not the -1000 of real scanned air. -1024 is the bottom of the CT
    /// range and reads as "no data here", so a bug that samples off the end shows up as a
    /// too-dark border rather than blending invisibly into lung.
    /// </remarks>
    public const short OutsideValue = -1024;

    /// <summary>
    /// True when a continuous voxel coordinate lies inside the sampling domain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The domain is [0, Dim-1] inclusive: voxel centre to voxel centre. A DICOM voxel
    /// index names the <em>centre</em> of that voxel (ImagePositionPatient is defined as
    /// the centre of the first pixel), so there is no half-voxel offset anywhere in this
    /// file. The half voxel of physical extent beyond the outermost centres is treated as
    /// outside so that nearest and trilinear agree on where the volume ends; if they
    /// disagreed, the two render modes would draw different silhouettes.
    /// </para>
    /// <para>
    /// NaN fails both comparisons and so reports as outside, which is the wanted answer.
    /// </para>
    /// </remarks>
    public bool ContainsContinuous(double x, double y, double z) =>
        x >= 0 && x <= DimX - 1 &&
        y >= 0 && y <= DimY - 1 &&
        z >= 0 && z <= DimZ - 1;

    /// <summary>Nearest-neighbour sample at a continuous voxel coordinate.</summary>
    public short SampleNearest(double x, double y, double z)
    {
        if (!ContainsContinuous(x, y, z))
        {
            return OutsideValue;
        }

        // floor(v + 0.5), not Math.Round: Math.Round defaults to banker's rounding, which
        // would send 0.5 down and 1.5 up. Ties round up, consistently.
        return Voxels[IndexOf(
            (int)Math.Floor(x + 0.5),
            (int)Math.Floor(y + 0.5),
            (int)Math.Floor(z + 0.5))];
    }

    /// <summary>Nearest-neighbour sample at a patient-space point.</summary>
    public short SampleNearest(Point3D patient)
    {
        Point3D v = PatientToVoxel.Transform(patient);
        return SampleNearest(v.X, v.Y, v.Z);
    }

    /// <summary>
    /// Trilinear sample at a continuous voxel coordinate: the eight surrounding voxels
    /// blended by proximity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns <c>double</c> rather than <c>short</c> because the blend genuinely is
    /// fractional. Slab MIP and slab average accumulate many samples, and rounding each
    /// one to an integer injects up to half a Hounsfield unit of bias per sample.
    /// </para>
    /// <para>
    /// Trilinear rather than a cubic kernel because a cubic has negative lobes and
    /// overshoots: sampling near a bone or metal edge would produce HU values that exist
    /// nowhere in the data. Trilinear never leaves the range of its eight inputs, which
    /// matters in a viewer where the user reads numbers off the image.
    /// </para>
    /// </remarks>
    public double SampleTrilinear(double x, double y, double z)
    {
        if (!ContainsContinuous(x, y, z))
        {
            return OutsideValue;
        }

        int i0 = (int)Math.Floor(x);
        int j0 = (int)Math.Floor(y);
        int k0 = (int)Math.Floor(z);

        double tx = x - i0;
        double ty = y - j0;
        double tz = z - k0;

        // Offset to the far neighbour on each axis, collapsed to zero on the last plane
        // so the read cannot run off the end. Its weight is exactly zero there, so the
        // clamp changes no result - it only stops an index fault.
        int dx = i0 + 1 < DimX ? 1 : 0;
        int dy = j0 + 1 < DimY ? DimX : 0;
        int dz = k0 + 1 < DimZ ? sliceStride : 0;

        int b = IndexOf(i0, j0, k0);

        // Collapse the cube one axis at a time: four lerps along x reduce it to a square,
        // two along y reduce that to a line, one along z gives the answer. Seven lerps,
        // no divisions, no branches.
        double c00 = Lerp(Voxels[b], Voxels[b + dx], tx);
        double c10 = Lerp(Voxels[b + dy], Voxels[b + dy + dx], tx);
        double c01 = Lerp(Voxels[b + dz], Voxels[b + dz + dx], tx);
        double c11 = Lerp(Voxels[b + dz + dy], Voxels[b + dz + dy + dx], tx);

        return Lerp(Lerp(c00, c10, ty), Lerp(c01, c11, ty), tz);
    }

    /// <summary>Trilinear sample at a patient-space point.</summary>
    /// <remarks>
    /// The voxel-space overload is the hot path. An MPR scanline walks by adding a
    /// constant step vector, so it converts once per row and increments; calling this
    /// per sample would put a matrix multiply in the inner loop for nothing.
    /// </remarks>
    public double SampleTrilinear(Point3D patient)
    {
        Point3D v = PatientToVoxel.Transform(patient);
        return SampleTrilinear(v.X, v.Y, v.Z);
    }

    // a + (b - a) * t rather than (1 - t) * a + t * b: exact at both endpoints and one
    // multiply cheaper.
    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}
