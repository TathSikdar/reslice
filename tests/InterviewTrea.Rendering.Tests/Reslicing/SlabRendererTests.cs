using System;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Reslicing;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering.Reslicing;
using InterviewTrea.Rendering.Windowing;
using InterviewTrea.TestData;
using Xunit;

namespace InterviewTrea.Rendering.Tests.Reslicing;

/// <summary>
/// Slab projection (FR-207). The phantom is a linear ramp read through a sagittal slab,
/// so the slab integrates <em>along</em> the ramp and the three modes have three
/// different, separately derivable answers - which is the only way to tell them apart.
/// </summary>
public sealed class SlabRendererTests
{
    // 16 voxels at 1 mm, centred on the patient origin, so x runs -7.5 .. +7.5 mm and
    // voxel i sits at x = i - 7.5. With 8 HU per voxel from -128:
    //     HU(x) = -128 + 8 * (x + 7.5) = -68 + 8x
    // Window 256 / level 0 makes the output byte exactly HU + 128, so:
    //     byte(x) = 60 + 8x
    private static Volume Ramp() => Phantoms.GradientAlongX(
        startHounsfield: -128, hounsfieldPerVoxel: 8, dimX: 16, dimY: 8, dimZ: 8);

    private static byte ByteAt(double xMillimetres) => (byte)(60 + (8 * xMillimetres));

    private static WindowLevelLut UnitLut() => new(new WindowLevel(256, 0));

    /// <summary>
    /// Reads the centre pixel of a sagittal slab through <paramref name="centreX"/>.
    /// Sagittal is the orientation whose normal is the x axis, so the slab runs along the
    /// ramp; a coronal or axial slab of this phantom is constant through its thickness and
    /// all three modes would agree, proving nothing.
    /// </summary>
    private static byte SlabCentre(SlabMode mode, double thicknessMm, double centreX = 0)
    {
        Volume volume = Ramp();
        ReslicePlane plane = ReslicePlane.Through(
            volume, PlaneOrientation.Sagittal, new Point3D(centreX, 0, 0), 1.0);

        byte[] image = new byte[plane.PixelCount];
        SlabRenderer.Render(volume, plane, mode, thicknessMm, UnitLut(), image);

        (double column, double row) = plane.ToPixel(new Point3D(centreX, 0, 0));
        return image[((int)Math.Round(row) * plane.Width) + (int)Math.Round(column)];
    }

    /// <summary>
    /// A 10 mm slab centred at x = 0 spans -5 .. +5 mm. On a linear ramp the maximum is
    /// the far end, the minimum the near end, and the mean - of samples placed
    /// symmetrically about the centre - is exactly the centre value. Three different
    /// numbers from one phantom.
    /// </summary>
    [Theory]
    [InlineData(SlabMode.Maximum, 5.0)]
    [InlineData(SlabMode.Minimum, -5.0)]
    [InlineData(SlabMode.Average, 0.0)]
    public void EachModeCollapsesTheSlabItsOwnWay(SlabMode mode, double expectedAtX)
    {
        SlabCentre(mode, thicknessMm: 10.0).Should().Be(ByteAt(expectedAtX));
    }

    /// <summary>
    /// Thickness has to move the answer, or the control is decorative. Doubling the slab
    /// doubles how far the maximum reaches up the ramp.
    /// </summary>
    [Fact]
    public void AThickerSlabReachesFurtherUpTheRamp()
    {
        SlabCentre(SlabMode.Maximum, 4.0).Should().Be(ByteAt(2.0));
        SlabCentre(SlabMode.Maximum, 8.0).Should().Be(ByteAt(4.0));
    }

    /// <summary>
    /// The thickness control must be able to pass through "no slab" without the viewport
    /// going blank or throwing, so a slab thinner than one sample pitch collapses to the
    /// plain plane render - byte for byte, in every mode.
    /// </summary>
    [Theory]
    [InlineData(SlabMode.Maximum)]
    [InlineData(SlabMode.Minimum)]
    [InlineData(SlabMode.Average)]
    public void ASlabThinnerThanOneSampleIsThePlaneItself(SlabMode mode)
    {
        Volume volume = Ramp();
        ReslicePlane plane = ReslicePlane.Through(
            volume, PlaneOrientation.Sagittal, Point3D.Origin, 1.0);
        WindowLevelLut lut = UnitLut();

        byte[] expected = new byte[plane.PixelCount];
        PlaneRenderer.Render(volume, plane, lut, expected);

        byte[] actual = new byte[plane.PixelCount];
        SlabRenderer.Render(volume, plane, mode, 0.4, lut, actual);

        actual.Should().Equal(expected);
    }

