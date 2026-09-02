using System;
using System.Globalization;
using System.Windows.Data;

namespace InterviewTrea.App.Views;

/// <summary>
/// Multiplies a 0-to-1 fraction by the width given as the converter parameter.
/// </summary>
/// <remarks>
/// So that a plugin can report a proportion and let the shell decide how many pixels that
/// is. A view model that returned pixels would be deciding the layout of a panel it has
/// never seen.
/// </remarks>
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double fraction = value is double d ? d : 0;
        double scale = parameter is string text &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : 1;

        // Never zero: a bar of width zero for a bin holding a handful of voxels reads as
        // "nothing here", and a hairline reads as "almost nothing", which is the truth.
        return Math.Max(fraction * scale, fraction > 0 ? 1 : 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
