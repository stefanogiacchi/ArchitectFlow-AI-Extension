// ============================================================
// Models/ReferenceFile.cs
// ============================================================
using System;
using System.IO;

namespace ArchitectFlow_AI.Models
{
    /// <summary>
    /// Rappresenta un file di riferimento selezionato dalla Solution Explorer.
    /// Il suo contenuto diventa contesto vincolante per la generazione AI.
    /// </summary>
    public class ReferenceFile
    {
        /// <summary>Path assoluto sul disco.</summary>
        public string FullPath { get; set; }

        /// <summary>Path relativo rispetto alla soluzione (per display).</summary>
        public string RelativePath { get; set; }

        /// <summary>Solo il nome del file con estensione.</summary>
        public string FileName => Path.GetFileName(FullPath);

        /// <summary>Estensione senza punto, lowercase.</summary>
        public string Extension => Path.GetExtension(FullPath).TrimStart('.').ToLowerInvariant();

        /// <summary>Linguaggio di programmazione dedotto dall'estensione.</summary>
        public string Language => DetectLanguage(Extension);

        /// <summary>Dimensione in byte.</summary>
        public long SizeBytes { get; set; }

        /// <summary>Dimensione formattata per il display.</summary>
        public string SizeDisplay => FormatSize(SizeBytes);

        /// <summary>Quando è stato aggiunto come reference.</summary>
        public DateTime AddedAt { get; set; } = DateTime.Now;

        /// <summary>Nome del progetto VS a cui appartiene.</summary>
        public string ProjectName { get; set; }

        /// <summary>Contenuto testuale del file (caricato on-demand).</summary>
        public string Content { get; set; }

        /// <summary>Indica se il contenuto è già stato caricato.</summary>
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
