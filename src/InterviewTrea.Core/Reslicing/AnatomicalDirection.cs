using System;
using InterviewTrea.Core.Geometry;

namespace InterviewTrea.Core.Reslicing;

/// <summary>
/// Names a patient-space direction with the single letter radiologists put at the edge of
/// a viewport (FR-204).
/// </summary>
/// <remarks>
/// DICOM patient coordinates are LPS, so +X is Left, +Y is Posterior, +Z is Superior, and
/// each negative axis is the opposite letter. The letter is the one edge marker that makes
/// a mirrored image obvious: left and right look identical on a chest, and an unlabelled
/// flip is the classic wrong-side error.
/// </remarks>
public static class AnatomicalDirection
{
    /// <summary>
    /// The letter for whichever patient axis <paramref name="direction"/> points along
    /// most strongly. An oblique direction is named by its dominant component, which is
    /// what a viewport edge can usefully say - the alternative is three letters and a
    /// number nobody reads.
    /// </summary>
    public static string Of(Vector3D direction)
    {
        double absX = Math.Abs(direction.X);
        double absY = Math.Abs(direction.Y);
        double absZ = Math.Abs(direction.Z);

        if (absX >= absY && absX >= absZ)
        {
            return direction.X >= 0 ? "L" : "R";
        }

        return absY >= absZ
            ? direction.Y >= 0 ? "P" : "A"
            : direction.Z >= 0 ? "S" : "I";
    }
}
