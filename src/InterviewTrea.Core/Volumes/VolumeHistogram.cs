using System;
using System.Collections.Generic;

namespace InterviewTrea.Core.Volumes;

/// <summary>
/// How many voxels of a volume fall in each band of Hounsfield units (FR-507).
/// </summary>
/// <remarks>
/// <para>
/// In Core rather than in the application that displays it, for the same reason every
/// other number in this project is: it is arithmetic over the volume, it has an
/// analytically checkable answer, and the plugin that draws it should own nothing but the
/// drawing.
/// </para>
/// <para>
/// The scale is fixed at -1024 to 3071, which is the range 12-bit CT reconstruction
/// actually produces: air at -1000, water at 0, cortical bone in the high hundreds, metal
/// and contrast above that. Values outside it are counted into the end bins rather than
/// dropped, because a total that does not equal the voxel count is a histogram nobody can
/// take a percentage from.
/// </para>
/// </remarks>
public sealed class VolumeHistogram
{
    /// <summary>Low edge of the first bin.</summary>
    public const int Minimum = -1024;

    /// <summary>High edge of the last bin, inclusive.</summary>
    public const int Maximum = 3071;

    private readonly int[] counts;

    private VolumeHistogram(int binWidth, int[] counts, long total)
    {
        BinWidth = binWidth;
        this.counts = counts;
        Total = total;
    }

    /// <summary>Width of every bin, in Hounsfield units.</summary>
    public int BinWidth { get; }

    /// <summary>Voxels counted, which is every voxel in the volume.</summary>
    public long Total { get; }

    /// <summary>Voxel count per bin, from <see cref="Minimum"/> upwards.</summary>
    public IReadOnlyList<int> Counts => counts;

    /// <summary>The busiest bin's count, which is what a bar chart scales against.</summary>
    public int Peak { get; private set; }

    /// <summary>
    /// Counts every voxel into a bin.
    /// </summary>
    /// <param name="binWidth">
    /// Hounsfield units per bin. Must divide the 4096-unit scale evenly, so that a bin
    /// edge is a round number and the last bin is the same width as the rest.
    /// </param>
    public static VolumeHistogram Compute(Volume volume, int binWidth = 16)
    {
        ArgumentNullException.ThrowIfNull(volume);

        if (binWidth <= 0 || (Maximum - Minimum + 1) % binWidth != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(binWidth), binWidth, "Bin width must divide the 4096 HU scale evenly.");
        }

        int[] counts = new int[(Maximum - Minimum + 1) / binWidth];
        short[] voxels = volume.Voxels;

        foreach (short hounsfield in voxels)
        {
            // Clamped rather than skipped. A padding value of -2048 outside the
            // reconstruction circle is real data that has to land somewhere, and a total
            // short of the voxel count would make every percentage quietly wrong.
            int bin = (Math.Clamp((int)hounsfield, Minimum, Maximum) - Minimum) / binWidth;
            counts[bin]++;
        }

        VolumeHistogram histogram = new(binWidth, counts, voxels.LongLength);

        foreach (int count in counts)
        {
            if (count > histogram.Peak)
            {
                histogram.Peak = count;
            }
        }

        return histogram;
    }

    /// <summary>The Hounsfield range a bin covers: low inclusive, high inclusive.</summary>
    public (int Low, int High) RangeOf(int bin)
    {
        if ((uint)bin >= (uint)counts.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(bin));
        }

        int low = Minimum + (bin * BinWidth);
        return (low, low + BinWidth - 1);
    }

    /// <summary>Which bin a Hounsfield value falls in, clamped to the scale.</summary>
    public int BinOf(int hounsfield) =>
        (Math.Clamp(hounsfield, Minimum, Maximum) - Minimum) / BinWidth;
}
