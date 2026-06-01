using System.Collections.Generic;

namespace AutonomousMcp.Editor.Templates
{
    // Pure, deterministic, unit-testable. No Unity API, no reflection.
    internal static class ProjectTemplateEngine
    {
        public static string ClassifyPlatform(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            var n = name.ToLowerInvariant();
            if (n.Contains("quest") || n.Contains("android")) return "quest";
            return "pc";
        }

        public static List<TemplateStep> ComputeSteps(AvatarState s)
        {
            return new List<TemplateStep>
            {
                new TemplateStep { id = "descriptor",  label = "VRC Avatar Descriptor",        done = s.hasDescriptor },
                new TemplateStep { id = "viewpoint",   label = "Viewpoint set",                done = s.hasViewpoint },
                new TemplateStep { id = "expressions", label = "Expression Menu + Parameters", done = s.hasExpressionMenu && s.hasExpressionParams },
                new TemplateStep { id = "folders",     label = "Project folders",              done = s.hasFolders },
            };
        }

        // Strip quest/android/pc tokens and non-alphanumerics, lowercase — the "base" avatar identity.
        public static string BaseName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var n = name.ToLowerInvariant();
            n = n.Replace("quest", " ").Replace("android", " ").Replace("(pc)", " ").Replace("pc", " ");
            var sb = new System.Text.StringBuilder();
            foreach (var c in n) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        // Map each avatar that has a PC<->Quest twin (same base name, different platform) to its twin's name.
        public static System.Collections.Generic.Dictionary<string, string> ComputePairs(
            System.Collections.Generic.List<string> names)
        {
            var pairs = new System.Collections.Generic.Dictionary<string, string>();
            for (int i = 0; i < names.Count; i++)
                for (int j = i + 1; j < names.Count; j++)
                {
                    if (BaseName(names[i]).Length == 0 || BaseName(names[i]) != BaseName(names[j])) continue;
                    if (ClassifyPlatform(names[i]) == ClassifyPlatform(names[j])) continue;
                    pairs[names[i]] = names[j];
                    pairs[names[j]] = names[i];
                }
            return pairs;
        }
    }
}
