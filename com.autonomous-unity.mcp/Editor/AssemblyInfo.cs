using System.Runtime.CompilerServices;

// Lets the EditMode self-test assembly call internal classification/timeout helpers in
// FreeTierImageClient and AutonomousMcpToolDispatcher (and see the internal AttemptOutcome enum)
// without widening the package's public API.
[assembly: InternalsVisibleTo("AutonomousMcp.Editor.Tests")]

// The Tools assembly (Editor/Tools/) is split out so editing a tool recompiles only it, not the
// whole package. A couple of tool files use the internal AutonomousMcpToolDispatcher, so expose
// Core internals to Tools (one-way: Tools references Core, never the reverse).
[assembly: InternalsVisibleTo("AutonomousMcp.Editor.Tools")]
