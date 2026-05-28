using System.Collections.Generic;

namespace AutonomousMcp.Editor.Core
{
    /// <summary>
    /// Asset kinds that can be produced by an IGenerator implementation.
    /// Mirrors the Unity AI Assistant Generators surface so we can layer parity
    /// (and beyond) on the same MCP tool shape.
    /// </summary>
    public enum GeneratorKind
    {
        Sprite = 0,
        Texture = 1,
        Material = 2,
        Cubemap = 3,
        Audio = 4,
        Animation = 5,
        Model = 6,
        TerrainLayer = 7
    }

    /// <summary>Input to a generator. Provider-specific extras go in <see cref="ProviderOptions"/>.</summary>
    public sealed class GenerationRequest
    {
        public GeneratorKind Kind;
        public string Prompt = string.Empty;
        public string OutputAssetPath;          // optional — generator may pick a default
        public Dictionary<string, object> ProviderOptions = new Dictionary<string, object>();
    }

    /// <summary>Result of a generator call. <see cref="Success"/>=false carries a human-readable error.</summary>
    public sealed class GenerationResult
    {
        public bool Success;
        public string AssetPath;                // path relative to project on success
        public string ProviderUsed;             // e.g. "openai", "anthropic", "local-llm", "stub"
        public string Error;
        public Dictionary<string, object> Metadata = new Dictionary<string, object>();

        public static GenerationResult Fail(string error, string provider = "stub")
            => new GenerationResult { Success = false, Error = error, ProviderUsed = provider };

        public static GenerationResult Ok(string assetPath, string provider, Dictionary<string, object> meta = null)
            => new GenerationResult
            {
                Success = true,
                AssetPath = assetPath,
                ProviderUsed = provider,
                Metadata = meta ?? new Dictionary<string, object>()
            };
    }

    /// <summary>
    /// Pluggable asset generator. Implementations should be lightweight and stateless;
    /// the registry constructs a single instance per type at editor load.
    /// Drop a new file under Editor/Generators/ to add a kind or provider.
    /// </summary>
    public interface IGenerator
    {
        /// <summary>Unique provider id, e.g. "openai-dalle3", "anthropic-vision", "local-llm", "stub".</summary>
        string ProviderId { get; }

        /// <summary>The asset kind this generator produces.</summary>
        GeneratorKind Kind { get; }

        /// <summary>Short human-readable label for the Settings UI.</summary>
        string DisplayName { get; }

        /// <summary>True if env vars / settings allow this generator to run end-to-end right now.</summary>
        bool IsConfigured();

        /// <summary>Free-form status string surfaced in the Settings UI. Should never throw.</summary>
        string GetStatus();

        /// <summary>Execute generation on the main thread (registry guarantees this).</summary>
        GenerationResult Generate(GenerationRequest request);
    }
}
