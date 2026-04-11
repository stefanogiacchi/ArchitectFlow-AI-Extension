using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ArchitectFlow_AI.Commands;
using ArchitectFlow_AI.Services;
using ArchitectFlow_AI.ToolWindows;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace ArchitectFlow_AI
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuids.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(ArchitectFlowToolWindow),
        Style = VsDockStyle.Tabbed,
        Window = EnvDTE.Constants.vsWindowKindSolutionExplorer,
        Orientation = ToolWindowOrientation.Right)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string,
        PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideOptionPage(typeof(ArchitectFlowOptionsPage),
        "ArchitectFlow AI", "General", 0, 0, true)]
    public sealed class ArchitectFlowPackage : AsyncPackage
    {
        public static ArchitectFlowPackage Instance { get; private set; }

        public ReferenceFileManager ReferenceFileManager { get; private set; }

        public ClaudeApiService ClaudeApiService { get; private set; }

        public CopilotBridgeService CopilotBridge { get; private set; }

        public BuildOrchestratorService BuildOrchestrator { get; private set; }

        public OutputWindowLogger OutputLogger { get; private set; }

        public AgentLoopService AgentLoop { get; private set; }

        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Instance = this;
            ReferenceFileManager = new ReferenceFileManager();
            ClaudeApiService = new ClaudeApiService(this);
            CopilotBridge = new CopilotBridgeService(this);
            BuildOrchestrator = new BuildOrchestratorService(this);
            await BuildOrchestrator.InitializeAsync();

            OutputLogger = new OutputWindowLogger(this);
            await OutputLogger.InitializeAsync();

            AgentLoop = new AgentLoopService(
                CopilotBridge, BuildOrchestrator, ReferenceFileManager, this);

            AgentLoop.LogMessage += (_, msg) => OutputLogger.Log(msg);

            await AddToReferencesCommand.InitializeAsync(this);
            await AddFolderToReferencesCommand.InitializeAsync(this);
            await ClearReferencesCommand.InitializeAsync(this);
            await OpenArchitectFlowCommand.InitializeAsync(this);

            var solution = (IVsSolution)await GetServiceAsync(typeof(SVsSolution));
            if (solution != null)
            {
                solution.AdviseSolutionEvents(new SolutionEventsHandler(ReferenceFileManager), out _);
            }
        }

        public async Task<ArchitectFlowToolWindow> GetOrCreateToolWindowAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            var window = (ArchitectFlowToolWindow)FindToolWindow(typeof(ArchitectFlowToolWindow), 0, true);

            if (window?.Content is ArchitectFlowToolWindowControl control)
            {
                control.EnsureInitialized();
            }

            return window;
        }

        public async Task ShowToolWindowAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            var window = await GetOrCreateToolWindowAsync();
            if (window?.Frame is IVsWindowFrame frame)
            {
                ErrorHandler.ThrowOnFailure(frame.Show());
            }
        }
    }

    internal static class PackageGuids
    {
        public const string PackageGuidString = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        public const string CommandSetGuidString = "b2c3d4e5-f6a7-8901-bcde-f12345678901";

        public static readonly Guid Package = new Guid(PackageGuidString);
        public static readonly Guid CommandSet = new Guid(CommandSetGuidString);
    }

    internal static class PackageCommandIds
    {
        public const int AddToReferences = 0x0100;
        public const int AddFolderToReferences = 0x0101;
        public const int ClearReferences = 0x0102;
        public const int OpenArchitectFlow = 0x0103;
        public const int RemoveReference = 0x0104;
    }

    public class ArchitectFlowOptionsPage : Microsoft.VisualStudio.Shell.DialogPage
    {
        [System.ComponentModel.Category("API")]
        [System.ComponentModel.DisplayName("Anthropic API Key")]
        [System.ComponentModel.Description("Chiave API Anthropic per Claude. Ottienila su https://console.anthropic.com")]
        public string ApiKey { get; set; } = string.Empty;

        [System.ComponentModel.Category("API")]
        [System.ComponentModel.DisplayName("Modello Claude")]
        [System.ComponentModel.Description("Modello Claude da usare per la generazione.")]
        public string Model { get; set; } = "claude-sonnet-4-20250514";

        [System.ComponentModel.Category("Copilot Loop")]
        [System.ComponentModel.DisplayName("Max Iterazioni Loop")]
        [System.ComponentModel.Description("Numero massimo di iterazioni del loop agente prima di fermarsi.")]
        public int MaxLoopIterations { get; set; } = 10;

        [System.ComponentModel.Category("Copilot Loop")]
        [System.ComponentModel.DisplayName("Delay dopo generazione (sec)")]
        [System.ComponentModel.Description("Secondi di attesa dopo che Copilot finisce di generare, prima di avviare la build.")]
        public int DelayAfterGenerationSeconds { get; set; } = 2;

        [System.ComponentModel.Category("Copilot Loop")]
        [System.ComponentModel.DisplayName("Timeout stabilità Copilot (ms)")]
        [System.ComponentModel.Description("Millisecondi di inattività del documento per considerare la generazione Copilot completata.")]
        public int CopilotStabilityTimeoutMs { get; set; } = 2000;

        [System.ComponentModel.Category("Copilot Loop")]
        [System.ComponentModel.DisplayName("Tratta warning come errori")]
        [System.ComponentModel.Description("Se true, anche i warning di compilazione vengono corretti dal loop.")]
        public bool TreatWarningsAsErrors { get; set; } = false;
        [System.ComponentModel.Description("Se true, il contenuto completo dei file viene inviato come contesto all'AI.")]
        public bool IncludeFileContent { get; set; } = true;

        [System.ComponentModel.Category("Reference Files")]
        [System.ComponentModel.DisplayName("Max file di riferimento")]
        [System.ComponentModel.Description("Numero massimo di file di riferimento accettati.")]
        public int MaxReferenceFiles { get; set; } = 30;
    }

    internal class SolutionEventsHandler : IVsSolutionEvents
    {
        private readonly ReferenceFileManager _manager;
        public SolutionEventsHandler(ReferenceFileManager manager) => _manager = manager;

        public int OnAfterCloseSolution(object pUnkReserved)
        {
            _manager.ClearAll();
            return VSConstants.S_OK;
        }

        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy) => VSConstants.S_OK;
        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded) => VSConstants.S_OK;
        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution) => VSConstants.S_OK;
        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved) => VSConstants.S_OK;
        public int OnBeforeCloseSolution(object pUnkReserved) => VSConstants.S_OK;
        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy) => VSConstants.S_OK;
        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel) => VSConstants.S_OK;
        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel) => VSConstants.S_OK;
        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel) => VSConstants.S_OK;
    }
}
