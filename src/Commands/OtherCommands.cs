using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace ArchitectFlow_AI.Commands
{
    /// <summary>Comando "Apri ArchitectFlow AI" — accessibile da menu Tools e dal context menu.</summary>
    internal sealed class OpenArchitectFlowCommand
    {
        private readonly AsyncPackage _package;

        private OpenArchitectFlowCommand(AsyncPackage package, OleMenuCommandService cs)
        {
            _package = package;
            var cmd = new OleMenuCommand(Execute,
                new CommandID(PackageGuids.CommandSet, PackageCommandIds.OpenArchitectFlow));
            cs.AddCommand(cmd);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var cs = (OleMenuCommandService)await package.GetServiceAsync(typeof(IMenuCommandService));
            new OpenArchitectFlowCommand(package, cs);
        }

        private void Execute(object sender, EventArgs e)
        {
            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                await (ArchitectFlowPackage.Instance?.ShowToolWindowAsync() ?? Task.CompletedTask);
            });
        }
    }

    /// <summary>Comando "Svuota tutti i riferimenti ArchitectFlow".</summary>
    internal sealed class ClearReferencesCommand
    {
        private readonly AsyncPackage _package;

        private ClearReferencesCommand(AsyncPackage package, OleMenuCommandService cs)
        {
            _package = package;
            var cmd = new OleMenuCommand(Execute,
                new CommandID(PackageGuids.CommandSet, PackageCommandIds.ClearReferences));
            cmd.BeforeQueryStatus += OnQueryStatus;
            cs.AddCommand(cmd);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var cs = (OleMenuCommandService)await package.GetServiceAsync(typeof(IMenuCommandService));
            new ClearReferencesCommand(package, cs);
        }

        private void OnQueryStatus(object sender, EventArgs e)
        {
            var cmd = (OleMenuCommand)sender;
            cmd.Enabled = ArchitectFlowPackage.Instance?.ReferenceFileManager.IsEmpty == false;
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var mgr = ArchitectFlowPackage.Instance?.ReferenceFileManager;
            if (mgr == null) return;

            var result = VsShellUtilities.ShowMessageBox(
                ArchitectFlowPackage.Instance,
                $"Rimuovere tutti i {mgr.Count} file di riferimento ArchitectFlow?",
                "ArchitectFlow AI",
                Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_QUERY,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);

            if (result == 6) // IDYES
                mgr.ClearAll();
        }
    }

    /// <summary>Stub per AddFolderToReferencesCommand — la logica reale è in AddToReferencesCommand.</summary>
    internal sealed class AddFolderToReferencesCommand
    {
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Registrato dentro AddToReferencesCommand — questo stub evita errori di inizializzazione.
            await System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
