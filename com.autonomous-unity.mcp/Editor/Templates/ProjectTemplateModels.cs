using System;
using System.Collections.Generic;

namespace AutonomousMcp.Editor.Templates
{
    // Snapshot of one avatar's setup state, built from the live scene by the handler and fed to
    // the pure engine. Booleans only — no Unity types — so the engine stays unit-testable.
    public struct AvatarState
    {
        public bool hasDescriptor;
        public bool hasViewpoint;
        public bool hasExpressionMenu;
        public bool hasExpressionParams;
        public bool hasFolders;
    }

    [Serializable]
    public sealed class TemplateStep
    {
        public string id;
        public string label;
        public bool done;
    }

    [Serializable]
    public sealed class InspectReport
    {
        public string avatarName;
        public string platform;      // "pc" | "quest" | "unknown"
        public bool isAvatar;
        public List<TemplateStep> steps = new List<TemplateStep>();
    }

    [Serializable]
    public sealed class ApplyResult
    {
        public string avatarName;
        public List<string> changed = new List<string>();
        public List<string> skipped = new List<string>();
        public List<string> notes = new List<string>();
    }
}
