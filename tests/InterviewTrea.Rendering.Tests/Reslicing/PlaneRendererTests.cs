using System;
using System.Linq;
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
/// The generalized reslice path. Every expected value here is derived from the phantom
/// geometry, because a reslice that is squashed, mirrored or sampling at the wrong
/// spacing still produces a perfectly convincing greyscale image.
/// </summary>
public sealed class PlaneRendererTests
{
    /// <summary>
    /// Width 256, level 0. Chosen so the transform is exactly one grey level per
    /// Hounsfield unit: for HU in [-128, 127] the output byte is simply HU + 128, so an
    /// expected pixel value can be read straight off the phantom without arithmetic.
    /// </summary>
    private static WindowLevelLut UnitLut() => new(new WindowLevel(256, 0));

    private static byte[] Render(Volume volume, ReslicePlane plane, WindowLevelLut lut)
    {
        byte[] buffer = new byte[plane.PixelCount];
        PlaneRenderer.Render(volume, plane, lut, buffer);
        return buffer;
    }

    /// <summary>
    /// The two paths must agree. On a 1 mm isotropic volume the axial plane's grid is
    /// exactly the native voxel grid, so the generalized trilinear walk - sampling at
    /// integer coordinates, all interpolation weights zero - has to reproduce the
    /// Iteration 2 memcpy-style path byte for byte. That ties the new code to the old
    /// code's mutation testing rather than re-deriving it.
    /// </summary>
    [Fact]
    public void OnANativeGridTheGeneralPathMatchesTheAxialFastPath()
    {
        Volume volume = Phantoms.GradientAlongX(
            startHounsfield: -128, hounsfieldPerVoxel: 8, dimX: 16, dimY: 12, dimZ: 8);
        WindowLevelLut lut = UnitLut();

        const int slice = 3;
        Point3D crosshair = volume.VoxelToPatient.Transform(0, 0, slice);
        ReslicePlane plane = ReslicePlane.Through(volume, PlaneOrientation.Axial, crosshair, 1.0);

        plane.Width.Should().Be(volume.DimX);
        plane.Height.Should().Be(volume.DimY);

        byte[] expected = new byte[volume.DimX * volume.DimY];
        ResliceRenderer.RenderAxial(volume, slice, lut, expected);

        Render(volume, plane, lut).Should().Equal(expected);
    }

    /// <summary>
    /// The trilinear midpoint test, run through the renderer rather than the sampler.
    /// A ramp of 8 HU per 1 mm voxel sampled onto a 0.5 mm grid puts every odd output
    /// column exactly halfway between two voxels, where the value must be their mean.
    /// The whole row is therefore 4 grey levels per column: 0, 4, 8, 12, ... Nearest
    /// neighbour would give 0, 0, 8, 8; a half-pixel offset would give 4, 8, 12.
    /// </summary>
    [Fact]
    public void SamplingBetweenVoxelsInterpolatesRatherThanRepeating()
    {
        Volume volume = Phantoms.GradientAlongX(
            startHounsfield: -128, hounsfieldPerVoxel: 8, dimX: 16, dimY: 8, dimZ: 8);

        ReslicePlane plane = ReslicePlane.Through(
            volume, PlaneOrientation.Axial, Point3D.Origin, 0.5);

        // 15 mm between the outermost voxel centres, sampled every 0.5 mm, both ends
        // included.
        plane.Width.Should().Be(31);

        byte[] image = Render(volume, plane, UnitLut());
        int middleRow = plane.Height / 2;

        for (int c = 0; c < plane.Width; c++)
        {
            image[(middleRow * plane.Width) + c].Should().Be((byte)(4 * c), "column {0}", c);
        }
    }

    /// <summary>
    /// The interpolated value is rounded to the nearest Hounsfield unit before the
    /// window/level lookup, not truncated. Truncation is <c>floor</c> here - the biased
    /// input is never negative - so it would darken every interpolated pixel by half a
    /// grey level on average, systematically rather than symmetrically.
    /// </summary>
    /// <remarks>
    /// This needs a ramp of 1 HU per voxel: at 8 HU per voxel the midpoints are still
    /// whole numbers and rounding has nothing to do. Sampling where the arithmetic
    /// happens to be exact proves nothing about the rounding.
    /// </remarks>
    [Fact]
    public void HalfUnitSamplesRoundRatherThanTruncate()
    {
        Volume volume = Phantoms.GradientAlongX(
            startHounsfield: -128, hounsfieldPerVoxel: 1, dimX: 16, dimY: 8, dimZ: 8);

        ReslicePlane plane = ReslicePlane.Through(
            volume, PlaneOrientation.Axial, Point3D.Origin, 0.5);

        byte[] image = Render(volume, plane, UnitLut());
        int middleRow = plane.Height / 2;

        for (int c = 0; c < plane.Width; c++)
        {
            // HU at column c is -128 + c/2, so the byte is c/2 rounded up at the half:
            // 0, 1, 1, 2, 2, 3, ... Truncating would give 0, 0, 1, 1, 2, 2.
            image[(middleRow * plane.Width) + c].Should().Be((byte)((c + 1) / 2), "column {0}", c);
        }
    }

