using System.Collections.Generic;
using System.Linq;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_optimization — mesh/texture/draw-call audit (read-only).
    /// </summary>
    public static class UnityOptimizationTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_optimization", ToolMode.Read, ToolCategory.Profiler,
                "Mesh + texture + draw-call audit. Actions: mesh_audit, texture_audit, " +
                "draw_call_estimate, scene_summary, oversized_textures.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "scene_summary";
            switch (action)
            {
                case "mesh_audit": return MeshAudit(args);
                case "texture_audit": return TextureAudit(args);
                case "draw_call_estimate": return DrawCallEstimate(args);
                case "scene_summary": return SceneSummary(args);
                case "oversized_textures": return OversizedTextures(args);
                default:
                    return Err($"Unsupported unity_optimization action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse MeshAudit(JObject args)
        {
            var triThreshold = args.Value<int?>("triangle_threshold") ?? 5000;
            var scene = SceneManager.GetActiveScene();
            var hits = new List<object>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = smr.sharedMesh;
                    if (mesh == null) continue;
                    int tris = mesh.triangles.Length / 3;
                    if (tris >= triThreshold)
                    {
                        hits.Add(new
                        {
                            path = GetPath(smr.transform),
                            type = "SkinnedMeshRenderer",
                            triangles = tris,
                            vertices = mesh.vertexCount,
                            blendshapes = mesh.blendShapeCount,
                            subMeshes = mesh.subMeshCount
                        });
                    }
                }
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null) continue;
                    int tris = mesh.triangles.Length / 3;
                    if (tris >= triThreshold)
                    {
                        hits.Add(new
                        {
                            path = GetPath(mf.transform),
                            type = "MeshFilter",
                            triangles = tris,
                            vertices = mesh.vertexCount,
                            subMeshes = mesh.subMeshCount
                        });
                    }
                }
            }
            return Ok(new { action = "mesh_audit", triThreshold, count = hits.Count, results = hits });
        }

        private static AutonomousMcpToolResponse TextureAudit(JObject args)
        {
            var folder = args.Value<string>("folder") ?? "Assets";
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            var hits = new List<object>();
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;
                hits.Add(new
                {
                    path,
                    width = tex.width,
                    height = tex.height,
                    format = tex.format.ToString(),
                    crunched = importer.crunchedCompression,
                    maxSize = importer.maxTextureSize,
                    isReadable = importer.isReadable,
                    mips = importer.mipmapEnabled,
                    sRGB = importer.sRGBTexture
                });
            }
            return Ok(new { action = "texture_audit", count = hits.Count, textures = hits });
        }

        private static AutonomousMcpToolResponse OversizedTextures(JObject args)
        {
            var maxAllowed = args.Value<int?>("max_size") ?? 2048;
            // Data textures (LUTs, Poiyomi TPS baked mesh strips) are extremely long and
            // 1-8 px on the short edge. Judging by the LARGER dimension alone inverted this
            // check in practice: an 8190x2 TPS strip costing 64 KB was reported while a
            // 2048x2048 albedo costing 11 MB was not — and shrinking that strip corrupts the
            // mesh data it encodes. Require a real short edge, and rank by actual memory.
            var minDimension = args.Value<int?>("min_dimension") ?? 64;
            var folder = args.Value<string>("folder") ?? "Assets";
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });

            var hits = new List<(long bytes, object entry)>();
            long totalBytes = 0;
            var skippedDataTextures = new List<object>();

            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (tex == null || importer == null) continue;

                int big = System.Math.Max(tex.width, tex.height);
                if (big <= maxAllowed) continue;

                int shortEdge = System.Math.Min(tex.width, tex.height);
                if (shortEdge < minDimension)
                {
                    // Surfaced, not hidden — but never presented as something to shrink.
                    skippedDataTextures.Add(new { path, width = tex.width, height = tex.height });
                    continue;
                }

                var bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                totalBytes += bytes;
                hits.Add((bytes, new
                {
                    path,
                    width = tex.width,
                    height = tex.height,
                    currentMaxSize = importer.maxTextureSize,
                    crunched = importer.crunchedCompression,
                    bytes
                }));
            }

            var ordered = hits.OrderByDescending(h => h.bytes).Select(h => h.entry).ToList();
            return Ok(new
            {
                action = "oversized_textures",
                maxAllowed,
                minDimension,
                count = ordered.Count,
                totalBytes,
                totalMB = System.Math.Round(totalBytes / 1048576.0, 1),
                textures = ordered,
                skippedDataTextureCount = skippedDataTextures.Count,
                skippedDataTextures,
                note = "Project-wide scan over 'folder', NOT scene-scoped — it will not move when you " +
                       "optimize the open scene. For a scene/avatar figure use unity_perception " +
                       "{action:'dossier', sections:['textures']}, which reports only what is actually loaded."
            });
        }

        private static AutonomousMcpToolResponse DrawCallEstimate(JObject args)
        {
            var scene = SceneManager.GetActiveScene();
            int total = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r.enabled) continue;
                    if (r.sharedMaterials == null) continue;
                    total += r.sharedMaterials.Count(m => m != null);
                }
            }
            return Ok(new
            {
                action = "draw_call_estimate",
                estimate = total,
                note = "Rough estimate: enabled renderer count × non-null shared materials. Real cost depends on batching, shadows, multiple cameras, lights."
            });
        }

        private static AutonomousMcpToolResponse SceneSummary(JObject args)
        {
            var scene = SceneManager.GetActiveScene();
            int gameObjects = 0, renderers = 0, smrCount = 0, mfCount = 0, totalTris = 0, totalVerts = 0;
            var materialSet = new HashSet<Material>();
            var meshSet = new HashSet<Mesh>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) gameObjects++;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    renderers++;
                    if (r.sharedMaterials != null) foreach (var m in r.sharedMaterials) if (m != null) materialSet.Add(m);
                    if (r is SkinnedMeshRenderer smr)
                    {
                        smrCount++;
                        if (smr.sharedMesh != null)
                        {
                            meshSet.Add(smr.sharedMesh);
                            totalTris += smr.sharedMesh.triangles.Length / 3;
                            totalVerts += smr.sharedMesh.vertexCount;
                        }
                    }
                }
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    mfCount++;
                    if (mf.sharedMesh != null && meshSet.Add(mf.sharedMesh))
                    {
                        totalTris += mf.sharedMesh.triangles.Length / 3;
                        totalVerts += mf.sharedMesh.vertexCount;
                    }
                }
            }
            return Ok(new
            {
                action = "scene_summary",
                scene = scene.name,
                gameObjects,
                renderers,
                skinnedMeshRenderers = smrCount,
                meshFilters = mfCount,
                uniqueMaterials = materialSet.Count,
                uniqueMeshes = meshSet.Count,
                totalTriangles = totalTris,
                totalVertices = totalVerts
            });
        }

        private static string GetPath(Transform t)
        {
            var sb = new System.Text.StringBuilder(t.name);
            var cur = t.parent;
            while (cur != null) { sb.Insert(0, cur.name + "/"); cur = cur.parent; }
            return sb.ToString();
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
