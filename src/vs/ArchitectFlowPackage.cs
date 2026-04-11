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
}
