using System;
using System.Collections.Generic;
using FluentAssertions;
using InterviewTrea.Rendering3D;

namespace InterviewTrea.Rendering3D.Tests;

public sealed class TransferFunctionTests
{
    // Black at 0 HU, white at 100 HU, transparent to half opaque. Every expected value
    // below is read straight off those two points.
    private static TransferFunction TwoPoints() => new(new[]
    {
        new TransferFunctionPoint(0, Rgb.Black, 0),
        new TransferFunctionPoint(100, new Rgb(255, 255, 255), 0.5),
    });

    private static Rgb ColourAt(TransferFunction function, double hounsfield)
    {
        int i = TransferFunction.IndexOf(hounsfield) * 3;
        return new Rgb(function.Colours[i], function.Colours[i + 1], function.Colours[i + 2]);
    }

    [Fact]
    public void AControlPointMapsToExactlyItsOwnColourAndOpacity()
    {
        TransferFunction function = TwoPoints();

        ColourAt(function, 0).Should().Be(Rgb.Black);
        ColourAt(function, 100).Should().Be(new Rgb(255, 255, 255));
        function.Opacities[TransferFunction.IndexOf(0)].Should().Be(0f);
        function.Opacities[TransferFunction.IndexOf(100)].Should().Be(0.5f);
    }

    [Fact]
    public void TheMidpointBetweenTwoPointsIsTheirAverage()
    {
        TransferFunction function = TwoPoints();

        // Halfway from 0 to 255 is 127.5, which rounds away from zero to 128. Truncating
        // instead would give 127 and leave every ramp in the table half a level dark.
        ColourAt(function, 50).Should().Be(new Rgb(128, 128, 128));
        function.Opacities[TransferFunction.IndexOf(50)].Should().BeApproximately(0.25f, 1e-6f);
    }

    [Fact]
    public void ValuesOutsideTheOutermostPointsClampRatherThanExtrapolating()
    {
        TransferFunction function = TwoPoints();

        // Extrapolating from this function would put the opacity at 2000 HU past 1 and
        // the colour past white long before that.
        ColourAt(function, -1000).Should().Be(Rgb.Black);
        function.Opacities[TransferFunction.IndexOf(-1000)].Should().Be(0f);

        ColourAt(function, 2000).Should().Be(new Rgb(255, 255, 255));
        function.Opacities[TransferFunction.IndexOf(2000)].Should().Be(0.5f);
    }

    [Fact]
    public void SamplesBeyondTheCtScaleLandOnTheEndsOfTheTableRatherThanOffIt()
    {
        TransferFunction.IndexOf(-5000).Should().Be(0);
        TransferFunction.IndexOf(99999).Should().Be(TransferFunction.TableLength - 1);
        TransferFunction.IndexOf(TransferFunction.MinimumHounsfield).Should().Be(0);
        TransferFunction.IndexOf(TransferFunction.MaximumHounsfield).Should().Be(TransferFunction.TableLength - 1);
    }

    [Fact]
    public void TheTableCoversEveryHounsfieldUnitOfTheCtScaleExactlyOnce()
    {
        TransferFunction.TableLength.Should().Be(4096);
        TwoPoints().Opacities.Length.Should().Be(4096);
        TwoPoints().Colours.Length.Should().Be(4096 * 3);
    }

    [Fact]
    public void HalvingTheStepHalvesWhatEachSampleStopsInTheCompoundingSense()
    {
        TransferFunction function = TwoPoints();
        int index = TransferFunction.IndexOf(100);

        float half = function.OpacitiesForStep(0.5)[index];

        // Two half-millimetre samples must stop the same light as one whole-millimetre
        // sample: 1 - (1 - a)^2 = 0.5, so a = 1 - sqrt(0.5) = 0.29289.
        half.Should().BeApproximately((float)(1 - Math.Sqrt(0.5)), 1e-6f);
        (1 - Math.Pow(1 - half, 2)).Should().BeApproximately(0.5, 1e-6);
    }

