using System;

namespace InterviewTrea.Rendering3D;

/// <summary>
/// What one ray has picked up so far: the front-to-back <c>over</c> operator (FR-602).
/// </summary>
/// <remarks>
/// <para>
/// Compositing front to back rather than back to front is what makes early termination
/// possible: the accumulated opacity only ever grows, so once it reaches one, nothing
/// further along the ray can change the pixel. Back to front gives the same answer and
/// gives it only at the end, having sampled everything hidden behind the skin.
/// </para>
/// <para>
/// The colour is carried premultiplied - each contribution is already scaled by the
/// opacity it arrived with - which is what makes the accumulation a plain sum. It is
/// separated out from the render loop because the operator is the one piece of arithmetic
/// here that can be checked against a value derived on paper without rendering anything.
/// </para>
/// </remarks>
public struct RayAccumulator : IEquatable<RayAccumulator>
{
    private double red;
    private double green;
    private double blue;
    private double opacity;

    /// <summary>How much of the light this ray has stopped, 0 to 1.</summary>
    public readonly double Opacity => opacity;

    /// <summary>
    /// Adds one sample in front of everything behind it.
    /// </summary>
    /// <remarks>
    /// <c>(1 - opacity)</c> is the fraction of this sample that is still unobscured by what
    /// the ray has already passed through. Everything else follows from that one factor.
    /// </remarks>
    public void Add(byte r, byte g, byte b, double alpha)
    {
        double weight = (1 - opacity) * alpha;

        red += weight * r;
        green += weight * g;
        blue += weight * b;
        opacity += weight;
    }

    /// <summary>
    /// The pixel, composited over black.
    /// </summary>
    /// <remarks>
    /// Over black, because that is what the viewport behind it is: the image area is
    /// #000000 and anything lighter costs perceived contrast. Compositing here rather than
    /// dividing the premultiplied colour back out also avoids dividing by an opacity that
    /// is legitimately zero wherever the ray saw nothing.
    /// </remarks>
    public readonly Rgb OverBlack() => new(Clamp(red), Clamp(green), Clamp(blue));

    private static byte Clamp(double channel) =>
        (byte)Math.Clamp(Math.Round(channel, MidpointRounding.AwayFromZero), 0, 255);

    public readonly bool Equals(RayAccumulator other) =>
        red == other.red && green == other.green && blue == other.blue && opacity == other.opacity;

    public override readonly bool Equals(object? obj) => obj is RayAccumulator other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(red, green, blue, opacity);

    public static bool operator ==(RayAccumulator left, RayAccumulator right) => left.Equals(right);

    public static bool operator !=(RayAccumulator left, RayAccumulator right) => !left.Equals(right);
}
