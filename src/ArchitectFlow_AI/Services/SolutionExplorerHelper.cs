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

            IntPtr hierarchyPtr = IntPtr.Zero;
            uint itemId = VSConstants.VSITEMID_NIL;
            IVsMultiItemSelect multiSelect = null;
            IntPtr selContainer = IntPtr.Zero;

            try
            {
                // Otteniamo la selezione
                monSel.GetCurrentSelection(out hierarchyPtr, out itemId, out multiSelect, out selContainer);

                // --- CASO 1: SELEZIONE MULTIPLA ---
                if (multiSelect != null)
                {
                    multiSelect.GetSelectionInfo(out uint count, out _);
                    var items = new VSITEMSELECTION[count];

                    // Il flag 0 indica di recuperare tutti gli item
                    multiSelect.GetSelectedItems(0, count, items);

                    foreach (var item in items)
                    {
                        // Estrarre in modo sicuro l'IVsHierarchy dal puntatore dell'array
                        if (item.pHier != null)
                        {
                            ProcessSingleItem(item.pHier, item.itemid, type, result);
                        }
                    }
                }
                // --- CASO 2: SELEZIONE SINGOLA ---
                else if (hierarchyPtr != IntPtr.Zero && itemId != VSConstants.VSITEMID_NIL)
                {
                    var hierarchy = Marshal.GetObjectForIUnknown(hierarchyPtr) as IVsHierarchy;
                    if (hierarchy != null)
                    {
                        ProcessSingleItem(hierarchy, itemId, type, result);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArchitectFlow] Errore selezione: {ex.Message}");
            }
            finally
            {
                // Rilascio fondamentale dei puntatori COM nativi
                if (hierarchyPtr != IntPtr.Zero) Marshal.Release(hierarchyPtr);
                if (selContainer != IntPtr.Zero) Marshal.Release(selContainer);
            }

            return result;
        }

        private static void ProcessSingleItem(
            IVsHierarchy hierarchy,
            uint itemId,
            SelectionType type,
List<(string FullPath, string ProjectName)> result)       {
            ThreadHelper.ThrowIfNotOnUIThread();

            string path = GetItemPath(hierarchy, itemId);
            if (string.IsNullOrEmpty(path)) return;

            bool isValid = (type == SelectionType.File) ? File.Exists(path) : Directory.Exists(path);

            if (isValid)
            {
                string projectName = GetProjectName(hierarchy);

                // Ora 'FullPath' esiste perché la firma della lista lo dichiara
                if (!result.Exists(r => r.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add((path, projectName));
                }
            }
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
