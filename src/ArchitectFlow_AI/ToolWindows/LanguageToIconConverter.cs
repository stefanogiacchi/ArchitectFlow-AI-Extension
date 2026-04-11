using System;
using System.Globalization;
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
}