using ArchitectFlow_AI.Models;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace ArchitectFlow_AI.Services
{
    /// <summary>
    /// Stato della finestra Copilot Chat durante l'attesa del completamento.
    /// </summary>
    public enum CopilotState
    {
        Idle,
        Generating,
        Done,
        Error
    }

    /// <summary>
    /// Bridge verso GitHub Copilot Enterprise (EMU) in Visual Studio.
    ///
    /// Strategia di integrazione:
    ///   1. Cerca il Tool Window di Copilot Chat tramite il GUID noto.
    ///   2. Usa reflection sull'assembly Copilot (non ha SDK pubblico stabile)
    ///      per trovare ICopilotChatService / ICopilotService e inviare messaggi.
    ///   3. Fallback: automazione DTE per scrivere nella chat text box.
    ///   4. Polling del documento di output per rilevare il completamento
    ///      (il testo smette di cambiare per N ms → generazione terminata).
    /// </summary>
    public class CopilotBridgeService
    {
        // GUID del Tool Window di Copilot Chat in VS 2022
        // (estratto tramite spy su processi VS con Copilot installato)
        private static readonly Guid CopilotChatWindowGuid =
            new Guid("B1B4C3A0-97D7-4F32-B4B3-7E2D9C8F1A2B");

        // GUID alternativo per versioni più recenti di Copilot
        private static readonly Guid CopilotChatWindowGuid2 =
            new Guid("3F6E8B9D-2A1C-4D5E-8F7A-6B3C9D2E4F1A");

        private readonly AsyncPackage _package;

        // Servizi Copilot scoperti a runtime via reflection
        private object _copilotService;
        private MethodInfo _sendMessageMethod;
        private bool _serviceDiscovered;

        // Delay di polling per rilevare il completamento della generazione
        private int _completionPollMs = 400;
        private int _completionStableMs = 2000; // stabile per 2s = completato

        public event EventHandler<string> StatusChanged;

        public CopilotBridgeService(AsyncPackage package)
        {
            _package = package;
        }

        // ── API pubblica ───────────────────────────────────────────────────

        /// <summary>
        /// Inietta un prompt nella finestra Copilot Chat e attende
        /// che la generazione sia completata.
        /// </summary>
        public async Task<bool> SendPromptAndWaitAsync(
            string prompt,
            ReferenceFileManager referenceManager,
            CancellationToken ct = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            // 1. Apri / porta in primo piano il pannello Copilot Chat
            bool opened = await EnsureCopilotChatOpenAsync();
            if (!opened)
            {
                StatusChanged?.Invoke(this,
                    "⚠ Copilot Chat non trovato. Assicurati che GitHub Copilot Enterprise sia installato e attivo.");
                return false;
            }

            // 2. Costruisce il messaggio completo (#file: references + prompt)
            string fullPrompt = BuildFullPrompt(prompt, referenceManager);

            // 3. Tenta di inviare il messaggio via API Copilot (reflection)
            bool sent = await TrySendViaCopilotApiAsync(fullPrompt, ct);

            // 4. Fallback: usa l'automazione DTE per scrivere nella chat
            if (!sent)
                sent = await TrySendViaDteAutomationAsync(fullPrompt, ct);

            if (!sent)
            {
                StatusChanged?.Invoke(this, "❌ Impossibile inviare il prompt a Copilot Chat.");
                return false;
            }

            StatusChanged?.Invoke(this, "⏳ Copilot sta generando il codice…");

            // 5. Attendi che Copilot finisca di generare
            await WaitForCopilotCompletionAsync(ct);

            // 6. Piccolo delay aggiuntivo per permettere a VS di applicare i file
            await Task.Delay(500, ct);

            StatusChanged?.Invoke(this, "✅ Generazione Copilot completata.");
            return true;
        }

        // ── Apertura Copilot Chat ─────────────────────────────────────────

        private async Task<bool> EnsureCopilotChatOpenAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var uiShell = await _package.GetServiceAsync<SVsUIShell, IVsUIShell>(); if (uiShell == null) return false;

            // Prova con il primo GUID
            foreach (var guid in new[] { CopilotChatWindowGuid, CopilotChatWindowGuid2 })
            {
                var guidCopy = guid;
                int hr = uiShell.FindToolWindow(
                    (uint)__VSFINDTOOLWIN.FTW_fFindFirst,
                    ref guidCopy,
                    out IVsWindowFrame frame);

                if (hr == Microsoft.VisualStudio.VSConstants.S_OK && frame != null)
                {
                    frame.Show();
                    await Task.Delay(300); // attendi che la finestra si apra
                    return true;
                }
            }

            // Fallback: cerca "Copilot" tra tutti i tool window tramite DTE
            return await TryOpenCopilotViaDteAsync();
        }

        private async Task<bool> TryOpenCopilotViaDteAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = await _package.GetServiceAsync<EnvDTE.DTE, EnvDTE.DTE>();
            if (dte == null) return false;

            try
            {
                // Comando VS per aprire Copilot Chat (scorciatoia ufficiale)
                dte.ExecuteCommand("View.GitHubCopilotChat");
                await Task.Delay(500);
                return true;
            }
            catch
            {
                try
                {
                    dte.ExecuteCommand("GitHub.Copilot.OpenCopilotChat");
                    await Task.Delay(500);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        // ── Invio prompt via API Copilot (reflection) ─────────────────────

        private async Task<bool> TrySendViaCopilotApiAsync(
            string prompt, CancellationToken ct)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            if (!_serviceDiscovered)
                DiscoverCopilotService();

            if (_copilotService == null || _sendMessageMethod == null)
                return false;

            try
            {
                var task = _sendMessageMethod.Invoke(
                    _copilotService,
                    new object[] { prompt, ct }) as Task;

                if (task != null)
                    await task;

                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"⚠ API Copilot: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Scopre a runtime il servizio Copilot tramite reflection
        /// (Copilot non ha un SDK pubblico stabile per VS 2022).
        /// </summary>
        private void DiscoverCopilotService()
        {
            _serviceDiscovered = true;

            // Cerca negli assembly caricati
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var asm in assemblies)
            {
                var name = asm.GetName().Name ?? string.Empty;
                if (!name.Contains("Copilot", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Cerca interfacce/classi che gestiscono la chat
                foreach (var type in asm.GetExportedTypes())
                {
                    if (!type.Name.Contains("Chat", StringComparison.OrdinalIgnoreCase) &&
                        !type.Name.Contains("Conversation", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Cerca metodo SendMessage / SubmitMessage / Ask
                    var method = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m =>
                            (m.Name.Contains("Send", StringComparison.OrdinalIgnoreCase) ||
                             m.Name.Contains("Submit", StringComparison.OrdinalIgnoreCase) ||
                             m.Name.Contains("Ask", StringComparison.OrdinalIgnoreCase)) &&
                            m.GetParameters().Any(p => p.ParameterType == typeof(string)));

                    if (method == null) continue;

                    // Cerca un'istanza registrata nei servizi VS
                    try
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        var instance = ((System.IServiceProvider)_package).GetService(type);
                        if (instance != null)
                        {
                            _copilotService = instance;
                            _sendMessageMethod = method;
                            return;
                        }
                    }
                    catch { /* continua */ }
                }
            }
        }

        // ── Invio prompt via DTE Automation (fallback) ────────────────────

        private async Task<bool> TrySendViaDteAutomationAsync(
            string prompt, CancellationToken ct)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            var dte = (EnvDTE.DTE)await _package.GetServiceAsync(typeof(EnvDTE.DTE));
            if (dte == null) return false;

            try
            {
                // Cerca la finestra Copilot Chat tra i tool windows DTE
                EnvDTE.Window copilotWindow = null;
                foreach (EnvDTE.Window w in dte.Windows)
                {
                    if (w.Caption?.Contains("Copilot", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        copilotWindow = w;
                        break;
                    }
                }

                if (copilotWindow == null) return false;
                copilotWindow.Activate();
                await Task.Delay(200, ct);

                // Usa il clipboard come canale di iniezione sicuro:
                // 1. Copia il prompt negli appunti
                // 2. Porta il focus nella text box Copilot
                // 3. Incolla (Ctrl+V) e premi Invio
                System.Windows.Clipboard.SetText(prompt);
                await Task.Delay(100, ct);

                // Invia CTRL+V poi ENTER
                System.Windows.Forms.SendKeys.SendWait("^v");
                await Task.Delay(150, ct);
                System.Windows.Forms.SendKeys.SendWait("{ENTER}");

                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"⚠ DTE fallback: {ex.Message}");
                return false;
            }
        }

        // ── Attesa completamento generazione ──────────────────────────────

        /// <summary>
        /// Polling: aspetta che il documento del file attivo smetta di cambiare
        /// per <see cref="_completionStableMs"/> ms consecutivi → generazione terminata.
        /// </summary>
        private async Task WaitForCopilotCompletionAsync(CancellationToken ct)
        {
            // Timeout massimo configurabile (default 3 minuti)
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            string lastSnapshot = null;
            int stableMs = 0;
            int dotsCount = 0;

            while (!linked.Token.IsCancellationRequested)
            {
                await Task.Delay(_completionPollMs, linked.Token);

                string snapshot = await GetActiveDocumentSnapshotAsync();

                if (snapshot == lastSnapshot)
                {
                    stableMs += _completionPollMs;

                    // Aggiorna UI con punti animati
                    dotsCount = (dotsCount + 1) % 4;
                    string dots = new string('.', dotsCount + 1);
                    StatusChanged?.Invoke(this,
                        $"⏳ Copilot genera{dots} ({stableMs / 1000}s stabile)");

                    if (stableMs >= _completionStableMs)
                        break; // Testo stabile → generazione completata
                }
                else
                {
                    // Il documento sta ancora cambiando
                    stableMs = 0;
                    lastSnapshot = snapshot;
                }
            }
        }

        private async Task<string> GetActiveDocumentSnapshotAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                var dte = await _package.GetServiceAsync<EnvDTE.DTE, EnvDTE.DTE>();
                var doc = dte?.ActiveDocument;
                if (doc?.Object("TextDocument") is EnvDTE.TextDocument textDoc)
                {
                    var ep = textDoc.CreateEditPoint(textDoc.StartPoint);
                    return ep.GetText(textDoc.EndPoint);
                }
            }
            catch { /* ignora */ }
            return string.Empty;
        }

        // ── Costruzione prompt con contesto ───────────────────────────────

        /// <summary>
        /// Costruisce il prompt completo per Copilot Chat.
        /// Include SIA i tag #file:'path' (sintassi nativa Copilot) SIA il contenuto
        /// inline dei file come rinforzo. Questo garantisce che il contesto arrivi
        /// comunque, anche se Copilot non risolve i #file: da testo incollato.
        /// </summary>
        internal static string BuildFullPrompt(string userPrompt, ReferenceFileManager referenceManager)
        {
            var sb = new StringBuilder();

            sb.AppendLine("@workspace");
            sb.AppendLine();

            var files = referenceManager?.GetFiles() ?? new List<ReferenceFile>();

            if (files.Count > 0)
            {
                sb.AppendLine("// SEGUI QUESTI ESEMPI PER PATTERN E NAMING:");
                foreach (var f in files)
                {
                    sb.AppendLine($"#file:'{f.FullPath}'");
                }
                sb.AppendLine();

                // Rinforzo inline: inietta il contenuto testuale dei file
                // così il contesto arriva anche se #file: non viene risolto
                sb.AppendLine("// ── CONTENUTO FILE DI RIFERIMENTO ──");
                foreach (var f in files)
                {
                    if (string.IsNullOrEmpty(f.FullPath)) continue;
                    sb.AppendLine($"// FILE: {f.RelativePath} ({f.Language})");
                    sb.AppendLine($"```{f.Language}");
                    sb.AppendLine(f.LoadContent());
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
                sb.AppendLine("// ── FINE FILE DI RIFERIMENTO ──");
                sb.AppendLine();
            }

            sb.AppendLine(userPrompt);
            return sb.ToString();
        }

        /// <summary>
        /// Costruisce il prompt completo con il contenuto dei file inline.
        /// Usato per il clipboard ("📎 Copia prompt") e come fallback generico.
        /// Il prompt è autocontenuto: chiunque lo legga vede i file.
        /// </summary>
        internal static string BuildFullPromptWithContent(
            string userPrompt,
            ReferenceFileManager referenceManager)
        {
            var sb = new StringBuilder();

            string ctx = referenceManager?.BuildContextPayload() ?? string.Empty;
            if (!string.IsNullOrEmpty(ctx))
            {
                sb.AppendLine(ctx);
                sb.AppendLine();
            }

            sb.AppendLine(userPrompt);
            sb.AppendLine();
            sb.AppendLine("Genera solo il codice C# richiesto, " +
                          "senza spiegazioni aggiuntive.");

            return sb.ToString();
        }
    }
}
