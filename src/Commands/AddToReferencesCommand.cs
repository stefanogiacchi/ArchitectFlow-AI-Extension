using ArchitectFlow_AI;
using ArchitectFlow_AI.ToolWindows;
using ArchitectFlow_AI.Services;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
            var selectedFolders = SolutionExplorerHelper.GetSelectedFolders();
            cmd.Visible = selectedFolders.Count > 0;
            cmd.Text = selectedFolders.Count > 1
                ? $"Aggiungi {selectedFolders.Count} cartelle a ArchitectFlow"
                : "Aggiungi cartella a ArchitectFlow";
        }

        // ── Esecuzione ────────────────────────────────────────────────────────

        private void OnAddFilesExecute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var files = SolutionExplorerHelper.GetSelectedFiles();
            if (files.Count == 0) return;

            // 1. Aggiunta all'istanza globale (quella che il Bridge userà dopo)
            int added = ArchitectFlowPackage.Instance.ReferenceFileManager.AddRange(files);

            // 2. Messaggio all'utente
            string msg = added == 1 ? "✓ File aggiunto." : $"✓ {added} file aggiunti.";
            if (added == 0) msg = "File già presenti.";
            ShowInfoBar(msg);

            // 3. AGGIORNAMENTO UI (Sincronizzazione forzata)
            //    Usa GetOrCreateToolWindowAsync che chiama EnsureInitialized()
            //    sul controllo, garantendo che il ViewModel sia sempre agganciato
            //    all'istanza globale di ReferenceFileManager.
            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                await _af.GetOrCreateToolWindowAsync();
            });
        }

        private void OnAddFolderExecute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            int totalAdded = 0;
            foreach (var (path, proj) in SolutionExplorerHelper.GetSelectedFolders())
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

        private List<string> GetSelectedFilePaths()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var files = SolutionExplorerHelper.GetSelectedFiles();
            var paths = new List<string>();
            foreach (var (p, _) in files) paths.Add(p);
            return paths;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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
