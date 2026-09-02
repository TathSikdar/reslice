using System;
using InterviewTrea.Applications.Abstractions;

namespace InterviewTrea.Applications.Histogram;

/// <summary>
/// FR-507. The reference application: a Hounsfield histogram of the loaded volume.
/// </summary>
/// <remarks>
/// <para>
/// It exists to demonstrate the contract, and it is deliberately the least clinical thing
/// that could: no interpretation, no thresholds, no claim about what the numbers mean. The
/// interesting part is what it does <em>not</em> reference - no WPF, no fo-dicom, no
/// viewport, no bitmap. It is handed a volume and a plane and hands back a view model and
/// an overlay, and the shell does the rest.
/// </para>
/// <para>
/// Registration in the composition root is one line, which is the claim this whole seam is
/// making about Phase 2:
/// <c>services.AddSingleton&lt;IClinicalApplication, HistogramApplication&gt;();</c>
/// </para>
/// </remarks>
public sealed class HistogramApplication : IClinicalApplication
{
    public string Id => "interviewtrea.histogram";

    public string DisplayName => "Volume Histogram";

    public string Description => "Distribution of Hounsfield units across the loaded volume.";

    /// <summary>
    /// CT only, like the rest of Phase 1. Asked before the menu entry is enabled, so an
    /// application that cannot run says so rather than failing once it is launched.
    /// </summary>
    public bool CanRun(IApplicationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return string.Equals(context.Volume.Metadata.Modality, "CT", StringComparison.Ordinal);
    }

    public IApplicationSession Start(IApplicationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new HistogramSession(context);
    }
}
