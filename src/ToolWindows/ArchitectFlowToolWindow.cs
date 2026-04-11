using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using ArchitectFlow_AI.Services;
namespace ArchitectFlow_AI.ToolWindows
{
    /// <summary>
    /// Host del Tool Window principale ArchitectFlow AI.
    /// Agganciato alla Solution Explorer (docking a destra).
    /// </summary>
    [Guid("c1d2e3f4-a5b6-7890-cdef-012345678902")]
    public class ArchitectFlowToolWindow : ToolWindowPane
    {
        public ArchitectFlowToolWindow() : base(null)
        {
            Caption = "ArchitectFlow AI";

            // Il controllo WPF vero e proprio
            Content = new ArchitectFlowToolWindowControl();
        }
    }
}
