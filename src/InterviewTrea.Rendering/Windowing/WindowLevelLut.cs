using System;

namespace InterviewTrea.Rendering.Windowing;

/// <summary>
/// Precomputed Hounsfield-to-grey lookup table for one <see cref="WindowLevel"/>.
/// </summary>
/// <remarks>
/// <para>
/// The mapping is a handful of floating-point operations per pixel, and a 512x512
/// viewport has 262,144 of them. Doing the arithmetic inline costs a divide and two
/// compares per pixel every frame; doing it once into a table costs one array read.
/// The table is rebuilt only when the window changes, which during a window/level drag
/// is once per frame rather than a quarter of a million times per frame.
/// </para>
/// <para>
/// The table spans the entire <see cref="short"/> range - 65,536 bytes, small enough to
/// sit in L2 cache - so a sample can never index outside it and the render loop needs no
/// bounds test of its own. A table sized to the volume's actual range would save 60 KB
/// and put a branch back in the hot loop, which is the wrong trade.
/// </para>
/// </remarks>
public sealed class WindowLevelLut
{
    /// <summary>Added to a Hounsfield value to index <see cref="Table"/>.</summary>
    public const int Bias = -short.MinValue;

    private readonly byte[] table = new byte[65536];

    public WindowLevelLut(WindowLevel window) => Rebuild(window);

    public WindowLevel Window { get; private set; }

    /// <summary>Indexed by <c>hounsfield + <see cref="Bias"/></c>.</summary>
    public ReadOnlySpan<byte> Table => table;

    public byte this[short hounsfield] => table[hounsfield + Bias];

    /// <summary>
    /// Refills the table in place. Reusing the array matters: reallocating 64 KB on every
    /// mouse-move during a window/level drag is a garbage collection the frame rate pays for.
    /// </summary>
    public void Rebuild(WindowLevel window)
    {
        if (window.Width < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(window),
                window.Width,
                "Window width must be at least 1; the DICOM LINEAR transform divides by (width - 1).");
        }

        Window = window;

        // DICOM PS3.3 C.11.2.1.2, the LINEAR VOI transform, written out rather than
        // simplified. The "- 0.5" and "width - 1" terms look like off-by-one noise and
        // are not: they place the window's centre exactly on mid-grey and make the two
        // clipping boundaries land on the first and last representable output. Using the
        // obvious (x - c + w/2) / w instead shifts every displayed value by half a grey
        // level and is wrong by one at the white end.
        double lower = window.Center - 0.5 - ((window.Width - 1) / 2.0);
        double upper = window.Center - 0.5 + ((window.Width - 1) / 2.0);
        double scale = 255.0 / (window.Width - 1);

        for (int i = 0; i < table.Length; i++)
        {
            double hounsfield = i - Bias;

            if (hounsfield <= lower)
            {
                table[i] = 0;
            }
            else if (hounsfield > upper)
            {
                table[i] = 255;
            }
            else
            {
                double grey = ((hounsfield - (window.Center - 0.5)) * scale) + 127.5;
                table[i] = (byte)Math.Round(grey, MidpointRounding.AwayFromZero);
            }
        }
    }
}