    [Fact]
    public void CorrectingToTheReferenceStepChangesNothing()
    {
        TransferFunction function = TwoPoints();

        function.OpacitiesForStep(TransferFunction.ReferenceStepMm)[TransferFunction.IndexOf(100)]
            .Should().BeApproximately(0.5f, 1e-6f);
    }

    [Fact]
    public void FullyTransparentAndFullyOpaqueEntriesSurviveTheCorrectionUnchanged()
    {
        // Neither can move: no length of nothing stops light, and no length of a solid
        // stops more than all of it. Worth pinning because the general formula is a Pow
        // that would return 0.9999999 for the second and dull every opaque surface.
        TransferFunction function = TwoPoints();
        float[] corrected = function.OpacitiesForStep(4.0);

        corrected[TransferFunction.IndexOf(0)].Should().Be(0f);

        TransferFunction solid = new(new[]
        {
            new TransferFunctionPoint(0, Rgb.Black, 1),
            new TransferFunctionPoint(100, Rgb.Black, 1),
        });

        solid.OpacitiesForStep(0.25)[TransferFunction.IndexOf(50)].Should().Be(1f);
    }

    [Fact]
    public void ATableWithFewerThanTwoPointsIsRejected()
    {
        Action act = () => _ = new TransferFunction(new[] { new TransferFunctionPoint(0, Rgb.Black, 1) });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PointsOutOfOrderAreRejectedRatherThanDividingByZero()
    {
        Action repeated = () => _ = new TransferFunction(new[]
        {
            new TransferFunctionPoint(0, Rgb.Black, 0),
            new TransferFunctionPoint(0, Rgb.Black, 1),
        });

        Action backwards = () => _ = new TransferFunction(new[]
        {
            new TransferFunctionPoint(100, Rgb.Black, 0),
            new TransferFunctionPoint(0, Rgb.Black, 1),
        });

        repeated.Should().Throw<ArgumentException>();
        backwards.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThreeSegmentsInterpolateWithinTheRightOneAtEveryValue()
    {
        // The table build walks its segment forward as it goes rather than searching per
        // entry; this is what catches it walking one segment too far or not far enough.
        TransferFunction function = new(new[]
        {
            new TransferFunctionPoint(-1000, Rgb.Black, 0),
            new TransferFunctionPoint(0, new Rgb(100, 0, 0), 0.2),
            new TransferFunctionPoint(1000, new Rgb(200, 0, 0), 0.4),
            new TransferFunctionPoint(2000, new Rgb(0, 0, 0), 0.0),
        });

        ColourAt(function, -500).Should().Be(new Rgb(50, 0, 0));
        ColourAt(function, 500).Should().Be(new Rgb(150, 0, 0));
        ColourAt(function, 1500).Should().Be(new Rgb(100, 0, 0));
        function.Opacities[TransferFunction.IndexOf(1500)].Should().BeApproximately(0.2f, 1e-6f);
    }

    [Theory]
    [InlineData("Bone")]
    [InlineData("Angio")]
    [InlineData("Lung")]
    [InlineData("Skin")]
    public void EveryPresetIsAValidTableThatLeavesAirInvisible(string name)
    {
        TransferFunction function = Preset(name);

        // Air must not be painted. Everything below the lowest point clamps up to it, and
        // a ray crosses hundreds of millimetres of air outside the patient, so an opacity
        // small enough to look harmless in the table still accumulates into a fog. This
        // caught exactly that in the Lung preset, which started its ramp at -950.
        function.Opacities[TransferFunction.IndexOf(-1000)].Should().Be(0f);
        function.Points[0].Hounsfield.Should().Be(TransferFunction.MinimumHounsfield);
        function.Points[^1].Hounsfield.Should().Be(TransferFunction.MaximumHounsfield);

        foreach (float opacity in function.Opacities)
        {
            opacity.Should().BeInRange(0, 1);
        }
    }

    private static TransferFunction Preset(string name)
    {
        foreach (KeyValuePair<string, TransferFunction> preset in TransferFunctionPreset.All)
        {
            if (preset.Key == name)
            {
                return preset.Value;
            }
        }

        throw new InvalidOperationException($"No preset named {name}.");
    }
}
