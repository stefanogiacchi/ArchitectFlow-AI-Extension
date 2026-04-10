using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using ArchitectFlow_AI.Models;

namespace ArchitectFlow_AI.Services
{
    /// <summary>
    /// Gestisce la collezione dei file di riferimento selezionati dalla Solution Explorer.
    /// Questi file diventano "context vincolante" per tutto il codice generato dall'AI.
    /// Thread-safe per accesso da comandi VS e dal pannello UI.
    /// </summary>
    public class ReferenceFileManager
    {
        // ── Stato ──────────────────────────────────────────────────────────────
        private readonly object _lock = new object();
        private readonly Dictionary<string, ReferenceFile> _files =
            new Dictionary<string, ReferenceFile>(StringComparer.OrdinalIgnoreCase);

        // ── Evento modifiche (per aggiornare la UI) ────────────────────────────
        public event EventHandler<ReferenceFilesChangedEventArgs> FilesChanged;

        // ── Observable collection per il binding WPF ──────────────────────────
        /// <summary>
        /// Collezione WPF-bindable. Viene aggiornata sul thread UI ad ogni modifica.
        /// Bindarla direttamente all'ItemsControl nel Tool Window.
        /// </summary>
        public ObservableCollection<ReferenceFile> ObservableFiles { get; }
            = new ObservableCollection<ReferenceFile>();

        // ── API pubblica ───────────────────────────────────────────────────────

        /// <summary>Numero attuale di file di riferimento.</summary>
        public int Count { get { lock (_lock) return _files.Count; } }

        /// <summary>True se non ci sono file di riferimento.</summary>
        public bool IsEmpty => Count == 0;

        /// <summary>
        /// Aggiunge un singolo file dalla Solution Explorer.
        /// Ritorna true se aggiunto, false se già presente o limite raggiunto.
        /// </summary>
        public bool TryAdd(string fullPath, string projectName = null)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                return false;

            var options = ArchitectFlowPackage.Instance
                ?.GetDialogPage(typeof(ArchitectFlowOptionsPage)) as ArchitectFlowOptionsPage;
            int maxFiles = options?.MaxReferenceFiles ?? 30;

            lock (_lock)
            {
                if (_files.ContainsKey(fullPath))
                    return false;

                if (_files.Count >= maxFiles)
                {
                    NotifyMaxReached(maxFiles);
                    return false;
                }

                var info = new FileInfo(fullPath);
                var solutionDir = GetSolutionDirectory();

                var refFile = new ReferenceFile
                {
                    FullPath = fullPath,
                    RelativePath = solutionDir != null
                        ? MakeRelative(solutionDir, fullPath)
                        : fullPath,
                    SizeBytes = info.Length,
                    ProjectName = projectName ?? string.Empty,
                };

                // Precarica il contenuto se abilitato nelle opzioni
                if (options?.IncludeFileContent == true)
                    refFile.LoadContent();

                _files[fullPath] = refFile;
            }

            // Aggiorna l'ObservableCollection sul thread UI
            UpdateObservableCollection();
            FireFilesChanged();
            return true;
        }

        /// <summary>Aggiunge una lista di file in batch (multi-select dalla Solution Explorer).</summary>
        public int AddRange(IEnumerable<(string FullPath, string ProjectName)> files)
        {
            int added = 0;
            foreach (var (path, proj) in files)
            {
                if (TryAdd(path, proj))
                    added++;
            }
            return added;
        }

        /// <summary>Aggiunge tutti i file di una cartella (ricorsivo, filtra i binari).</summary>
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

            var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var parts = f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return !parts.Any(p => ignore.Contains(p));
                })
                .Where(f => sourceExtensions.Contains(Path.GetExtension(f)))
                .Select(f => (f, projectName ?? string.Empty));

            return AddRange(files);
        }

        /// <summary>Rimuove un file di riferimento per path.</summary>
        public bool Remove(string fullPath)
        {
            bool removed;
            lock (_lock)
            {
                removed = _files.Remove(fullPath);
            }
            if (removed)
            {
                UpdateObservableCollection();
                FireFilesChanged();
            }
            return removed;
        }

        /// <summary>Rimuove un ReferenceFile.</summary>
        public bool Remove(ReferenceFile file) => Remove(file.FullPath);

        /// <summary>Svuota tutti i riferimenti.</summary>
        public void ClearAll()
        {
            lock (_lock) _files.Clear();
            UpdateObservableCollection();
            FireFilesChanged();
        }

        /// <summary>Ritorna la copia corrente dei file come lista.</summary>
        public IReadOnlyList<ReferenceFile> GetFiles()
        {
            lock (_lock)
                return _files.Values.OrderBy(f => f.RelativePath).ToList();
        }

        /// <summary>True se il path è già tra i reference.</summary>
        public bool Contains(string fullPath)
        {
            lock (_lock)
                return _files.ContainsKey(fullPath);
        }

        // ── Context payload per il prompt AI ──────────────────────────────────

        /// <summary>
        /// Costruisce il blocco di testo da iniettare nel system prompt dell'AI.
        /// Contiene i path + il contenuto di tutti i file di riferimento.
        /// Questo payload rende i file "vincolanti" per la generazione.
        /// </summary>
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

            // Mostra sommario
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

        // ── Private helpers ────────────────────────────────────────────────────

        private void UpdateObservableCollection()
        {
            // Eseguiamo sempre sul Dispatcher del thread UI
            Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory
                    .SwitchToMainThreadAsync();

                ObservableFiles.Clear();
                foreach (var f in GetFiles())
                    ObservableFiles.Add(f);
            });
        }

        private void FireFilesChanged()
        {
            FilesChanged?.Invoke(this, new ReferenceFilesChangedEventArgs(Count));
        }

        private static void NotifyMaxReached(int max)
        {
            _ = Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory
                    .SwitchToMainThreadAsync();
                Microsoft.VisualStudio.Shell.VsShellUtilities.ShowMessageBox(
                    ArchitectFlowPackage.Instance,
                    $"Limite massimo di {max} file di riferimento raggiunto.\nRimuovi alcuni file prima di aggiungerne altri.",
                    "ArchitectFlow AI",
                    Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_WARNING,
                    Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            });
        }

        private static string GetSolutionDirectory()
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            var dte = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            var solutionPath = dte?.Solution?.FullName;
            return string.IsNullOrEmpty(solutionPath) ? null : Path.GetDirectoryName(solutionPath);
        }

        private static string MakeRelative(string basePath, string fullPath)
        {
            var baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) +
                                  Path.DirectorySeparatorChar);
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
