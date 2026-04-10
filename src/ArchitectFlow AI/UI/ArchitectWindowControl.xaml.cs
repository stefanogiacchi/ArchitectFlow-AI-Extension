using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ArchitectFlowAI.Models;
using ArchitectFlowAI.Services;
using Microsoft.VisualStudio.Shell;

namespace ArchitectFlowAI.UI
{
    /// <summary>
    /// Code-behind della Tool Window WPF.
    /// Gestisce gli eventi UI e orchestra i servizi.
    /// </summary>
    public partial class ArchitectWindowControl : UserControl
    {
        private readonly SolutionScanner _scanner   = new SolutionScanner();
        private readonly PromptBuilder   _builder   = new PromptBuilder();

        private GenerationContext _currentContext;

        public ArchitectWindowControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        // -----------------------------------------------------------------
        // Init
        // -----------------------------------------------------------------

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ScanSolution();
        }

        // -----------------------------------------------------------------
        // Scan Solution
        // -----------------------------------------------------------------

        private void BtnScan_Click(object sender, RoutedEventArgs e) => ScanSolution();

        private void ScanSolution()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var projects = _scanner.GetProjects();
                LvProjects.ItemsSource = projects;

                // Pre-seleziona Application e Api
                foreach (var item in LvProjects.Items)
                {
                    if (item is ProjectInfo pi &&
                        (pi.Layer == ProjectLayer.Application || pi.Layer == ProjectLayer.Api))
                        LvProjects.SelectedItems.Add(item);
                }

                SetStatus(projects.Count > 0
                    ? $"Trovati {projects.Count} progetti. Seleziona i target e premi Analyze."
                    : "Nessun progetto C# trovato. Assicurati che una Solution sia aperta.");
            }
            catch (Exception ex)
            {
                SetStatus($"Errore scan: {ex.Message}", isError: true);
            }
        }

        // -----------------------------------------------------------------
        // Analyze & Sync
        // -----------------------------------------------------------------

        private async void BtnAnalyze_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs()) return;

            BtnAnalyze.IsEnabled = false;
            BtnGenerate.IsEnabled = false;
            SetStatus("Analisi in corso... scarico i link.");

            try
            {
                var pat = PwdPat.Password;
                using (var parser = new LinkParserService(string.IsNullOrEmpty(pat) ? null : pat))
                {
                    // 1) Scarica User Story
                    SetStatus("Scarico User Story...");
                    var usRaw  = await parser.FetchRawContentAsync(TxtUserStoryUrl.Text.Trim());
                    var ac     = parser.ExtractAcceptanceCriteria(usRaw);

                    // 2) Scarica Wiki
                    SetStatus("Scarico Wiki design...");
                    var wikiRaw = await parser.FetchRawContentAsync(TxtWikiUrl.Text.Trim());
                    var sql     = parser.ExtractSql(wikiRaw);

                    if (string.IsNullOrWhiteSpace(sql))
                        throw new InvalidOperationException(
                            "Nessun blocco SQL trovato nella Wiki. Verifica il formato (```sql ... ```).");

                    // 3) Costruisci contesto
                    ThreadHelper.ThrowIfNotOnUIThread();
                    _currentContext = new GenerationContext
                    {
                        SolutionName      = _scanner.GetSolutionName(),
                        SelectedProjects  = LvProjects.SelectedItems.Cast<ProjectInfo>().ToList(),
                        AcceptanceCriteria = ac,
                        SqlQuery          = sql,
                        UserStoryUrl      = TxtUserStoryUrl.Text.Trim(),
                        WikiUrl           = TxtWikiUrl.Text.Trim()
                    };

                    // 4) Mostra anteprima prompt
                    TxtPromptPreview.Text = _builder.Build(_currentContext);
                    BtnGenerate.IsEnabled = true;

                    SetStatus($"✅ Analisi completata. AC e SQL estratti. Premi Generate Code.");
                }
            }
            catch (Exception ex)
            {
                SetStatus($"❌ Errore: {ex.Message}", isError: true);
            }
            finally
            {
                BtnAnalyze.IsEnabled = true;
            }
        }

        // -----------------------------------------------------------------
        // Generate Code
        // -----------------------------------------------------------------

        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (_currentContext == null || !_currentContext.IsValid)
            {
                SetStatus("⚠️ Esegui prima Analyze & Sync.", isError: true);
                return;
            }

            try
            {
                // Copia il prompt negli appunti: Copilot Chat lo riceve via paste
                var fullPrompt = _builder.Build(_currentContext);
                Clipboard.SetText(fullPrompt);

                // Apri Copilot Chat (command standard VS)
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider
                    .GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                dte?.ExecuteCommand("View.GitHubCopilotChat");

                SetStatus("✅ Prompt copiato negli appunti e Copilot Chat aperto. " +
                          "Incolla (Ctrl+V) nella chat e premi Invio.");
            }
            catch (Exception ex)
            {
                // Copilot Chat potrebbe non essere installato: fallback al solo clipboard
                SetStatus($"⚠️ Prompt copiato negli appunti ({ex.Message}). " +
                          "Aprire manualmente Copilot Chat e incollare.");
            }
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private bool ValidateInputs()
        {
            if (!Uri.TryCreate(TxtUserStoryUrl.Text.Trim(), UriKind.Absolute, out _))
            {
                SetStatus("⚠️ URL User Story non valido.", isError: true);
                return false;
            }
            if (!Uri.TryCreate(TxtWikiUrl.Text.Trim(), UriKind.Absolute, out _))
            {
                SetStatus("⚠️ URL Wiki non valido.", isError: true);
                return false;
            }
            if (LvProjects.SelectedItems.Count == 0)
            {
                SetStatus("⚠️ Seleziona almeno un progetto.", isError: true);
                return false;
            }
            return true;
        }

        private void SetStatus(string message, bool isError = false)
        {
            TxtStatus.Text = message;
            TxtStatus.Foreground = isError
                ? System.Windows.Media.Brushes.OrangeRed
                : (System.Windows.Media.Brush)FindResource("VsBrush.WindowText");
        }
    }
}
