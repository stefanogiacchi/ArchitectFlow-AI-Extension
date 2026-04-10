using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace ArchitectFlowAI
{
    /// <summary>
    /// Comando che apre la Tool Window dal menu Strumenti > ArchitectFlow AI.
    /// </summary>
    internal sealed class ArchitectWindowCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901");

        private readonly AsyncPackage _package;

        private ArchitectWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            _ = new ArchitectWindowCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                var window = await _package.ShowToolWindowAsync(
                    typeof(ArchitectWindow), 0, true, _package.DisposalToken);

                if (window?.Frame == null)
                    throw new NotSupportedException("Impossibile creare la Tool Window.");
            });
        }
    }
}
