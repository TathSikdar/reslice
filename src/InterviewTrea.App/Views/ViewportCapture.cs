using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InterviewTrea.Core;

namespace InterviewTrea.App.Views;

/// <summary>
/// FR-409. Turns a viewport into a PNG with the RQ-1 disclaimer burned into the pixels.
/// </summary>
/// <remarks>
/// <para>
/// Burned in, not drawn beside: the point of the requirement is that the disclaimer cannot
/// be separated from the image afterwards. A caption in a surrounding document survives
/// exactly as long as the document does, and a screenshot of a CT slice with no statement
/// on it is the artefact that ends up in someone's slide deck.
/// </para>
/// <para>
/// This lives in the App layer because it is WPF from end to end - the rendering project
/// may never reference <c>System.Windows</c>. What it captures is the pane as it stands,
/// crosshair, overlays, measurements and all, rather than a re-render from the volume, so
/// what is exported is provably what was on screen.
/// </para>
/// </remarks>
internal static class ViewportCapture
{
    /// <summary>Space above and below the caption text, in device-independent pixels.</summary>
    private const double CaptionPadding = 6.0;

    /// <summary>
    /// Caption height as a fraction of the image width, bounded. A fixed point size is
    /// unreadable on a large export and comically large on a small one; tying it to the
    /// width keeps it about the same size relative to the image it qualifies.
    /// </summary>
    private const double CaptionScale = 1.0 / 70.0;
    private const double MinimumCaptionSize = 9.0;
    private const double MaximumCaptionSize = 18.0;

    public static BitmapSource Render(
        FrameworkElement pane, Typeface typeface, Brush background, Brush foreground)
    {
        ArgumentNullException.ThrowIfNull(pane);

        double width = pane.ActualWidth;
        double height = pane.ActualHeight;

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The viewport has not been laid out yet.");
        }

        // Device pixels, not layout pixels. On a 150% display the two differ by half again,
        // and capturing at layout size would export a visibly soft image of a viewer whose
        // whole subject is fine greyscale detail.
        DpiScale dpi = VisualTreeHelper.GetDpi(pane);

        RenderTargetBitmap captured = Surface(width, height, dpi);
        captured.Render(pane);

        double size = Math.Clamp(width * CaptionScale, MinimumCaptionSize, MaximumCaptionSize);

        FormattedText caption = new(
            Disclaimer.Text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            foreground,
            dpi.PixelsPerDip);

        double bar = Math.Ceiling(caption.Height) + (CaptionPadding * 2);

        DrawingVisual composed = new();

        using (DrawingContext context = composed.RenderOpen())
        {
            context.DrawImage(captured, new Rect(0, 0, width, height));

            // Over the bottom of the image rather than added below it: the exported file
            // keeps the aspect ratio of the pane, and a strip cropped off the bottom is a
            // more obvious act of removal than a caption that was never inside the frame.
            context.DrawRectangle(background, pen: null, new Rect(0, height - bar, width, bar));
            context.DrawText(caption, new Point((width - caption.Width) / 2, height - bar + CaptionPadding));
        }

        RenderTargetBitmap output = Surface(width, height, dpi);
        output.Render(composed);
        output.Freeze();

        return output;
    }

    private static RenderTargetBitmap Surface(double width, double height, DpiScale dpi) => new(
        (int)Math.Ceiling(width * dpi.DpiScaleX),
        (int)Math.Ceiling(height * dpi.DpiScaleY),
        96 * dpi.DpiScaleX,
        96 * dpi.DpiScaleY,
        PixelFormats.Pbgra32);
}
