using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArchitectFlow_AI;
using Microsoft.VisualStudio.Shell;

namespace ArchitectFlow_AI.Services
{
    /// <summary>
    /// Servizio che chiama l'API Claude (Anthropic) con streaming SSE.
    /// Inietta il contesto vincolante dei file di riferimento nel system prompt.
    /// </summary>
    public class ClaudeApiService
    {
        private readonly AsyncPackage _package;
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        public ClaudeApiService(AsyncPackage package)
        {
            _package = package;
        }

        // ── Evento per lo streaming del testo ────────────────────────────────
        public event EventHandler<string> ChunkReceived;
        public event EventHandler<string> ErrorOccurred;
        public event EventHandler GenerationCompleted;

        // ── Generazione principale ────────────────────────────────────────────

        /// <summary>
        /// Invia il prompt all'API Claude con i reference file come contesto vincolante.
        /// Lo streaming avviene via evento <see cref="ChunkReceived"/>.
        /// </summary>
        public async Task GenerateAsync(
            string userPrompt,
            string generationMode,
            CancellationToken cancellationToken = default)
        {
            var options = _package.GetDialogPage(typeof(ArchitectFlowOptionsPage))
                as ArchitectFlowOptionsPage;

            var apiKey = options?.ApiKey ?? string.Empty;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ErrorOccurred?.Invoke(this,
                    "Chiave API non configurata. Vai su Strumenti → Opzioni → ArchitectFlow AI.");
                return;
            }

            var model = options?.Model ?? "claude-sonnet-4-20250514";

            // Costruisce il system prompt con il contesto dei file di riferimento
            var referenceContext = ArchitectFlowPackage.Instance.ReferenceFileManager
                .BuildContextPayload();
            var systemPrompt = BuildSystemPrompt(generationMode, referenceContext);

            var requestBody = new
            {
                model,
                max_tokens = 8192,
                stream = true,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = userPrompt }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://api.anthropic.com/v1/messages");

            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                using var response = await _http.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errBody = await response.Content.ReadAsStringAsync();
                    ErrorOccurred?.Invoke(this,
                        $"Errore API ({response.StatusCode}): {errBody}");
                    return;
                }

                await ReadStreamAsync(response, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Generazione annullata dall'utente — non un errore
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Errore di rete: {ex.Message}");
            }
        }

        // ── SSE streaming parser ──────────────────────────────────────────────

        private async Task ReadStreamAsync(HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;
                if (!line.StartsWith("data: ")) continue;

                var data = line.Substring(6).Trim();
                if (data == "[DONE]") break;

                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("type", out var typeEl))
                    {
                        var type = typeEl.GetString();
                        if (type == "content_block_delta")
                        {
                            if (root.TryGetProperty("delta", out var delta) &&
                                delta.TryGetProperty("text", out var textEl))
                            {
                                var chunk = textEl.GetString() ?? string.Empty;
                                if (!string.IsNullOrEmpty(chunk))
                                    ChunkReceived?.Invoke(this, chunk);
                            }
                        }
                        else if (type == "message_stop")
                        {
                            break;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Chunk malformato — ignoralo
                }
            }

            GenerationCompleted?.Invoke(this, EventArgs.Empty);
        }

        // ── System prompt builder ─────────────────────────────────────────────

        private static string BuildSystemPrompt(string mode, string referenceContext)
        {
            var modeInstruction = mode switch
            {
                "cqrs_command"  => BuildCqrsCommandInstruction(),
                "cqrs_query"    => BuildCqrsQueryInstruction(),
                "api_endpoint"  => BuildApiEndpointInstruction(),
                "repository"    => BuildRepositoryInstruction(),
                "dapper_query"  => BuildDapperQueryInstruction(),
                "handler"       => BuildHandlerInstruction(),
                "freeform"      => "Sei un esperto sviluppatore C#. Rispondi con precisione e codice pronto per la produzione.",
                _               => "Sei un esperto sviluppatore C# specializzato in CQRS, Mediator e Dapper."
            };

            var sb = new StringBuilder();
            sb.AppendLine(modeInstruction);
            sb.AppendLine();
            sb.AppendLine("REGOLE DI STILE:");
            sb.AppendLine("- Segui esattamente i pattern mostrati nei file di riferimento");
            sb.AppendLine("- Non inventare namespace o pattern non presenti nel codice esistente");
            sb.AppendLine("- Usa gli stessi attributi, interfacce e decoratori già in uso");
            sb.AppendLine("- Il codice generato deve compilare senza modifiche aggiuntive");
            sb.AppendLine("- Aggiungi XML doc comments coerenti con quelli esistenti");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(referenceContext))
            {
                sb.AppendLine(referenceContext);
            }
            else
            {
                sb.AppendLine("⚠ Nessun file di riferimento selezionato. Il codice sarà generato in base");
                sb.AppendLine("  ai pattern CQRS/Dapper/Mediator standard senza contesto specifico.");
            }

            return sb.ToString();
        }

        private static string BuildCqrsCommandInstruction() =>
            """
            Sei un esperto di pattern CQRS con MediatR e FluentValidation.
            Genera un Command completo con: record Command, IRequestHandler, Validator.
            Il Command deve seguire il pattern Request/Response con MediatR.
            Includi: using, namespace, validazioni, gestione errori.
            """;

        private static string BuildCqrsQueryInstruction() =>
            """
            Sei un esperto di pattern CQRS con MediatR e Dapper.
            Genera una Query completa con: record Query, DTO di risposta, IRequestHandler.
            L'Handler deve usare Dapper per le query SQL con IDbConnection.
            Includi: using, namespace, SQL parametrizzato, mapping DTO.
            """;

        private static string BuildApiEndpointInstruction() =>
            """
            Sei un esperto di ASP.NET Core Minimal API e Controller-based API.
            Genera un endpoint API completo: route, validazione, dispatch via MediatR,
            gestione errori HTTP (400, 404, 500), documentazione Swagger/OpenAPI.
            Includi: using, namespace, attributi, response types.
            """;

        private static string BuildRepositoryInstruction() =>
            """
            Sei un esperto di Repository Pattern con Dapper.
            Genera un repository con interfaccia e implementazione.
            Usa Dapper con IDbConnection, query SQL parametrizzate, async/await.
            Includi: CRUD, transazioni dove necessario, mapping entità.
            """;

        private static string BuildDapperQueryInstruction() =>
            """
            Sei un esperto SQL e Dapper per .NET.
            Genera query SQL ottimizzate con la mappatura Dapper corrispondente.
            Include: stored procedure o query inline, parametri, gestione NULL, indici suggeriti.
            """;

        private static string BuildHandlerInstruction() =>
            """
            Sei un esperto MediatR IRequestHandler.
            Genera un Handler completo con: dependency injection, logica di business,
            gestione eccezioni dominio, logging strutturato, Unit of Work se necessario.
            """;
    }
}
