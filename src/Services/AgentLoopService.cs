using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArchitectFlow_AI.Models;
using Microsoft.VisualStudio.Shell;

namespace ArchitectFlow_AI.Services
{
    public class LoopIterationState
    {
        public int    Iteration         { get; set; }
        public string Phase             { get; set; } = string.Empty;   
        public int    ErrorCount        { get; set; }
        public bool   BuildSucceeded    { get; set; }
        public string StatusMessage     { get; set; } = string.Empty;
        public IReadOnlyList<CompilationError> LastErrors { get; set; }
            = Array.Empty<CompilationError>();
    }

    public class AgentLoopOptions
    {
        public int MaxIterations { get; set; } = 10;
        public int DelayAfterGenerationSeconds { get; set; } = 2;
        public bool TreatWarningsAsErrors { get; set; } = false;
    }

    public class AgentLoopService
    {
        private readonly CopilotBridgeService   _copilot;
        private readonly BuildOrchestratorService _builder;
        private readonly ReferenceFileManager   _refMgr;
        private readonly AsyncPackage           _package;

        public event EventHandler<LoopIterationState>? IterationChanged;
        public event EventHandler<string>?             LogMessage;

        private CancellationTokenSource? _cts;

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

        public AgentLoopService(
            CopilotBridgeService    copilot,
            BuildOrchestratorService builder,
            ReferenceFileManager    refMgr,
            AsyncPackage            package)
        {
            _copilot  = copilot;
            _builder  = builder;
            _refMgr   = refMgr;
            _package  = package;

            _builder.BuildOutput   += (_, m) => Log(m);
            _copilot.StatusChanged += (_, m) => Log(m);
        }

        public Task StartAsync(string initialPrompt, AgentLoopOptions options = null)
        {
            if (IsRunning)
                throw new InvalidOperationException("Loop già in esecuzione.");

            options ??= new AgentLoopOptions();
            _cts = new CancellationTokenSource();

            return Task.Run(() => RunLoopAsync(initialPrompt, options, _cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            Log("⏹ Loop interrotto dall'utente.");
        }

        private async Task RunLoopAsync(
            string initialPrompt,
            AgentLoopOptions options,
            CancellationToken ct)
        {
            Log($"🚀 Loop agente avviato — max {options.MaxIterations} iterazioni");

            string currentPrompt = initialPrompt;
            IReadOnlyList<CompilationError> lastErrors = Array.Empty<CompilationError>();

            for (int i = 1; i <= options.MaxIterations; i++)
            {
                ct.ThrowIfCancellationRequested();

                Log($"\n━━━ ITERAZIONE {i}/{options.MaxIterations} ━━━");

                string prompt = BuildPrompt(currentPrompt, lastErrors, i);
                Notify(i, "inject", 0, false, "📝 Invio prompt a Copilot…", lastErrors);

                bool sent = await _copilot.SendPromptAndWaitAsync(prompt, _refMgr, ct);
                if (!sent)
                {
                    Notify(i, "error", 0, false, "❌ Impossibile comunicare con Copilot.", lastErrors);
                    return;
                }

                await SaveAllFilesAsync(ct);
                Log($"💾 File salvati — attendo {options.DelayAfterGenerationSeconds}s…");
                await Task.Delay(options.DelayAfterGenerationSeconds * 1000, ct);

                Notify(i, "build", 0, false, "🔨 Build in corso…", lastErrors);
                BuildResult buildResult;
                try
                {
                    buildResult = await _builder.BuildAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    Log("⏹ Build annullata.");
                    return;
                }

                lastErrors = buildResult.Errors;

                if (buildResult.Succeeded)
                {
                    Notify(i, "done", 0, true,
                        $"✅ Build riuscita all'iterazione {i}! Nessun errore.", lastErrors);
                    Log($"🎉 Loop completato con successo all'iterazione {i}.");
                    return;
                }

                Log($"❌ {buildResult.ErrorCount} errori — preparo prompt di correzione…");
                Notify(i, "inject", buildResult.ErrorCount, false,
                    $"🔄 {buildResult.ErrorCount} errori → iterazione {i + 1}…", lastErrors);

                currentPrompt = CompilationErrorParser.FormatForPrompt(lastErrors, i);

                await Task.Delay(500, ct);
            }

            Notify(options.MaxIterations, "error", lastErrors.Count, false,
                $"⛔ Max iterazioni ({options.MaxIterations}) raggiunto — {lastErrors.Count} errori rimasti.",
                lastErrors);

            Log($"⛔ Loop terminato: limite iterazioni raggiunto.");
        }

        private static string BuildPrompt(
            string basePrompt,
            IReadOnlyList<CompilationError> previousErrors,
            int iteration)
        {
            if (iteration == 1 || previousErrors.Count == 0)
                return basePrompt;

            return basePrompt;
        }

        private async Task SaveAllFilesAsync(CancellationToken ct)
        {
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync(ct);
            try
            {
                var dte = (EnvDTE.DTE)await _package.GetServiceAsync(typeof(EnvDTE.DTE));
                dte?.ExecuteCommand("File.SaveAll");
            }
            catch (Exception ex)
            {
                Log($"⚠ Salvataggio file: {ex.Message}");
            }
        }

        private void Notify(
            int iteration, string phase, int errorCount, bool buildOk,
            string message,
            IReadOnlyList<CompilationError> errors)
        {
            Log(message);
            IterationChanged?.Invoke(this, new LoopIterationState
            {
                Iteration      = iteration,
                Phase          = phase,
                ErrorCount     = errorCount,
                BuildSucceeded = buildOk,
                StatusMessage  = message,
                LastErrors     = errors,
            });
        }

        private void Log(string message)
        {
            LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");
        }
    }
}
