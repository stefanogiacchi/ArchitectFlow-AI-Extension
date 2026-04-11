using System;
using System.IO;

namespace ArchitectFlow_AI.Models
{
    public class ReferenceFile
    {
        public string FullPath { get; set; }
        public string RelativePath { get; set; }
        public string FileName => Path.GetFileName(FullPath);
        public string Extension => Path.GetExtension(FullPath).TrimStart('.').ToLowerInvariant();
        public string Language => DetectLanguage(Extension);
        public long SizeBytes { get; set; }
        public string SizeDisplay => FormatSize(SizeBytes);
        public DateTime AddedAt { get; set; } = DateTime.Now;
        public string ProjectName { get; set; }
        public string Content { get; set; }
        public bool IsContentLoaded => Content != null;

        public string LoadContent()
        {
            if (!IsContentLoaded)
            {
                try { Content = File.ReadAllText(FullPath); }
                catch { Content = "[Impossibile leggere il file]"; }
            }
            return Content;
        }

        private static string DetectLanguage(string ext) => ext switch
        {
            "cs" => "csharp",
            "vb" => "vbnet",
            "ts" or "tsx" => "typescript",
            "js" or "jsx" => "javascript",
            "py" => "python",
            "java" => "java",
            "go" => "go",
            "rs" => "rust",
            "cpp" or "cc" or "cxx" => "cpp",
            "c" or "h" => "c",
            "sql" => "sql",
            "xml" => "xml",
            "json" => "json",
            "yaml" or "yml" => "yaml",
            "md" => "markdown",
            "xaml" => "xml",
            "razor" or "cshtml" => "razor",
            "html" => "html",
            "css" => "css",
            "ps1" => "powershell",
            "sh" => "bash",
            _ => "plaintext"
        };

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / 1024.0 / 1024.0:F1} MB";
        }
    }
}
