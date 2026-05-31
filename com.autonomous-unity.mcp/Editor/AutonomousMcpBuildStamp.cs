using System;
using UnityEditor;

namespace AutonomousMcp.Editor
{
    /// <summary>
    /// A token regenerated on every domain reload. Surfaced by <c>health_check</c> so a caller can
    /// PROVE the editor reloaded after an edit: capture the stamp → edit → trigger a recompile →
    /// re-read health. A <b>changed</b> stamp means the new assembly loaded (the edit is live); an
    /// <b>unchanged</b> stamp means a stale last-good assembly is still serving (the edit didn't
    /// compile/take — e.g. a CS error, or the editor was unfocused and never recompiled).
    /// </summary>
    [InitializeOnLoad]
    internal static class AutonomousMcpBuildStamp
    {
        /// <summary>Short random id, regenerated each domain load.</summary>
        public static readonly string Stamp;

        /// <summary>UTC time this assembly's static state initialized (≈ last reload time).</summary>
        public static readonly string CompiledAtUtc;

        static AutonomousMcpBuildStamp()
        {
            Stamp = Guid.NewGuid().ToString("N").Substring(0, 8);
            CompiledAtUtc = DateTime.UtcNow.ToString("o");
        }
    }
}
