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
    public enum CopilotState
    {
        Idle,
        Generating,
        Done,
        Error
    }

    public class CopilotBridgeService
    {
        private static readonly Guid CopilotChatWindowGuid =
            new Guid("B1B4C3A0-97D7-4F32-B4B3-7E2D9C8F1A2B");

        private static readonly Guid CopilotChatWindowGuid2 =
            new Guid("3F6E8B9D-2A1C-4D5E-8F7A-6B3C9D2E4F1A");

        private readonly AsyncPackage _package;

        private object _copilotService;
        private MethodInfo _sendMessageMethod;
        private bool _serviceDiscovered;

        private int _completionPollMs = 400;
        private int _completionStableMs = 2000;

        public event EventHandler<string> StatusChanged;

        public CopilotBridgeService(AsyncPackage package)
        {
            _package = package;
        }

        public async Task<bool> SendPromptAndWaitAsync(
            string prompt,
            ReferenceFileManager referenceManager,
            CancellationToken ct = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            bool opened = await EnsureCopilotChatOpenAsync();
            if (!opened)
            {
                StatusChanged?.Invoke(this,
                    "⚠ Copilot Chat non trovato. Assicurati che GitHub Copilot Enterprise sia installato e attivo.");
                return false;
            }

            string fullPrompt = BuildFullPrompt(prompt, referenceManager);

            bool sent = await TrySendViaCopilotApiAsync(fullPrompt, ct);

            if (!sent)
                sent = await TrySendViaDteAutomationAsync(fullPrompt, ct);

            if (!sent)
            {
                StatusChanged?.Invoke(this, "❌ Impossibile inviare il prompt a Copilot Chat.");
                return false;
            }

            StatusChanged?.Invoke(this, "⏳ Copilot sta generando il codice…");

            await WaitForCopilotCompletionAsync(ct);

            await Task.Delay(500, ct);

            StatusChanged?.Invoke(this, "✅ Generazione Copilot completata.");
            return true;
        }

        private async Task<bool> EnsureCopilotChatOpenAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var uiShell = await _package.GetServiceAsync<SVsUIShell, IVsUIShell>(); if (uiShell == null) return false;

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
                    await Task.Delay(300);
                    return true;
                }
            }

            return await TryOpenCopilotViaDteAsync();
        }

        private async Task<bool> TryOpenCopilotViaDteAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = await _package.GetServiceAsync<EnvDTE.DTE, EnvDTE.DTE>();
            if (dte == null) return false;

            try
            {
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

        private void DiscoverCopilotService()
        {
            _serviceDiscovered = true;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var asm in assemblies)
            {
                var name = asm.GetName().Name ?? string.Empty;
                if (!name.Contains("Copilot", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var type in asm.GetExportedTypes())
                {
                    if (!type.Name.Contains("Chat", StringComparison.OrdinalIgnoreCase) &&
                        !type.Name.Contains("Conversation", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var method = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m =>
                            (m.Name.Contains("Send", StringComparison.OrdinalIgnoreCase) ||
                             m.Name.Contains("Submit", StringComparison.OrdinalIgnoreCase) ||
                             m.Name.Contains("Ask", StringComparison.OrdinalIgnoreCase)) &&
                            m.GetParameters().Any(p => p.ParameterType == typeof(string)));

                    if (method == null) continue;

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

        private async Task<bool> TrySendViaDteAutomationAsync(
            string prompt, CancellationToken ct)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            var dte = (EnvDTE.DTE)await _package.GetServiceAsync(typeof(EnvDTE.DTE));
            if (dte == null) return false;

            try
            {
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

                System.Windows.Clipboard.SetText(prompt);
                await Task.Delay(100, ct);

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

        private async Task WaitForCopilotCompletionAsync(CancellationToken ct)
        {
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

                    dotsCount = (dotsCount + 1) % 4;
                    string dots = new string('.', dotsCount + 1);
                    StatusChanged?.Invoke(this,
                        $"⏳ Copilot genera{dots} ({stableMs / 1000}s stabile)");

                    if (stableMs >= _completionStableMs)
                        break;
                }
                else
                {
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
