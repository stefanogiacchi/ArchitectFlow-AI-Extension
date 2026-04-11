using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using ArchitectFlow_AI.Models;
using Microsoft.VisualStudio.Shell;

namespace ArchitectFlow_AI.Services
{
    public class ReferenceFileManager
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, ReferenceFile> _files =
            new Dictionary<string, ReferenceFile>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<ReferenceFilesChangedEventArgs> FilesChanged;

        public ObservableCollection<ReferenceFile> ObservableFiles { get; }
            = new ObservableCollection<ReferenceFile>();

        public int Count { get { lock (_lock) return _files.Count; } }
        public bool IsEmpty => Count == 0;

        public bool TryAdd(string fullPath, string projectName = null)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                return false;

            var options = ArchitectFlowPackage.Instance
                ?.GetDialogPage(typeof(ArchitectFlowOptionsPage)) as ArchitectFlowOptionsPage;
            int maxFiles = options?.MaxReferenceFiles ?? 30;

            lock (_lock)
            {
                if (_files.ContainsKey(fullPath)) return false;
                if (_files.Count >= maxFiles) { NotifyMaxReached(maxFiles); return false; }

                var info = new FileInfo(fullPath);

                // BUG FIX: GetSolutionDirectory() requires the UI thread.
                // Resolve it safely: if we're already on UI thread use it directly,
                // otherwise run a blocking switch to UI thread (still safe here
                // because we hold no VS locks that could deadlock).
                string solutionDir = null;
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    solutionDir = GetSolutionDirectory();
                });

                var refFile = new ReferenceFile
                {
                    FullPath = fullPath,
                    RelativePath = solutionDir != null
                        ? MakeRelative(solutionDir, fullPath)
                        : fullPath,
                    SizeBytes = info.Length,
                    ProjectName = projectName ?? string.Empty,
                };

                if (options?.IncludeFileContent == true)
                    refFile.LoadContent();

                _files[fullPath] = refFile;
            }

            UpdateObservableCollection();
            FireFilesChanged();
            return true;
        }

        public int AddRange(IEnumerable<(string FullPath, string ProjectName)> files)
        {
            int added = 0;
            foreach (var (path, proj) in files)
                if (TryAdd(path, proj)) added++;
            return added;
        }

        public int AddFolder(string folderPath, string projectName = null)
        {
            if (!Directory.Exists(folderPath)) return 0;

            var ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "bin", "obj", ".git", ".vs", "node_modules", "packages",
                "__pycache__", "dist", "out", "build"
            };

            var sourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".cs", ".vb", ".ts", ".tsx", ".js", ".jsx", ".py", ".java",
                ".go", ".rs", ".cpp", ".c", ".h", ".sql", ".xml", ".json",
                ".yaml", ".yml", ".md", ".xaml", ".razor", ".cshtml",
                ".html", ".css", ".ps1", ".sh"
            };

            var files = Directory
                .EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var parts = f.Split(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return !parts.Any(p => ignore.Contains(p));
                })
                .Where(f => sourceExtensions.Contains(Path.GetExtension(f)))
                .Select(f => (f, projectName ?? string.Empty));

            return AddRange(files);
        }

        public bool Remove(string fullPath)
        {
            bool removed;
            lock (_lock) { removed = _files.Remove(fullPath); }
            if (removed) { UpdateObservableCollection(); FireFilesChanged(); }
            return removed;
        }

        public bool Remove(ReferenceFile file) => Remove(file.FullPath);

        public void ClearAll()
        {
            lock (_lock) _files.Clear();
            UpdateObservableCollection();
            FireFilesChanged();
        }

        public IReadOnlyList<ReferenceFile> GetFiles()
        {
            lock (_lock)
                return _files.Values.OrderBy(f => f.RelativePath).ToList();
        }

        public bool Contains(string fullPath)
        {
            lock (_lock) return _files.ContainsKey(fullPath);
        }

        public string BuildContextPayload()
        {
            var files = GetFiles();
            if (files.Count == 0) return string.Empty;

            var options = ArchitectFlowPackage.Instance
                ?.GetDialogPage(typeof(ArchitectFlowOptionsPage)) as ArchitectFlowOptionsPage;
            bool includeContent = options?.IncludeFileContent ?? true;

            var sb = new StringBuilder();
            sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║           FILE DI RIFERIMENTO — CONTESTO VINCOLANTE          ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
            sb.AppendLine();
            sb.AppendLine($"Sono stati selezionati {files.Count} file di riferimento dalla Solution Explorer.");
            sb.AppendLine("OBBLIGATORIO: Tutto il codice generato DEVE:");
            sb.AppendLine("  1. Rispettare le stesse convenzioni di naming (namespace, classi, metodi)");
            sb.AppendLine("  2. Seguire gli stessi pattern architetturali (CQRS, Mediator, Dapper, ecc.)");
            sb.AppendLine("  3. Integrarsi perfettamente con i file esistenti mostrati di seguito");
            sb.AppendLine("  4. Usare gli stessi package NuGet / using già presenti");
            sb.AppendLine("  5. Mantenere la struttura di cartelle e namespace coerente");
            sb.AppendLine();

            sb.AppendLine("── SOMMARIO FILE DI RIFERIMENTO ──");
            foreach (var f in files)
                sb.AppendLine($"  • [{f.Language}] {f.RelativePath}  ({f.SizeDisplay})");
            sb.AppendLine();

            if (includeContent)
            {
                sb.AppendLine("── CONTENUTO FILE ──────────────────────────────────────────────");
                foreach (var f in files)
                {
                    sb.AppendLine();
                    sb.AppendLine($"▶ FILE: {f.RelativePath}");
                    sb.AppendLine($"  Progetto: {f.ProjectName}  |  Linguaggio: {f.Language}");
                    sb.AppendLine($"```{f.Language}");
                    sb.AppendLine(f.LoadContent());
                    sb.AppendLine("```");
                    sb.AppendLine(new string('─', 64));
                }
            }

            sb.AppendLine();
            sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║                   FINE FILE DI RIFERIMENTO                   ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
            sb.AppendLine();

            return sb.ToString();
        }

        // BUG FIX: was using .Run() (blocking) which risks a deadlock when called
        // from the UI thread. Switched to .RunAsync() + fire-and-forget pattern.
        // The observable collection must be updated on the UI thread (WPF requirement).
        private void UpdateObservableCollection()
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var snapshot = GetFiles();
                ObservableFiles.Clear();
                foreach (var f in snapshot)
                    ObservableFiles.Add(f);
            });
        }

        private void FireFilesChanged()
        {
            FilesChanged?.Invoke(this, new ReferenceFilesChangedEventArgs(Count));
        }

        private static void NotifyMaxReached(int max)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                VsShellUtilities.ShowMessageBox(
                    ArchitectFlowPackage.Instance,
                    $"Limite massimo di {max} file di riferimento raggiunto.\n" +
                    "Rimuovi alcuni file prima di aggiungerne altri.",
                    "ArchitectFlow AI",
                    Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_WARNING,
                    Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            });
        }

        // BUG FIX: must only be called from the UI thread.
        // Callers are responsible for the thread switch (see TryAdd above).
        private static string GetSolutionDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            var solutionPath = dte?.Solution?.FullName;
            return string.IsNullOrEmpty(solutionPath)
                ? null
                : Path.GetDirectoryName(solutionPath);
        }

        private static string MakeRelative(string basePath, string fullPath)
        {
            var baseUri = new Uri(
                basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var fileUri = new Uri(fullPath);
            return Uri.UnescapeDataString(
                baseUri.MakeRelativeUri(fileUri).ToString()
                       .Replace('/', Path.DirectorySeparatorChar));
        }
    }

    public class ReferenceFilesChangedEventArgs : EventArgs
    {
        public int Count { get; }
        public ReferenceFilesChangedEventArgs(int count) => Count = count;
    }
}