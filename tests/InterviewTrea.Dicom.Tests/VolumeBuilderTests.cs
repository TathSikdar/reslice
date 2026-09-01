using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Dicom.Tests.TestData;
using Xunit;

namespace InterviewTrea.Dicom.Tests;

/// <summary>
/// The end of the Iteration 1 pipeline: a directory of files becomes a volume whose
/// geometry and Hounsfield units are both checkable by hand.
/// </summary>
public sealed class VolumeBuilderTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "itrea-build-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private VolumeBuildResult Build(SyntheticSeries series)
    {
        string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        series.WriteTo(directory);

        SeriesDescriptor descriptor = new SeriesLoader().Scan(directory).Series.Single();
        SeriesGeometry geometry = new GeometryValidator().Validate(descriptor.Slices);

        return new VolumeBuilder().Build(descriptor, geometry);
    }

    [Fact]
    public void Build_ProducesTheExpectedDimensionsAndSpacing()
    {
        VolumeBuildResult result = Build(new SyntheticSeries());
        Volume volume = result.Volume;

        volume.DimX.Should().Be(8);
        volume.DimY.Should().Be(6);
        volume.DimZ.Should().Be(5);

        // Spacing is x, y, z in patient millimetres. PixelSpacing was [0.7, 0.5], so the
        // x step is 0.5 and the y step is 0.7 - the crossover, end to end. Reversing it
        // would give 0.7 x 0.5 here and a subtly stretched image everywhere downstream.
        volume.Spacing.X.Should().BeApproximately(0.5, 1e-9);
        volume.Spacing.Y.Should().BeApproximately(0.7, 1e-9);
        volume.Spacing.Z.Should().BeApproximately(3.0, 1e-9);

        volume.Origin.Should().Be(new Point3D(-100, -80, -60));
    }

    /// <summary>
    /// Voxel (i, j, k) must land where the affine says. Built by hand: the first slice sits
    /// at (-100, -80, -60), the x step is 0.5 mm, the y step 0.7 mm, the z step 3.0 mm.
    /// </summary>
    [Fact]
    public void Build_PlacesVoxelsAtTheRightPatientCoordinates()
    {
        Volume volume = Build(new SyntheticSeries()).Volume;

        volume.VoxelToPatient.Transform(0, 0, 0).Should().Be(new Point3D(-100, -80, -60));

        Point3D corner = volume.VoxelToPatient.Transform(7, 5, 4);
        corner.X.Should().BeApproximately(-100 + (7 * 0.5), 1e-9);
        corner.Y.Should().BeApproximately(-80 + (5 * 0.7), 1e-9);
        corner.Z.Should().BeApproximately(-60 + (4 * 3.0), 1e-9);
    }

    /// <summary>
    /// FR-104 through the whole pipeline. Stored value is 1024 + i + 10j + 100k with an
    /// intercept of -1024, so voxel (i, j, k) must hold exactly i + 10j + 100k HU.
    /// </summary>
    [Fact]
    public void Build_FillsTheVolumeWithHounsfieldUnits()
    {
        VolumeBuildResult result = Build(new SyntheticSeries());
        Volume volume = result.Volume;

        volume[0, 0, 0].Should().Be(0);
        volume[7, 0, 0].Should().Be(7);
        volume[0, 5, 0].Should().Be(50);
        volume[0, 0, 4].Should().Be(400);
        volume[7, 5, 4].Should().Be(457);

        result.MinimumHounsfield.Should().Be(0);
        result.MaximumHounsfield.Should().Be(457);
        result.SaturatedSampleCount.Should().Be(0);
    }

    /// <summary>
    /// FR-103 end to end: the files are written in reverse position order and numbered
    /// backwards, so anything that trusts file order or InstanceNumber stacks the volume
    /// upside down and the z gradient runs the wrong way.
    /// </summary>
    [Fact]
    public void Build_StacksByPositionRegardlessOfFileOrder()
    {
        SyntheticSeries series = new()
        {
            SliceSpacing = -3.0,
            ReverseInstanceNumbers = true,
        };

        Volume volume = Build(series).Volume;

        // Slice k = 4 was written last but sits lowest, so it becomes voxel plane 0.
        volume[0, 0, 0].Should().Be(400);
        volume[0, 0, 4].Should().Be(0);
        volume.Origin.Z.Should().BeApproximately(-72.0, 1e-9);
    }

    /// <summary>
    /// The affine is built from the measured step between slice positions, not from
    /// <c>spacing * normal</c>. For a perfectly axial series the two are identical, so this
    /// uses one degree of tilt - inside the validator's tolerance, and therefore a series
    /// that loads - where the measured step leans and the assumed one does not.
    /// </summary>
    [Fact]
    public void Build_UsesTheMeasuredSliceStepRatherThanTheAssumedOne()
    {
        Volume volume = Build(new SyntheticSeries { GantryTiltDegrees = 1.0 }).Volume;

        Point3D second = volume.VoxelToPatient.Transform(0, 0, 1);
        double radians = 1.0 * Math.PI / 180.0;

        // Leaning by one degree moves the next slice 3 * sin(1 degree) in y. Assuming
        // spacing * normal would leave y unchanged at -80.
        //
        // Tolerance is 1e-6, not 1e-9, and that is a property of DICOM rather than of the
        // arithmetic: ImagePositionPatient has VR DS, a decimal string of at most sixteen
        // characters, so a position near 100 mm survives a round trip to about eight
        // decimal places and no further. Real geometry is only ever as exact as the file.
        second.Y.Should().BeApproximately(-80 + (3.0 * Math.Sin(radians)), 1e-6);
        second.Z.Should().BeApproximately(-60 + (3.0 * Math.Cos(radians)), 1e-6);
        second.Y.Should().NotBeApproximately(-80, 1e-6);
    }

    [Fact]
    public void Build_CarriesTheSeriesMetadataOntoTheVolume()
    {
        Volume volume = Build(new SyntheticSeries()).Volume;

        volume.Metadata.Modality.Should().Be("CT");
        volume.Metadata.SeriesDescription.Should().Be("SYNTHETIC CHEST");
        volume.Metadata.FrameOfReferenceUid.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Build_ReportsSaturation()
    {
        VolumeBuildResult result = Build(new SyntheticSeries
        {
            RescaleIntercept = 0.0,
            StoredValueAt = (_, _, _) => 60000,
        });

        result.MaximumHounsfield.Should().Be(short.MaxValue);
        result.SaturatedSampleCount.Should().Be(6 * 8 * 5);
    }
}
