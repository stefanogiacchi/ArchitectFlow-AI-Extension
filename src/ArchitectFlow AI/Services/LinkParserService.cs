using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ArchitectFlowAI.Services
{
    /// <summary>
    /// Scarica e analizza il contenuto di User Story (Jira/ADO) e Wiki (Confluence).
    /// Supporta autenticazione PAT o credenziali Windows.
    /// </summary>
    public class LinkParserService : IDisposable
    {
        private readonly HttpClient _client;

        // Pattern per estrarre blocchi SQL da Markdown/HTML
        private static readonly Regex SqlBlockMarkdown =
            new Regex(@"```sql\s*([\s\S]*?)```", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SqlBlockHtml =
            new Regex(@"<code[^>]*class=""[^""]*sql[^""]*""[^>]*>([\s\S]*?)<\/code>",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Pattern greedy per AC in formato Jira ("Given/When/Then" o elenco puntato)
        private static readonly Regex AcBlock =
            new Regex(@"(?:acceptance criteria|given|when|then|ac\d*[:.])([\s\S]{20,800}?)(?=\n\n|\Z)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public LinkParserService(string pat = null)
        {
            var handler = new HttpClientHandler { UseDefaultCredentials = string.IsNullOrEmpty(pat) };
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

            if (!string.IsNullOrEmpty(pat))
            {
                var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
                _client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", token);
            }

            _client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("text/html"));
            _client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json", 0.9));
        }

        // -----------------------------------------------------------------
        // Pubblici
        // -----------------------------------------------------------------

        /// <summary>Scarica e restituisce il testo grezzo della pagina.</summary>
        public async Task<string> FetchRawContentAsync(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                throw new ArgumentException($"URL non valido: {url}");

            // Se è un'API REST di Jira/ADO, aggiungi header JSON
            if (url.Contains("/rest/api/") || url.Contains("/_apis/"))
                _client.DefaultRequestHeaders.Accept.Clear();

            var response = await _client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>Estrae gli Acceptance Criteria dal contenuto HTML/Markdown della User Story.</summary>
        public string ExtractAcceptanceCriteria(string rawContent)
        {
            var clean = StripHtmlTags(rawContent);
            var match = AcBlock.Match(clean);
            return match.Success
                ? match.Value.Trim()
                : ExtractFallback(clean, "acceptance criteria", lines: 15);
        }

        /// <summary>Estrae il primo blocco SQL trovato nella Wiki.</summary>
        public string ExtractSql(string rawContent)
        {
            // Prova prima il blocco Markdown ```sql ... ```
            var mdMatch = SqlBlockMarkdown.Match(rawContent);
            if (mdMatch.Success) return mdMatch.Groups[1].Value.Trim();

            // Poi il tag HTML <code class="sql">
            var htmlMatch = SqlBlockHtml.Match(rawContent);
            if (htmlMatch.Success) return DecodeHtml(htmlMatch.Groups[1].Value).Trim();

            // Fallback: cerca linee che iniziano con SELECT/INSERT/UPDATE/DELETE
            return ExtractSqlFallback(rawContent);
        }

        // -----------------------------------------------------------------
        // Privati
        // -----------------------------------------------------------------

        private static string StripHtmlTags(string html)
        {
            var noScript = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
            var noStyle  = Regex.Replace(noScript, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
            var noTags   = Regex.Replace(noStyle, @"<[^>]+>", " ");
            var decoded  = DecodeHtml(noTags);
            return Regex.Replace(decoded, @"\s{2,}", "\n").Trim();
        }

        private static string DecodeHtml(string text)
            => System.Net.WebUtility.HtmlDecode(text);

        private static string ExtractFallback(string text, string keyword, int lines = 10)
        {
            var idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return text.Length > 1000 ? text.Substring(0, 1000) : text;
            var snippet = text.Substring(idx, Math.Min(1500, text.Length - idx));
            var splitLines = snippet.Split('\n');
            return string.Join("\n", splitLines, 0, Math.Min(lines, splitLines.Length));
        }

        private static string ExtractSqlFallback(string text)
        {
            var lines = text.Split('\n');
            var sb = new StringBuilder();
            bool capturing = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (Regex.IsMatch(trimmed, @"^(SELECT|INSERT|UPDATE|DELETE|WITH|MERGE)\b",
                    RegexOptions.IgnoreCase))
                    capturing = true;

                if (capturing)
                {
                    sb.AppendLine(line);
                    if (trimmed.EndsWith(";")) break;
                }
            }
            return sb.ToString().Trim();
        }

        public void Dispose() => _client?.Dispose();
    }
}
