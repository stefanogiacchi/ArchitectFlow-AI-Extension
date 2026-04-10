using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace ArchitectFlowAI.UI
{
    /// <summary>
    /// Tool Window ancorabile che ospita ArchitectWindowControl.
    /// Registrata in ArchitectFlowPackage tramite [ProvideToolWindow].
    /// </summary>
    [Guid("c3d4e5f6-a7b8-9012-cdef-123456789012")]
    public class ArchitectWindow : ToolWindowPane
    {
        public ArchitectWindow() : base(null)
        {
            Caption = "ArchitectFlow AI";
            Content = new ArchitectWindowControl();
        }
    }
}
