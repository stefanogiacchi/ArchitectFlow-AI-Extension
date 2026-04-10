using System.Collections.Generic;

namespace ArchitectFlowAI.Models
{
    public class ProjectInfo
    {
        public string Name { get; set; }
        public string PhysicalPath { get; set; }
        public string RootNamespace { get; set; }
        public List<string> DetectedBaseClasses { get; set; } = new List<string>();
        public ProjectLayer Layer { get; set; }
        public bool IsSelected { get; set; }

        public override string ToString() => $"{Name} [{Layer}]";
    }

    public enum ProjectLayer { Unknown, Domain, Application, Infrastructure, Api }
}
