using System.IO;

namespace AutonomousMcp.Editor.Templates
{
    // Resolves package-relative template data paths robustly, regardless of how the package is
    // mounted (file:/embedded/registry) — same PackageInfo.FindForAssembly approach as the Skills tab.
    internal static class TemplatePaths
    {
        public static string InteractionNotesPath()
        {
            try
            {
                var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(TemplatePaths).Assembly);
                if (pkg != null && !string.IsNullOrEmpty(pkg.resolvedPath))
                {
                    var p = Path.Combine(pkg.resolvedPath, "Editor", "Templates", "InteractionNotes.json");
                    if (File.Exists(p)) return p;
                }
            }
            catch { /* fall through */ }
            return string.Empty;
        }
    }
}
