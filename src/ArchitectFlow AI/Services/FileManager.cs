using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ArchitectFlowAI.Models;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace ArchitectFlowAI.Services
{
    /// <summary>
    /// Crea fisicamente i file .cs generati da Copilot
    /// nelle cartelle corrette dei progetti selezionati.
    /// </summary>
    public class FileManager
    {
        private readonly DTE _dte;

        public FileManager()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _dte = (DTE)ServiceProvider.GlobalProvider.GetService(typeof(DTE));
        }

        // -----------------------------------------------------------------
        // Pubblici
        // -----------------------------------------------------------------

        /// <summary>
        /// Scrive il contenuto nel file corretto e lo aggiunge al progetto VS.
        /// </summary>
        /// <param name="fileName">Es. "GetOrderQuery.cs"</param>
        /// <param name="content">Codice C# generato</param>
        /// <param name="targetProject">Progetto di destinazione</param>
        /// <param name="subfolder">Sottocartella opzionale (es. "Features/Orders")</param>
        public string WriteFile(string fileName, string content,
            ProjectInfo targetProject, string subfolder = null)
        {
            if (targetProject == null)
                throw new ArgumentNullException(nameof(targetProject));

            var folder = string.IsNullOrEmpty(subfolder)
                ? targetProject.PhysicalPath
                : Path.Combine(targetProject.PhysicalPath, subfolder);

            Directory.CreateDirectory(folder);
            var fullPath = Path.Combine(folder, fileName);
            File.WriteAllText(fullPath, content, System.Text.Encoding.UTF8);

            // Aggiunge il file al progetto Visual Studio (visibile in Solution Explorer)
            AddFileToProject(fullPath, targetProject);

            return fullPath;
        }

        /// <summary>
        /// Distribuisce una lista di (fileName, content) ai progetti giusti
        /// basandosi sul layer estratto dal nome file.
        /// </summary>
        public List<string> DistributeGeneratedFiles(
            Dictionary<string, string> generatedFiles,
            GenerationContext ctx,
            string subfolder = null)
        {
            var written = new List<string>();
            foreach (var kv in generatedFiles)
            {
                var targetProject = ResolveProject(kv.Key, ctx);
                if (targetProject == null)
                {
                    // Fallback: primo progetto Application disponibile
                    targetProject = ctx.SelectedProjects.Find(p => p.Layer == ProjectLayer.Application)
                                 ?? ctx.SelectedProjects[0];
                }
                var path = WriteFile(kv.Key, kv.Value, targetProject, subfolder);
                written.Add(path);
            }
            return written;
        }

        // -----------------------------------------------------------------
        // Privati
        // -----------------------------------------------------------------

        private ProjectInfo ResolveProject(string fileName, GenerationContext ctx)
        {
            // Controller -> Api layer
            if (Regex.IsMatch(fileName, @"Controller\.cs$", RegexOptions.IgnoreCase))
                return ctx.SelectedProjects.Find(p => p.Layer == ProjectLayer.Api);

            // Handler, Query, Command, Validator, Dto -> Application
            if (Regex.IsMatch(fileName, @"(Handler|Query|Command|Validator|Dto)\.cs$",
                RegexOptions.IgnoreCase))
                return ctx.SelectedProjects.Find(p => p.Layer == ProjectLayer.Application);

            // Entity, Repository -> Domain
            if (Regex.IsMatch(fileName, @"(Entity|Repository|Aggregate)\.cs$",
                RegexOptions.IgnoreCase))
                return ctx.SelectedProjects.Find(p => p.Layer == ProjectLayer.Domain);

            return null;
        }

        private void AddFileToProject(string fullPath, ProjectInfo targetProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                // Cerca il progetto VS corrispondente tramite nome
                foreach (Project proj in _dte.Solution.Projects)
                {
                    if (proj.Name == targetProject.Name)
                    {
                        proj.ProjectItems.AddFromFile(fullPath);
                        return;
                    }
                }
            }
            catch
            {
                // Non bloccare il flusso se l'aggiunta al progetto fallisce
                // Il file è già stato scritto su disco
            }
        }
    }
}
