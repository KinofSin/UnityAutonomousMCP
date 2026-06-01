using System.Collections.Generic;
using AutonomousMcp.Editor.Core;

namespace AutonomousMcp.Editor.Generators
{
    // OpenAI (BYOK) image generators: provider "openai". Compose the key-gated source with the
    // key-free writer. The registry (last-write-wins per Kind+ProviderId) keeps these alongside the
    // free-tier provider; pick one with manage_generator { provider:"openai" } or set it as default.
    internal abstract class OpenAiImageGeneratorBase : IGenerator
    {
        private static readonly IImageSource Source = new OpenAiImageSource();

        public string ProviderId => "openai";
        public abstract GeneratorKind Kind { get; }
        public string DisplayName => $"OpenAI ({Kind})";
        public bool IsConfigured() => OpenAiImageSource.HasKey();
        public string GetStatus() => OpenAiImageSource.HasKey()
            ? "OpenAI key set (GENERATOR_OPENAI_API_KEY)."
            : "Set GENERATOR_OPENAI_API_KEY for OpenAI generation.";

        public GenerationResult Generate(GenerationRequest request)
        {
            if (request == null) return GenerationResult.Fail("Null request.", ProviderId);
            var png = Source.FetchPng(request.Prompt, request, out var err);
            if (png == null) return GenerationResult.Fail(err ?? "OpenAI returned no image.", ProviderId);
            var path = GeneratedAssetWriter.Write(Kind, png, request.OutputAssetPath, out var werr);
            if (path == null) return GenerationResult.Fail(werr ?? "Generated image but failed to write the asset.", ProviderId);
            return GenerationResult.Ok(path, ProviderId, new Dictionary<string, object>
            {
                ["provider"] = "openai",
                ["bytes"] = png.Length,
                ["importedAs"] = Kind.ToString()
            });
        }
    }

    internal sealed class OpenAiTextureGenerator : OpenAiImageGeneratorBase { public override GeneratorKind Kind => GeneratorKind.Texture; }
    internal sealed class OpenAiSpriteGenerator : OpenAiImageGeneratorBase { public override GeneratorKind Kind => GeneratorKind.Sprite; }
    internal sealed class OpenAiMaterialGenerator : OpenAiImageGeneratorBase { public override GeneratorKind Kind => GeneratorKind.Material; }
}
