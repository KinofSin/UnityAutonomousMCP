using System;
using System.Linq;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// manage_generator — Phase 7 surface for the IGenerator scaffold.
    /// Actions:
    ///   list           : enumerate every registered (kind, provider) pair + configured/status
    ///   generate       : kind + prompt [+ provider, output, options] → assetPath or error
    ///   get_config     : return persisted GeneratorConfig (provider map + default output dir + env detection)
    ///   set_provider   : kind + providerId → persist provider override (empty providerId clears it)
    ///   set_output_dir : path → persist default output directory (under Assets/)
    /// </summary>
    public static class ManageGeneratorTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("manage_generator", ToolMode.Mutate, ToolCategory.Generator,
                "Pluggable asset generator surface (sprite/texture/material/cubemap/audio/animation/model/terrain_layer). " +
                "Actions: list, generate, get_config, set_provider, set_output_dir. " +
                "Real provider implementations live under Editor/Generators/ — stubs ship by default and report 'not implemented'.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "list";
            try
            {
                switch (action)
                {
                    case "list":           return ListGenerators();
                    case "generate":       return Generate(args);
                    case "get_config":     return GetConfig();
                    case "set_provider":   return SetProvider(args);
                    case "set_output_dir": return SetOutputDir(args);
                    default:
                        return Err($"Unsupported manage_generator action '{action}'. " +
                                   "Valid: list, generate, get_config, set_provider, set_output_dir.");
                }
            }
            catch (Exception ex)
            {
                return Err($"manage_generator '{action}' threw: {ex.Message}");
            }
        }

        // ── Actions ────────────────────────────────────────────────────────────────

        private static AutonomousMcpToolResponse ListGenerators()
        {
            var generators = GeneratorRegistry.List();
            var arr = new JArray();
            foreach (var g in generators)
            {
                arr.Add(new JObject
                {
                    ["kind"] = g.Kind.ToString(),
                    ["provider"] = g.ProviderId,
                    ["displayName"] = g.DisplayName,
                    ["configured"] = g.IsConfigured(),
                    ["status"] = g.GetStatus()
                });
            }
            return Ok(new JObject
            {
                ["count"] = generators.Count,
                ["generators"] = arr
            });
        }

        private static AutonomousMcpToolResponse Generate(JObject args)
        {
            var kindStr = args.Value<string>("kind");
            if (string.IsNullOrEmpty(kindStr))
                return Err("'kind' is required (sprite|texture|material|cubemap|audio|animation|model|terrain_layer).");
            if (!Enum.TryParse<GeneratorKind>(kindStr, ignoreCase: true, out var kind))
                return Err($"Unknown generator kind '{kindStr}'.");

            var prompt = args.Value<string>("prompt");
            if (string.IsNullOrWhiteSpace(prompt))
                return Err("'prompt' is required.");

            var requestedProvider = args.Value<string>("provider");
            var generator = GeneratorRegistry.Resolve(kind, requestedProvider);
            if (generator == null)
                return Err($"No generator registered for kind '{kind}'.");

            var outputPath = args.Value<string>("outputAssetPath");
            if (string.IsNullOrEmpty(outputPath))
                outputPath = $"{GeneratorConfig.Data.defaultOutputDirectory.TrimEnd('/')}/{kind}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

            var providerOptions = args["options"] as JObject;
            var request = new GenerationRequest
            {
                Kind = kind,
                Prompt = prompt,
                OutputAssetPath = outputPath
            };
            if (providerOptions != null)
            {
                foreach (var prop in providerOptions.Properties())
                    request.ProviderOptions[prop.Name] = prop.Value?.ToObject<object>();
            }

            var result = generator.Generate(request);
            return new AutonomousMcpToolResponse
            {
                success = result.Success,
                data = new JObject
                {
                    ["kind"] = kind.ToString(),
                    ["provider"] = result.ProviderUsed ?? generator.ProviderId,
                    ["assetPath"] = result.AssetPath,
                    ["metadata"] = JObject.FromObject(result.Metadata ?? new System.Collections.Generic.Dictionary<string, object>())
                },
                error = result.Success ? null : result.Error
            };
        }

        private static AutonomousMcpToolResponse GetConfig()
        {
            var data = GeneratorConfig.Data;
            var providers = new JObject();
            foreach (var kvp in data.providerByKind)
                providers[kvp.Key] = kvp.Value;

            var envs = new JObject
            {
                ["GENERATOR_API_KEY"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GENERATOR_API_KEY")),
                ["GENERATOR_OPENAI_API_KEY"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GENERATOR_OPENAI_API_KEY")),
                ["GENERATOR_ANTHROPIC_API_KEY"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GENERATOR_ANTHROPIC_API_KEY")),
                ["GENERATOR_LOCAL_LLM_URL"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GENERATOR_LOCAL_LLM_URL"))
            };

            return Ok(new JObject
            {
                ["defaultOutputDirectory"] = data.defaultOutputDirectory,
                ["providerByKind"] = providers,
                ["envDetected"] = envs,
                ["registeredCount"] = GeneratorRegistry.Count,
                ["kinds"] = new JArray(Enum.GetNames(typeof(GeneratorKind)))
            });
        }

        private static AutonomousMcpToolResponse SetProvider(JObject args)
        {
            var kindStr = args.Value<string>("kind");
            if (string.IsNullOrEmpty(kindStr) ||
                !Enum.TryParse<GeneratorKind>(kindStr, ignoreCase: true, out var kind))
                return Err("'kind' is required and must be a valid GeneratorKind.");

            var providerId = args.Value<string>("provider");
            GeneratorConfig.SetProviderFor(kind, providerId);
            return Ok(new JObject
            {
                ["kind"] = kind.ToString(),
                ["provider"] = string.IsNullOrEmpty(providerId) ? null : providerId,
                ["cleared"] = string.IsNullOrEmpty(providerId)
            });
        }

        private static AutonomousMcpToolResponse SetOutputDir(JObject args)
        {
            var path = args.Value<string>("path");
            if (string.IsNullOrWhiteSpace(path))
                return Err("'path' is required.");
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                return Err("'path' must start with 'Assets/'.");

            GeneratorConfig.Data.defaultOutputDirectory = path.TrimEnd('/');
            GeneratorConfig.Save();
            return Ok(new JObject { ["defaultOutputDirectory"] = GeneratorConfig.Data.defaultOutputDirectory });
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static AutonomousMcpToolResponse Ok(JToken data) =>
            new AutonomousMcpToolResponse { success = true, data = data, error = null };

        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