    /// <summary>
    /// FR-208, in the form the spec describes it: a sphere scanned at 0.7 x 0.7 x 3.0 mm
    /// must still be round in a coronal reslice. The coronal view crosses one in-plane
    /// axis and the slice axis, so it is where a missing aspect correction shows up - a
    /// renderer that treated one slice as one pixel would stretch the vertical extent by
    /// 3.0 / 0.7, more than four to one, and the patient would come out squashed.
    /// </summary>
    [Fact]
    public void ASphereStaysRoundInACoronalReslice()
    {
        const double radiusMm = 15.0;
        const double pixelSize = 0.7;

        Volume volume = Phantoms.Sphere(
            radiusMm, dimX: 64, dimY: 64, dimZ: 32, spacing: Phantoms.ChestSpacing);

        ReslicePlane plane = ReslicePlane.Through(
            volume, PlaneOrientation.Coronal, Point3D.Origin, pixelSize);

        // Bone against air, so any threshold between them separates them; 0 HU is the
        // obvious one and lands nowhere near either material.
        byte[] image = Render(volume, plane, new WindowLevelLut(new WindowLevel(2, 0)));

        (double column, double row) = plane.ToPixel(Point3D.Origin);
        int centreColumn = (int)Math.Round(column);
        int centreRow = (int)Math.Round(row);

        int across = Enumerable.Range(0, plane.Width)
            .Count(c => image[(centreRow * plane.Width) + c] > 0);
        int down = Enumerable.Range(0, plane.Height)
            .Count(r => image[(r * plane.Width) + centreColumn] > 0);

        // A great circle of radius 15 mm sampled every 0.7 mm spans 2 * 15 / 0.7 + 1 =
        // about 43.9 pixels; the tolerance is the sampling grid's own quantisation, not
        // slack in the expectation.
        const double expected = (2 * radiusMm / pixelSize) + 1;
        across.Should().BeInRange((int)expected - 2, (int)expected + 2);
        down.Should().BeInRange((int)expected - 2, (int)expected + 2);

        // The claim being made is roundness, so state it directly as well: the two
        // diameters must agree far more tightly than the 4.3x a missing correction costs.
        Math.Abs(across - down).Should().BeLessThanOrEqualTo(2);
    }

    /// <summary>
    /// An oblique plane's corners hang off the volume, and those samples must read as
    /// the out-of-bounds value rather than wrapping into the opposite edge.
    /// </summary>
    [Fact]
    public void SamplesOffTheEndOfTheVolumeReadAsOutside()
    {
        Volume volume = Phantoms.Uniform(Phantoms.SoftTissue, dimX: 32, dimY: 32, dimZ: 16);
        WindowLevelLut lut = new(new WindowLevel(4000, 0));

        // 45 degrees about the patient Z axis, 64 mm square centred on the volume
        // centre. The volume only spans 31 mm, so the plane's own corners - 45 mm out
        // along the diagonal - are comfortably off the end of it.
        const double half = 0.7071067811865476;
        Vector3D row = new(half, half, 0);
        Vector3D column = new(-half, half, 0);
        const int side = 64;

        ReslicePlane plane = new(
            Point3D.Origin + row.Scale(-side / 2.0) + column.Scale(-side / 2.0),
            row,
            column,
            side,
            side);

        byte[] image = Render(volume, plane, lut);

        byte outside = lut[Volume.OutsideValue];
        byte inside = lut[Phantoms.SoftTissue];
        outside.Should().NotBe(inside);

        image[0].Should().Be(outside);
        image[side - 1].Should().Be(outside);
        image[^1].Should().Be(outside);
        image.Should().Contain(inside, "the plane crosses the volume as well as missing it");
    }

    [Fact]
    public void AWronglySizedDestinationIsRejected()
    {
        Volume volume = Phantoms.Uniform(Phantoms.SoftTissue, dimX: 8, dimY: 8, dimZ: 4);
        ReslicePlane plane = ReslicePlane.Through(
            volume, PlaneOrientation.Axial, Point3D.Origin, 1.0);

        Action render = () => PlaneRenderer.Render(
            volume, plane, UnitLut(), new byte[plane.PixelCount - 1]);

        render.Should().Throw<ArgumentException>();
    }
}
