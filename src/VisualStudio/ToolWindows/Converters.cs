using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ArchitectFlow_AI.ToolWindows
{
    public class LanguageToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value as string) switch
            {
                "csharp" => "🔷",
                "vbnet" => "🔵",
                "typescript" => "🟦",
                "javascript" => "🟡",
                "python" => "🐍",
                "java" => "☕",
                "go" => "🐹",
                "rust" => "🦀",
                "cpp" => "⚙",
                "sql" => "🗃",
                "json" => "{ }",
                "yaml" => "📋",
                "xml" => "📄",
                "razor" => "🪥",
                "markdown" => "📝",
                "html" => "🌐",
                "css" => "🎨",
                "powershell" => "💙",
                "bash" => "🐚",
                _ => "📄",
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is int n && n > 0) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    public class InverseCountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is int n && n == 0) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

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

    public class ProgressWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return 0.0;

            double iteration = System.Convert.ToDouble(value ?? 0);
            double maxIter = System.Convert.ToDouble(parameter ?? 1);
            double container = System.Convert.ToDouble(value ?? 0);

            if (maxIter <= 0) return 0.0;
            if (double.IsNaN(container) || container < 0) return 0.0;

            double ratio = Math.Min(Math.Max(iteration / maxIter, 0.0), 1.0);

            return container * ratio;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
