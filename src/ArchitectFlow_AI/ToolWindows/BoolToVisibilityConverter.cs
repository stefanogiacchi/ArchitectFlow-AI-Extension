using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ArchitectFlow_AI.ToolWindows
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;
            bool inverse = parameter?.ToString() == "inverse";
            return (flag ^ inverse) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}