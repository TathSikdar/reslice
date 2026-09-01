using System;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.TestData;

/// <summary>
/// Synthetic volumes with analytically known contents (spec 8.1).
/// </summary>
/// <remarks>
/// <para>
/// These exist so that measurement and rendering code can be asserted against numbers
/// derived on paper rather than captured from a first run. A distance across
/// <see cref="Cube"/>(50) must read 50.0 mm because a 50 mm cube is 50 mm, not because
/// that is what the code returned yesterday.
/// </para>
/// <para>
/// Every phantom is built in patient millimetres via the voxel-to-patient transform,
/// and is centred on the patient-space origin, so the centre of any phantom is exactly
/// <see cref="Point3D.Origin"/>. Anisotropic spacing therefore comes out right for free,
/// which is what makes FR-208 testable.
/// </para>
/// <para>
/// This relies on <see cref="Matrix4x4Affine"/> being correct - but that is tested
/// independently against hand-computed values, so the dependency runs one way and is
/// not circular.
/// </para>
/// </remarks>
public static class Phantoms
{
    /// <summary>1 mm cubic voxels: geometry is easy to reason about by hand.</summary>
    public static Vector3D IsotropicSpacing => new(1.0, 1.0, 1.0);

    /// <summary>A typical chest CT: thin in plane, coarse between slices (FR-208).</summary>
    public static Vector3D ChestSpacing => new(0.7, 0.7, 3.0);

    public const short Air = -1000;
    public const short SoftTissue = 40;
    public const short Bone = 700;

    /// <summary>Every voxel the same value. Verifies LUTs, ROI statistics, and sanity.</summary>
    public static Volume Uniform(
        short hounsfield,
        int dimX = 64,
        int dimY = 64,
        int dimZ = 32,
        Vector3D? spacing = null) =>
        Build(dimX, dimY, dimZ, spacing, "UNIFORM", (_, _, _, _) => hounsfield);

    /// <summary>
    /// A linear ramp along the x axis: voxel i holds <c>start + (i * step)</c>.
    /// </summary>
    /// <remarks>
    /// Expressed as HU per voxel rather than as start-and-end values so that every voxel
    /// holds an exact integer. That matters for interpolation tests - the trilinear
    /// midpoint between two voxels of 0 and 100 must be exactly 50, and a ramp defined
    /// by division would put rounding error into the expected value.
    /// </remarks>
    public static Volume GradientAlongX(
        short startHounsfield = 0,
        short hounsfieldPerVoxel = 100,
        int dimX = 16,
        int dimY = 8,
        int dimZ = 8,
        Vector3D? spacing = null) =>
        Build(dimX, dimY, dimZ, spacing, "GRADIENT",
            (i, _, _, _) => (short)(startHounsfield + (i * hounsfieldPerVoxel)));

    /// <summary>
    /// A solid sphere of the given radius in millimetres, centred on the patient origin.
    /// Verifies ROI area, distance measurement, and anisotropic aspect handling.
    /// </summary>
    public static Volume Sphere(
        double radiusMm,
        short insideHounsfield = Bone,
        short outsideHounsfield = Air,
        int dimX = 64,
        int dimY = 64,
        int dimZ = 32,
        Vector3D? spacing = null) =>
        Build(dimX, dimY, dimZ, spacing, "SPHERE",
            // Boundary voxels are inclusive. A voxel exactly on the surface counts as
            // inside, which keeps the convention the same as Cube below.
            (_, _, _, patient) => patient.DistanceTo(Point3D.Origin) <= radiusMm
                ? insideHounsfield
                : outsideHounsfield);

    /// <summary>
    /// An axis-aligned cube of the given edge length in millimetres, centred on the
    /// patient origin. A distance measurement across it must return the edge length.
    /// </summary>
    public static Volume Cube(
        double edgeMm,
        short insideHounsfield = Bone,
        short outsideHounsfield = Air,
        int dimX = 64,
        int dimY = 64,
        int dimZ = 32,
        Vector3D? spacing = null)
    {
        double halfEdge = edgeMm / 2.0;

        return Build(dimX, dimY, dimZ, spacing, "CUBE",
            (_, _, _, patient) =>
                Math.Abs(patient.X) <= halfEdge &&
                Math.Abs(patient.Y) <= halfEdge &&
                Math.Abs(patient.Z) <= halfEdge
                    ? insideHounsfield
                    : outsideHounsfield);
    }

    /// <summary>
    /// Alternating blocks of <paramref name="periodVoxels"/> on a side. The worst case
    /// for an interpolator: verifies aliasing behaviour and that smoothing happens where
    /// it should.
    /// </summary>
    public static Volume Checker(
        int periodVoxels = 4,
        short low = Air,
        short high = Bone,
        int dimX = 32,
        int dimY = 32,
        int dimZ = 32,
        Vector3D? spacing = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(periodVoxels, 1);

        return Build(dimX, dimY, dimZ, spacing, "CHECKER",
            (i, j, k, _) =>
                ((i / periodVoxels) + (j / periodVoxels) + (k / periodVoxels)) % 2 == 0
                    ? low
                    : high);
    }

    private static Volume Build(
        int dimX,
        int dimY,
        int dimZ,
        Vector3D? spacing,
        string description,
        Func<int, int, int, Point3D, short> valueAt)
    {
        Vector3D voxelSize = spacing ?? IsotropicSpacing;

        // Place the origin so the geometric centre of the volume lands on the patient
        // origin. Every phantom is then centred at (0,0,0) and tests never have to
        // carry a centre around.
        Point3D origin = new(
            -(dimX - 1) / 2.0 * voxelSize.X,
            -(dimY - 1) / 2.0 * voxelSize.Y,
            -(dimZ - 1) / 2.0 * voxelSize.Z);

        // PixelSpacing[0] is the gap between rows and scales the column cosine (y);
        // PixelSpacing[1] is the gap between columns and scales the row cosine (x).
        Matrix4x4Affine voxelToPatient = Matrix4x4Affine.FromImagePlane(
            rowCosine: new Vector3D(1, 0, 0),
            columnCosine: new Vector3D(0, 1, 0),
            adjacentRowSpacing: voxelSize.Y,
            adjacentColumnSpacing: voxelSize.X,
            sliceStep: new Vector3D(0, 0, voxelSize.Z),
            origin: origin);

        short[] voxels = new short[(long)dimX * dimY * dimZ];
        int index = 0;
        for (int k = 0; k < dimZ; k++)
        {
            for (int j = 0; j < dimY; j++)
            {
                for (int i = 0; i < dimX; i++)
                {
                    voxels[index++] = valueAt(i, j, k, voxelToPatient.Transform(i, j, k));
                }
            }
        }

        return new Volume(voxels, dimX, dimY, dimZ, voxelToPatient, new VolumeMetadata
        {
            StudyInstanceUid = "1.2.826.0.1.3680043.9.7133.1",
            SeriesInstanceUid = "1.2.826.0.1.3680043.9.7133.2",
            FrameOfReferenceUid = "1.2.826.0.1.3680043.9.7133.3",
            Modality = "CT",
            SeriesDescription = description,
        });
    }
}
