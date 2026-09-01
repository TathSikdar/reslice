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
}
