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
    }
}
