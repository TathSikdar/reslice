using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;
using InterviewTrea.TestData;
using Xunit;

namespace InterviewTrea.Core.Tests.Volumes;

public class VolumeSamplingTests
{
    private const int RampDim = 4;

    /// <summary>
    /// A volume where voxel (i, j, k) holds <c>i + 10j + 100k</c>.
    /// </summary>
    /// <remarks>
    /// Each axis carries a distinct, separable ramp, so a transposed axis or a swapped
    /// stride changes the answer instead of cancelling out. The function is affine in
    /// each variable, which is exactly the class trilinear interpolation reproduces
    /// exactly - so every expected value below is the function evaluated on paper.
    /// Kept local rather than added to <see cref="Phantoms"/> because nothing else needs
    /// it yet.
    /// </remarks>
    private static Volume SeparableRamp()
    {
        short[] voxels = new short[RampDim * RampDim * RampDim];
        int index = 0;
        for (int k = 0; k < RampDim; k++)
        {
            for (int j = 0; j < RampDim; j++)
            {
                for (int i = 0; i < RampDim; i++)
                {
                    voxels[index++] = (short)(i + (10 * j) + (100 * k));
                }
            }
        }

        Matrix4x4Affine voxelToPatient = Matrix4x4Affine.FromImagePlane(
            rowCosine: new Vector3D(1, 0, 0),
            columnCosine: new Vector3D(0, 1, 0),
            adjacentRowSpacing: 1.0,
            adjacentColumnSpacing: 1.0,
            sliceStep: new Vector3D(0, 0, 1),
            origin: Point3D.Origin);

        return new Volume(voxels, RampDim, RampDim, RampDim, voxelToPatient, new VolumeMetadata
        {
            StudyInstanceUid = "1.2.3",
            SeriesInstanceUid = "1.2.3.4",
            FrameOfReferenceUid = "1.2.3.5",
            Modality = "CT",
        });
    }

    [Fact]
    public void Trilinear_AtVoxelCentres_ReturnsTheStoredValue()
    {
        Volume volume = SeparableRamp();

        for (int k = 0; k < RampDim; k++)
        {
            for (int j = 0; j < RampDim; j++)
            {
                for (int i = 0; i < RampDim; i++)
                {
                    volume.SampleTrilinear(i, j, k).Should().Be(volume[i, j, k]);
                }
            }
        }
    }

    /// <summary>
    /// The gradient is 100 HU per voxel, so the point halfway between voxel 0 and voxel 1
    /// must read exactly 50. No tolerance: linear data through a linear interpolator has
    /// no rounding error to absorb.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.25, 25.0)]
    [InlineData(0.5, 50.0)]
    [InlineData(1.0, 100.0)]
    [InlineData(2.5, 250.0)]
    public void Trilinear_AlongAGradient_ReturnsTheAnalyticValue(double x, double expected) =>
        Phantoms.GradientAlongX(hounsfieldPerVoxel: 100).SampleTrilinear(x, 0, 0)
            .Should().Be(expected);

    /// <summary>
    /// GradientAlongX is constant in y and z, so moving within a slice or between slices
    /// must not change the reading. This is the test that catches a swapped stride.
    /// </summary>
    [Fact]
    public void Trilinear_IgnoresAxesTheFieldIsConstantIn() =>
        Phantoms.GradientAlongX(hounsfieldPerVoxel: 100).SampleTrilinear(0.5, 3.7, 2.2)
            .Should().Be(50.0);

    /// <summary>
    /// i + 10j + 100k evaluated at each half step. Every axis contributes a different
    /// amount, so transposing any two of them produces a different number.
    /// </summary>
    [Theory]
    [InlineData(0.5, 0.0, 0.0, 0.5)]
    [InlineData(0.0, 0.5, 0.0, 5.0)]
    [InlineData(0.0, 0.0, 0.5, 50.0)]
    [InlineData(0.5, 0.5, 0.5, 55.5)]
    [InlineData(1.25, 2.5, 0.75, 101.25)]
    public void Trilinear_SeparatesTheThreeAxes(double x, double y, double z, double expected) =>
        SeparableRamp().SampleTrilinear(x, y, z).Should().Be(expected);

    [Fact]
    public void Trilinear_OfAUniformVolume_ReturnsThatValue() =>
        Phantoms.Uniform(300).SampleTrilinear(12.3, 40.7, 8.1).Should().Be(300.0);

    /// <summary>
    /// The far face is inside the volume, but its +1 neighbour is not. The weight there
    /// is zero, so the answer is the last voxel - and reaching it must not fault.
    /// </summary>
    [Fact]
    public void Trilinear_OnTheFarFace_ReturnsTheLastVoxel()
    {
        Volume volume = SeparableRamp();

        volume.SampleTrilinear(RampDim - 1, RampDim - 1, RampDim - 1)
            .Should().Be(volume[RampDim - 1, RampDim - 1, RampDim - 1]);
    }

    [Theory]
    [InlineData(-0.001, 0, 0)]
    [InlineData(0, -0.001, 0)]
    [InlineData(0, 0, -0.001)]
    [InlineData(RampDim - 1 + 0.001, 0, 0)]
    [InlineData(0, RampDim - 1 + 0.001, 0)]
    [InlineData(0, 0, RampDim - 1 + 0.001)]
    [InlineData(double.NaN, 0, 0)]
    public void Trilinear_OutsideTheVolume_ReturnsAir(double x, double y, double z) =>
        SeparableRamp().SampleTrilinear(x, y, z).Should().Be(Volume.OutsideValue);

    [Fact]
    public void Nearest_AtVoxelCentres_ReturnsTheStoredValue()
    {
        Volume volume = SeparableRamp();

        for (int k = 0; k < RampDim; k++)
        {
            for (int j = 0; j < RampDim; j++)
            {
                for (int i = 0; i < RampDim; i++)
                {
                    volume.SampleNearest(i, j, k).Should().Be(volume[i, j, k]);
                }
            }
        }
    }

    /// <summary>
    /// Ties round up. Pinned so the rule is a decision rather than an accident - but note
    /// that it only ever applies to an exact half, and coordinates that arrive through a
    /// transform never are exactly half. The rule is for reproducibility, not for a case
    /// the renderer will actually hit.
    /// </summary>
    [Theory]
    [InlineData(0.49, 0)]
    [InlineData(0.5, 1)]
    [InlineData(1.5, 2)]
    public void Nearest_RoundsTiesUp(double x, short expected) =>
        SeparableRamp().SampleNearest(x, 0, 0).Should().Be(expected);

    [Fact]
    public void Nearest_OutsideTheVolume_ReturnsAir() =>
        SeparableRamp().SampleNearest(-0.001, 0, 0).Should().Be(Volume.OutsideValue);

    /// <summary>
    /// Anisotropic spacing, so this fails if the patient-space overload skips the inverse
    /// transform or applies it with the axes in the wrong order.
    /// </summary>
    [Fact]
    public void Sampling_InPatientSpace_MatchesVoxelSpace()
    {
        Volume volume = Phantoms.GradientAlongX(
            hounsfieldPerVoxel: 100,
            spacing: Phantoms.ChestSpacing);

        volume.SampleTrilinear(volume.VoxelToPatient.Transform(0.5, 0, 0))
            .Should().BeApproximately(50.0, 1e-9);

        // 0.6 rather than 0.5: a round trip through the inverse lands a hair either side
        // of an exact half, so a tie here would be testing floating-point luck.
        volume.SampleNearest(volume.VoxelToPatient.Transform(0.6, 0, 0))
            .Should().Be(100);
    }
}
