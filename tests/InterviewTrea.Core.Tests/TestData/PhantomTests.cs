using System;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Tests.Geometry;
using InterviewTrea.Core.Volumes;
using InterviewTrea.TestData;
using Xunit;

namespace InterviewTrea.Core.Tests.TestData;

/// <summary>
/// The phantoms are the ground truth every downstream numeric test leans on, so they
/// get checked against closed-form geometry themselves. A wrong phantom would make a
/// whole suite green and meaningless.
/// </summary>
public class PhantomTests
{
    [Fact]
    public void Uniform_HoldsTheSameValueEverywhere()
    {
        Volume volume = Phantoms.Uniform(Phantoms.SoftTissue);

        foreach (short voxel in volume.Voxels)
        {
            voxel.Should().Be(Phantoms.SoftTissue);
        }
    }

    [Fact]
    public void EveryPhantom_IsCentredOnThePatientOrigin()
    {
        Volume volume = Phantoms.Uniform(0, dimX: 64, dimY: 64, dimZ: 32);

        Point3D centre = volume.VoxelToPatient.Transform(
            (volume.DimX - 1) / 2.0,
            (volume.DimY - 1) / 2.0,
            (volume.DimZ - 1) / 2.0);

        centre.ShouldBeApproximately(Point3D.Origin, 1e-12);
    }

    [Fact]
    public void GradientAlongX_StepsByExactlyTheGivenAmountPerVoxel()
    {
        Volume volume = Phantoms.GradientAlongX(startHounsfield: 0, hounsfieldPerVoxel: 100);

        for (int i = 0; i < volume.DimX; i++)
        {
            volume[i, 3, 4].Should().Be((short)(i * 100));
        }
    }

    [Fact]
    public void GradientAlongX_DoesNotVaryWithYOrZ()
    {
        Volume volume = Phantoms.GradientAlongX();

        volume[5, 0, 0].Should().Be(volume[5, 7, 7]);
    }

    /// <summary>
    /// Counting the occupied voxels and multiplying by the voxel volume must reproduce
    /// (4/3)pi r^3. The tolerance is a bound, not a fit: discretising a sphere misplaces
    /// roughly half a voxel across its surface, and for a 12 mm sphere at 1 mm voxels
    /// that shell is a few percent of the total. Errors partly cancel, so 5% is
    /// comfortable without being so loose it would accept a wrong radius.
    /// <para>
    /// Measured at the time of writing: 7208.0 against an analytic 7238.2, an error of
    /// 0.42%. The anisotropic case below comes in at 1.06%. Both figures are recorded so
    /// that a change which quietly doubles the error is visible in review rather than
    /// simply still passing.
    /// </para>
    /// </summary>
    [Fact]
    public void Sphere_OccupiesTheAnalyticVolume()
    {
        const double radiusMm = 12.0;
        double expected = 4.0 / 3.0 * Math.PI * Math.Pow(radiusMm, 3);

        Volume volume = Phantoms.Sphere(radiusMm);

        OccupiedVolumeMm3(volume, Phantoms.Bone)
            .Should().BeApproximately(expected, expected * 0.05);
    }

    /// <summary>
    /// FR-208. The same sphere at 0.7 x 0.7 x 3.0 mm must occupy the same number of
    /// cubic millimetres, even though it occupies far fewer voxels. If spacing were
    /// ignored anywhere in the transform this number would come out wrong by the ratio
    /// of the axes, which is the "squashed patient" failure in visible form.
    /// </summary>
    [Fact]
    public void Sphere_OccupiesTheSamePhysicalVolumeAtAnisotropicSpacing()
    {
        const double radiusMm = 12.0;
        double expected = 4.0 / 3.0 * Math.PI * Math.Pow(radiusMm, 3);

        Volume isotropic = Phantoms.Sphere(radiusMm);
        Volume anisotropic = Phantoms.Sphere(radiusMm, spacing: Phantoms.ChestSpacing);

        // Far fewer voxels...
        anisotropic.Voxels.Length.Should().Be(isotropic.Voxels.Length);
        CountOf(anisotropic, Phantoms.Bone).Should().BeLessThan(CountOf(isotropic, Phantoms.Bone));

        // ...but the same physical volume.
        OccupiedVolumeMm3(anisotropic, Phantoms.Bone)
            .Should().BeApproximately(expected, expected * 0.05);
    }

    [Fact]
    public void Cube_OccupiesEdgeLengthCubed()
    {
        const double edgeMm = 20.0;
        double expected = Math.Pow(edgeMm, 3);

        Volume volume = Phantoms.Cube(edgeMm);

        // A cube discretises far more cleanly than a sphere - its faces are voxel
        // aligned - so this tolerance is tight on purpose.
        OccupiedVolumeMm3(volume, Phantoms.Bone)
            .Should().BeApproximately(expected, expected * 0.02);
    }

    [Fact]
    public void Cube_SpansTheEdgeLengthAlongEachPatientAxis()
    {
        const double edgeMm = 20.0;

        Volume volume = Phantoms.Cube(edgeMm);

        double minX = double.MaxValue;
        double maxX = double.MinValue;
        for (int i = 0; i < volume.DimX; i++)
        {
            if (volume[i, volume.DimY / 2, volume.DimZ / 2] != Phantoms.Bone)
            {
                continue;
            }

            double x = volume.VoxelToPatient.Transform(i, 0, 0).X;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
        }

        // Voxel centres, so the span is one voxel short of the full extent.
        (maxX - minX).Should().BeApproximately(edgeMm, 1.0);
    }

    [Fact]
    public void Checker_AlternatesEveryPeriod()
    {
        Volume volume = Phantoms.Checker(periodVoxels: 4);

        volume[0, 0, 0].Should().Be(Phantoms.Air);
        volume[3, 0, 0].Should().Be(Phantoms.Air);
        volume[4, 0, 0].Should().Be(Phantoms.Bone);
        volume[0, 4, 0].Should().Be(Phantoms.Bone);
        volume[0, 0, 4].Should().Be(Phantoms.Bone);
        volume[4, 4, 0].Should().Be(Phantoms.Air);
    }

    private static int CountOf(Volume volume, short hounsfield)
    {
        int count = 0;
        foreach (short voxel in volume.Voxels)
        {
            if (voxel == hounsfield)
            {
                count++;
            }
        }

        return count;
    }

    private static double OccupiedVolumeMm3(Volume volume, short hounsfield)
    {
        double voxelVolume = volume.Spacing.X * volume.Spacing.Y * volume.Spacing.Z;
        return CountOf(volume, hounsfield) * voxelVolume;
    }
}
