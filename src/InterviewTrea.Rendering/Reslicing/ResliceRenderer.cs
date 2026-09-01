using System;
using InterviewTrea.Core.Volumes;
using InterviewTrea.Rendering.Windowing;
using static System.FormattableString;

namespace InterviewTrea.Rendering.Reslicing;

/// <summary>
/// Resamples a plane out of a <see cref="Volume"/> into an 8-bit greyscale buffer (FR-202).
/// </summary>
/// <remarks>
/// <para>
/// Iteration 2 renders the axial plane only, and for that plane there is nothing to
/// resample. Voxels are stored with x varying fastest, then y, then z, so one axial slice
/// is a single unbroken run of <c>DimX * DimY</c> values already in display order. The
/// render is a walk of that run through the window/level table: no interpolation, because
/// every sample point lands exactly on a voxel centre.
/// </para>
/// <para>
/// Trilinear interpolation arrives in Iteration 3 with the coronal, sagittal and oblique
/// planes, where sample points fall between voxels. Writing it now would be untestable
/// weight arithmetic on a path that cannot exercise it.
/// </para>
/// <para>
/// The output is at native voxel resolution, not at viewport resolution. Zoom, pan and
/// anisotropic aspect correction are display transforms applied when the buffer is blitted,
/// which keeps them out of the per-pixel loop and off the critical path for NFR-201.
/// </para>
/// </remarks>
public static class ResliceRenderer
{
    /// <summary>
    /// Renders axial slice <paramref name="sliceIndex"/> into <paramref name="destination"/>,
    /// which must hold exactly <c>DimX * DimY</c> bytes in row-major order.
    /// </summary>
    public static void RenderAxial(
        Volume volume,
        int sliceIndex,
        WindowLevelLut lut,
        Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(lut);

        if ((uint)sliceIndex >= (uint)volume.DimZ)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sliceIndex),
                sliceIndex,
                Invariant($"The volume has {volume.DimZ} axial slices."));
        }

        int pixels = volume.DimX * volume.DimY;
        if (destination.Length != pixels)
        {
            throw new ArgumentException(
                Invariant($"Destination holds {destination.Length} bytes; an axial slice is {volume.DimX}x{volume.DimY} = {pixels}."),
                nameof(destination));
        }

        // Both spans are hoisted out of the loop deliberately. Reading volume.Voxels or
        // lut.Table inside it is a property call per pixel that the JIT will not always
        // hoist through, and this loop runs a quarter of a million times per frame.
        ReadOnlySpan<short> source = volume.Voxels.AsSpan(sliceIndex * pixels, pixels);
        ReadOnlySpan<byte> table = lut.Table;

        for (int p = 0; p < pixels; p++)
        {
            // The table spans the whole short range, so this index is always valid and
            // needs no clamp. That is the reason it is 64 KB rather than the 4 KB the
            // volume's actual Hounsfield range would need.
            destination[p] = table[source[p] + WindowLevelLut.Bias];
        }
    }
}
