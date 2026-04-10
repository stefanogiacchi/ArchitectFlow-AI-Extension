using System.Collections.Generic;

namespace ArchitectFlowAI.Models
{
    /// <summary>
    /// Contesto completo passato al PromptBuilder dopo Analyze & Sync.
    /// </summary>
    public class GenerationContext
    {
        public string SolutionName { get; set; }
        public List<ProjectInfo> SelectedProjects { get; set; } = new List<ProjectInfo>();
        public string AcceptanceCriteria { get; set; }
        public string SqlQuery { get; set; }
        public string UserStoryTitle { get; set; }
        public string WikiUrl { get; set; }
        public string UserStoryUrl { get; set; }
        public bool IsValid => !string.IsNullOrWhiteSpace(AcceptanceCriteria)
                            && !string.IsNullOrWhiteSpace(SqlQuery)
                            && SelectedProjects.Count > 0;
    }
}
