using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutonomousMcp.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Generators
{
    /// <summary>
    /// Real Model generator built on <see cref="FreeTierModel3DClient"/> (Meshy text-to-3D with
    /// owned-key rotation). Writes a GLB into Assets and imports it. If a glTF importer package
    /// (glTFast / UnityGLTF) is installed in the project it is soft-detected and the imported
    /// mesh/prefab is available; otherwise the raw .glb file is still returned.
    ///
    /// Requires GENERATOR_MESHY_API_KEY. Generation may block the main thread for up to
    /// GENERATOR_MODEL_3D_TIMEOUT_SEC while the provider task completes.
    /// </summary>
    internal sealed class FreeTierModelGenerator : IGenerator
    {
        public string ProviderId => "free-tier";
        public GeneratorKind Kind => GeneratorKind.Model;
        public string DisplayName => "Free-tier model (Meshy text-to-3D)";

        public bool IsConfigured() => FreeTierModel3DClient.AnyProviderAvailable();
        public string GetStatus() => FreeTierModel3DClient.DescribeAvailability();

        public GenerationResult Generate(GenerationRequest request)
        {
            if (request == null)
                return GenerationResult.Fail("Null request.", ProviderId);

            Model3DGenResult model;
            try
            {
                model = FreeTierModel3DClient.Generate(request.Prompt);
            }
            catch (Exception ex)
            {
                return GenerationResult.Fail($"Model client threw: {ex.Message}", ProviderId);
            }

            if (!model.Success || model.Bytes == null || model.Bytes.Length == 0)
                return GenerationResult.Fail(model.Error ?? "Model generation failed.", ProviderId);

            string assetPath;
            bool gltfImporterPresent;
            bool meshLoaded;
            try
            {
                assetPath = WriteAndImport(request.OutputAssetPath, model, out gltfImporterPresent, out meshLoaded);
            }
            catch (Exception ex)
            {
                return GenerationResult.Fail($"Generated GLB but failed to import: {ex.Message}", model.ProviderUsed);
            }

            return GenerationResult.Ok(assetPath, model.ProviderUsed, new Dictionary<string, object>
            {
                ["taskId"] = model.TaskId,
                ["pollCount"] = model.PollCount,
                ["bytes"] = model.Bytes.Length,
                ["format"] = ".glb",
                ["gltfImporterDetected"] = gltfImporterPresent,
                ["meshAssetLoaded"] = meshLoaded,
                ["importNote"] = gltfImporterPresent
                    ? (meshLoaded ? "GLB imported via detected glTF package." : "GLB written; glTF package present but mesh not yet loadable.")
                    : "GLB written. Install glTFast or UnityGLTF for automatic mesh import.",
                ["attempts"] = string.Join(" | ", model.Attempts)
            });
        }

        private string WriteAndImport(string requestedOutput, Model3DGenResult model,
            out bool gltfImporterPresent, out bool meshLoaded)
        {
            gltfImporterPresent = DetectGltfImporter();
            meshLoaded = false;

            var rel = string.IsNullOrWhiteSpace(requestedOutput)
                ? $"{GeneratorConfig.Data.defaultOutputDirectory.TrimEnd('/')}/Model_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
                : requestedOutput.Replace('\\', '/');

            if (!rel.StartsWith("Assets/", StringComparison.Ordinal))
                rel = "Assets/" + rel.TrimStart('/');

            if (!rel.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                rel += ".glb";

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            var absolute = Path.GetFullPath(Path.Combine(projectRoot, rel));
            var dir = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(absolute, model.Bytes);
            AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceSynchronousImport);

            if (gltfImporterPresent)
            {
                // glTF importers may produce a GameObject prefab or mesh sub-asset.
                meshLoaded = AssetDatabase.LoadAssetAtPath<GameObject>(rel) != null
                             || AssetDatabase.LoadAllAssetsAtPath(rel).Any(a => a is Mesh);
            }

            return rel;
        }

        /// <summary>Soft-detect a glTF importer without referencing it at compile time.</summary>
        private static bool DetectGltfImporter()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try { name = asm.GetName().Name ?? ""; }
                catch { continue; }

                if (name.IndexOf("GLTFast", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("UnityGLTF", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("glTF", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
