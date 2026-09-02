using System;
using InterviewTrea.Core.Geometry;
using InterviewTrea.Core.Volumes;

namespace InterviewTrea.Rendering3D;

/// <summary>The lighting constants. A single headlight; there is nothing to place.</summary>
/// <param name="Ambient">Floor brightness, so a surface turned away is dim rather than black.</param>
/// <param name="Diffuse">Weight of the Lambert term.</param>
/// <param name="Specular">Weight of the highlight.</param>
/// <param name="Shininess">Highlight tightness. Larger is smaller and harder.</param>
public readonly record struct ShadingParameters(
    double Ambient,
    double Diffuse,
    double Specular,
    double Shininess)
{
    /// <summary>The constants the renderer uses.</summary>
    /// <remarks>
    /// Spelled out here rather than as default arguments on the parameters above. A record
    /// struct's parameterless constructor zero-initialises and does not run the primary
    /// constructor, so <c>new()</c> would silently produce a light of no brightness at all -
    /// which renders a correctly shaded image entirely black.
    /// </remarks>
    public static ShadingParameters Default => new(Ambient: 0.30, Diffuse: 0.60, Specular: 0.25, Shininess: 24);
}

/// <summary>
/// Surface shading from the gradient of the density field (FR-607).
/// </summary>
/// <remarks>
/// <para>
/// Without this a volume rendering is a fog bank: correct, and unreadable. Shading needs a
/// surface normal, and a volume has no surfaces - but wherever there is something that
/// looks like a surface, the density is changing fastest across it, so the gradient of the
/// field is the normal. It points toward increasing density, which is into the object, so
/// the outward normal is its negative.
/// </para>
/// <para>
/// The gradient is six extra trilinear samples per step, which roughly quadruples the cost
/// of a sample. That is the single biggest performance decision in the phase, and it is
/// paid for by only shading samples the transfer function did not already make nearly
/// invisible, and by skipping it entirely during interaction.
/// </para>
/// </remarks>
public static class GradientShader
{
    /// <summary>
    /// Below this the field is flat and the direction of its gradient is noise. Uniform
    /// tissue and empty air are both exactly here, and normalising would turn a rounding
    /// error into a confident-looking normal.
    /// </summary>
    public const double MinimumGradient = 1e-6;

    /// <summary>
    /// The gradient of the density field at a continuous voxel coordinate, in Hounsfield
    /// units per patient millimetre.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Central differences either side on each axis. A forward difference would be half the
    /// samples and would bias the normal half a step toward the front of every surface.
    /// </para>
    /// <para>
    /// The division is by patient millimetres, not by voxel indices, and that is the whole
    /// reason this returns a patient-space vector. On 0.7 x 0.7 x 3.0 mm voxels the same
    /// physical slope reads four times steeper along z if you divide by index, and every
    /// surface in the rendering tilts toward the axial plane - a plausible-looking image
    /// that is lit wrongly. Each axis contributes along its own patient direction, which is
    /// exact because gantry tilt is rejected at load and the three axes are perpendicular.
    /// </para>
    /// <para>
    /// The offset is the same physical distance on every axis - one voxel of the coarsest
    /// one - rather than one index step per axis. Both give the same answer on a linear
    /// ramp, so this is not about correctness in the analytic sense; it is about what the
    /// estimate is averaged over. One index step per axis smooths the normal over 0.7 mm
    /// across a slice and 3.0 mm through the stack, so the normal is filtered four times
    /// harder in one direction than the others. On a real chest study that shows up as
    /// horizontal banding across the rendering, strong enough to read as a defect, and it
    /// disappears when the offset is made isotropic. It costs nothing: the same six
    /// trilinear samples, taken further apart on the fine axes.
    /// </para>
    /// <para>
    /// Within one coarse voxel of the edge of the volume the far sample falls outside and
    /// reads <see cref="Volume.OutsideValue"/>, so the boundary itself carries a strong
    /// gradient. That is the correct answer - the data really does end there - and it is
    /// the same shell of air the transfer function is already making invisible.
    /// </para>
    /// </remarks>
    public static Vector3D Gradient(Volume volume, double x, double y, double z)
    {
        ArgumentNullException.ThrowIfNull(volume);

        // One voxel of the coarsest axis, expressed in index steps on each of the three.
        double h = Math.Max(volume.Spacing.X, Math.Max(volume.Spacing.Y, volume.Spacing.Z));
        double hi = h / volume.Spacing.X;
        double hj = h / volume.Spacing.Y;
        double hk = h / volume.Spacing.Z;

        double alongI = volume.SampleTrilinear(x + hi, y, z) - volume.SampleTrilinear(x - hi, y, z);
        double alongJ = volume.SampleTrilinear(x, y + hj, z) - volume.SampleTrilinear(x, y - hj, z);
        double alongK = volume.SampleTrilinear(x, y, z + hk) - volume.SampleTrilinear(x, y, z - hk);

        Matrix4x4Affine toPatient = volume.VoxelToPatient;

        return toPatient.AxisI.Scale(alongI / (2 * h * volume.Spacing.X))
            + toPatient.AxisJ.Scale(alongJ / (2 * h * volume.Spacing.Y))
            + toPatient.AxisK.Scale(alongK / (2 * h * volume.Spacing.Z));
    }

    /// <summary>
    /// How brightly a surface with this gradient is lit, as a multiplier on its colour.
    /// </summary>
    /// <param name="gradient">From <see cref="Gradient"/>. Need not be normalised.</param>
    /// <param name="towardViewer">Unit vector from the surface back to the eye.</param>
    /// <param name="parameters">The lighting constants.</param>
    /// <remarks>
    /// <para>
    /// A single headlight at the eye, so the light direction and the view direction are the
    /// same vector. That collapses the Blinn half-vector onto the light direction and makes
    /// the specular term the diffuse term raised to a power - one dot product for both.
    /// </para>
    /// <para>
    /// The normal is taken as facing the viewer whichever way the gradient runs. A ray
    /// crosses a surface from outside going in, but the sign of the gradient depends on
    /// whether the ray reached dense tissue from air or air from dense tissue, and a
    /// surface that goes black when seen from the far side is a rendering artefact, not
    /// anatomy.
    /// </para>
    /// <para>
    /// Where there is no gradient there is no surface, and the sample is returned at full
    /// brightness rather than shaded: homogeneous tissue lit as if it were a surface is
    /// lit by whatever the numerical noise happened to point at.
    /// </para>
    /// </remarks>
    public static double Shade(Vector3D gradient, Vector3D towardViewer, ShadingParameters parameters)
    {
        double length = gradient.Length;
        if (length < MinimumGradient)
        {
            return 1.0;
        }

        double lambert = Math.Abs(gradient.Dot(towardViewer) / length);

        return parameters.Ambient
            + (parameters.Diffuse * lambert)
            + (parameters.Specular * Math.Pow(lambert, parameters.Shininess));
    }
}
