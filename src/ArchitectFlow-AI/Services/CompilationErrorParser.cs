using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ArchitectFlow_AI.Services
{
    public class CompilationError
    {
        public string Code        { get; set; }   // CS0246
        public string Message     { get; set; }   // 'Foo' not found
        public string FilePath    { get; set; }   // C:\...\Foo.cs
        public string FileName    { get; set; }   // Foo.cs
        public int    Line        { get; set; }
        public int    Column      { get; set; }
        public string Project     { get; set; }
        public __VSERRORCATEGORY Category { get; set; }

        public bool IsError   => Category == __VSERRORCATEGORY.EC_ERROR;
        public bool IsWarning => Category == __VSERRORCATEGORY.EC_WARNING;

        public override string ToString() =>
            $"{FileName}({Line},{Column}): {(IsError ? "error" : "warning")} {Code}: {Message}";
    }

    public static class CompilationErrorParser
    {
        public static IReadOnlyList<CompilationError> GetCurrentErrors(
            IServiceProvider serviceProvider,
            bool includeWarnings = false)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var result = new List<CompilationError>();

            var taskList = serviceProvider.GetService(typeof(SVsTaskList)) as IVsTaskList;
            if (taskList == null) return result;

            taskList.EnumTaskItems(out var enumerator);
            if (enumerator == null) return result;

            var items = new IVsTaskItem[1];
            var fetched = new uint[1];
            while (true)
            {
                enumerator.Next(1, items, fetched);
                if (fetched[0] == 0) break;

                var item = items[0];
                if (item == null) continue;

                var cats = new VSTASKCATEGORY[1];
                item.Category(cats);
                if (cats[0] != VSTASKCATEGORY.CAT_BUILDCOMPILE) continue;

                var priorities = new VSTASKPRIORITY[1];
                item.get_Priority(priorities);
                var vsCategory = priorities[0] == VSTASKPRIORITY.TP_HIGH
                    ? __VSERRORCATEGORY.EC_ERROR
                    : __VSERRORCATEGORY.EC_WARNING;

                if (!includeWarnings && vsCategory == __VSERRORCATEGORY.EC_WARNING)
                    continue;

                item.get_Text(out string text);
                item.Document(out string document);
                item.Line(out int line);
                item.Column(out int column);

                string code = ExtractErrorCode(text);

                result.Add(new CompilationError
                {
                    Code     = code,
                    Message  = StripErrorCode(text, code),
                    FilePath = document,
                    FileName = string.IsNullOrEmpty(document)
                        ? string.Empty
                        : System.IO.Path.GetFileName(document),
                    Line     = line + 1,
                    Column   = column + 1,
                    Category = vsCategory,
                });
            }

            return result;
        }

        public static string FormatForPrompt(
            IReadOnlyList<CompilationError> errors,
            int iteration)
        {
            if (errors.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine($"## Errori di compilazione — Iterazione {iteration}");
            sb.AppendLine();
            sb.AppendLine($"La build ha prodotto **{errors.Count} errore/i**. " +
                          "Correggi il codice generato in modo da eliminare tutti gli errori.");
            sb.AppendLine();
            sb.AppendLine("```");

            foreach (var grp in errors.GroupBy(e => e.FileName))
            {
                sb.AppendLine($"// {grp.Key}");
                foreach (var e in grp)
                    sb.AppendLine($"  {e}");
                sb.AppendLine();
            }

            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("Riscrivi o correggi **solo** le parti che causano questi errori, " +
                          "senza alterare la struttura generale del codice.");
            return sb.ToString();
        }

        private static string ExtractErrorCode(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var match = System.Text.RegularExpressions.Regex.Match(
                text, @"\b([A-Z]{1,8}\d{3,5})\b");
            return match.Success ? match.Value : string.Empty;
        }

        private static string StripErrorCode(string text, string code)
        {
            if (string.IsNullOrEmpty(code)) return text;
            return text.Replace(code + ":", "").Replace(code, "").Trim(' ', ':', '\t');
        }
    }
}