    /// <summary>
    /// The trap that makes minimum-intensity projection different from maximum. A slab at
    /// the edge of the volume has half its samples outside, and the sampler reports those
    /// as -1024. Folding them in would make MIP unchanged - air is never the brightest
    /// thing - while MinIP returned solid air for the whole border and the average was
    /// dragged down everywhere near an edge.
    /// </summary>
    [Fact]
    public void SamplesOutsideTheVolumeAreSkippedRatherThanCountedAsAir()
    {
        // Centred on the last voxel plane, so a 10 mm slab has its outer half in nothing.
        // The in-bounds samples run x = 2.5 .. 7.5.
        const double edgeX = 7.5;

        SlabCentre(SlabMode.Minimum, 10.0, edgeX).Should().Be(ByteAt(2.5));
        SlabCentre(SlabMode.Maximum, 10.0, edgeX).Should().Be(ByteAt(7.5));

        // Mean of the six in-bounds samples at 2.5, 3.5 ... 7.5, which is 5.0.
        SlabCentre(SlabMode.Average, 10.0, edgeX).Should().Be(ByteAt(5.0));
    }

    /// <summary>
    /// Samples along the normal are spaced at the plane's own pixel pitch, not at some
    /// fixed millimetre step. A one-voxel sheet at x = 0, read by a 2 mm slab centred at
    /// x = 0.5: a quarter-millimetre grid lands a sample exactly on the sheet and the MIP
    /// reports bone, while a one-millimetre grid samples at -0.5, 0.5 and 1.5, sees only
    /// the half-and-half interpolated shoulders, and reports the mean of bone and air.
    /// </summary>
    /// <remarks>
    /// The ramp used by the other tests cannot show this. The maximum of a monotonic
    /// function over an interval is its endpoint however many samples you take, so the
    /// ramp gives the same answer at every pitch and a hard-coded pitch survives it
    /// untouched. Detecting an aliasing bug needs something to alias.
    /// </remarks>
    [Fact]
    public void TheDepthPitchFollowsTheGridRatherThanBeingFixed()
    {
        Volume volume = Phantoms.SheetAcrossX();
        WindowLevelLut lut = new(new WindowLevel(4000, 0));

        byte MipAt(double pixelSizeMm)
        {
            Point3D crosshair = new(0.5, 0, 0);
            ReslicePlane plane = ReslicePlane.Through(
                volume, PlaneOrientation.Sagittal, crosshair, pixelSizeMm);

            byte[] image = new byte[plane.PixelCount];
            SlabRenderer.Render(volume, plane, SlabMode.Maximum, 2.0, lut, image);

            (double column, double row) = plane.ToPixel(crosshair);
            return image[((int)Math.Round(row) * plane.Width) + (int)Math.Round(column)];
        }

        MipAt(0.25).Should().Be(lut[Phantoms.Bone]);

        // (700 + -1000) / 2 = -150: the trilinear value half a voxel either side of the
        // sheet, which is all a one-millimetre pitch ever gets to see.
        MipAt(1.0).Should().Be(lut[-150]);
    }

    [Fact]
    public void APixelWithNoSamplesInsideTheVolumeReadsAsOutside()
    {
        Volume volume = Ramp();
        WindowLevelLut lut = UnitLut();

        // A one-pixel plane parked well clear of the volume in every direction.
        ReslicePlane plane = new(
            new Point3D(500, 500, 500), Vector3D.UnitX, Vector3D.UnitY, 1, 1);

        byte[] image = new byte[1];
        SlabRenderer.Render(volume, plane, SlabMode.Minimum, 10.0, lut, image);

        image[0].Should().Be(lut[Volume.OutsideValue]);
    }

    [Fact]
    public void ANonPositiveThicknessIsRejected()
    {
        Volume volume = Ramp();
        ReslicePlane plane = ReslicePlane.Through(
            volume, PlaneOrientation.Sagittal, Point3D.Origin, 1.0);

        Action render = () => SlabRenderer.Render(
            volume, plane, SlabMode.Maximum, 0, UnitLut(), new byte[plane.PixelCount]);

        render.Should().Throw<ArgumentOutOfRangeException>();
    }
}
