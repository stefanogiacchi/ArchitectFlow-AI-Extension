namespace ArchitectFlow_AI
{
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
}
