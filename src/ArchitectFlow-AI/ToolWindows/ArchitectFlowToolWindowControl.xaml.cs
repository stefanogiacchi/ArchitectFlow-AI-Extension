using ArchitectFlow_AI.Models;
using ArchitectFlow_AI.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
namespace ArchitectFlow_AI.ToolWindows
{
    public class ArchitectFlowViewModel : INotifyPropertyChanged
    {
        private readonly ReferenceFileManager _refMgr;
        private string _promptText = string.Empty;
        private string _outputText = string.Empty;
        private string _statusText = "Pronto.";
        private bool _isGenerating;
        private bool _refPanelCollapsed;
        private readonly StringBuilder _streamBuffer = new StringBuilder();
        public ArchitectFlowViewModel(ReferenceFileManager refMgr)
        {
            _refMgr = refMgr;
            ReferenceFiles = refMgr.ObservableFiles;
            refMgr.FilesChanged += (_, args) =>
            {
                OnPropertyChanged(nameof(ReferenceCount));
                OnPropertyChanged(nameof(HasReferences));
                OnPropertyChanged(nameof(CanGenerate));
                OnPropertyChanged(nameof(CanStartLoop));
                OnPropertyChanged(nameof(GenerateButtonLabel));
                OnPropertyChanged(nameof(RefPanelMaxHeight));
            };
        }
        public ObservableCollection<ReferenceFile> ReferenceFiles { get; }
        public int ReferenceCount => _refMgr.Count;
        public bool HasReferences => _refMgr.Count > 0;
        public string PromptText
        {
            get => _promptText;
            set
            {
                _promptText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPromptEmpty));
                OnPropertyChanged(nameof(CanGenerate));
            }
        }
        public bool IsPromptEmpty => string.IsNullOrWhiteSpace(_promptText);
        public string OutputText
        {
            get => _outputText;
            set { _outputText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasOutput)); }
        }
        public bool HasOutput => !string.IsNullOrWhiteSpace(_outputText);
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }
        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                _isGenerating = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanGenerate));
                OnPropertyChanged(nameof(GenerateButtonLabel));
            }
        }
        public bool CanGenerate => !IsGenerating && !IsLoopRunning && !IsPromptEmpty;
        public string GenerateButtonLabel =>
            IsGenerating ? "Generazione…" : "Genera";
        public string BuildCopilotPrompt(string userPrompt)
        {
            var globalMgr = ArchitectFlowPackage.Instance?.ReferenceFileManager;
            return CopilotBridgeService.BuildFullPrompt(userPrompt, globalMgr);
        }
        public string BuildSelfContainedPrompt(string userPrompt)
        {
            var globalMgr = ArchitectFlowPackage.Instance?.ReferenceFileManager;
            return CopilotBridgeService.BuildFullPromptWithContent(userPrompt, globalMgr);
        }
        public bool RefPanelCollapsed
        {
            get => _refPanelCollapsed;
            set
            {
                _refPanelCollapsed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CollapseIcon));
                OnPropertyChanged(nameof(RefPanelMaxHeight));
            }
        }
        public string CollapseIcon => _refPanelCollapsed ? "▼" : "▲";
        public double RefPanelMaxHeight =>
            _refPanelCollapsed ? 0 : Math.Min(220, Math.Max(80, ReferenceCount * 38 + 10));
        public void BeginGeneration()
        {
            _streamBuffer.Clear();
            OutputText = string.Empty;
            IsGenerating = true;
            StatusText = "⏳ Generazione in corso con Claude…";
        }
        public void AppendChunk(string chunk)
        {
            _streamBuffer.Append(chunk);
            OutputText = _streamBuffer.ToString();
        }
        public void EndGeneration()
        {
            IsGenerating = false;
            int chars = _streamBuffer.Length;
            int lines = OutputText.Split('\n').Length;
            StatusText = $"✓ Completato · {chars:N0} caratteri · {lines} righe · {DateTime.Now:HH:mm:ss}";
        }
        private bool _loopRunning;
        private int _loopIteration;
        private int _loopMaxIterations = 10;
        private int _loopErrorCount;
        private string _loopPhase = string.Empty;
        public bool IsLoopRunning
        {
            get => _loopRunning;
            set { _loopRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartLoop)); OnPropertyChanged(nameof(LoopButtonLabel)); }
        }
        public int LoopIteration
        {
            get => _loopIteration;
            set { _loopIteration = value; OnPropertyChanged(); OnPropertyChanged(nameof(LoopProgressText)); }
        }
        public int LoopMaxIterations
        {
            get => _loopMaxIterations;
            set { _loopMaxIterations = value; OnPropertyChanged(); }
        }
        public int LoopErrorCount
        {
            get => _loopErrorCount;
            set { _loopErrorCount = value; OnPropertyChanged(); }
        }
        public string LoopPhase
        {
            get => _loopPhase;
            set { _loopPhase = value; OnPropertyChanged(); OnPropertyChanged(nameof(LoopPhaseIcon)); }
        }
        public string LoopPhaseIcon => _loopPhase switch
        {
            "inject" => "📝",
            "build" => "🔨",
            "done" => "✅",
            "error" => "❌",
            _ => "⏳",
        };
        public string LoopProgressText =>
            _loopRunning
                ? $"Iterazione {_loopIteration}/{_loopMaxIterations} · {_loopErrorCount} errori"
                : "Pronto";
        public bool CanStartLoop => !IsLoopRunning && !IsPromptEmpty;
        public string LoopButtonLabel => IsLoopRunning ? "Stop Loop" : "▶ Avvia Loop AI";
        public void SetError(string message)
        {
            IsGenerating = false;
            OutputText = $"❌ ERRORE\n\n{message}";
            StatusText = $"✗ Errore · {DateTime.Now:HH:mm:ss}";
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
