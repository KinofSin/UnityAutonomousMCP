namespace AutonomousMcp.Editor.Generators
{
    // The network half of an image generator. Implementations are key-gated; the Unity write half
    // is GeneratedAssetWriter (key-free), so most of the pipeline is testable without a key.
    internal interface IImageSource
    {
        byte[] FetchPng(string prompt, AutonomousMcp.Editor.Core.GenerationRequest req, out string error);
    }
}
