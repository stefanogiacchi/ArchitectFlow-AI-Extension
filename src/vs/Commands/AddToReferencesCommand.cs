using ArchitectFlow_AI;
using ArchitectFlow_AI.ToolWindows;
using ArchitectFlow_AI.Services;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace ArchitectFlow_AI.Commands
{
    internal sealed class AddToReferencesCommand
    {
        private readonly AsyncPackage _package;
        private readonly ArchitectFlowPackage _af;

        private AddToReferencesCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package;
            _af = package as ArchitectFlowPackage;

            var fileCmd = new OleMenuCommand(OnAddFilesExecute,
                new CommandID(PackageGuids.CommandSet, PackageCommandIds.AddToReferences));
            fileCmd.BeforeQueryStatus += OnQueryStatus_Files;
            commandService.AddCommand(fileCmd);

            var folderCmd = new OleMenuCommand(OnAddFolderExecute,
                new CommandID(PackageGuids.CommandSet, PackageCommandIds.AddFolderToReferences));
            folderCmd.BeforeQueryStatus += OnQueryStatus_Folders;
            commandService.AddCommand(folderCmd);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var cs = (OleMenuCommandService)await package.GetServiceAsync(typeof(IMenuCommandService));
            if (cs == null) return;
            new AddToReferencesCommand(package, cs);
        }

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

        private void OnAddFilesExecute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var files = SolutionExplorerHelper.GetSelectedFiles();
            if (files.Count == 0) return;

            int added = ArchitectFlowPackage.Instance.ReferenceFileManager.AddRange(files);

            string msg = added == 1 ? "✓ File aggiunto." : $"✓ {added} file aggiunti.";
            if (added == 0) msg = "File già presenti.";
            ShowInfoBar(msg);

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

        private List<string> GetSelectedFilePaths()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var files = SolutionExplorerHelper.GetSelectedFiles();
            var paths = new List<string>();
            foreach (var (p, _) in files) paths.Add(p);
            return paths;
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
