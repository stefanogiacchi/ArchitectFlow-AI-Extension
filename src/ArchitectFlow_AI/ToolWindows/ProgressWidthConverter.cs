using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ArchitectFlow_AI.ToolWindows
{

    /// <summary>
    /// Calculates the pixel width of the progress bar fill.
    /// Used in a MultiBinding: values[0]=LoopIteration, values[1]=LoopMaxIterations,
    /// values[2]=ActualWidth of the container Border.
    ///
    /// BUG FIX: was incorrectly implementing IValueConverter; MultiBinding requires
    /// IMultiValueConverter. The old Convert() also read 'value' for all three
    /// inputs instead of the values[] array, so the bar was always 0.
    /// </summary>
    public class ProgressWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3)
                return 0.0;

            if (!TryToDouble(values[0], out double iteration) ||
                !TryToDouble(values[1], out double maxIter) ||
                !TryToDouble(values[2], out double container))
                return 0.0;

            if (maxIter <= 0 || double.IsNaN(container) || container <= 0)
                return 0.0;

            double ratio = Math.Min(Math.Max(iteration / maxIter, 0.0), 1.0);
            return container * ratio;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static bool TryToDouble(object obj, out double result)
        {
            result = 0;
            if (obj == null || obj == DependencyProperty.UnsetValue) return false;
            try { result = System.Convert.ToDouble(obj); return true; }
            catch { return false; }
        }
    }
}