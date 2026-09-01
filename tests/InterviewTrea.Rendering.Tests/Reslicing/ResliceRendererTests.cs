using System;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering.Reslicing;
using InterviewTrea.Rendering.Windowing;
using Xunit;

namespace InterviewTrea.Rendering.Tests.Reslicing;

/// <summary>
/// The window W256/L0 is chosen so the whole render is checkable in one's head: it maps
/// Hounsfield value x to grey level x + 128 exactly, for every x the phantom contains.
/// Any expected byte below is therefore just the voxel's HU plus 128, and a mismatch
/// points at the render loop rather than at the transform, which has its own tests.
/// </summary>
public sealed class ResliceRendererTests
{
    private static readonly WindowLevelLut Unit = new(new WindowLevel(Width: 256, Center: 0));

    private const int DimX = 4;
    private const int DimY = 3;
    private const int DimZ = 2;

    /// <summary>
    /// Voxel (i, j, k) holds i + 10j + 100k HU. Each axis contributes its own decimal
    /// place, so a transposed or misindexed read is not merely wrong, it is wrong in a
    /// way that names the axis that went wrong. Kept local rather than promoted into
    /// InterviewTrea.TestData: two test projects want a separable ramp, and two is not
    /// yet enough to justify a shared generator.
    /// </summary>
    private static Volume SeparableRamp()
    {
        short[] voxels = new short[DimX * DimY * DimZ];
        int index = 0;
        for (int k = 0; k < DimZ; k++)
        {
            for (int j = 0; j < DimY; j++)
            {
                for (int i = 0; i < DimX; i++)
                {
                    voxels[index++] = (short)(i + (10 * j) + (100 * k));
                }
            }
        }

        Matrix4x4Affine affine = Matrix4x4Affine.FromImagePlane(
            rowCosine: new Vector3D(1, 0, 0),
            columnCosine: new Vector3D(0, 1, 0),
            adjacentRowSpacing: 1,
            adjacentColumnSpacing: 1,
            sliceStep: new Vector3D(0, 0, 1),
            origin: new Point3D(0, 0, 0));

        return new Volume(voxels, DimX, DimY, DimZ, affine, new VolumeMetadata
        {
            StudyInstanceUid = "1.2.3",
            SeriesInstanceUid = "1.2.3.4",
            FrameOfReferenceUid = "1.2.3.5",
            Modality = "CT",
        });
    }

    /// <summary>
    /// The output must be row-major with x fastest, matching both the volume's storage and
    /// what a Gray8 bitmap expects. Transposing it produces an image that still looks like
    /// anatomy - rotated - which is exactly why it needs an assertion rather than an eye.
    /// </summary>
    [Fact]
    public void RenderAxial_WritesRowMajorWithXFastest()
    {
        byte[] destination = new byte[DimX * DimY];

        ResliceRenderer.RenderAxial(SeparableRamp(), 0, Unit, destination);

        for (int j = 0; j < DimY; j++)
        {
            for (int i = 0; i < DimX; i++)
            {
                destination[(j * DimX) + i].Should().Be((byte)(i + (10 * j) + 128));
            }
        }
    }

    [Fact]
    public void RenderAxial_SelectsTheRequestedSlice()
    {
        byte[] destination = new byte[DimX * DimY];

        ResliceRenderer.RenderAxial(SeparableRamp(), 1, Unit, destination);

        // Every value on slice 1 is 100 HU higher than its counterpart on slice 0.
        destination[0].Should().Be(228);
        destination[^1].Should().Be((byte)(3 + 20 + 100 + 128));
    }

    /// <summary>
    /// Clipping happens in the table, not in the loop, so a value far outside the window
    /// must still land on black or white rather than wrapping. A wrapped index would turn
    /// dense bone into black and is the failure this indexing scheme exists to prevent.
    /// </summary>
    [Fact]
    public void RenderAxial_ClipsRatherThanWrapsOutsideTheWindow()
    {
        short[] voxels = [-2000, -1000, 0, 3000, short.MinValue, short.MaxValue];
        Matrix4x4Affine affine = Matrix4x4Affine.FromImagePlane(
            new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), 1, 1, new Vector3D(0, 0, 1), new Point3D(0, 0, 0));

        Volume volume = new(voxels, 3, 2, 1, affine, SeparableRamp().Metadata);
        byte[] destination = new byte[6];

        ResliceRenderer.RenderAxial(volume, 0, Unit, destination);

        destination.Should().Equal(0, 0, 128, 255, 0, 255);
    }

    [Fact]
    public void RenderAxial_AppliesTheCurrentWindow()
    {
        Volume volume = SeparableRamp();
        WindowLevelLut lut = new(WindowLevel.Bone);
        byte[] destination = new byte[DimX * DimY];

        ResliceRenderer.RenderAxial(volume, 0, lut, destination);

        // Bone is W1800/L400: a band from -500 to +1300 HU, so this phantom's 0..23 HU
        // sits low in it and compresses into four grey levels near 71. That flatness is
        // the preset doing its job - a bone window is not meant to resolve soft tissue.
        destination[0].Should().Be(71);
        destination[^1].Should().Be(74);

        lut.Rebuild(new WindowLevel(Width: 256, Center: 0));
        ResliceRenderer.RenderAxial(volume, 0, lut, destination);

        destination[0].Should().Be(128);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void RenderAxial_RejectsASliceIndexOutsideTheVolume(int sliceIndex)
    {
        byte[] destination = new byte[DimX * DimY];

        Action render = () => ResliceRenderer.RenderAxial(SeparableRamp(), sliceIndex, Unit, destination);

        render.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RenderAxial_RejectsAWronglySizedDestination()
    {
        Action render = () => ResliceRenderer.RenderAxial(SeparableRamp(), 0, Unit, new byte[DimX * DimY - 1]);

        render.Should().Throw<ArgumentException>();
    }
}
