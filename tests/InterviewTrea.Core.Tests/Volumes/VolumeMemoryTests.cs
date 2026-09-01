using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;
using Xunit;

namespace InterviewTrea.Core.Tests.Volumes;

public class VolumeMemoryTests
{
    /// <summary>
    /// NFR-101: a 512 x 512 x 400 volume shall occupy under 300 MB of managed heap.
    /// </summary>
    /// <remarks>
    /// This really does allocate the array rather than assert the arithmetic, because
    /// the arithmetic is not the risk - the risk is the storage decision. It proves
    /// 104,857,600 shorts fit in a single .NET array (comfortably under the 2 GB
    /// per-object ceiling) and that the choice of short over float is what buys it:
    /// the same volume as float[] would be 400 MB and fail this test.
    /// </remarks>
    [Fact]
    public void AFullChestVolume_FitsTheMemoryBudget()
    {
        const int dim = 512;
        const int slices = 400;

        Volume volume = new(
            new short[(long)dim * dim * slices],
            dim,
            dim,
            slices,
            Matrix4x4Affine.FromImagePlane(
                new Vector3D(1, 0, 0),
                new Vector3D(0, 1, 0),
                0.68,
                0.68,
                new Vector3D(0, 0, 1.0),
                Point3D.Origin),
            new VolumeMetadata
            {
                StudyInstanceUid = "1.2.3",
                SeriesInstanceUid = "1.2.3.4",
                FrameOfReferenceUid = "1.2.3.5",
                Modality = "CT",
            });

        volume.VoxelCount.Should().Be(104_857_600);
        volume.ByteCount.Should().BeLessThan(300L * 1024 * 1024);

        // The same data as float would be 400 MB - state the margin, do not imply it.
        volume.ByteCount.Should().Be(209_715_200);
    }
}
