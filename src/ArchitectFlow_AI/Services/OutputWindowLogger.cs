using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ArchitectFlow_AI.Services
{
    /// <summary>
    /// Scrive messaggi nel riquadro "ArchitectFlow AI" della finestra Output di Visual Studio.
    /// Crea automaticamente il riquadro se non esiste.
    /// </summary>
    public class OutputWindowLogger
    {
        private static readonly Guid PaneGuid = new Guid("D1E2F3A4-B5C6-7890-DEFA-012345678903");
        private const string PaneName = "ArchitectFlow AI";

        private IVsOutputWindowPane _pane;
        private readonly AsyncPackage _package;

        public OutputWindowLogger(AsyncPackage package)
        {
            _package = package;
        }

        public async System.Threading.Tasks.Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var outputWindow = (IVsOutputWindow)await _package
                .GetServiceAsync(typeof(SVsOutputWindow));

            if (outputWindow == null) return;

            // Crea il riquadro se non esiste
            var guid = PaneGuid;
            outputWindow.GetPane(ref guid, out _pane);
            if (_pane == null)
            {
                outputWindow.CreatePane(ref guid, PaneName, 1, 1);
                outputWindow.GetPane(ref guid, out _pane);
            }
        }

        /// <summary>Scrive una riga nel riquadro Output di VS.</summary>
        public void Log(string message)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                try
                {
                    _pane?.OutputString(message + "\n");
                }
                catch { /* ignora se la finestra non è disponibile */ }
            });
        }

        /// <summary>Porta in primo piano il riquadro ArchitectFlow nell'Output Window.</summary>
        public void Activate()
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _pane?.Activate();
            });
        }

        /// <summary>Svuota il riquadro Output.</summary>
        public void Clear()
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _pane?.Clear();
            });
        }
    }
}
