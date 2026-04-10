using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using ArchitectFlow_AI;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace ArchitectFlow_AI.Commands
{
    /// <summary>
    /// Comando "Aggiungi a ArchitectFlow References" nel menu contestuale della Solution Explorer.
    ///
    /// Supporta:
    ///  - Click singolo su un file
    ///  - Multi-selezione (Ctrl+Click / Shift+Click) di più file
    ///  - Click su una cartella (aggiunge ricorsivamente tutti i file sorgente)
    /// </summary>
    internal sealed class AddToReferencesCommand
    {
        private readonly AsyncPackage _package;
        private readonly ArchitectFlowPackage _af;

        private AddToReferencesCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package;
            _af = package as ArchitectFlowPackage;

            // ── Comando per FILE ──────────────────────────────────────────────
            var fileCmd = new OleMenuCommand(OnAddFilesExecute,
                new CommandID(PackageGuids.CommandSet, PackageCommandIds.AddToReferences));
            fileCmd.BeforeQueryStatus += OnQueryStatus_Files;
            commandService.AddCommand(fileCmd);

            // ── Comando per CARTELLE ──────────────────────────────────────────
            var folderCmd = new OleMenuCommand(OnAddFolderExecute,
                new CommandID(PackageGuids.CommandSet, PackageCommandIds.AddFolderToReferences));
            folderCmd.BeforeQueryStatus += OnQueryStatus_Folders;
            commandService.AddCommand(folderCmd);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var cs = (OleMenuCommandService)await package.GetServiceAsync(typeof(IMenuCommandService));
            new AddToReferencesCommand(package, cs);
        }

        // ── Visibilità ────────────────────────────────────────────────────────

        private void OnQueryStatus_Files(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var cmd = (OleMenuCommand)sender;
            var selectedPaths = GetSelectedFilePaths();
            cmd.Visible = selectedPaths.Count > 0;
            cmd.Text = selectedPaths.Count > 1
                ? $"Aggiungi {selectedPaths.Count} file a ArchitectFlow"
                : "Aggiungi a ArchitectFlow References";
        }

        private void OnQueryStatus_Folders(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var cmd = (OleMenuCommand)sender;
            var selectedFolders = GetSelectedFolderPaths();
            cmd.Visible = selectedFolders.Count > 0;
            cmd.Text = selectedFolders.Count > 1
                ? $"Aggiungi {selectedFolders.Count} cartelle a ArchitectFlow"
                : "Aggiungi cartella a ArchitectFlow";
        }

        // ── Esecuzione ────────────────────────────────────────────────────────

        private void OnAddFilesExecute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var files = GetSelectedFilesWithProject();
            if (files.Count == 0) return;

            int added = _af.ReferenceFileManager.AddRange(files);

            string msg = added == 1
                ? $"✓ File aggiunto come riferimento ArchitectFlow."
                : $"✓ {added} file aggiunti come riferimento ArchitectFlow.";

            if (added == 0)
                msg = "I file selezionati sono già tutti nei riferimenti.";

            ShowInfoBar(msg);

            // Apri il Tool Window per mostrare i reference aggiornati
            _ = _package.JoinableTaskFactory.RunAsync(() => _af.ShowToolWindowAsync());
        }

        private void OnAddFolderExecute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            int totalAdded = 0;
            foreach (var (path, proj) in GetSelectedFoldersWithProject())
            {
                totalAdded += _af.ReferenceFileManager.AddFolder(path, proj);
            }

            string msg = totalAdded > 0
                ? $"✓ {totalAdded} file aggiunti dalla cartella a ArchitectFlow References."
                : "Nessun file sorgente trovato nelle cartelle selezionate.";

            ShowInfoBar(msg);
            _ = _package.JoinableTaskFactory.RunAsync(() => _af.ShowToolWindowAsync());
        }

        // ── Lettura selezione dalla Solution Explorer ─────────────────────────

        /// <summary>
        /// Usa IVsMonitorSelection per recuperare TUTTI gli item selezionati,
        /// incluse le selezioni multiple con Ctrl+Click.
        /// </summary>
        private List<(string FullPath, string ProjectName)> GetSelectedFilesWithProject()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new List<(string, string)>();

            var monSel = Package.GetGlobalService(typeof(SVsShellMonitorSelection))
                as IVsMonitorSelection;
            if (monSel == null) return result;

            monSel.GetCurrentSelection(
                out var hierarchy, out var itemId,
                out var multiSelect, out _);

            if (multiSelect != null)
            {
                // Multi-selezione: itera su tutti gli item selezionati
                uint[] itemIds = new uint[1];
                IVsHierarchy[] hierarchies = new IVsHierarchy[1];
                int fetched;

                while (true)
                {
                    Marshal.ThrowExceptionForHR(
                        multiSelect.GetSelectedItems(0, 1, hierarchies, itemIds, out fetched));
                    if (fetched == 0) break;

                    var path = GetItemPath(hierarchies[0], itemIds[0]);
                    var proj = GetProjectName(hierarchies[0]);

                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        result.Add((path, proj));
                }
            }
            else if (hierarchy != null && itemId != VSConstants.VSITEMID_NIL)
            {
                // Selezione singola
                var path = GetItemPath(hierarchy, itemId);
                var proj = GetProjectName(hierarchy);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    result.Add((path, proj));
            }

            return result;
        }

        private List<string> GetSelectedFilePaths()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var files = GetSelectedFilesWithProject();
            var paths = new List<string>();
            foreach (var (p, _) in files) paths.Add(p);
            return paths;
        }

        private List<(string FolderPath, string ProjectName)> GetSelectedFoldersWithProject()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new List<(string, string)>();

            var monSel = Package.GetGlobalService(typeof(SVsShellMonitorSelection))
                as IVsMonitorSelection;
            if (monSel == null) return result;

            monSel.GetCurrentSelection(
                out var hierarchy, out var itemId,
                out var multiSelect, out _);

            if (multiSelect != null)
            {
                uint[] itemIds = new uint[1];
                IVsHierarchy[] hierarchies = new IVsHierarchy[1];

                while (true)
                {
                    multiSelect.GetSelectedItems(0, 1, hierarchies, itemIds, out int fetched);
                    if (fetched == 0) break;

                    var path = GetItemPath(hierarchies[0], itemIds[0]);
                    var proj = GetProjectName(hierarchies[0]);

                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        result.Add((path, proj));
                }
            }
            else if (hierarchy != null && itemId != VSConstants.VSITEMID_NIL)
            {
                var path = GetItemPath(hierarchy, itemId);
                var proj = GetProjectName(hierarchy);
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    result.Add((path, proj));
            }

            return result;
        }

        private List<string> GetSelectedFolderPaths()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var folders = GetSelectedFoldersWithProject();
            var paths = new List<string>();
            foreach (var (p, _) in folders) paths.Add(p);
            return paths;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string GetItemPath(IVsHierarchy hierarchy, uint itemId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            hierarchy.GetCanonicalName(itemId, out string path);
            return path;
        }

        private static string GetProjectName(IVsHierarchy hierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            hierarchy.GetProperty(VSConstants.VSITEMID_ROOT,
                (int)__VSHPROPID.VSHPROPID_Name, out object name);
            return name?.ToString() ?? string.Empty;
        }

        private static void ShowInfoBar(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                ArchitectFlowPackage.Instance,
                message,
                "ArchitectFlow AI",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
