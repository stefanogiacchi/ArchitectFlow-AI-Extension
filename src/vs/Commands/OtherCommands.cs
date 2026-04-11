using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace ArchitectFlow_AI.Commands
{
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
            if (cs == null) return;
            new OpenArchitectFlowCommand(package, cs);
        }

        private void Execute(object sender, EventArgs e)
        {
            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    var pkg = _package as ArchitectFlowPackage;
                    if (pkg != null)
                    {
                        await pkg.ShowToolWindowAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ArchitectFlow AI] OpenArchitectFlow failed: {ex}");
                    ActivityLog.TryLogError("ArchitectFlow AI",
                        $"OpenArchitectFlow command failed: {ex}");
                }
            });
        }
    }

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
            if (cs == null) return;
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

            if (result == 6)
                mgr.ClearAll();
        }
    }

    internal sealed class AddFolderToReferencesCommand
    {
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
