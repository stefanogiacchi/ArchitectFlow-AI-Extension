using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ArchitectFlow_AI.Models;
using ArchitectFlow_AI.Services;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ArchitectFlow_AI.ToolWindows
{
    // ══════════════════════════════════════════════════════════════════════
    //  CONVERTERS
    // ══════════════════════════════════════════════════════════════════════

    public class LanguageToIconConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            return (value as string) switch
            {
                "csharp"      => "🔷",
                "typescript"  => "🟦",
                "javascript"  => "🟡",
                "python"      => "🐍",
                "java"        => "☕",
                "go"          => "🐹",
                "rust"        => "🦀",
                "cpp"         => "⚙",
                "sql"         => "🗃",
                "json"        => "{ }",
                "yaml"        => "📋",
                "xml"         => "📄",
                "razor"       => "🪥",
                "markdown"    => "📝",
                "html"        => "🌐",
                "css"         => "🎨",
                "powershell"  => "💙",
                _             => "📄",
            };
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => Binding.DoNothing;
    }

    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => (value is int n && n > 0) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => Binding.DoNothing;
    }

    public class InverseCountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => (value is int n && n == 0) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => Binding.DoNothing;
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool b = value is bool bv && bv;
            bool inverse = p?.ToString() == "inverse";
            return (b ^ inverse) ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => Binding.DoNothing;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  VIEWMODEL
    // ══════════════════════════════════════════════════════════════════════

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

            // Aggiorna le proprietà derivate quando cambia la lista reference
            refMgr.FilesChanged += (_, args) =>
            {
                OnPropertyChanged(nameof(ReferenceCount));
                OnPropertyChanged(nameof(HasReferences));
                OnPropertyChanged(nameof(CanGenerate));
                OnPropertyChanged(nameof(GenerateButtonLabel));
            };
        }

        // ── Reference files ──────────────────────────────────────────────
        public ObservableCollection<ReferenceFile> ReferenceFiles { get; }
        public int  ReferenceCount => _refMgr.Count;
        public bool HasReferences  => _refMgr.Count > 0;

        // ── Prompt ───────────────────────────────────────────────────────
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

        // ── Output ───────────────────────────────────────────────────────
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

        // ── Stato generazione ────────────────────────────────────────────
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

        public bool CanGenerate => !IsGenerating && !IsPromptEmpty;

        public string GenerateButtonLabel =>
            IsGenerating ? "Generazione…" : "Genera";

        // ── UI stato pannello ────────────────────────────────────────────
        public bool RefPanelCollapsed
        {
            get => _refPanelCollapsed;
            set { _refPanelCollapsed = value; OnPropertyChanged(); OnPropertyChanged(nameof(CollapseIcon)); OnPropertyChanged(nameof(RefPanelHeight)); }
        }

        public string CollapseIcon => _refPanelCollapsed ? "▼" : "▲";

        public GridLength RefPanelHeight =>
            _refPanelCollapsed
                ? new GridLength(0)
                : new GridLength(Math.Min(220, Math.Max(80, ReferenceCount * 38 + 10)));

        // ── Streaming output ─────────────────────────────────────────────

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

        public void SetError(string message)
        {
            IsGenerating = false;
            OutputText = $"❌ ERRORE\n\n{message}";
            StatusText = $"✗ Errore · {DateTime.Now:HH:mm:ss}";
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CODE-BEHIND
    // ══════════════════════════════════════════════════════════════════════

    public partial class ArchitectFlowToolWindowControl : UserControl
    {
        private ArchitectFlowViewModel _vm;
        private CancellationTokenSource _cts;

        public ArchitectFlowToolWindowControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Aspetta che il package sia inizializzato
            if (ArchitectFlowPackage.Instance == null) return;

            _vm = new ArchitectFlowViewModel(ArchitectFlowPackage.Instance.ReferenceFileManager);
            DataContext = _vm;

            // Aggancia i callback di streaming dell'API service
            var api = ArchitectFlowPackage.Instance.ClaudeApiService;
            api.ChunkReceived      += OnChunkReceived;
            api.ErrorOccurred      += OnApiError;
            api.GenerationCompleted += OnGenerationCompleted;
        }

        // ── Streaming callbacks ──────────────────────────────────────────

        private void OnChunkReceived(object sender, string chunk)
        {
            Dispatcher.Invoke(() =>
            {
                _vm.AppendChunk(chunk);
                // Auto-scroll verso il basso
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

        // ── Generazione ──────────────────────────────────────────────────

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
            if (!_vm.CanGenerate) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            _vm.BeginGeneration();

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

        // ── Output actions ────────────────────────────────────────────────

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
                // "panel" → già mostrato, niente da fare
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

                // Apri il file in VS
                var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                dte?.ItemOperations.OpenFile(dlg.FileName);
            }
        }

        // ── Reference file actions ────────────────────────────────────────

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

            if (result == 6) // IDYES
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

        // ── Drag & drop dalla Solution Explorer ──────────────────────────

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

        // ── UI helpers ────────────────────────────────────────────────────

        private void OnClearPromptClick(object sender, RoutedEventArgs e)
        {
            _vm.PromptText = string.Empty;
            PromptBox.Focus();
        }

        private void OnToggleRefPanelClick(object sender, RoutedEventArgs e)
        {
            _vm.RefPanelCollapsed = !_vm.RefPanelCollapsed;
        }

        private void OnModeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm == null) return;
            var mode = (ModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            // Aggiorna placeholder in base alla modalità
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
    }
}
