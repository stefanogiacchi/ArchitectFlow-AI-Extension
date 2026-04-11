using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArchitectFlow_AI.Models;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ArchitectFlow_AI.Services
{
    /// <summary>Risultato di una singola build.</summary>
    public class BuildResult
    {
        public bool   Succeeded          { get; set; }
        public int    ErrorCount         { get; set; }
        public int    WarningCount       { get; set; }
        public IReadOnlyList<CompilationError> Errors { get; set; }
            = Array.Empty<CompilationError>();
        public TimeSpan Duration         { get; set; }
    }

    /// <summary>
    /// Esegue la build della soluzione corrente in VS e raccoglie gli errori.
    /// Implementa <see cref="IVsUpdateSolutionEvents2"/> per ricevere il completamento
    /// in modo asincrono senza polling.
    /// </summary>
    public class BuildOrchestratorService : IVsUpdateSolutionEvents2, IDisposable
    {
        private readonly AsyncPackage        _package;
        private IVsSolutionBuildManager2     _buildManager;
        private uint                         _eventsCookie;

        private TaskCompletionSource<BuildResult> _buildTcs;
        private DateTime                          _buildStart;
        private bool                              _disposed;

        public event EventHandler<string> BuildOutput;

        public BuildOrchestratorService(AsyncPackage package)
        {
            _package = package;
        }

        /// <summary>Inizializza il servizio e si aggancia agli eventi di build.</summary>
        public async Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            _buildManager = (IVsSolutionBuildManager2)await _package
                .GetServiceAsync(typeof(SVsSolutionBuildManager));

            if (_buildManager != null)
                _buildManager.AdviseUpdateSolutionEvents(this, out _eventsCookie);
        }

        /// <summary>
        /// Avvia una build della soluzione e attende il completamento in modo asincrono.
        /// </summary>
        public async Task<BuildResult> BuildAsync(CancellationToken ct = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            // Una build alla volta
            if (_buildTcs != null && !_buildTcs.Task.IsCompleted)
                throw new InvalidOperationException("Build già in corso.");

            _buildTcs  = new TaskCompletionSource<BuildResult>();
            _buildStart = DateTime.UtcNow;

            BuildOutput?.Invoke(this, "▶ Avvio build soluzione…");

            // Avvia build (equivale a Ctrl+Shift+B)
            int hr = _buildManager.StartSimpleUpdateSolutionConfiguration(
                (uint)VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_BUILD,
                0,
                0);

            if (!ErrorHandler.Succeeded(hr))
            {
                _buildTcs.SetException(new Exception($"Impossibile avviare la build (hr=0x{hr:X8})"));
            }

            // Registra la cancellazione
            using var reg = ct.Register(() => _buildTcs.TrySetCanceled());

            return await _buildTcs.Task;
        }

        // ── IVsUpdateSolutionEvents2 ──────────────────────────────────────

        public int UpdateSolution_Done(int fSucceeded, int fModified, int fCancelCommand)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var duration = DateTime.UtcNow - _buildStart;
            bool success = fSucceeded == 1 && fCancelCommand == 0;

            // Leggi gli errori dalla Error List
            var errors = CompilationErrorParser.GetCurrentErrors(
                _package, includeWarnings: false);

            // Una build è "successa" solo se non ci sono errori
            success = success && errors.Count == 0;

            var result = new BuildResult
            {
                Succeeded    = success,
                ErrorCount   = errors.Count,
                Errors       = errors,
                Duration     = duration,
            };

            string icon = success ? "✅" : "❌";
            BuildOutput?.Invoke(this,
                $"{icon} Build completata in {duration.TotalSeconds:F1}s — " +
                $"{errors.Count} errori");

            _buildTcs?.TrySetResult(result);
            return VSConstants.S_OK;
        }

        public int UpdateSolution_Begin(ref int pfCancelUpdate)
        {
            BuildOutput?.Invoke(this, "⚙ Build in corso…");
            return VSConstants.S_OK;
        }

        public int UpdateSolution_Cancel()
        {
            _buildTcs?.TrySetCanceled();
            return VSConstants.S_OK;
        }

        // Stub degli altri eventi richiesti dall'interfaccia
        public int OnActiveProjectCfgChange(IVsHierarchy hier)             => VSConstants.S_OK;
        public int UpdateProjectCfg_Begin(IVsHierarchy hier, IVsCfg cfg,
            IVsCfg cfgNew, uint dwAction, ref int pfCancel)                => VSConstants.S_OK;
        public int UpdateProjectCfg_Done(IVsHierarchy hier, IVsCfg cfg,
            IVsCfg cfgNew, uint dwAction, int fSuccess, int fCancel)       => VSConstants.S_OK;
        public int UpdateSolution_StartUpdate(ref int pfCancelUpdate)      => VSConstants.S_OK;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_buildManager != null && _eventsCookie != 0)
                    _buildManager.UnadviseUpdateSolutionEvents(_eventsCookie);
            });
        }
    }
}
