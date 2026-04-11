using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArchitectFlow_AI.Models;
using Microsoft.VisualStudio.Shell;

namespace ArchitectFlow_AI.Services
{
    /// <summary>Snapshot dello stato di un'iterazione del loop.</summary>
    public class LoopIterationState
    {
        public int    Iteration         { get; set; }
        public string Phase             { get; set; } = string.Empty;   // "inject" | "build" | "done" | "error"
        public int    ErrorCount        { get; set; }
        public bool   BuildSucceeded    { get; set; }
        public string StatusMessage     { get; set; } = string.Empty;
        public IReadOnlyList<CompilationError> LastErrors { get; set; }
            = Array.Empty<CompilationError>();
    }

    /// <summary>Configurazione del loop agente.</summary>
    public class AgentLoopOptions
    {
        /// <summary>Numero massimo di iterazioni prima di arrendersi.</summary>
        public int MaxIterations { get; set; } = 10;

        /// <summary>Secondi di attesa dopo che Copilot ha completato la generazione
        /// prima di avviare la build (tempo per salvare i file).</summary>
        public int DelayAfterGenerationSeconds { get; set; } = 2;

        /// <summary>Includi i warning come errori da correggere.</summary>
        public bool TreatWarningsAsErrors { get; set; } = false;
    }

    /// <summary>
    /// Orchestratore del loop agente ArchitectFlow ↔ Copilot Enterprise.
    ///
    /// Flusso per iterazione:
    ///   1. Costruisce prompt (riferimenti + richiesta / errori precedenti)
    ///   2. Inietta il prompt in Copilot Chat e attende la generazione
    ///   3. Salva tutti i file aperti
    ///   4. Avvia la build della soluzione
    ///   5. Se build OK → fine (successo)
    ///   6. Se errori → formatta gli errori come nuovo prompt → torna a 1
    ///   7. Se max iterazioni raggiunto → termina con fallimento
    /// </summary>
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

            // Aggancia l'output di build e del bridge al nostro log
            _builder.BuildOutput   += (_, m) => Log(m);
            _copilot.StatusChanged += (_, m) => Log(m);
        }

        // ── Avvio / Stop ──────────────────────────────────────────────────

        /// <summary>
        /// Avvia il loop agente in background.
        /// </summary>
        public Task StartAsync(string initialPrompt, AgentLoopOptions options = null)
        {
            if (IsRunning)
                throw new InvalidOperationException("Loop già in esecuzione.");

            options ??= new AgentLoopOptions();
            _cts = new CancellationTokenSource();

            return Task.Run(() => RunLoopAsync(initialPrompt, options, _cts.Token));
        }

        /// <summary>Ferma il loop corrente.</summary>
        public void Stop()
        {
            _cts?.Cancel();
            Log("⏹ Loop interrotto dall'utente.");
        }

        // ── Loop principale ───────────────────────────────────────────────

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

                // ── FASE 1: Costruisci prompt ─────────────────────────────
                string prompt = BuildPrompt(currentPrompt, lastErrors, i);
                Notify(i, "inject", 0, false, "📝 Invio prompt a Copilot…", lastErrors);

                // ── FASE 2: Inietta in Copilot e attendi generazione ──────
                bool sent = await _copilot.SendPromptAndWaitAsync(prompt, _refMgr, ct);
                if (!sent)
                {
                    Notify(i, "error", 0, false, "❌ Impossibile comunicare con Copilot.", lastErrors);
                    return;
                }

                // ── FASE 3: Salva tutti i file ────────────────────────────
                await SaveAllFilesAsync(ct);
                Log($"💾 File salvati — attendo {options.DelayAfterGenerationSeconds}s…");
                await Task.Delay(options.DelayAfterGenerationSeconds * 1000, ct);

                // ── FASE 4: Build ─────────────────────────────────────────
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

                // ── FASE 5: Valuta risultato ──────────────────────────────
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

                // Prepara il prompt per la prossima iterazione con gli errori
                currentPrompt = CompilationErrorParser.FormatForPrompt(lastErrors, i);

                // Pausa minima prima della prossima iterazione
                await Task.Delay(500, ct);
            }

            // ── Max iterazioni raggiunto ──────────────────────────────────
            Notify(options.MaxIterations, "error", lastErrors.Count, false,
                $"⛔ Max iterazioni ({options.MaxIterations}) raggiunto — {lastErrors.Count} errori rimasti.",
                lastErrors);

            Log($"⛔ Loop terminato: limite iterazioni raggiunto.");
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static string BuildPrompt(
            string basePrompt,
            IReadOnlyList<CompilationError> previousErrors,
            int iteration)
        {
            if (iteration == 1 || previousErrors.Count == 0)
                return basePrompt;

            // Nelle iterazioni successive il prompt è già formattato dagli errori
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
