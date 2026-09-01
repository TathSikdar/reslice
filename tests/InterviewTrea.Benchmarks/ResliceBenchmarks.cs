using BenchmarkDotNet.Attributes;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Reslicing;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering.Reslicing;
using InterviewTrea.Rendering.Windowing;
using InterviewTrea.TestData;

namespace InterviewTrea.Benchmarks;

/// <summary>
/// The NFR-200 targets, measured rather than asserted (NFR-304).
/// </summary>
/// <remarks>
/// <para>
/// This is the <em>baseline</em> harness. It is committed before any optimization so that
/// every later change has a before figure to be compared against, which is the whole point
/// of NFR-304: the difference between "it got about ten times faster" and a table.
/// </para>
/// <para>
/// The phantom is a checkerboard rather than a uniform block on purpose. A uniform volume
/// is the best case for the cache and for branch prediction, and a sphere spends most of
/// its samples in constant air; a checker keeps the samples varying so nothing downstream
/// can be short-circuited. It is deliberately pessimistic - real chest CT will be no
/// slower than this.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class ResliceBenchmarks
{
    // 512 x 512 x 256 at 0.7 x 0.7 x 1.0 mm: about 134 MB, the shape of a real chest CT
    // and large enough that the volume does not fit in any cache.
    private const int Dim = 512;
    private const int Slices = 256;
    private const double PixelSize = 0.7;

    private Volume volume = null!;
    private WindowLevelLut lut = null!;
    private ReslicePlane axial = null!;
    private ReslicePlane oblique = null!;
    private byte[] axialBuffer = null!;
    private byte[] obliqueBuffer = null!;
    private byte[] nativeBuffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        volume = Phantoms.Checker(
            periodVoxels: 8,
            dimX: Dim,
            dimY: Dim,
            dimZ: Slices,
            spacing: new Vector3D(PixelSize, PixelSize, 1.0));

        lut = new WindowLevelLut(WindowLevel.SoftTissue);

        axial = ReslicePlane.Through(volume, PlaneOrientation.Axial, Point3D.Origin, PixelSize);

        // 30 degrees out of axial, about the patient x axis. Every sample lands between
        // voxels and every scanline crosses slice boundaries, so this is the case
        // NFR-202 is written for.
        const double cos = 0.8660254037844387;
        const double sin = 0.5;
        oblique = new ReslicePlane(
            axial.Origin,
            new Vector3D(PixelSize, 0, 0),
            new Vector3D(0, PixelSize * cos, PixelSize * sin),
            axial.Width,
            axial.Height);

        axialBuffer = new byte[axial.PixelCount];
        obliqueBuffer = new byte[oblique.PixelCount];
        nativeBuffer = new byte[Dim * Dim];
    }

    /// <summary>NFR-201, via the Iteration 2 fast path: no interpolation, one contiguous run.</summary>
    [Benchmark(Baseline = true)]
    public void AxialFastPath() =>
        ResliceRenderer.RenderAxial(volume, Slices / 2, lut, nativeBuffer);

    /// <summary>NFR-201, via the general path the viewports actually use.</summary>
    [Benchmark]
    public void AxialThroughThePlaneRenderer() =>
        PlaneRenderer.Render(volume, axial, lut, axialBuffer);

    /// <summary>NFR-202.</summary>
    [Benchmark]
    public void ObliqueReslice() =>
        PlaneRenderer.Render(volume, oblique, lut, obliqueBuffer);

    /// <summary>NFR-203: a 20 mm slab, which at 0.7 mm pitch is 30 samples per pixel.</summary>
    [Benchmark]
    public void SlabMaximum20Mm() =>
        SlabRenderer.Render(volume, axial, SlabMode.Maximum, 20.0, lut, axialBuffer);

    [Benchmark]
    public void SlabAverage20Mm() =>
        SlabRenderer.Render(volume, axial, SlabMode.Average, 20.0, lut, axialBuffer);
}
