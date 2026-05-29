using System;
using System.Collections.Generic;
using System.IO;
using AutonomousMcp.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Generators
{
    /// <summary>
    /// Real Audio generator built on <see cref="FreeTierAudioClient"/> (HuggingFace text-to-audio
    /// with owned-key rotation + rate-limit backoff). Writes the returned clip (wav/flac/ogg/mp3)
    /// into Assets and imports it as an <see cref="AudioClip"/>.
    ///
    /// Requires one of your own HF tokens (GENERATOR_HF_TOKEN); without it this reports unconfigured
    /// and the stub provider is used instead. Model is configurable via GENERATOR_HF_AUDIO_MODEL
    /// (default MusicGen small for music; swap to an MMS/Bark TTS model for speech).
    /// </summary>
    internal sealed class FreeTierAudioGenerator : IGenerator
    {
        public string ProviderId => "free-tier";
        public GeneratorKind Kind => GeneratorKind.Audio;
        public string DisplayName => "Free-tier audio (HuggingFace text-to-audio)";

        public bool IsConfigured() => FreeTierAudioClient.AnyProviderAvailable();
        public string GetStatus() => FreeTierAudioClient.DescribeAvailability();

        public GenerationResult Generate(GenerationRequest request)
        {
            if (request == null)
                return GenerationResult.Fail("Null request.", ProviderId);

            AudioGenResult audio;
            try
            {
                audio = FreeTierAudioClient.Generate(request.Prompt);
            }
            catch (Exception ex)
            {
                return GenerationResult.Fail($"Audio client threw: {ex.Message}", ProviderId);
            }

            if (!audio.Success || audio.Bytes == null || audio.Bytes.Length == 0)
                return GenerationResult.Fail(audio.Error ?? "Audio generation failed.", ProviderId);

            string assetPath;
            try
            {
                assetPath = WriteAndImport(request.OutputAssetPath, audio);
            }
            catch (Exception ex)
            {
                return GenerationResult.Fail($"Generated audio but failed to import it: {ex.Message}", audio.ProviderUsed);
            }

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            return GenerationResult.Ok(assetPath, audio.ProviderUsed, new Dictionary<string, object>
            {
                ["model"] = audio.Model,
                ["bytes"] = audio.Bytes.Length,
                ["format"] = audio.Extension,
                ["importedAsAudioClip"] = clip != null,
                ["attempts"] = string.Join(" | ", audio.Attempts)
            });
        }

        private string WriteAndImport(string requestedOutput, AudioGenResult audio)
        {
            var ext = string.IsNullOrEmpty(audio.Extension) ? ".wav" : audio.Extension;

            var rel = string.IsNullOrWhiteSpace(requestedOutput)
                ? $"{GeneratorConfig.Data.defaultOutputDirectory.TrimEnd('/')}/Audio_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
                : requestedOutput.Replace('\\', '/');

            if (!rel.StartsWith("Assets/", StringComparison.Ordinal))
                rel = "Assets/" + rel.TrimStart('/');

            foreach (var e in new[] { ".wav", ".flac", ".ogg", ".mp3" })
                if (rel.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                    rel = rel.Substring(0, rel.Length - e.Length);
            rel += ext;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            var absolute = Path.GetFullPath(Path.Combine(projectRoot, rel));
            var dir = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(absolute, audio.Bytes);
            AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceSynchronousImport);
            return rel;
        }
    }
}
