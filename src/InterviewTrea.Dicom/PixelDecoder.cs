using System;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using static System.FormattableString;

namespace InterviewTrea.Dicom;

/// <summary>What one slice contributed, gathered while decoding so nothing is rescanned.</summary>
/// <param name="Saturated">
/// Samples whose rescaled value did not fit in a <see cref="short"/> and were clamped.
/// Non-zero means dense metal or a bad rescale, and the caller should say so.
/// </param>
public readonly record struct DecodeStatistics(short Minimum, short Maximum, int Saturated);

/// <summary>
/// Turns one slice's stored pixel values into Hounsfield units (FR-104).
/// </summary>
/// <remarks>
/// <para>
/// Raw stored values are not HU. <c>HU = raw * RescaleSlope + RescaleIntercept</c>, and
/// skipping it is the classic silent failure: air reads 0 instead of about -1000, the
/// image still looks like a chest, and every measurement taken from it is wrong. The
/// probe prints the HU range for exactly this reason.
/// </para>
/// <para>
/// Decoding writes into a span the caller owns, so a whole series can be decoded straight
/// into one preallocated flat array rather than into per-slice arrays that are then copied
/// (NFR-102).
/// </para>
/// </remarks>
public static class PixelDecoder
{
    public static DecodeStatistics DecodeInto(DicomDataset dataset, Span<short> destination)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        int rows = dataset.GetSingleValue<ushort>(DicomTag.Rows);
        int columns = dataset.GetSingleValue<ushort>(DicomTag.Columns);
        int expected = rows * columns;

        if (destination.Length != expected)
        {
            throw new ArgumentException(
                Invariant($"Destination holds {destination.Length} samples; this slice is {columns}x{rows} = {expected}."),
                nameof(destination));
        }

        ushort bitsAllocated = dataset.GetSingleValue<ushort>(DicomTag.BitsAllocated);
        if (bitsAllocated != 16)
        {
            // CT is 16-bit allocated in every acquisition this project targets. Refusing
            // the rest is honest; guessing at an 8-bit or 32-bit layout is not.
            throw new SeriesRejectedException(
                SeriesRejectionReason.InconsistentGeometry,
                Invariant($"BitsAllocated (0028,0100) is {bitsAllocated}. Only 16-bit CT pixel data is supported."));
        }

        ushort bitsStored = dataset.GetSingleValueOrDefault<ushort>(DicomTag.BitsStored, 16);
        ushort highBit = dataset.GetSingleValueOrDefault<ushort>(DicomTag.HighBit, (ushort)(bitsStored - 1));
        bool signed = dataset.GetSingleValueOrDefault<ushort>(DicomTag.PixelRepresentation, 0) == 1;
        double slope = dataset.GetSingleValueOrDefault(DicomTag.RescaleSlope, 1.0);
        double intercept = dataset.GetSingleValueOrDefault(DicomTag.RescaleIntercept, 0.0);

        byte[] frame = DicomPixelData.Create(dataset).GetFrame(0).Data;
        if (frame.Length < expected * 2)
        {
            throw new SeriesRejectedException(
                SeriesRejectionReason.InconsistentGeometry,
                Invariant($"Pixel data holds {frame.Length} bytes; a {columns}x{rows} 16-bit slice needs {expected * 2}."));
        }

        // The stored value occupies BitsStored bits ending at HighBit, so anything above
        // HighBit or below the run is padding and must not reach the arithmetic. On almost
        // all CT this is a no-op - HighBit is BitsStored - 1 and BitsStored is 16 - but a
        // 12-bit-in-16 scanner leaves the top four bits undefined.
        int shift = highBit + 1 - bitsStored;
        int mask = bitsStored >= 32 ? -1 : (1 << bitsStored) - 1;
        int signBit = 1 << (bitsStored - 1);

        short minimum = short.MaxValue;
        short maximum = short.MinValue;
        int saturated = 0;

        for (int p = 0; p < expected; p++)
        {
            int raw = (frame[p * 2] | (frame[(p * 2) + 1] << 8)) >> shift & mask;

            // Two's complement in BitsStored bits, not in 16. Sign-extending from the wrong
            // width turns dense bone into deep negative values, which is why the width has
            // to come from the header rather than from the storage type.
            if (signed && (raw & signBit) != 0)
            {
                raw |= ~mask;
            }

            double hounsfield = (raw * slope) + intercept;

            short value;
            if (hounsfield >= short.MaxValue)
            {
                value = short.MaxValue;
                saturated++;
            }
            else if (hounsfield <= short.MinValue)
            {
                value = short.MinValue;
                saturated++;
            }
            else
            {
                // Away from zero rather than the default banker's rounding: a half-unit is
                // rare, and a rule that depends on the parity of the neighbouring integer
                // is not something worth defending.
                value = (short)Math.Round(hounsfield, MidpointRounding.AwayFromZero);
            }

            destination[p] = value;

            if (value < minimum)
            {
                minimum = value;
            }

            if (value > maximum)
            {
                maximum = value;
            }
        }

        return new DecodeStatistics(minimum, maximum, saturated);
    }
}
