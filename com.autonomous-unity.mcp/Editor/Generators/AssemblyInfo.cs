using System.Runtime.CompilerServices;

// Lets the EditMode self-test assembly reach internal generator helpers (GeneratedAssetWriter,
// the OpenAI generators) now that Generators is its own assembly.
[assembly: InternalsVisibleTo("AutonomousMcp.Editor.Tests")]
