using System;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Tests.Geometry;
using InterviewTrea.Core.Volumes;
using Xunit;

namespace InterviewTrea.Core.Tests.Volumes;

public class VolumeTests
{
    private const int DimX = 4;
    private const int DimY = 3;
    private const int DimZ = 2;

    private static VolumeMetadata Metadata() => new()
    {
        StudyInstanceUid = "1.2.3",
        SeriesInstanceUid = "1.2.3.4",
        FrameOfReferenceUid = "1.2.3.5",
        Modality = "CT",
    };

    /// <summary>Anisotropic on purpose: 0.7 x 0.7 x 3.0 mm is a typical chest CT (FR-208).</summary>
    private static Matrix4x4Affine Transform() => Matrix4x4Affine.FromImagePlane(
        rowCosine: new Vector3D(1, 0, 0),
        columnCosine: new Vector3D(0, 1, 0),
        adjacentRowSpacing: 0.7,
        adjacentColumnSpacing: 0.7,
        sliceStep: new Vector3D(0, 0, 3.0),
        origin: new Point3D(-100, -100, 50));

    private static Volume Build(short[]? voxels = null) => new(
        voxels ?? new short[DimX * DimY * DimZ],
        DimX,
        DimY,
        DimZ,
        Transform(),
        Metadata());

    [Fact]
    public void Constructor_RejectsAVoxelArrayThatDoesNotMatchTheDimensions()
    {
        Action act = () => _ = new Volume(new short[10], DimX, DimY, DimZ, Transform(), Metadata());

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// The storage layout the whole rendering strategy depends on: x fastest, then y,
    /// then z. Stepping one voxel in x moves one element; one in y moves a row; one in z
    /// moves a whole slice. That is why an axial read is contiguous and coronal is not.
    /// </summary>
    [Fact]
    public void IndexOf_LaysVoxelsOutWithXFastest()
    {
        Volume volume = Build();

        int origin = volume.IndexOf(0, 0, 0);

        (volume.IndexOf(1, 0, 0) - origin).Should().Be(1);
        (volume.IndexOf(0, 1, 0) - origin).Should().Be(DimX);
        (volume.IndexOf(0, 0, 1) - origin).Should().Be(DimX * DimY);
    }

    [Fact]
    public void Indexer_ReturnsTheVoxelAtThatCoordinate()
    {
        short[] voxels = new short[DimX * DimY * DimZ];
        // (2, 1, 1) -> 1*12 + 1*4 + 2 = 18
        voxels[18] = 1234;

        Build(voxels)[2, 1, 1].Should().Be((short)1234);
    }

    /// <summary>
    /// The reason the indexer validates each axis instead of trusting the array's own
    /// bounds check: i == DimX is out of range, but the flat index it produces is
    /// perfectly valid and lands on the first voxel of the next row. That would read as
    /// a faint rendering artefact rather than an error, which is the worst outcome.
    /// </summary>
    [Fact]
    public void Indexer_RejectsAnOutOfRangeIndexThatWouldSilentlyWrapToTheNextRow()
    {
        Volume volume = Build();

        volume.IndexOf(DimX, 0, 0).Should().Be(volume.IndexOf(0, 1, 0), "the wrap is real");

        Action act = () => _ = volume[DimX, 0, 0];

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(DimX, 0, 0)]
    [InlineData(0, DimY, 0)]
    [InlineData(0, 0, DimZ)]
    public void Contains_IsFalseOutsideEveryFace(int i, int j, int k)
    {
        Build().Contains(i, j, k).Should().BeFalse();
    }

    [Fact]
    public void Spacing_IsDerivedFromTheAffineColumnMagnitudes()
    {
        Build().Spacing.ShouldBeApproximately(new Vector3D(0.7, 0.7, 3.0), 1e-12);
    }

    [Fact]
    public void Origin_IsTheImagePositionOfTheFirstSlice()
    {
        Build().Origin.Should().Be(new Point3D(-100, -100, 50));
    }

    [Fact]
    public void PatientToVoxel_IsTheInverseOfVoxelToPatient()
    {
        Volume volume = Build();

        Point3D patient = volume.VoxelToPatient.Transform(2, 1, 1);

        volume.PatientToVoxel.Transform(patient)
            .ShouldBeApproximately(new Point3D(2, 1, 1), 1e-9);
    }

    [Fact]
    public void Constructor_RejectsAVolumeWithACollapsedAxis()
    {
        // Zero slice spacing has no inverse, so it cannot be constructed at all rather
        // than failing later inside a render loop.
        Matrix4x4Affine singular = Matrix4x4Affine.FromImagePlane(
            new Vector3D(1, 0, 0),
            new Vector3D(0, 1, 0),
            0.7,
            0.7,
            Vector3D.Zero,
            Point3D.Origin);

        Action act = () => _ = new Volume(
            new short[DimX * DimY * DimZ], DimX, DimY, DimZ, singular, Metadata());

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>DI-3: de-identified data routinely omits these, and that must not throw.</summary>
    [Fact]
    public void Metadata_AcceptsAbsentOptionalIdentifiers()
    {
        VolumeMetadata metadata = Metadata();

        metadata.SeriesDescription.Should().BeNull();
        metadata.PatientName.Should().BeNull();
        metadata.StudyDate.Should().BeNull();
        metadata.WindowCenter.Should().BeNull();
    }
}
