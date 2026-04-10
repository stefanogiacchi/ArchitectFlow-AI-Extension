using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ArchitectFlowAI.Models;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace ArchitectFlowAI.Services
{
    /// <summary>
    /// Scansiona la Solution VS attiva tramite EnvDTE e restituisce
    /// la lista dei progetti con namespace, path e layer inferito.
    /// </summary>
    public class SolutionScanner
    {
        private static readonly string[] BaseClassMarkers =
            { "ApiBase", "ExceptionMiddleware", "DapperMapperClass", "IUnitOfWork" };

        private static readonly Dictionary<string, ProjectLayer> LayerKeywords = new Dictionary<string, ProjectLayer>
        {
            { "domain",         ProjectLayer.Domain         },
            { "application",    ProjectLayer.Application    },
            { "infrastructure", ProjectLayer.Infrastructure },
            { "infra",          ProjectLayer.Infrastructure },
            { "api",            ProjectLayer.Api            },
            { "web",            ProjectLayer.Api            }
        };

        /// <summary>
        /// Restituisce tutti i progetti C# nella solution correntemente aperta.
        /// Deve essere chiamato sul UI thread.
        /// </summary>
        public List<ProjectInfo> GetProjects()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dte = (DTE2)ServiceProvider.GlobalProvider.GetService(typeof(DTE));
            if (dte?.Solution == null || !dte.Solution.IsOpen)
                return new List<ProjectInfo>();

            var results = new List<ProjectInfo>();
            FlattenProjects(dte.Solution.Projects, results);
            return results;
        }

        /// <summary>
        /// Nome della solution corrente.
        /// </summary>
        public string GetSolutionName()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = (DTE2)ServiceProvider.GlobalProvider.GetService(typeof(DTE));
            return Path.GetFileNameWithoutExtension(dte?.Solution?.FileName ?? "Unknown");
        }

        // -----------------------------------------------------------------
        // Privati
        // -----------------------------------------------------------------

        private void FlattenProjects(Projects projects, List<ProjectInfo> results)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (projects == null) return;

            foreach (Project project in projects)
            {
                if (project.Kind == ProjectKinds.vsProjectKindSolutionFolder)
                {
                    // Cartella di solution: scendi ricorsivamente
                    FlattenProjects(project.ProjectItems
                        .Cast<ProjectItem>()
                        .Select(i => { ThreadHelper.ThrowIfNotOnUIThread(); return i.SubProject; })
                        .Where(p => p != null)
                        .Aggregate(new List<Project>(), (acc, p) => { acc.Add(p); return acc; }), results);
                }
                else
                {
                    var info = BuildProjectInfo(project);
                    if (info != null) results.Add(info);
                }
            }
        }

        private void FlattenProjects(IEnumerable<Project> projects, List<ProjectInfo> results)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (var p in projects)
            {
                var info = BuildProjectInfo(p);
                if (info != null) results.Add(info);
            }
        }

        private ProjectInfo BuildProjectInfo(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var csprojPath = project.FileName;
                if (string.IsNullOrEmpty(csprojPath) || !File.Exists(csprojPath))
                    return null;

                var physicalPath = Path.GetDirectoryName(csprojPath);
                var rootNamespace = ReadRootNamespace(csprojPath) ?? project.Name;
                var layer = InferLayer(project.Name);
                var baseClasses = ScanBaseClasses(physicalPath);

                return new ProjectInfo
                {
                    Name = project.Name,
                    PhysicalPath = physicalPath,
                    RootNamespace = rootNamespace,
                    Layer = layer,
                    DetectedBaseClasses = baseClasses
                };
            }
            catch
            {
                return null;
            }
        }

        private string ReadRootNamespace(string csprojPath)
        {
            try
            {
                var xml = XDocument.Load(csprojPath);
                var ns = xml.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "RootNamespace")?.Value;
                if (!string.IsNullOrEmpty(ns)) return ns;

                // Fallback: leggi AssemblyName
                return xml.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "AssemblyName")?.Value;
            }
            catch { return null; }
        }

        private ProjectLayer InferLayer(string projectName)
        {
            var lower = projectName.ToLower();
            foreach (var kv in LayerKeywords)
                if (lower.Contains(kv.Key)) return kv.Value;
            return ProjectLayer.Unknown;
        }

        private List<string> ScanBaseClasses(string physicalPath)
        {
            var found = new List<string>();
            try
            {
                var csFiles = Directory.GetFiles(physicalPath, "*.cs", SearchOption.AllDirectories);
                foreach (var file in csFiles)
                {
                    var content = File.ReadAllText(file);
                    foreach (var marker in BaseClassMarkers)
                        if (content.Contains(marker) && !found.Contains(marker))
                            found.Add(marker);
                }
            }
            catch { /* non bloccare se un file non è leggibile */ }
            return found;
        }
    }
}
