using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System.Collections.Generic;

namespace ArchitectFlow_AI
{
    public class SolutionScanner
    {
        public static List<ProjectContext> GetActiveProjects()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            DTE dte = (DTE)Package.GetGlobalService(typeof(DTE));
            List<ProjectContext> projects = new List<ProjectContext>();

            foreach (Project project in dte.Solution.Projects)
            {
                if (project.Kind != "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}")
                { // Esclude cartelle di soluzione
                    projects.Add(new ProjectContext
                    {
                        Name = project.Name,
                        Namespace = project.Properties.Item("DefaultNamespace").Value.ToString(),
                        FullPath = project.FullName
                    });
                }
            }
            return projects;
        }
    }

    public class ProjectContext
    {
        public string Name { get; set; }
        public string Namespace { get; set; }
        public string FullPath { get; set; }
    }
}