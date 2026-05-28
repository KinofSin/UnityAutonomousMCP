using System;
using AutonomousMcp.Editor.Core;

namespace AutonomousMcp.Editor.Generators
{
    /// <summary>
    /// Default "stub" provider for every GeneratorKind. Reports IsConfigured=false unless the
    /// matching env var is set; even then, Generate() returns a deliberate "not implemented"
    /// error so nothing is fabricated. Real providers (OpenAI, Anthropic, local LLM, etc.) can
    /// be added by dropping a new IGenerator class in this folder — discovery is automatic.
    ///
    /// Env-var convention (read at request time; never persisted):
    ///   GENERATOR_API_KEY            — global fallback
    ///   GENERATOR_OPENAI_API_KEY     — OpenAI-specific
    ///   GENERATOR_ANTHROPIC_API_KEY  — Anthropic-specific
    ///   GENERATOR_LOCAL_LLM_URL      — local OpenAI-compatible endpoint
    /// </summary>
    internal abstract class StubGeneratorBase : IGenerator
    {
        public string ProviderId => "stub";
        public abstract GeneratorKind Kind { get; }
        public string DisplayName => $"Stub ({Kind})";

        public bool IsConfigured() =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GENERATOR_API_KEY")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GENERATOR_OPENAI_API_KEY")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GENERATOR_ANTHROPIC_API_KEY")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GENERATOR_LOCAL_LLM_URL"));

        public string GetStatus()
        {
            return IsConfigured()
                ? "API key detected, but stub provider has no implementation. Add a real IGenerator class to use."
                : "No GENERATOR_* env vars set. Configure provider keys, then drop a real IGenerator implementation in Editor/Generators/.";
        }

        public GenerationResult Generate(GenerationRequest request)
        {
            return GenerationResult.Fail(
                $"Generator '{Kind}/stub' is a scaffold. " +
                "Implement a real IGenerator (e.g. OpenAiTextureGenerator) and the registry will pick it up automatically.",
                "stub");
        }
    }

    internal sealed class StubSpriteGenerator       : StubGeneratorBase { public override GeneratorKind Kind => GeneratorKind.Sprite; }
    internal sealed class StubTextureGenerator      : StubGeneratorBase { public override GeneratorKind Kind => GeneratorKind.Texture; }
    internal sealed class StubMaterialGenerator     : StubGeneratorBase { public override GeneratorKind Kind => GeneratorKind.Material; }
    internal sealed class StubCubemapGenerator      : StubGeneratorBase { public override GeneratorKind Kind => GeneratorKind.Cubemap; }
    internal sealed class StubAudioGenerator        : StubGeneratorBase { public override GeneratorKind Kind => GeneratorKind.Audio; }
    internal sealed class StubAnimationGenerator    : StubGeneratorBase { public override GeneratorKind Kind => GeneratorKind.Animation; }
    internal sealed class StubModelGenerator        : StubGeneratorBase { public override GeneratorKind Kind => GeneratorKind.Model; }
    internal sealed class StubTerrainLayerGenerator : StubGeneratorBase { public override GeneratorKind Kind => GeneratorKind.TerrainLayer; }
}
