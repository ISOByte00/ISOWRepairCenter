using System.Globalization;
using System.Windows.Data;

namespace ISOWRepairCenter;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool enabled ? !enabled : true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool enabled ? !enabled : true;
}
