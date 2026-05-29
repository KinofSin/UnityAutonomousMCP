using System.Runtime.CompilerServices;

// Lets the EditMode self-test assembly call internal classification/timeout helpers in
// FreeTierImageClient and AutonomousMcpToolDispatcher (and see the internal AttemptOutcome enum)
// without widening the package's public API.
[assembly: InternalsVisibleTo("AutonomousMcp.Editor.Tests")]
