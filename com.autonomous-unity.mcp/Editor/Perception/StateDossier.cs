using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutonomousMcp.Editor.Perception
{
    /// <summary>
    /// Sectioned scene/avatar state builder for unity_perception action "dossier".
    /// One section per call keeps each request under the 10s default dispatch budget.
    /// </summary>
    public static class StateDossier
    {
        public const int DefaultMaxRenderers = 200;
        public const int DefaultMaxMaterials = 80;

        // PC avatar performance rank thresholds (VRChat creator docs). Quest is stricter.
        public static readonly object PcBudgets = new
        {
            polygons = new { excellent = 32000, good = 70000 },
            materialSlots = new { excellent = 4, good = 8, medium = 16 },
            skinnedMeshes = new { excellent = 1, good = 8, medium = 16 },
            bones = new { excellent = 75, good = 150, medium = 256 },
            physBones = new { excellent = 4, good = 8, medium = 16 },
            physBoneColliders = new { excellent = 4, good = 8, medium = 16 },
            expressionParameterCost = new { budget = 256 }
        };

        public static object Build(JObject args)
        {
            var sections = ParseSections(args);
            var maxRenderers = args.Value<int?>("max_renderers") ?? DefaultMaxRenderers;
            var maxMaterials = args.Value<int?>("max_materials") ?? DefaultMaxMaterials;
            var mode = (args.Value<string>("mode") ?? string.Empty).Trim().ToLowerInvariant();
            var wantsScene = mode == "scene" ||
                             (args.Value<bool?>("scene") ?? false) ||
                             (!args.Value<int?>("instanceId").HasValue &&
                              string.IsNullOrWhiteSpace(args.Value<string>("target") ?? args.Value<string>("name")));

            GameObject root = null;
            if (!wantsScene)
            {
                root = ResolveTarget(args, out var resolveErr);
                if (root == null)
                    return new { action = "dossier", success = false, error = resolveErr };
            }

            var truncated = new Dictionary<string, bool>();
            var built = new Dictionary<string, object>();
            var scene = SceneManager.GetActiveScene();

            foreach (var section in sections)
            {
                switch (section)
                {
                    case "identity":
                        built[section] = BuildIdentity(root, scene);
                        break;
                    case "descriptor":
                        built[section] = root != null ? BuildDescriptor(root) : Note("avatar target required");
                        break;
                    case "frameworks":
                        built[section] = root != null ? BuildFrameworks(root) : Note("avatar target required");
                        break;
                    case "renderers":
                        built[section] = BuildRenderers(root, scene, maxRenderers, truncated);
                        break;
                    case "materials":
                        built[section] = BuildMaterials(root, scene, maxMaterials, truncated, detail: false);
                        break;
                    case "material_detail":
                        built[section] = BuildMaterials(root, scene, maxMaterials, truncated, detail: true);
                        break;
                    case "textures":
                        built[section] = BuildTextures(root, scene, maxMaterials, truncated);
                        break;
                    case "physbones":
                        built[section] = root != null ? BuildPhysBones(root) : Note("avatar target required");
                        break;
                    case "animators":
                        built[section] = root != null ? BuildAnimators(root) : Note("avatar target required");
                        break;
                    case "budgets":
                        built[section] = root != null ? BuildBudgets(root) : Note("avatar target required");
                        break;
                    case "world":
                        built[section] = BuildWorld(scene);
                        break;
                    default:
                        built[section] = Note($"unknown section '{section}'");
                        break;
                }
            }

            return new
            {
                action = "dossier",
                mode = wantsScene || root == null ? "scene" : "avatar",
                target = root != null ? root.name : null,
                instanceId = root != null ? root.GetInstanceID() : (int?)null,
                sections = built,
                truncated,
                requestedSections = sections
            };
        }

        // ── sections ──────────────────────────────────────────────────────────

        private static object BuildIdentity(GameObject root, Scene scene)
        {
            var stateHash = ComputeStateHash(root, scene);
            return new
            {
                target = root != null ? root.name : null,
                instanceId = root != null ? root.GetInstanceID() : (int?)null,
                path = root != null ? HierarchyPath(root.transform) : null,
                activeSelf = root != null ? root.activeSelf : (bool?)null,
                activeInHierarchy = root != null ? root.activeInHierarchy : (bool?)null,
                scene = new { name = scene.name, path = scene.path, isDirty = scene.isDirty },
                unityVersion = Application.unityVersion,
                buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                stateHash,
                timestampUtc = DateTime.UtcNow.ToString("o")
            };
        }

        private static object BuildDescriptor(GameObject root)
        {
            var type = FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (type == null) return new { hasAvatarDescriptor = false, note = "VRChat SDK not detected." };
            var descriptor = root.GetComponent(type);
            if (descriptor == null) return new { hasAvatarDescriptor = false, warning = "No VRCAvatarDescriptor on target." };

            var so = new SerializedObject(descriptor);
            object viewPosition = null;
            var viewPos = so.FindProperty("ViewPosition");
            if (viewPos != null)
                viewPosition = new { x = viewPos.vector3Value.x, y = viewPos.vector3Value.y, z = viewPos.vector3Value.z };

            string lipSync = null;
            var lip = so.FindProperty("lipSync");
            if (lip != null && lip.enumValueIndex >= 0 && lip.enumValueIndex < lip.enumNames.Length)
                lipSync = lip.enumNames[lip.enumValueIndex];

            string visemeMesh = null;
            var vm = so.FindProperty("VisemeSkinnedMesh");
            if (vm?.objectReferenceValue != null) visemeMesh = vm.objectReferenceValue.name;

            var paramsPayload = ReadExpressionParameters(so.FindProperty("expressionParameters")?.objectReferenceValue);
            string menuAsset = null;
            var menu = so.FindProperty("expressionsMenu");
            if (menu?.objectReferenceValue != null)
                menuAsset = AssetDatabase.GetAssetPath(menu.objectReferenceValue);
            so.Dispose();

            return new
            {
                hasAvatarDescriptor = true,
                lipSyncType = lipSync,
                visemeMesh,
                viewPosition,
                expressionsMenuAsset = menuAsset,
                expressionParameters = paramsPayload
            };
        }

        private static object BuildFrameworks(GameObject root)
        {
            var list = new List<object>();
            AddFramework(list, root, "Modular Avatar", "nadena.dev.modular_avatar.core.AvatarTagComponent");
            AddFramework(list, root, "VRCFury", "VF.Model.VRCFury");
            var aao = FindType("Anatawa12.AvatarOptimizer.TraceAndOptimize");
            if (aao != null)
            {
                var comps = root.GetComponentsInChildren(aao, true);
                list.Add(new
                {
                    framework = "AAO: Avatar Optimizer",
                    hasTraceAndOptimize = comps.Length > 0,
                    enabled = comps.Any(c => c is Behaviour b && b.enabled)
                });
            }
            var poiLocked = 0;
            var poiUnlocked = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.sharedMaterials == null) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m?.shader == null) continue;
                    var id = MaterialDigest.ParseShaderIdentity(m.shader.name);
                    if (id.family != "poiyomi") continue;
                    if (id.locked) poiLocked++; else poiUnlocked++;
                }
            }
            if (poiLocked + poiUnlocked > 0)
                list.Add(new { framework = "Poiyomi", lockedMaterials = poiLocked, unlockedMaterials = poiUnlocked });
            return list;
        }

        private static object BuildRenderers(GameObject root, Scene scene, int max, Dictionary<string, bool> truncated)
        {
            var rows = new List<object>();
            foreach (var r in EnumerateRenderers(root, scene))
            {
                if (rows.Count >= max) { truncated["renderers"] = true; break; }
                Mesh mesh = null;
                int blendshapes = 0, bones = 0;
                string rootBone = null;
                bool? updateWhenOffscreen = null;
                if (r is SkinnedMeshRenderer smr)
                {
                    mesh = smr.sharedMesh;
                    blendshapes = mesh != null ? mesh.blendShapeCount : 0;
                    bones = smr.bones != null ? smr.bones.Length : 0;
                    rootBone = smr.rootBone != null ? smr.rootBone.name : null;
                    updateWhenOffscreen = smr.updateWhenOffscreen;
                }
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    mesh = mf != null ? mf.sharedMesh : null;
                }

                var b = r.bounds;
                rows.Add(new
                {
                    path = HierarchyPath(r.transform),
                    instanceId = r.gameObject.GetInstanceID(),
                    type = r.GetType().Name,
                    active = r.gameObject.activeInHierarchy,
                    enabled = r.enabled,
                    mesh = mesh != null ? mesh.name : null,
                    meshPath = mesh != null ? AssetDatabase.GetAssetPath(mesh) : null,
                    tris = mesh != null ? mesh.triangles.Length / 3 : 0,
                    verts = mesh != null ? mesh.vertexCount : 0,
                    submeshes = mesh != null ? mesh.subMeshCount : 0,
                    materialCount = r.sharedMaterials != null ? r.sharedMaterials.Length : 0,
                    blendshapes,
                    bones,
                    rootBone,
                    updateWhenOffscreen,
                    shadowCasting = r.shadowCastingMode.ToString(),
                    bounds = new { center = Vec(b.center), size = Vec(b.size) }
                });
            }
            return new { count = rows.Count, renderers = rows };
        }

        private static object BuildMaterials(GameObject root, Scene scene, int max, Dictionary<string, bool> truncated, bool detail)
        {
            var usedBy = new Dictionary<int, List<string>>();
            var mats = CollectMaterials(root, scene, usedBy);
            var list = new List<object>();
            foreach (var mat in mats)
            {
                if (list.Count >= max) { truncated[detail ? "material_detail" : "materials"] = true; break; }
                usedBy.TryGetValue(mat.GetInstanceID(), out var paths);
                if (!detail)
                {
                    list.Add(MaterialDigest.Summarize(mat, paths ?? new List<string>()));
                    continue;
                }

                var identity = MaterialDigest.ParseShaderIdentity(mat.shader != null ? mat.shader.name : "");
                var diff = MaterialDigest.DiffNonDefault(mat);
                list.Add(new
                {
                    name = mat.name,
                    instanceId = mat.GetInstanceID(),
                    path = AssetDatabase.GetAssetPath(mat),
                    shader = mat.shader != null ? mat.shader.name : null,
                    family = identity.family,
                    locked = identity.locked,
                    displayShader = identity.displayName,
                    renderQueue = mat.renderQueue,
                    usedBy = paths,
                    propertiesNotMeaningfullyReadable = diff.propertiesNotMeaningfullyReadable,
                    note = diff.note,
                    suppressedDefaults = diff.suppressedDefaults,
                    changedPropertyCount = diff.changed.Count,
                    changedProperties = diff.changed.Select(p => new
                    {
                        p.name, p.displayName, p.type, p.value, p.defaultValue
                    }).ToList()
                });
            }
            return new { count = list.Count, materials = list };
        }

        private static object BuildTextures(GameObject root, Scene scene, int maxMats, Dictionary<string, bool> truncated)
        {
            var usedBy = new Dictionary<int, List<string>>();
            var mats = CollectMaterials(root, scene, usedBy);
            var texRefs = new Dictionary<int, List<string>>();
            var textures = new List<Texture>();
            var matCount = 0;
            foreach (var mat in mats)
            {
                if (matCount++ >= maxMats) { truncated["textures"] = true; break; }
                if (mat?.shader == null) continue;
                int count = mat.shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    if (mat.shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                    var prop = mat.shader.GetPropertyName(i);
                    var tex = mat.GetTexture(prop);
                    if (tex == null) continue;
                    textures.Add(tex);
                    var id = tex.GetInstanceID();
                    if (!texRefs.TryGetValue(id, out var refs))
                        texRefs[id] = refs = new List<string>();
                    var label = $"{mat.name}.{prop}";
                    if (!refs.Contains(label)) refs.Add(label);
                }
            }
            var digests = TextureDigest.DigestsFor(textures, texRefs);
            return new { count = ((List<object>)digests).Count, textures = digests };
        }

        private static object BuildPhysBones(GameObject root)
        {
            var type = FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
            if (type == null) return new { count = 0, note = "PhysBone type not found (SDK missing)." };
            var comps = root.GetComponentsInChildren(type, true);
            var list = new List<object>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                var so = new SerializedObject(c);
                list.Add(new
                {
                    path = HierarchyPath(c.transform),
                    instanceId = c.GetInstanceID(),
                    enabled = ((Behaviour)c).enabled,
                    rootTransform = ReadObjName(so, "rootTransform") ?? c.transform.name,
                    transformCount = CountPbTransforms(c.transform),
                    pull = ReadFloat(so, "pull"),
                    spring = ReadFloat(so, "spring"),
                    stiffness = ReadFloat(so, "stiffness"),
                    gravity = ReadFloat(so, "gravity"),
                    gravityFalloff = ReadFloat(so, "gravityFalloff"),
                    immobile = ReadFloat(so, "immobile"),
                    limitType = ReadEnum(so, "limitType"),
                    maxAngleX = ReadFloat(so, "maxAngleX"),
                    maxAngleZ = ReadFloat(so, "maxAngleZ"),
                    radius = ReadFloat(so, "radius"),
                    colliders = CountArray(so, "colliders")
                });
                so.Dispose();
            }
            return new { count = list.Count, physBones = list };
        }

        private static object BuildAnimators(GameObject root)
        {
            var animators = root.GetComponentsInChildren<Animator>(true);
            var list = new List<object>();
            foreach (var a in animators)
            {
                var ctrl = a.runtimeAnimatorController as AnimatorController;
                int clipCount = 0;
                int layerCount = 0;
                if (ctrl != null)
                {
                    layerCount = ctrl.layers.Length;
                    var clips = new HashSet<AnimationClip>(ctrl.animationClips);
                    clipCount = clips.Count;
                }
                list.Add(new
                {
                    path = HierarchyPath(a.transform),
                    instanceId = a.GetInstanceID(),
                    controller = a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : null,
                    controllerPath = a.runtimeAnimatorController != null
                        ? AssetDatabase.GetAssetPath(a.runtimeAnimatorController) : null,
                    layerCount,
                    clipCount,
                    avatarIsHuman = a.avatar != null && a.avatar.isHuman
                });
            }

            // Playable layers from the descriptor when present.
            object playableLayers = null;
            var descType = FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            var desc = descType != null ? root.GetComponent(descType) : null;
            if (desc != null)
            {
                var so = new SerializedObject(desc);
                playableLayers = new
                {
                    baseAnimationLayers = ReadPlayableLayers(so.FindProperty("baseAnimationLayers")),
                    specialAnimationLayers = ReadPlayableLayers(so.FindProperty("specialAnimationLayers"))
                };
                so.Dispose();
            }

            return new { count = list.Count, animators = list, playableLayers };
        }

        private static object BuildBudgets(GameObject root)
        {
            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var mrs = root.GetComponentsInChildren<MeshRenderer>(true);
            int polys = 0, mats = 0, blendshapes = 0;
            foreach (var smr in smrs)
            {
                if (smr.sharedMesh != null)
                {
                    polys += smr.sharedMesh.triangles.Length / 3;
                    blendshapes += smr.sharedMesh.blendShapeCount;
                }
                mats += smr.sharedMaterials?.Length ?? 0;
            }
            foreach (var mr in mrs)
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf?.sharedMesh != null) polys += mf.sharedMesh.triangles.Length / 3;
                mats += mr.sharedMaterials?.Length ?? 0;
            }

            var pbType = FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
            var pbcType = FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider");
            int physBones = pbType != null ? root.GetComponentsInChildren(pbType, true).Length : 0;
            int physBoneColliders = pbcType != null ? root.GetComponentsInChildren(pbcType, true).Length : 0;
            int bones = root.GetComponentsInChildren<Transform>(true).Length;

            return new
            {
                measured = new
                {
                    polygons = polys,
                    materialSlots = mats,
                    skinnedMeshes = smrs.Length,
                    meshRenderers = mrs.Length,
                    blendshapes,
                    bones,
                    physBones,
                    physBoneColliders
                },
                pc = PcBudgets,
                note = "PC avatar rank thresholds. Quest is stricter — measure the Quest twin separately."
            };
        }

        private static object BuildWorld(Scene scene)
        {
            var lights = UnityEngine.Object.FindObjectsOfType<Light>();
            var probes = UnityEngine.Object.FindObjectsOfType<ReflectionProbe>();
            var audio = UnityEngine.Object.FindObjectsOfType<AudioSource>();
            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            int staticCount = 0;
            var topMeshes = new List<(string path, int tris)>();
            foreach (var r in renderers)
            {
                if (r.gameObject.isStatic) staticCount++;
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    mesh = mf != null ? mf.sharedMesh : null;
                }
                if (mesh == null) continue;
                topMeshes.Add((HierarchyPath(r.transform), mesh.triangles.Length / 3));
            }
            topMeshes.Sort((a, b) => b.tris.CompareTo(a.tris));

            var udonType = FindType("VRC.Udon.UdonBehaviour");
            int udonCount = udonType != null
                ? UnityEngine.Object.FindObjectsOfType(udonType).Length
                : 0;

            return new
            {
                scene = new { name = scene.name, path = scene.path, isDirty = scene.isDirty },
                lighting = new
                {
                    bakedGI = Lightmapping.bakedGI,
                    realtimeGI = Lightmapping.realtimeGI,
                    lightmaps = LightmapSettings.lightmaps != null ? LightmapSettings.lightmaps.Length : 0
                },
                fog = new
                {
                    enabled = RenderSettings.fog,
                    mode = RenderSettings.fogMode.ToString(),
                    density = RenderSettings.fogDensity
                },
                skybox = RenderSettings.skybox != null ? RenderSettings.skybox.name : null,
                lightCount = lights.Length,
                reflectionProbeCount = probes.Length,
                audioSourceCount = audio.Length,
                staticRendererCount = staticCount,
                udonBehaviourCount = udonCount,
                topMeshesByTris = topMeshes.Take(25).Select(t => new { t.path, t.tris }).ToList()
            };
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static List<string> ParseSections(JObject args)
        {
            var token = args["sections"];
            if (token is JArray arr && arr.Count > 0)
                return arr.Select(t => (t?.ToString() ?? "").Trim().ToLowerInvariant())
                    .Where(s => s.Length > 0).Distinct().ToList();
            // Default: identity only — harness requests the rest one-by-one.
            return new List<string> { "identity" };
        }

        private static GameObject ResolveTarget(JObject args, out string error)
        {
            error = null;
            var instanceId = args.Value<int?>("instanceId");
            if (instanceId.HasValue)
            {
                var byId = EditorUtility.InstanceIDToObject(instanceId.Value) as GameObject;
                if (byId != null) return byId;
                error = $"instanceId {instanceId.Value} is not a GameObject.";
                return null;
            }

            var name = args.Value<string>("target") ?? args.Value<string>("name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "dossier requires instanceId, target/name, or mode:\"scene\".";
                return null;
            }

            var matches = new List<GameObject>();
            foreach (var go in EnumerateSceneGameObjects())
            {
                if (string.Equals(go.name, name, StringComparison.Ordinal))
                    matches.Add(go);
            }
            if (matches.Count == 0)
            {
                error = $"no GameObject named '{name}' (including inactive).";
                return null;
            }
            if (matches.Count > 1)
            {
                error = $"ambiguous name '{name}' matched {matches.Count} objects; pass instanceId. " +
                        string.Join(", ", matches.Select(m => HierarchyPath(m.transform) + "#" + m.GetInstanceID()));
                return null;
            }
            return matches[0];
        }

        private static IEnumerable<GameObject> EnumerateSceneGameObjects()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) yield break;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    yield return t.gameObject;
            }
        }

        private static IEnumerable<Renderer> EnumerateRenderers(GameObject root, Scene scene)
        {
            if (root != null)
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    yield return r;
                yield break;
            }
            foreach (var go in scene.GetRootGameObjects())
            {
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    yield return r;
            }
        }

        private static List<Material> CollectMaterials(GameObject root, Scene scene, Dictionary<int, List<string>> usedBy)
        {
            var ordered = new List<Material>();
            var seen = new HashSet<int>();
            foreach (var r in EnumerateRenderers(root, scene))
            {
                if (r.sharedMaterials == null) continue;
                var path = HierarchyPath(r.transform);
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    var id = m.GetInstanceID();
                    if (!usedBy.TryGetValue(id, out var refs))
                        usedBy[id] = refs = new List<string>();
                    if (!refs.Contains(path)) refs.Add(path);
                    if (seen.Add(id)) ordered.Add(m);
                }
            }
            return ordered;
        }

        private static object ReadExpressionParameters(UnityEngine.Object paramAsset)
        {
            if (paramAsset == null) return new { count = 0, cost = 0, budget = 256, remaining = 256, parameters = new object[0] };
            var paramSO = new SerializedObject(paramAsset);
            var paramsList = paramSO.FindProperty("parameters");
            var entries = new List<object>();
            int totalCost = 0;
            if (paramsList != null && paramsList.isArray)
            {
                for (int i = 0; i < paramsList.arraySize; i++)
                {
                    var elem = paramsList.GetArrayElementAtIndex(i);
                    var paramName = elem.FindPropertyRelative("name")?.stringValue ?? "";
                    var paramType = elem.FindPropertyRelative("valueType");
                    int cost = 0;
                    string typeName = "Unknown";
                    if (paramType != null)
                    {
                        switch (paramType.intValue)
                        {
                            case 0: cost = 8; typeName = "Int"; break;
                            case 1: cost = 8; typeName = "Float"; break;
                            case 2: cost = 1; typeName = "Bool"; break;
                        }
                    }
                    totalCost += cost;
                    if (!string.IsNullOrEmpty(paramName))
                        entries.Add(new { name = paramName, type = typeName, cost });
                }
            }
            paramSO.Dispose();
            return new { count = entries.Count, cost = totalCost, budget = 256, remaining = 256 - totalCost, parameters = entries };
        }

        private static List<object> ReadPlayableLayers(SerializedProperty layers)
        {
            var list = new List<object>();
            if (layers == null || !layers.isArray) return list;
            for (int i = 0; i < layers.arraySize; i++)
            {
                var elem = layers.GetArrayElementAtIndex(i);
                var ctrl = elem.FindPropertyRelative("animatorController")?.objectReferenceValue;
                var isEnabled = elem.FindPropertyRelative("isEnabled");
                var typeProp = elem.FindPropertyRelative("type");
                string typeName = typeProp != null && typeProp.enumValueIndex >= 0 &&
                                  typeProp.enumValueIndex < typeProp.enumNames.Length
                    ? typeProp.enumNames[typeProp.enumValueIndex]
                    : i.ToString();
                list.Add(new
                {
                    type = typeName,
                    isEnabled = isEnabled == null || isEnabled.boolValue,
                    controller = ctrl != null ? ctrl.name : null,
                    controllerPath = ctrl != null ? AssetDatabase.GetAssetPath(ctrl) : null
                });
            }
            return list;
        }

        private static void AddFramework(List<object> list, GameObject root, string label, string typeName)
        {
            var t = FindType(typeName);
            if (t == null) return;
            var comps = root.GetComponentsInChildren(t, true);
            if (comps.Length == 0) return;
            list.Add(new { framework = label, componentCount = comps.Length });
        }

        private static Type FindType(string fullName)
        {
            var t = Type.GetType(fullName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType(fullName); }
                catch { continue; }
                if (t != null) return t;
            }
            return null;
        }

        public static string ComputeStateHash(GameObject root, Scene scene)
        {
            var sb = new StringBuilder();
            sb.Append(scene.path).Append('|').Append(scene.isDirty).Append('|');
            if (root != null)
            {
                sb.Append(root.GetInstanceID()).Append('|');
                int polys = 0, mats = 0, rends = 0;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    rends++;
                    if (r.sharedMaterials != null) mats += r.sharedMaterials.Length;
                    Mesh mesh = null;
                    if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                    else
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        mesh = mf != null ? mf.sharedMesh : null;
                    }
                    if (mesh != null) polys += mesh.triangles.Length / 3;
                }
                sb.Append(rends).Append('|').Append(mats).Append('|').Append(polys);
            }
            using (var sha = SHA1.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) hex.Append(b.ToString("x2"));
                return hex.ToString().Substring(0, 16);
            }
        }

        private static string HierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var cur = t.parent;
            while (cur != null) { sb.Insert(0, cur.name + "/"); cur = cur.parent; }
            return sb.ToString();
        }

        private static object Vec(Vector3 v) => new { x = v.x, y = v.y, z = v.z };
        private static object Note(string n) => new { note = n };

        private static float? ReadFloat(SerializedObject so, string name)
        {
            var p = so.FindProperty(name);
            return p != null && p.propertyType == SerializedPropertyType.Float ? p.floatValue : (float?)null;
        }

        private static string ReadEnum(SerializedObject so, string name)
        {
            var p = so.FindProperty(name);
            if (p == null || p.propertyType != SerializedPropertyType.Enum) return null;
            return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumNames.Length
                ? p.enumNames[p.enumValueIndex] : p.enumValueIndex.ToString();
        }

        private static string ReadObjName(SerializedObject so, string name)
        {
            var p = so.FindProperty(name);
            return p?.objectReferenceValue != null ? p.objectReferenceValue.name : null;
        }

        private static int CountArray(SerializedObject so, string name)
        {
            var p = so.FindProperty(name);
            return p != null && p.isArray ? p.arraySize : 0;
        }

        private static int CountPbTransforms(Transform root)
        {
            // Approximate: all descendants under the PhysBone host (real rootTransform may differ).
            return root.GetComponentsInChildren<Transform>(true).Length;
        }
    }
}
