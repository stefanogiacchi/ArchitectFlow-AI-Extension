using System.Collections.Generic;
using System.Linq;
using System.Text;
using ArchitectFlowAI.Models;

namespace ArchitectFlowAI.Services
{
    /// <summary>
    /// Assembla il prompt strutturato da inviare a Copilot/Claude
    /// partendo dal GenerationContext.
    /// </summary>
    public class PromptBuilder
    {
        // -----------------------------------------------------------------
        // Regole infrastrutturali fisse (dal TDD §5)
        // -----------------------------------------------------------------
        private const string InfraRules = @"
[INFRA RULES — MANDATORY — DO NOT DEVIATE]
- GET operations: use DapperMapperClass (Fabric pattern) for DB connections.
  Generate DTOs mapped 1:1 on the SQL query columns.
- CUD operations (Create/Update/Delete): inject IUnitOfWork in the Handler.
  Always call Commit() at the end of the Handle method.
- Validation: create FluentValidation classes with one rule per AC point.
  Validator class name must match the Command/Query name + 'Validator'.
- Error handling: NO try-catch blocks inside handlers or controllers.
  Rely entirely on ExceptionMiddleware for unhandled exceptions.
- Inheritance: all controllers MUST inherit from ApiBase.
- Namespaces: use the exact namespaces listed in [PROJECT CONTEXT].
";

        // -----------------------------------------------------------------
        // Pubblici
        // -----------------------------------------------------------------

        /// <summary>
        /// Genera il prompt completo pronto per Copilot.
        /// </summary>
        public string Build(GenerationContext ctx)
        {
            var sb = new StringBuilder();

            AppendSection(sb, "CONTEXT", BuildContextSection(ctx));
            AppendSection(sb, "INPUT",   BuildInputSection(ctx));
            AppendSection(sb, "TASK",    BuildTaskSection(ctx));
            sb.AppendLine(InfraRules);

            return sb.ToString();
        }

        /// <summary>
        /// Restituisce solo la sezione [TASK] per mostrarla in anteprima nella UI.
        /// </summary>
        public string BuildTaskPreview(GenerationContext ctx) => BuildTaskSection(ctx);

        // -----------------------------------------------------------------
        // Privati
        // -----------------------------------------------------------------

        private string BuildContextSection(GenerationContext ctx)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Solution: {ctx.SolutionName}");
            sb.AppendLine($"Projects selected: {string.Join(", ", ctx.SelectedProjects.Select(p => p.Name))}");
            sb.AppendLine();

            foreach (var proj in ctx.SelectedProjects)
            {
                sb.AppendLine($"[{proj.Layer}] {proj.Name}");
                sb.AppendLine($"  Namespace : {proj.RootNamespace}");
                sb.AppendLine($"  Path      : {proj.PhysicalPath}");

                if (proj.DetectedBaseClasses.Any())
                    sb.AppendLine($"  BaseClasses: {string.Join(", ", proj.DetectedBaseClasses)}");

                sb.AppendLine();
            }
            return sb.ToString();
        }

        private string BuildInputSection(GenerationContext ctx)
        {
            var sb = new StringBuilder();
            sb.AppendLine("--- Acceptance Criteria ---");
            sb.AppendLine(ctx.AcceptanceCriteria);
            sb.AppendLine();
            sb.AppendLine("--- SQL Query (from Wiki) ---");
            sb.AppendLine(ctx.SqlQuery);
            return sb.ToString();
        }

        private string BuildTaskSection(GenerationContext ctx)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Generate the following files:");
            sb.AppendLine();

            var files = InferFilesToGenerate(ctx);
            for (int i = 0; i < files.Count; i++)
            {
                var (fileName, projectLayer, description) = files[i];
                var proj = ctx.SelectedProjects.FirstOrDefault(p => p.Layer == projectLayer);
                var ns = proj?.RootNamespace ?? projectLayer.ToString();
                sb.AppendLine($"{i + 1}. {fileName}");
                sb.AppendLine($"   Namespace : {ns}");
                sb.AppendLine($"   Layer     : {projectLayer}");
                sb.AppendLine($"   Notes     : {description}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private List<(string FileName, ProjectLayer Layer, string Description)> InferFilesToGenerate(GenerationContext ctx)
        {
            // Deriva il nome entity dalla prima parola dell'AC o dal titolo della US
            var entityName = InferEntityName(ctx);
            var isReadOnly = !ctx.AcceptanceCriteria.ToLower().Contains("crea")
                          && !ctx.AcceptanceCriteria.ToLower().Contains("aggiorna")
                          && !ctx.AcceptanceCriteria.ToLower().Contains("elimina")
                          && !ctx.AcceptanceCriteria.ToLower().Contains("insert")
                          && !ctx.AcceptanceCriteria.ToLower().Contains("update")
                          && !ctx.AcceptanceCriteria.ToLower().Contains("delete");

            var files = new List<(string, ProjectLayer, string)>();

            if (isReadOnly)
            {
                files.Add(($"Get{entityName}Query.cs",   ProjectLayer.Application,
                    "CQRS Query record with parameters derived from SQL WHERE clause."));
                files.Add(($"Get{entityName}Handler.cs", ProjectLayer.Application,
                    "IRequestHandler using DapperMapperClass. Returns List<Get{entityName}Dto>."));
                files.Add(($"Get{entityName}Dto.cs",     ProjectLayer.Application,
                    "DTO with properties mapped 1:1 on SQL SELECT columns."));
            }
            else
            {
                files.Add(($"{entityName}Command.cs",    ProjectLayer.Application,
                    "CQRS Command record with properties from AC."));
                files.Add(($"{entityName}Handler.cs",    ProjectLayer.Application,
                    "IRequestHandler injecting IUnitOfWork. Calls Commit() at the end."));
                files.Add(($"{entityName}Validator.cs",  ProjectLayer.Application,
                    "FluentValidation AbstractValidator with one rule per AC point."));
            }

            files.Add(($"{entityName}Controller.cs", ProjectLayer.Api,
                "Controller inheriting ApiBase. Maps HTTP verb to MediatR.Send()."));

            return files;
        }

        private string InferEntityName(GenerationContext ctx)
        {
            if (!string.IsNullOrEmpty(ctx.UserStoryTitle))
            {
                var words = ctx.UserStoryTitle.Split(' ', '-', '_');
                foreach (var w in words)
                {
                    var clean = new string(w.Where(char.IsLetter).ToArray());
                    if (clean.Length > 3) return Capitalize(clean);
                }
            }

            // Fallback: estrai il nome della tabella SQL (FROM <table>)
            var sqlMatch = System.Text.RegularExpressions.Regex.Match(
                ctx.SqlQuery ?? "", @"\bFROM\s+(\w+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return sqlMatch.Success ? Capitalize(sqlMatch.Groups[1].Value) : "Entity";
        }

        private static string Capitalize(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1).ToLower();

        private static void AppendSection(StringBuilder sb, string title, string content)
        {
            sb.AppendLine($"[{title}]");
            sb.AppendLine(content);
            sb.AppendLine();
        }
    }
}
