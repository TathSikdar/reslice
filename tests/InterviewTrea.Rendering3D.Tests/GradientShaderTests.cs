using System;
using FluentAssertions;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering3D;
using InterviewTrea.TestData;

namespace InterviewTrea.Rendering3D.Tests;

public sealed class GradientShaderTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void ARampAlongXHasItsGradientAlongXAtEveryInteriorVoxel()
    {
        // 100 HU per 1 mm voxel, so the gradient is 100 HU/mm along +x and exactly zero on
        // the other two axes. Any leak into y or z is a swapped stride in the differencing.
        Volume volume = Phantoms.GradientAlongX(hounsfieldPerVoxel: 100, spacing: Phantoms.IsotropicSpacing);

        for (int i = 1; i < volume.DimX - 1; i++)
        {
            Vector3D gradient = GradientShader.Gradient(volume, i, 4, 4);

            gradient.X.Should().BeApproximately(100, Tolerance);
            gradient.Y.Should().BeApproximately(0, Tolerance);
            gradient.Z.Should().BeApproximately(0, Tolerance);
        }
    }

    [Fact]
    public void TheGradientIsPerMillimetreAndNotPerVoxel()
    {
        // The same 100 HU per voxel, but the voxels are 0.7 mm wide: the physical slope is
        // 100 / 0.7 = 142.857 HU/mm. Dividing by index instead of by millimetres would
        // report 100 here and tilt every surface in an anisotropic study.
        Volume volume = Phantoms.GradientAlongX(hounsfieldPerVoxel: 100, spacing: Phantoms.ChestSpacing);

        GradientShader.Gradient(volume, 8, 4, 4).X.Should().BeApproximately(100 / 0.7, 1e-9);
    }

    [Fact]
    public void TheDifferenceIsTakenOverTheSamePhysicalDistanceOnEveryAxis()
    {
        // 0.7 x 0.7 x 3.0 mm voxels, so the coarsest axis is 3.0 mm and that is the offset
        // used on all three. A 6 mm cube has its face at x = 3 mm; sample 2 mm outside it,
        // at x = 5. An isotropic 3 mm reach still touches the cube at x = 2 and reports an
        // edge. One index step per axis reaches 0.7 mm in x, sees air on both sides, and
        // reports nothing - which is what smooths the normal four times harder through the
        // slice stack than across it and bands a real chest study horizontally.
        Volume volume = Phantoms.Cube(
            edgeMm: 6, insideHounsfield: 1000, outsideHounsfield: -1000,
            dimX: 41, dimY: 41, dimZ: 21, spacing: Phantoms.ChestSpacing);

        Point3D outside = new(5, 0, 0);
        Point3D asVoxels = volume.PatientToVoxel.Transform(outside);

        GradientShader.Gradient(volume, asVoxels.X, asVoxels.Y, asVoxels.Z).Length
            .Should().BeGreaterThan(0);

        // The converse, so the test cannot pass on a renderer that reaches everywhere: a
        // point 5 mm outside is beyond even the isotropic reach and must read flat.
        Point3D wellClear = volume.PatientToVoxel.Transform(new Point3D(8.5, 0, 0));

        GradientShader.Gradient(volume, wellClear.X, wellClear.Y, wellClear.Z).Length
            .Should().BeApproximately(0, Tolerance);
    }

    [Fact]
    public void AUniformVolumeHasNoGradientAndIsNotShadedByOne()
    {
        Volume volume = Phantoms.Uniform(40);

        Vector3D gradient = GradientShader.Gradient(volume, 30, 30, 15);

        gradient.Length.Should().BeApproximately(0, Tolerance);

        // The interesting half: normalising this would divide by zero. Shading has to hand
        // back an unshaded sample rather than a NaN or a normal pointing at whatever the
        // last bits of the subtraction happened to hold.
        double shade = GradientShader.Shade(gradient, Vector3D.UnitY, ShadingParameters.Default);

        shade.Should().Be(1.0);
        double.IsNaN(shade).Should().BeFalse();
    }

    [Fact]
    public void ASurfaceFacingTheViewerIsLitByEveryTerm()
    {
        ShadingParameters lighting = ShadingParameters.Default;

        double shade = GradientShader.Shade(Vector3D.UnitZ.Scale(500), Vector3D.UnitZ, lighting);

        // Lambert is 1 and so is the highlight: ambient + diffuse + specular.
        shade.Should().BeApproximately(lighting.Ambient + lighting.Diffuse + lighting.Specular, 1e-12);
    }

    [Fact]
    public void ASurfaceEdgeOnToTheViewerFallsBackToAmbient()
    {
        double shade = GradientShader.Shade(Vector3D.UnitZ.Scale(500), Vector3D.UnitX, ShadingParameters.Default);

        shade.Should().BeApproximately(ShadingParameters.Default.Ambient, 1e-12);
    }

    [Fact]
    public void ASurfaceSeenFromEitherSideIsLitTheSame()
    {
        // The sign of the gradient says whether the ray reached tissue from air or air from
        // tissue, which is a property of the ray and not of the surface. A face that went
        // black when orbited past would be an artefact, not anatomy.
        ShadingParameters lighting = ShadingParameters.Default;

        double front = GradientShader.Shade(Vector3D.UnitZ, Vector3D.UnitZ, lighting);
        double back = GradientShader.Shade(Vector3D.UnitZ.Negate(), Vector3D.UnitZ, lighting);

        front.Should().BeApproximately(back, 1e-12);
    }

    [Fact]
    public void TheGradientMagnitudeDoesNotDependOnHowLongTheVectorIs()
    {
        // Shade normalises internally; a steeper edge is not a brighter one.
        double gentle = GradientShader.Shade(new Vector3D(0, 0, 1), Vector3D.UnitZ, ShadingParameters.Default);
        double steep = GradientShader.Shade(new Vector3D(0, 0, 9999), Vector3D.UnitZ, ShadingParameters.Default);

        gentle.Should().BeApproximately(steep, 1e-12);
    }

    [Fact]
    public void ShadingIsOffUnlessAskedForAndChangesThePictureWhenItIsOn()
    {
        // End to end: a shaded sphere must not come out the flat disc an unshaded one does.
        // The unshaded render is the control - if both were shaded the test would pass on a
        // renderer that ignored the flag.
        Volume volume = Phantoms.Sphere(radiusMm: 25, insideHounsfield: 1000, outsideHounsfield: -1000,
            dimX: 65, dimY: 65, dimZ: 65, spacing: Phantoms.IsotropicSpacing);

        TransferFunction function = new(
        [
            new TransferFunctionPoint(TransferFunction.MinimumHounsfield, Rgb.Black, 0),
            new TransferFunctionPoint(499, new Rgb(200, 200, 200), 0),
            new TransferFunctionPoint(500, new Rgb(200, 200, 200), 1),
            new TransferFunctionPoint(TransferFunction.MaximumHounsfield, new Rgb(200, 200, 200), 1),
        ]);

        Camera3D camera = new()
        {
            Target = Point3D.Origin,
            Azimuth = -Math.PI / 2,
            Elevation = 0,
            ViewHeightMm = 64,
        };

        byte[] flat = new byte[64 * 64 * VolumeRaycaster.BytesPerPixel];
        byte[] shaded = new byte[64 * 64 * VolumeRaycaster.BytesPerPixel];

        RaycastSettings settings = new() { StepMm = 0.5 };
        VolumeRaycaster.Render(volume, camera, function, settings, 64, 64, flat);
        VolumeRaycaster.Render(volume, camera, function, settings with { IsShaded = true }, 64, 64, shaded);

        // Every lit pixel of the flat render is the same flat 200: a disc with no shape.
        Red(flat, 32, 32).Should().Be(200);
        Red(flat, 32, 20).Should().Be(200);

        // Shaded, the middle of the sphere faces the viewer and the rim turns away, so the
        // middle is bright and the rim is dimmer. That difference is the whole point.
        Red(shaded, 32, 32).Should().BeGreaterThan(Red(shaded, 32, 20));
    }

    private static byte Red(byte[] pixels, int column, int row) =>
        pixels[(((row * 64) + column) * VolumeRaycaster.BytesPerPixel) + 2];
}
