using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System;
namespace ArchitectFlow_AI.Services
{
    public static class SolutionExplorerHelper
    {
        private enum SelectionType { File, Folder }
        public static List<(string FullPath, string ProjectName)> GetSelectedFiles() 
            => GetSelectedItems(SelectionType.File);
        public static List<(string FullPath, string ProjectName)> GetSelectedFolders() 
            => GetSelectedItems(SelectionType.Folder);
        private static List<(string FullPath, string ProjectName)> GetSelectedItems(SelectionType type)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new List<(string, string)>();
            var monSel = Package.GetGlobalService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
            if (monSel == null) return result;
            monSel.GetCurrentSelection(out var hierPtr, out var itemId, out var multiSelect, out var selContainer);
            try
            {
                if (multiSelect != null)
                {
                    multiSelect.GetSelectionInfo(out uint count, out _);
                    var items = new VSITEMSELECTION[count];
                    multiSelect.GetSelectedItems(0, count, items);
                    for (uint i = 0; i < count; i++)
                    {
                        var path = GetItemPath(items[i].pHier, items[i].itemid);
                        bool exists = type == SelectionType.File ? File.Exists(path) : Directory.Exists(path);
                        if (!string.IsNullOrEmpty(path) && exists)
                        {
                            var proj = GetProjectName(items[i].pHier);
                            result.Add((path, proj));
                        }
                    }
                }
                else if (hierPtr != IntPtr.Zero && itemId != VSConstants.VSITEMID_NIL)
                {
                    var hierarchy = (IVsHierarchy)Marshal.GetObjectForIUnknown(hierPtr);
                    var path = GetItemPath(hierarchy, itemId);
                    bool exists = type == SelectionType.File ? File.Exists(path) : Directory.Exists(path);
                    if (!string.IsNullOrEmpty(path) && exists)
                    {
                        var proj = GetProjectName(hierarchy);
                        result.Add((path, proj));
                    }
                }
            }
            finally
            {
                if (hierPtr != IntPtr.Zero) Marshal.Release(hierPtr);
                if (selContainer != IntPtr.Zero) Marshal.Release(selContainer);
            }
            return result;
        }
        private static string GetItemPath(IVsHierarchy hierarchy, uint itemId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (hierarchy == null) return null;
            hierarchy.GetCanonicalName(itemId, out string path);
            return path;
        }
        private static string GetProjectName(IVsHierarchy hierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (hierarchy == null) return string.Empty;
            hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_Name, out object name);
            return name?.ToString() ?? string.Empty;
        }
    }
}
