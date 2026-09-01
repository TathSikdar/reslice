using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace InterviewTrea.App.Views;

/// <summary>
/// Looks up a theme resource by the key a view model supplies.
/// </summary>
/// <remarks>
/// Which colour a crosshair line takes is a fact about geometry - it names the plane whose
/// normal is that screen axis - so the decision belongs in the view model. Which shade of
/// green that plane is drawn in belongs in <c>Themes/Dark.xaml</c>, and the project rule
/// is that views reference resources and never literals. This converter is the seam: the
/// view model passes a key, the dictionary still owns the colour.
/// </remarks>
public sealed class ResourceKeyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key && Application.Current is Application app
            ? app.TryFindResource(key) ?? DependencyProperty.UnsetValue
            : DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
