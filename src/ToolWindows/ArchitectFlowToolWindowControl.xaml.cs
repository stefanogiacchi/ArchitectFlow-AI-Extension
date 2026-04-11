using ArchitectFlow_AI.Models;
using ArchitectFlow_AI.Services;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
    public partial class ArchitectFlowToolWindowControl : UserControl
    {
        private ArchitectFlowViewModel _vm;
        private CancellationTokenSource _cts;
        private bool _apiEventsHooked;
        public ArchitectFlowToolWindowControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RegisterVsThemeResources();
            EnsureInitialized();
        }
        public void EnsureInitialized()
        {
            if (_vm != null) return; 
            if (ArchitectFlowPackage.Instance?.ReferenceFileManager == null) return;
            _vm = new ArchitectFlowViewModel(ArchitectFlowPackage.Instance.ReferenceFileManager);
            DataContext = _vm;
            if (!_apiEventsHooked && ArchitectFlowPackage.Instance.ClaudeApiService != null)
            {
                var api = ArchitectFlowPackage.Instance.ClaudeApiService;
                api.ChunkReceived += OnChunkReceived;
                api.ErrorOccurred += OnApiError;
                api.GenerationCompleted += OnGenerationCompleted;
                _apiEventsHooked = true;
            }
        }
        private void RegisterVsThemeResources()
        {
            try
            {
                var bgColor = EnvironmentColors.ToolWindowBackgroundColorKey;
                var fgColor = EnvironmentColors.ToolWindowTextColorKey;
                var bg = VSColorTheme.GetThemedColor(bgColor);
                var fg = VSColorTheme.GetThemedColor(fgColor);
                Resources["ToolWindowBackground"] =
                    new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B));
                Resources["ToolWindowForeground"] =
                    new SolidColorBrush(Color.FromArgb(fg.A, fg.R, fg.G, fg.B));
                VSColorTheme.ThemeChanged += _ => Dispatcher.Invoke(RegisterVsThemeResources);
            }
            catch
            {
                Resources["ToolWindowBackground"] = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                Resources["ToolWindowForeground"] = new SolidColorBrush(Colors.WhiteSmoke);
            }
        }
        private void OnChunkReceived(object sender, string chunk)
        {
            Dispatcher.Invoke(() =>
            {
                _vm.AppendChunk(chunk);
                OutputScroll.ScrollToBottom();
            });
        }
        private void OnApiError(object sender, string error)
        {
            Dispatcher.Invoke(() => _vm.SetError(error));
        }
        private void OnGenerationCompleted(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _vm.EndGeneration();
                HandleOutputTarget();
            });
        }
        private async void OnGenerateClick(object sender, RoutedEventArgs e)
        {
            await StartGenerationAsync();
        }
        private async void OnPromptKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                e.Handled = true;
                await StartGenerationAsync();
            }
        }
        private async Task StartGenerationAsync()
        {
            if (_vm == null || !_vm.CanGenerate) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            AutoAddFromSolutionExplorerIfEmpty();
            int refCount = ArchitectFlowPackage.Instance?.ReferenceFileManager?.Count ?? 0;
            _vm.BeginGeneration();
            _vm.StatusText = refCount > 0
                ? $"⏳ Invio a Claude: {refCount} file di riferimento + prompt…"
                : "⏳ Invio a Claude (nessun file di riferimento)…";
            var mode = (ModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "freeform";
            await ArchitectFlowPackage.Instance.ClaudeApiService
                .GenerateAsync(_vm.PromptText, mode, _cts.Token);
        }
        private void OnStopClick(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _vm.IsGenerating = false;
            _vm.StatusText = "⏹ Generazione interrotta.";
        }
        private void HandleOutputTarget()
        {
            var target = (OutputCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "panel";
            switch (target)
            {
                case "clipboard":
                CopyToClipboard();
                break;
                case "active_editor":
                _ = InsertToActiveEditorAsync();
                break;
                case "new_file":
                _ = SaveAsNewFileAsync();
                break;
            }
        }
        private void OnCopyOutputClick(object sender, RoutedEventArgs e) => CopyToClipboard();
        private void CopyToClipboard()
        {
            if (!string.IsNullOrWhiteSpace(_vm.OutputText))
            {
                Clipboard.SetText(_vm.OutputText);
                _vm.StatusText = "📋 Copiato negli appunti!";
            }
        }
        private void OnInsertToEditorClick(object sender, RoutedEventArgs e)
        {
            _ = InsertToActiveEditorAsync();
        }
        private async Task InsertToActiveEditorAsync()
        {
            if (string.IsNullOrWhiteSpace(_vm.OutputText)) return;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            var doc = dte?.ActiveDocument;
            if (doc?.Object("TextDocument") is EnvDTE.TextDocument textDoc)
            {
                textDoc.Selection.Insert(_vm.OutputText);
                _vm.StatusText = "⬆ Inserito nell'editor attivo.";
            }
            else
            {
                _vm.StatusText = "⚠ Nessun editor di testo attivo.";
            }
        }
        private void OnSaveAsFileClick(object sender, RoutedEventArgs e)
        {
            _ = SaveAsNewFileAsync();
        }
        private async Task SaveAsNewFileAsync()
        {
            if (string.IsNullOrWhiteSpace(_vm.OutputText)) return;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Salva output ArchitectFlow",
                Filter = "C# File (*.cs)|*.cs|SQL File (*.sql)|*.sql|Tutti i file (*.*)|*.*",
                FileName = "Generated.cs",
            };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, _vm.OutputText, Encoding.UTF8);
                _vm.StatusText = $"💾 Salvato in {Path.GetFileName(dlg.FileName)}";
                var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                dte?.ItemOperations.OpenFile(dlg.FileName);
            }
        }
        private void OnRemoveReferenceClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                ArchitectFlowPackage.Instance.ReferenceFileManager.Remove(path);
            }
        }
        private void OnClearAllClick(object sender, RoutedEventArgs e)
        {
            var result = VsShellUtilities.ShowMessageBox(
                ArchitectFlowPackage.Instance,
                $"Rimuovere tutti i {_vm.ReferenceCount} file di riferimento?",
                "ArchitectFlow AI",
                OLEMSGICON.OLEMSGICON_QUERY,
                OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
            if (result == 6) 
                ArchitectFlowPackage.Instance.ReferenceFileManager.ClearAll();
        }
        private async void OnBrowseFilesClick(object sender, RoutedEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Seleziona file di riferimento ArchitectFlow",
                Multiselect = true,
                Filter = "File sorgente|*.cs;*.vb;*.ts;*.tsx;*.js;*.jsx;*.py;*.java;*.go;*.rs;*.sql;*.xml;*.json;*.yaml;*.yml;*.md;*.razor;*.cshtml|Tutti i file (*.*)|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                int added = 0;
                foreach (var file in dlg.FileNames)
                {
                    if (ArchitectFlowPackage.Instance.ReferenceFileManager.TryAdd(file))
                        added++;
                }
                if (added > 0)
                    _vm.StatusText = $"✓ {added} file aggiunti come riferimento.";
            }
        }
        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }
        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            int added = 0;
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    if (ArchitectFlowPackage.Instance.ReferenceFileManager.TryAdd(path))
                        added++;
                }
                else if (Directory.Exists(path))
                {
                    added += ArchitectFlowPackage.Instance.ReferenceFileManager.AddFolder(path);
                }
            }
            if (added > 0)
                _vm.StatusText = $"✓ {added} file aggiunti per trascinamento.";
        }
        private void OnClearPromptClick(object sender, RoutedEventArgs e)
        {
            _vm.PromptText = string.Empty;
            PromptBox.Focus();
        }
        private void OnCopyPromptClick(object sender, RoutedEventArgs e)
        {
            AutoAddFromSolutionExplorerIfEmpty();
            var manager = ArchitectFlowPackage.Instance?.ReferenceFileManager;
            int fileCount = manager?.Count ?? 0;
            string fullPrompt = CopilotBridgeService.BuildFullPromptWithContent(
                _vm.PromptText, manager);
            Clipboard.SetText(fullPrompt);
            if (fileCount > 0)
            {
                _vm.StatusText = $"📋 Copiato: {fileCount} file + prompt ({fullPrompt.Length:N0} caratteri)";
            }
            else
            {
                _vm.StatusText = "📋 Copiato (nessun file di riferimento selezionato).";
            }
        }
        private void OnToggleRefPanelClick(object sender, RoutedEventArgs e)
        {
            _vm.RefPanelCollapsed = !_vm.RefPanelCollapsed;
        }
        private void OnModeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm == null) return;
            var mode = (ModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            _vm.StatusText = $"Modalità: {mode}";
        }
        private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ArchitectFlowPackage.Instance?.ShowOptionPage(typeof(ArchitectFlowOptionsPage));
            });
        }
        private void AutoAddFromSolutionExplorerIfEmpty()
        {
            var mgr = ArchitectFlowPackage.Instance?.ReferenceFileManager;
            if (mgr == null || mgr.Count > 0) return;
            var files = SolutionExplorerHelper.GetSelectedFiles();
            if (files.Count == 0) return;
            int added = mgr.AddRange(files);
            if (added > 0 && _vm != null)
                _vm.StatusText = $"📎 Auto-aggiunti {added} file dalla Solution Explorer.";
        }
        private void OnStartLoopClick(object sender, RoutedEventArgs e)
        {
            if (_vm.IsLoopRunning)
            {
                ArchitectFlowPackage.Instance?.AgentLoop?.Stop();
                _vm.IsLoopRunning = false;
                return;
            }
            if (string.IsNullOrWhiteSpace(_vm.PromptText)) return;
            AutoAddFromSolutionExplorerIfEmpty();
            var loop = ArchitectFlowPackage.Instance?.AgentLoop;
            if (loop == null)
            {
                _vm.StatusText = "❌ AgentLoopService non disponibile.";
                return;
            }
            _vm.IsLoopRunning = true;
            _vm.LoopIteration = 0;
            _vm.LoopErrorCount = 0;
            _vm.LoopPhase = "inject";
            _vm.OutputText = string.Empty;
            var settings = ArchitectFlowPackage.Instance
                ?.GetDialogPage(typeof(ArchitectFlowOptionsPage)) as ArchitectFlowOptionsPage;
            var options = new AgentLoopOptions
            {
                MaxIterations = settings?.MaxLoopIterations ?? _vm.LoopMaxIterations,
                DelayAfterGenerationSeconds = settings?.DelayAfterGenerationSeconds ?? 2,
                TreatWarningsAsErrors = settings?.TreatWarningsAsErrors ?? false,
            };
            options.MaxIterations = _vm.LoopMaxIterations;
            loop.IterationChanged -= OnLoopIterationChanged;
            loop.LogMessage -= OnLoopLogMessage;
            loop.IterationChanged += OnLoopIterationChanged;
            loop.LogMessage += OnLoopLogMessage;
            _ = loop.StartAsync(_vm.PromptText, options)
                .ContinueWith(_ => Dispatcher.Invoke(() =>
                {
                    _vm.IsLoopRunning = false;
                    _vm.StatusText = _vm.LoopPhase == "done"
                        ? "✅ Loop completato con successo!"
                        : "⏹ Loop terminato.";
                }));
        }
        private void OnLoopIterationChanged(object sender, LoopIterationState state)
        {
            Dispatcher.Invoke(() =>
            {
                _vm.LoopIteration = state.Iteration;
                _vm.LoopErrorCount = state.ErrorCount;
                _vm.LoopPhase = state.Phase;
                _vm.StatusText = state.StatusMessage;
                _vm.IsLoopRunning = state.Phase != "done" && state.Phase != "error";
            });
        }
        private void OnLoopLogMessage(object sender, string message)
        {
            Dispatcher.Invoke(() =>
            {
                _vm.AppendChunk(message + "\n");
                OutputScroll.ScrollToBottom();
            });
        }
    }
}
